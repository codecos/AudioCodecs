using System.Buffers.Binary;
using System.Text;
using No_Glass_Player.PlayerHost.Playback;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Aac;

internal static class AacMp4Demuxer
{
    private static ReadOnlySpan<int> SamplingFrequencyTable => [96_000, 88_200, 64_000, 48_000, 44_100, 32_000, 24_000, 22_050, 16_000, 12_000, 11_025, 8_000, 7_350];

    internal static AacContainerInfo ReadInfo(string path)
    {
        if (AacAdtsDemuxer.IsAdtsFile(path))
        {
            return AacAdtsDemuxer.ReadInfo(path);
        }

        using var info = ReadDecodeInfo(path);
        return info.Container;
    }

    internal static AacDecodeInfo ReadDecodeInfo(string path)
    {
        if (AacAdtsDemuxer.IsAdtsFile(path))
        {
            return AacAdtsDemuxer.ReadDecodeInfo(path);
        }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);

        if (!TryFindTopLevelBox(fs, "moov", out var moov))
        {
            throw new InvalidDataException("MP4 moov box not found.");
        }

        var cursor = moov.PayloadOffset;
        while (cursor < moov.EndOffset)
        {
            if (!TryReadBoxHeader(fs, cursor, moov.EndOffset, out var trak))
            {
                break;
            }

            if (trak.Type == "trak" && TryReadAacTrackInfo(fs, moov, trak, out var info))
            {
                return info;
            }

            cursor = trak.EndOffset;
        }

        throw new NotSupportedException("No AAC audio track found in MP4/M4A container.");
    }

    private static bool TryReadAacTrackInfo(FileStream fs, BoxHeader moov, BoxHeader trak, out AacDecodeInfo info)
    {
        info = default;

        if (!TryFindChildBox(fs, trak, "mdia", out var mdia))
        {
            return false;
        }

        if (!TryFindChildBox(fs, mdia, "hdlr", out var hdlr))
        {
            return false;
        }

        if (!TryReadHandlerType(fs, hdlr, out var handlerType) || handlerType != "soun")
        {
            return false;
        }

        if (!TryFindChildBox(fs, mdia, "minf", out var minf) ||
            !TryFindChildBox(fs, minf, "stbl", out var stbl))
        {
            return false;
        }

        if (!TryFindChildBox(fs, stbl, "stsd", out var stsd) ||
            !TryReadStsdMp4aInfo(fs, stsd, out var stsdInfo))
        {
            return false;
        }

        if (!string.Equals(stsdInfo.CodecFourCc, "mp4a", StringComparison.Ordinal))
        {
            return false;
        }

        var declaredSampleRate = ResolveSampleRate(stsdInfo.SampleEntrySampleRate, stsdInfo.Config.Config);
        var bitsPerSample = stsdInfo.SampleEntryBitsPerSample > 0 ? stsdInfo.SampleEntryBitsPerSample : 16;
        if (!TryReadMediaTimescale(fs, mdia, out var mediaTimescale))
        {
            throw new InvalidDataException("AAC track missing a valid mdhd timescale.");
        }

        if (TryFindChildBox(fs, stbl, "stsz", out var stsz) && ReadStszSummary(fs, stsz).PacketCount > 0)
        {
            using var stszInfo = ReadStszInfo(fs, stsz);
            if (!TryFindChildBox(fs, stbl, "stsc", out var stsc) ||
                !TryReadStscInfo(fs, stsc, out var stscInfo) || stscInfo.Entries.Length == 0)
            {
                throw new InvalidDataException("AAC track missing valid stsc table.");
            }

            if (!TryFindChildBox(fs, stbl, "stco", out var stco) &&
                !TryFindChildBox(fs, stbl, "co64", out stco))
            {
                throw new InvalidDataException("AAC track missing valid chunk offsets.");
            }

            if (!TryFindChildBox(fs, stbl, "stts", out var stts))
            {
                throw new InvalidDataException("AAC track missing a valid stts table.");
            }

            using var chunkOffsets = ReadChunkOffsets(fs, stco, out var chunkOffsetCount);
            var packets = BuildPacketTable(fs, stszInfo, stscInfo, chunkOffsets, chunkOffsetCount);
            try
            {
                var packetCount = checked((int)stszInfo.PacketCount);
                var output = ProbeOutputFormat(fs, packets.Span.Slice(0, packetCount), stsdInfo.Config.DecoderSpecificInfo);
                var resolvedConfig = ResolveImplicitExtensions(
                    stsdInfo.Config,
                    ResolveChannelCount(stsdInfo.SampleEntryChannels, stsdInfo.Config.Config.ChannelConfiguration),
                    output);
                var sampleRate = output.SampleRate;
                var channels = output.Channels;
                var totalPcmFrames = ApplySttsTimeline(fs, stts, packets.Span.Slice(0, packetCount), mediaTimescale, sampleRate);
                var trimInfo = ResolveTrimInfo(fs, moov, trak, mediaTimescale, sampleRate, totalPcmFrames);
                var container = new AacContainerInfo(
                    channels, sampleRate, bitsPerSample, stszInfo.PacketCount, stszInfo.MaxPacketBytes,
                    totalPcmFrames, trimInfo.PrimingFrames, trimInfo.PaddingFrames, trimInfo.TrimmedFrames,
                    stsdInfo.CodecFourCc);
                info = new AacDecodeInfo(container, resolvedConfig, packets, packetCount);
                packets = null!;
                return true;
            }
            finally
            {
                packets?.Dispose();
            }
        }

        if (!TryReadTrackId(fs, trak, out var trackId) || !TryReadTrexDefaults(fs, moov, trackId, out var defaults))
        {
            throw new InvalidDataException("Fragmented AAC track is missing tkhd/trex defaults.");
        }

        var fragmentPackets = BuildFragmentedPacketTable(
            fs, trackId, defaults, mediaTimescale, declaredSampleRate,
            out var fragmentPacketCount, out var fragmentMaxPacketBytes, out var fragmentTotalFrames);
        try
        {
            var output = ProbeOutputFormat(fs, fragmentPackets.Span.Slice(0, fragmentPacketCount), stsdInfo.Config.DecoderSpecificInfo);
            var resolvedConfig = ResolveImplicitExtensions(
                stsdInfo.Config,
                ResolveChannelCount(stsdInfo.SampleEntryChannels, stsdInfo.Config.Config.ChannelConfiguration),
                output);
            var sampleRate = output.SampleRate;
            var channels = output.Channels;
            if (sampleRate != declaredSampleRate)
            {
                fragmentPackets.Dispose();
                fragmentPackets = BuildFragmentedPacketTable(
                    fs, trackId, defaults, mediaTimescale, sampleRate,
                    out fragmentPacketCount, out fragmentMaxPacketBytes, out fragmentTotalFrames);
            }

            var trimInfo = ResolveTrimInfo(fs, moov, trak, mediaTimescale, sampleRate, fragmentTotalFrames);
            var container = new AacContainerInfo(
                channels, sampleRate, bitsPerSample, checked((uint)fragmentPacketCount), fragmentMaxPacketBytes,
                fragmentTotalFrames, trimInfo.PrimingFrames, trimInfo.PaddingFrames, trimInfo.TrimmedFrames,
                stsdInfo.CodecFourCc);
            info = new AacDecodeInfo(container, resolvedConfig, fragmentPackets, fragmentPacketCount);
            fragmentPackets = null!;
            return true;
        }
        finally
        {
            fragmentPackets?.Dispose();
        }
    }


    private static bool TryReadAacContainerInfo(FileStream fs, BoxHeader moov, BoxHeader trak, out AacContainerInfo info)
    {
        info = default;
        if (!TryFindChildBox(fs, trak, "mdia", out var mdia) ||
            !TryFindChildBox(fs, mdia, "hdlr", out var hdlr) ||
            !TryReadHandlerType(fs, hdlr, out var handlerType) || handlerType != "soun" ||
            !TryFindChildBox(fs, mdia, "minf", out var minf) ||
            !TryFindChildBox(fs, minf, "stbl", out var stbl) ||
            !TryFindChildBox(fs, stbl, "stsd", out var stsd) ||
            !TryReadStsdMp4aInfo(fs, stsd, out var stsdInfo) ||
            !string.Equals(stsdInfo.CodecFourCc, "mp4a", StringComparison.Ordinal))
        {
            return false;
        }

        var sampleRate = ResolveSampleRate(stsdInfo.SampleEntrySampleRate, stsdInfo.Config.Config);
        if (!TryReadMediaTimescale(fs, mdia, out var mediaTimescale))
        {
            throw new InvalidDataException("AAC track missing a valid mdhd timescale.");
        }

        uint packetCount;
        uint maxPacketBytes;
        long totalPcmFrames;
        if (TryFindChildBox(fs, stbl, "stsz", out var stsz) && ReadStszSummary(fs, stsz) is var stszSummary && stszSummary.PacketCount > 0)
        {
            packetCount = stszSummary.PacketCount;
            maxPacketBytes = stszSummary.MaxPacketBytes;
            totalPcmFrames = 0;
            if (TryFindChildBox(fs, stbl, "stts", out var stts) && TryReadSttsTotalDuration(fs, stts, out var duration))
            {
                totalPcmFrames = ScaleTimestamp(duration, mediaTimescale, sampleRate);
            }
        }
        else
        {
            if (!TryReadTrackId(fs, trak, out var trackId) || !TryReadTrexDefaults(fs, moov, trackId, out var defaults))
            {
                throw new InvalidDataException("Fragmented AAC track is missing tkhd/trex defaults.");
            }

            using var packets = BuildFragmentedPacketTable(
                fs, trackId, defaults, mediaTimescale, sampleRate,
                out var count, out maxPacketBytes, out totalPcmFrames);
            packetCount = checked((uint)count);
        }

        var trimInfo = ResolveTrimInfo(fs, moov, trak, mediaTimescale, sampleRate, totalPcmFrames);

        info = new AacContainerInfo(
            ResolveChannelCount(stsdInfo.SampleEntryChannels, stsdInfo.Config.Config.ChannelConfiguration),
            sampleRate,
            stsdInfo.SampleEntryBitsPerSample > 0 ? stsdInfo.SampleEntryBitsPerSample : 16,
            packetCount,
            maxPacketBytes,
            totalPcmFrames,
            trimInfo.PrimingFrames,
            trimInfo.PaddingFrames,
            trimInfo.TrimmedFrames,
            stsdInfo.CodecFourCc);
        return true;
    }

    private static ProbeFormat ProbeOutputFormat(
        FileStream stream,
        Span<AacPacketInfo> packets,
        byte[] decoderSpecificInfo)
    {
        var decoder = new SharpJaad.AAC.Decoder(decoderSpecificInfo);
        var sampleBuffer = new SharpJaad.AAC.SampleBuffer();
        sampleBuffer.SetBigEndian(false);
        var packetBuffer = new byte[packets.Length == 0 ? 0 : packets[0].Size];
        var probeCount = Math.Min(packets.Length, 16);
        for (var index = 0; index < probeCount; index++)
        {
            var packet = packets[index];
            if (packetBuffer.Length < packet.Size)
            {
                packetBuffer = new byte[packet.Size];
            }

            stream.Position = packet.Offset;
            stream.ReadExactly(packetBuffer.AsSpan(0, packet.Size));
            decoder.DecodeFrame(packetBuffer.AsSpan(0, packet.Size), sampleBuffer);
            if (sampleBuffer.SampleRate > 0 && sampleBuffer.Channels > 0 && sampleBuffer.FramesPerChannel > 0)
            {
                return new ProbeFormat(sampleBuffer.SampleRate, sampleBuffer.Channels, sampleBuffer.FramesPerChannel);
            }
        }

        throw new InvalidDataException("JAAD produced no output while probing the first AAC packets.");
    }

    private static AacEsdsInfo ResolveImplicitExtensions(AacEsdsInfo esds, int declaredChannels, ProbeFormat output)
    {
        var config = esds.Config;
        var coreSampleRate = config.SampleRate;
        if (coreSampleRate <= 0)
        {
            throw new InvalidDataException("AAC AudioSpecificConfig has no valid core sample rate.");
        }

        var hasSbr = config.HasSbrExtension || output.SampleRate != coreSampleRate;
        if (output.SampleRate != coreSampleRate && output.SampleRate != checked(coreSampleRate * 2))
        {
            throw new NotSupportedException(
                $"AAC output rate is not supported. core={coreSampleRate} output={output.SampleRate}");
        }

        var hasParametricStereo = declaredChannels == 1 && output.Channels == 2;
        if (output.Channels != declaredChannels && !hasParametricStereo)
        {
            throw new NotSupportedException(
                $"AAC changed its channel layout during SBR/PS processing. declared={declaredChannels} output={output.Channels}");
        }

        if (output.Channels is < 1 or > 2)
        {
            throw new NotSupportedException($"JAAD AAC path currently supports mono/stereo only. channels={output.Channels}");
        }

        var expectedFrames = hasSbr ? 2048 : 1024;
        if (output.FramesPerChannel != expectedFrames)
        {
            throw new NotSupportedException(
                $"AAC frame length is not supported. expected={expectedFrames} actual={output.FramesPerChannel}");
        }

        var resolved = config with
        {
            AudioObjectType = hasParametricStereo ? 29 : config.AudioObjectType,
            HasSbrExtension = hasSbr,
            ExtensionAudioObjectType = hasSbr ? 5 : config.ExtensionAudioObjectType,
            ExtensionSamplingFrequencyIndex = hasSbr ? FindSamplingFrequencyIndex(output.SampleRate) : config.ExtensionSamplingFrequencyIndex,
            ExtensionSampleRate = hasSbr ? output.SampleRate : config.ExtensionSampleRate
        };
        return esds with { Config = resolved };
    }

    private static int FindSamplingFrequencyIndex(int sampleRate)
    {
        for (var index = 0; index < SamplingFrequencyTable.Length; index++)
        {
            if (SamplingFrequencyTable[index] == sampleRate)
            {
                return index;
            }
        }

        return -1;
    }

    private static int ResolveChannelCount(int sampleEntryChannels, int channelConfiguration)
    {
        return channelConfiguration switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            6 => 6,
            7 => 8,
            _ => Math.Max(1, sampleEntryChannels)
        };
    }

    private static int ResolveSampleRate(int sampleEntrySampleRate, AacAudioSpecificConfig config)
    {
        if (config.HasSbrExtension && config.ExtensionSampleRate > 0)
        {
            return config.ExtensionSampleRate;
        }

        if (config.SampleRate > 0)
        {
            return config.SampleRate;
        }

        return Math.Max(1, sampleEntrySampleRate);
    }

    private static StszInfo ReadStszInfo(FileStream fs, BoxHeader stsz)
    {
        if (stsz.PayloadLength < 12)
        {
            throw new InvalidDataException("Invalid stsz box.");
        }

        Span<byte> header = stackalloc byte[12];
        fs.Position = stsz.PayloadOffset;
        fs.ReadExactly(header);

        var sampleSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4));
        var sizes = new NativeBufferOwner<int>(checked((int)Math.Max(1U, sampleCount)));
        try
        {
            sizes.PreTouch();
            var packetSizes = sizes.Span.Slice(0, checked((int)sampleCount));
            uint maxPacketBytes = 0;

            if (sampleSize != 0)
            {
                maxPacketBytes = sampleSize;
                for (var i = 0; i < packetSizes.Length; i++)
                {
                    packetSizes[i] = checked((int)sampleSize);
                }
            }
            else
            {
                var remaining = stsz.PayloadLength - 12;
                var needed = (long)sampleCount * 4;
                if (needed > remaining)
                {
                    throw new InvalidDataException("Invalid stsz packet sizes.");
                }

                Span<byte> sizeBytes = stackalloc byte[4];
                for (var i = 0; i < packetSizes.Length; i++)
                {
                    fs.ReadExactly(sizeBytes);
                    var packetBytes = BinaryPrimitives.ReadUInt32BigEndian(sizeBytes);
                    if (packetBytes > int.MaxValue)
                    {
                        throw new InvalidDataException("AAC packet exceeds supported size.");
                    }

                    packetSizes[i] = (int)packetBytes;
                    if (packetBytes > maxPacketBytes)
                    {
                        maxPacketBytes = packetBytes;
                    }
                }
            }

            return new StszInfo(sampleCount, maxPacketBytes, sizes);
        }
        catch
        {
            sizes.Dispose();
            throw;
        }
    }


    private static bool TryReadSttsTotalDuration(FileStream fs, BoxHeader stts, out long duration)
    {
        duration = 0;
        if (stts.PayloadLength < 8)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        fs.Position = stts.PayloadOffset;
        fs.ReadExactly(header);
        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));

        var remaining = stts.PayloadLength - 8;
        var needed = (long)entryCount * 8;
        if (needed > remaining)
        {
            return false;
        }

        Span<byte> entry = stackalloc byte[8];
        long total = 0;
        for (var i = 0U; i < entryCount; i++)
        {
            fs.ReadExactly(entry);
            var count = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0, 4));
            var delta = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(4, 4));
            total += (long)count * delta;
        }

        duration = total;
        return true;
    }

    private static long ApplySttsTimeline(
        FileStream fs,
        BoxHeader stts,
        Span<AacPacketInfo> packets,
        uint mediaTimescale,
        int sampleRate)
    {
        if (stts.PayloadLength < 8)
        {
            throw new InvalidDataException("Invalid AAC stts box.");
        }

        Span<byte> header = stackalloc byte[8];
        fs.Position = stts.PayloadOffset;
        fs.ReadExactly(header);
        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        if ((long)entryCount * 8 > stts.PayloadLength - 8)
        {
            throw new InvalidDataException("Invalid AAC stts entries.");
        }

        Span<byte> entry = stackalloc byte[8];
        long mediaTimestamp = 0;
        var packetIndex = 0;
        for (var entryIndex = 0U; entryIndex < entryCount; entryIndex++)
        {
            fs.ReadExactly(entry);
            var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0, 4));
            var sampleDelta = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(4, 4));
            if (sampleDelta == 0 || sampleCount > packets.Length - packetIndex)
            {
                throw new InvalidDataException("AAC stts table does not match the packet table.");
            }

            for (var sample = 0U; sample < sampleCount; sample++)
            {
                var startFrame = ScaleTimestamp(mediaTimestamp, mediaTimescale, sampleRate);
                mediaTimestamp = checked(mediaTimestamp + sampleDelta);
                var endFrame = ScaleTimestamp(mediaTimestamp, mediaTimescale, sampleRate);
                var packet = packets[packetIndex];
                packets[packetIndex++] = packet with
                {
                    DecodeStartFrame = startFrame,
                    DurationFrames = checked((int)(endFrame - startFrame))
                };
            }
        }

        if (packetIndex != packets.Length)
        {
            throw new InvalidDataException("AAC stts table does not cover all packets.");
        }

        return ScaleTimestamp(mediaTimestamp, mediaTimescale, sampleRate);
    }

    private static bool TryReadStsdMp4aInfo(FileStream fs, BoxHeader stsd, out StsdMp4aInfo info)
    {
        info = default;
        if (stsd.PayloadLength < 16)
        {
            return false;
        }

        Span<byte> stsdHeader = stackalloc byte[8];
        fs.Position = stsd.PayloadOffset;
        fs.ReadExactly(stsdHeader);

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(stsdHeader.Slice(4, 4));
        if (entryCount == 0)
        {
            return false;
        }

        var entryOffset = fs.Position;
        Span<byte> entryHead = stackalloc byte[8];
        fs.ReadExactly(entryHead);

        var entrySize = BinaryPrimitives.ReadUInt32BigEndian(entryHead.Slice(0, 4));
        if (entrySize < 36)
        {
            return false;
        }

        var codec = ReadFourCc(entryHead.Slice(4, 4));
        var entryEnd = entryOffset + entrySize;
        if (entryEnd > stsd.EndOffset)
        {
            return false;
        }

        Span<byte> sampleEntryAudio = stackalloc byte[28];
        fs.ReadExactly(sampleEntryAudio);
        var sampleEntryChannels = BinaryPrimitives.ReadUInt16BigEndian(sampleEntryAudio.Slice(16, 2));
        var sampleEntryBitsPerSample = BinaryPrimitives.ReadUInt16BigEndian(sampleEntryAudio.Slice(18, 2));
        var sampleEntrySampleRate = (int)(BinaryPrimitives.ReadUInt32BigEndian(sampleEntryAudio.Slice(24, 4)) >> 16);

        var childCursor = entryOffset + 8 + 28;
        while (childCursor + 8 <= entryEnd)
        {
            if (!TryReadBoxHeader(fs, childCursor, entryEnd, out var child))
            {
                break;
            }

            if (TryReadEsdsFromSampleEntryChild(fs, child, out var esdsInfo))
            {
                info = new StsdMp4aInfo(codec, sampleEntryChannels, sampleEntryBitsPerSample, sampleEntrySampleRate, esdsInfo);
                return true;
            }

            childCursor = child.EndOffset;
        }

        return false;
    }

    private static bool TryReadEsdsFromSampleEntryChild(FileStream fs, BoxHeader child, out AacEsdsInfo info)
    {
        info = default;

        if (child.Type == "esds")
        {
            return TryReadEsdsInfo(fs, child, out info);
        }

        if (child.Type != "wave")
        {
            return false;
        }

        var cursor = child.PayloadOffset;
        while (cursor + 8 <= child.EndOffset)
        {
            if (!TryReadBoxHeader(fs, cursor, child.EndOffset, out var nested))
            {
                break;
            }

            if (nested.Type == "esds" && TryReadEsdsInfo(fs, nested, out info))
            {
                return true;
            }

            cursor = nested.EndOffset;
        }

        return false;
    }

    private static bool TryReadEsdsInfo(FileStream fs, BoxHeader esds, out AacEsdsInfo info)
    {
        info = default;
        if (esds.PayloadLength < 4)
        {
            return false;
        }

        var payloadBytes = checked((int)esds.PayloadLength);
        var payload = new byte[payloadBytes];
        fs.Position = esds.PayloadOffset;
        fs.ReadExactly(payload);

        var offset = 4;
        if (!TryReadDescriptorHeader(payload, ref offset, out var tag, out var size) || tag != 0x03)
        {
            return false;
        }

        var esEnd = offset + size;
        if (esEnd > payload.Length || size < 3)
        {
            return false;
        }

        offset += 2;
        var flags = payload[offset++];
        if ((flags & 0x80) != 0)
        {
            offset += 2;
        }

        if ((flags & 0x40) != 0)
        {
            if (offset >= esEnd)
            {
                return false;
            }

            var urlLength = payload[offset++];
            offset += urlLength;
        }

        if ((flags & 0x20) != 0)
        {
            offset += 2;
        }

        if (offset >= esEnd ||
            !TryReadDescriptorHeader(payload, ref offset, out tag, out size) ||
            tag != 0x04)
        {
            return false;
        }

        var decoderConfigEnd = offset + size;
        if (decoderConfigEnd > esEnd || size < 13)
        {
            return false;
        }

        var objectTypeIndication = payload[offset++];
        var streamType = (payload[offset++] >> 2) & 0x3F;
        var bufferSizeDb = (payload[offset] << 16) | (payload[offset + 1] << 8) | payload[offset + 2];
        offset += 3;
        var maxBitrate = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset, 4));
        offset += 4;
        var avgBitrate = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset, 4));
        offset += 4;

        if (!TryReadDescriptorHeader(payload, ref offset, out tag, out size) || tag != 0x05)
        {
            return false;
        }

        var ascEnd = offset + size;
        if (ascEnd > decoderConfigEnd)
        {
            return false;
        }

        var configBytes = payload.AsSpan(offset, size).ToArray();
        if (!TryParseAudioSpecificConfig(configBytes, out var config))
        {
            return false;
        }

        info = new AacEsdsInfo(objectTypeIndication, streamType, bufferSizeDb, maxBitrate, avgBitrate, configBytes, config);
        return true;
    }

    private static bool TryParseAudioSpecificConfig(ReadOnlySpan<byte> asc, out AacAudioSpecificConfig config)
    {
        config = default;
        if (asc.Length < 2)
        {
            return false;
        }

        var reader = new AacConfigBitReader(asc);
        var audioObjectType = ReadAudioObjectType(ref reader);
        var samplingFrequencyIndex = reader.ReadBits(4);
        var sampleRate = ReadSampleRate(ref reader, samplingFrequencyIndex);
        var channelConfiguration = reader.ReadBits(4);

        var effectiveAudioObjectType = audioObjectType;
        var hasSbrExtension = false;
        var extensionAudioObjectType = 0;
        var extensionSamplingFrequencyIndex = -1;
        var extensionSampleRate = 0;

        if (audioObjectType == 5 || audioObjectType == 29)
        {
            hasSbrExtension = true;
            extensionAudioObjectType = audioObjectType;
            extensionSamplingFrequencyIndex = reader.ReadBits(4);
            extensionSampleRate = ReadSampleRate(ref reader, extensionSamplingFrequencyIndex);
            effectiveAudioObjectType = ReadAudioObjectType(ref reader);
        }

        config = new AacAudioSpecificConfig(
            audioObjectType,
            effectiveAudioObjectType,
            samplingFrequencyIndex,
            sampleRate,
            channelConfiguration,
            hasSbrExtension,
            extensionAudioObjectType,
            extensionSamplingFrequencyIndex,
            extensionSampleRate);
        return true;
    }

    private static int ReadAudioObjectType(ref AacConfigBitReader reader)
    {
        var audioObjectType = reader.ReadBits(5);
        if (audioObjectType == 31)
        {
            audioObjectType = 32 + reader.ReadBits(6);
        }

        return audioObjectType;
    }

    private static int ReadSampleRate(ref AacConfigBitReader reader, int samplingFrequencyIndex)
    {
        if (samplingFrequencyIndex == 0x0F)
        {
            return reader.ReadBits(24);
        }

        return (uint)samplingFrequencyIndex < (uint)SamplingFrequencyTable.Length
            ? SamplingFrequencyTable[samplingFrequencyIndex]
            : 0;
    }

    private static bool TryReadDescriptorHeader(ReadOnlySpan<byte> data, ref int offset, out byte tag, out int size)
    {
        tag = 0;
        size = 0;

        if ((uint)offset >= (uint)data.Length)
        {
            return false;
        }

        tag = data[offset++];
        for (var i = 0; i < 4; i++)
        {
            if ((uint)offset >= (uint)data.Length)
            {
                return false;
            }

            var b = data[offset++];
            size = (size << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadHandlerType(FileStream fs, BoxHeader hdlr, out string handlerType)
    {
        handlerType = string.Empty;
        if (hdlr.PayloadLength < 12)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[12];
        fs.Position = hdlr.PayloadOffset;
        fs.ReadExactly(header);
        handlerType = ReadFourCc(header.Slice(8, 4));
        return true;
    }

    private static TrimInfo ResolveTrimInfo(
        FileStream fs,
        BoxHeader moov,
        BoxHeader trak,
        uint mediaTimescale,
        int sampleRate,
        long totalPcmFrames)
    {
        if (TryReadEditListTrimInfo(fs, moov, trak, mediaTimescale, sampleRate, totalPcmFrames, out var editInfo))
        {
            return editInfo;
        }

        if (TryReadItunesSmpbTrimInfo(fs, moov, totalPcmFrames, out var smpbInfo))
        {
            return new TrimInfo(smpbInfo.PrimingFrames, smpbInfo.PaddingFrames, smpbInfo.TrimmedFrames);
        }

        return new TrimInfo(0, 0, Math.Max(0, totalPcmFrames));
    }

    private static bool TryReadEditListTrimInfo(
        FileStream fs,
        BoxHeader moov,
        BoxHeader trak,
        uint mediaTimescale,
        int sampleRate,
        long totalPcmFrames,
        out TrimInfo info)
    {
        info = default;
        if (!TryFindChildBox(fs, trak, "edts", out var edts) ||
            !TryFindChildBox(fs, edts, "elst", out var elst) ||
            !TryReadMovieTimescale(fs, moov, out var movieTimescale) ||
            elst.PayloadLength < 8)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        fs.Position = elst.PayloadOffset;
        fs.ReadExactly(header);
        var version = header[0];
        if (version is not (0 or 1))
        {
            return false;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        var entryBytes = version == 1 ? 20 : 12;
        if (entryCount == 0 || (long)entryCount * entryBytes > elst.PayloadLength - 8)
        {
            return false;
        }

        Span<byte> entry = stackalloc byte[20];
        var foundMediaEdit = false;
        long mediaStart = 0;
        ulong presentationDuration = 0;
        for (var index = 0U; index < entryCount; index++)
        {
            var bytes = entry.Slice(0, entryBytes);
            fs.ReadExactly(bytes);
            var segmentDuration = version == 1
                ? BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(0, 8))
                : BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(0, 4));
            var mediaTime = version == 1
                ? BinaryPrimitives.ReadInt64BigEndian(bytes.Slice(8, 8))
                : BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(4, 4));
            var rateOffset = version == 1 ? 16 : 8;
            var rateInteger = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(rateOffset, 2));
            var rateFraction = BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(rateOffset + 2, 2));

            // A leading empty edit requires synthesized silence, and multiple media edits
            // require a discontinuous presentation map. Neither is a gapless trim.
            if (mediaTime < 0 || rateInteger != 1 || rateFraction != 0 || foundMediaEdit)
            {
                return false;
            }

            foundMediaEdit = true;
            mediaStart = mediaTime;
            presentationDuration = segmentDuration;
        }

        if (!foundMediaEdit || presentationDuration > long.MaxValue)
        {
            return false;
        }

        var startFrame = ScaleTimestamp(mediaStart, mediaTimescale, sampleRate);
        var durationFrames = ScaleTimestamp((long)presentationDuration, movieTimescale, sampleRate);
        if (startFrame < 0 || startFrame > totalPcmFrames || durationFrames <= 0)
        {
            return false;
        }

        var endFrame = Math.Min(totalPcmFrames, checked(startFrame + durationFrames));
        var paddingFrames = totalPcmFrames - endFrame;
        if (startFrame > int.MaxValue || paddingFrames > int.MaxValue)
        {
            throw new NotSupportedException("AAC edit-list trim exceeds the supported frame range.");
        }

        info = new TrimInfo((int)startFrame, (int)paddingFrames, endFrame - startFrame);
        return true;
    }

    private static bool TryReadMediaTimescale(FileStream fs, BoxHeader mdia, out uint timescale)
    {
        timescale = 0;
        return TryFindChildBox(fs, mdia, "mdhd", out var mdhd) && TryReadHeaderTimescale(fs, mdhd, out timescale);
    }

    private static bool TryReadMovieTimescale(FileStream fs, BoxHeader moov, out uint timescale)
    {
        timescale = 0;
        return TryFindChildBox(fs, moov, "mvhd", out var mvhd) && TryReadHeaderTimescale(fs, mvhd, out timescale);
    }

    private static bool TryReadHeaderTimescale(FileStream fs, BoxHeader box, out uint timescale)
    {
        timescale = 0;
        if (box.PayloadLength < 20)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[24];
        fs.Position = box.PayloadOffset;
        var version = fs.ReadByte();
        if (version is not (0 or 1))
        {
            return false;
        }

        var timescaleOffset = version == 1 ? 20 : 12;
        if (box.PayloadLength < timescaleOffset + 4)
        {
            return false;
        }

        fs.Position = box.PayloadOffset;
        fs.ReadExactly(header.Slice(0, timescaleOffset + 4));
        timescale = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(timescaleOffset, 4));
        return timescale != 0;
    }

    private static long ScaleTimestamp(long timestamp, uint sourceTimescale, int targetSampleRate)
    {
        if (timestamp < 0 || sourceTimescale == 0 || targetSampleRate <= 0)
        {
            throw new InvalidDataException("Invalid MP4 audio timestamp or timescale.");
        }

        return checked((long)(((Int128)timestamp * targetSampleRate) / sourceTimescale));
    }

    private static bool TryReadItunesSmpbTrimInfo(FileStream fs, BoxHeader moov, long totalPcmFrames, out ItunesSmpbInfo info)
    {
        info = default;

        if (!TryFindChildBox(fs, moov, "udta", out var udta) ||
            !TryFindChildBox(fs, udta, "meta", out var meta))
        {
            return false;
        }

        if (meta.PayloadLength < 4)
        {
            return false;
        }

        var metaChildren = new BoxHeader(meta.Type, meta.PayloadOffset + 4, meta.EndOffset);
        if (!TryFindChildBox(fs, metaChildren, "ilst", out var ilst))
        {
            return false;
        }

        var cursor = ilst.PayloadOffset;
        while (cursor < ilst.EndOffset)
        {
            if (!TryReadBoxHeader(fs, cursor, ilst.EndOffset, out var entry))
            {
                return false;
            }

            if (entry.Type == "----" &&
                TryReadItunesFreeformValue(fs, entry, out var mean, out var name, out var value) &&
                string.Equals(mean, "com.apple.iTunes", StringComparison.Ordinal) &&
                string.Equals(name, "iTunSMPB", StringComparison.Ordinal) &&
                TryParseItunesSmpb(value, totalPcmFrames, out info))
            {
                return true;
            }

            cursor = entry.EndOffset;
        }

        return false;
    }

    private static bool TryReadItunesFreeformValue(FileStream fs, BoxHeader entry, out string mean, out string name, out string value)
    {
        mean = string.Empty;
        name = string.Empty;
        value = string.Empty;

        var cursor = entry.PayloadOffset;
        while (cursor < entry.EndOffset)
        {
            if (!TryReadBoxHeader(fs, cursor, entry.EndOffset, out var child))
            {
                return false;
            }

            switch (child.Type)
            {
                case "mean":
                    if (!TryReadMetadataString(fs, child, 4, out mean))
                    {
                        return false;
                    }

                    break;

                case "name":
                    if (!TryReadMetadataString(fs, child, 4, out name))
                    {
                        return false;
                    }

                    break;

                case "data":
                    if (!TryReadMetadataString(fs, child, 8, out value))
                    {
                        return false;
                    }

                    break;
            }

            cursor = child.EndOffset;
        }

        return !string.IsNullOrEmpty(mean) && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value);
    }

    private static bool TryReadMetadataString(FileStream fs, BoxHeader box, int payloadSkipBytes, out string value)
    {
        value = string.Empty;
        if (box.PayloadLength < payloadSkipBytes)
        {
            return false;
        }

        var length = checked((int)(box.PayloadLength - payloadSkipBytes));
        if (length == 0)
        {
            value = string.Empty;
            return true;
        }

        var bytes = new byte[length];
        fs.Position = box.PayloadOffset + payloadSkipBytes;
        fs.ReadExactly(bytes);
        value = Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ', '\r', '\n', '\t');
        return true;
    }

    private static bool TryParseItunesSmpb(string value, long totalPcmFrames, out ItunesSmpbInfo info)
    {
        info = default;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var primingFrames) ||
            !int.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var paddingFrames) ||
            !long.TryParse(parts[3], System.Globalization.NumberStyles.HexNumber, null, out var trimmedFramesFromTag))
        {
            return false;
        }

        var trimmedFrames = trimmedFramesFromTag > 0
            ? trimmedFramesFromTag
            : Math.Max(0, totalPcmFrames - primingFrames - paddingFrames);
        info = new ItunesSmpbInfo(primingFrames, paddingFrames, trimmedFrames);
        return true;
    }

    private static bool TryFindTopLevelBox(FileStream fs, string type, out BoxHeader box)
    {
        box = default;
        var cursor = 0L;
        var limit = fs.Length;

        while (cursor < limit)
        {
            if (!TryReadBoxHeader(fs, cursor, limit, out var current))
            {
                return false;
            }

            if (current.Type == type)
            {
                box = current;
                return true;
            }

            cursor = current.EndOffset;
        }

        return false;
    }

    private static bool TryFindChildBox(FileStream fs, BoxHeader parent, string childType, out BoxHeader child)
    {
        child = default;
        var cursor = parent.PayloadOffset;

        while (cursor < parent.EndOffset)
        {
            if (!TryReadBoxHeader(fs, cursor, parent.EndOffset, out var current))
            {
                return false;
            }

            if (current.Type == childType)
            {
                child = current;
                return true;
            }

            cursor = current.EndOffset;
        }

        return false;
    }

    private static bool TryReadStscInfo(FileStream fs, BoxHeader stsc, out StscInfo info)
    {
        info = default;
        if (stsc.PayloadLength < 8)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        fs.Position = stsc.PayloadOffset;
        fs.ReadExactly(header);
        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        var remaining = stsc.PayloadLength - 8;
        var needed = (long)entryCount * 12;
        if (needed > remaining)
        {
            return false;
        }

        var entries = new StscEntry[entryCount];
        Span<byte> entry = stackalloc byte[12];
        for (var i = 0; i < entries.Length; i++)
        {
            fs.ReadExactly(entry);
            entries[i] = new StscEntry(
                BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(0, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(4, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(8, 4)));
        }

        info = new StscInfo(entries);
        return true;
    }

    private static NativeBufferOwner<long> ReadChunkOffsets(FileStream fs, BoxHeader stco, out int count)
    {
        if (stco.PayloadLength < 8)
        {
            throw new InvalidDataException("Invalid chunk offset box.");
        }

        Span<byte> header = stackalloc byte[8];
        fs.Position = stco.PayloadOffset;
        fs.ReadExactly(header);
        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        var remaining = stco.PayloadLength - 8;
        var entryBytes = string.Equals(stco.Type, "co64", StringComparison.Ordinal) ? 8 : 4;
        var needed = (long)entryCount * entryBytes;
        if (needed > remaining)
        {
            throw new InvalidDataException("Invalid chunk offsets.");
        }

        count = checked((int)entryCount);
        var result = new NativeBufferOwner<long>(Math.Max(1, count));
        try
        {
            result.PreTouch();
            var offsets = result.Span.Slice(0, count);
            if (entryBytes == 8)
            {
                Span<byte> entry = stackalloc byte[8];
                for (var i = 0; i < offsets.Length; i++)
                {
                    fs.ReadExactly(entry);
                    offsets[i] = checked((long)BinaryPrimitives.ReadUInt64BigEndian(entry));
                }
            }
            else
            {
                Span<byte> entry = stackalloc byte[4];
                for (var i = 0; i < offsets.Length; i++)
                {
                    fs.ReadExactly(entry);
                    offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(entry);
                }
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static NativeBufferOwner<AacPacketInfo> BuildPacketTable(
        FileStream fs,
        StszInfo stsz,
        StscInfo stsc,
        NativeBufferOwner<long> chunkOffsetOwner,
        int chunkOffsetCount)
    {
        if (stsc.Entries.Length == 0)
        {
            throw new InvalidDataException("AAC stsc table is empty.");
        }

        var packetCount = checked((int)stsz.PacketCount);
        var packets = new NativeBufferOwner<AacPacketInfo>(Math.Max(1, packetCount));
        try
        {
            packets.PreTouch();
            var packetSpan = packets.Span.Slice(0, packetCount);
            var packetSizes = stsz.PacketSizes;
            var chunkOffsets = chunkOffsetOwner.Span.Slice(0, chunkOffsetCount);
            var packetIndex = 0;
            var stscIndex = 0;

            for (var chunkZeroBased = 0; chunkZeroBased < chunkOffsets.Length && packetIndex < packetSpan.Length; chunkZeroBased++)
            {
                var chunkOneBased = (uint)(chunkZeroBased + 1);
                while (stscIndex + 1 < stsc.Entries.Length && chunkOneBased >= stsc.Entries[stscIndex + 1].FirstChunk)
                {
                    stscIndex++;
                }

                var mapping = stsc.Entries[stscIndex];
                var offset = chunkOffsets[chunkZeroBased];

                for (var sampleInChunk = 0U; sampleInChunk < mapping.SamplesPerChunk && packetIndex < packetSpan.Length; sampleInChunk++)
                {
                    var size = stsz.PacketSizes[packetIndex];
                    if (size < 0)
                    {
                        throw new InvalidDataException("Negative packet size.");
                    }

                    if (offset < 0 || offset + size > fs.Length)
                    {
                        throw new InvalidDataException("AAC packet offset exceeds file length.");
                    }

                    packetSpan[packetIndex] = new AacPacketInfo(offset, size);
                    offset += size;
                    packetIndex++;
                }
            }

            if (packetIndex != packetSpan.Length)
            {
                throw new InvalidDataException("AAC sample table does not cover all packets.");
            }

            return packets;
        }
        catch
        {
            packets.Dispose();
            throw;
        }
    }

    private static NativeBufferOwner<AacPacketInfo> BuildFragmentedPacketTable(
        FileStream fs,
        uint trackId,
        TrexDefaults defaults,
        uint mediaTimescale,
        int sampleRate,
        out int packetCount,
        out uint maxPacketBytes,
        out long totalPcmFrames)
    {
        var summary = ScanFragmentedPackets(fs, trackId, defaults, mediaTimescale, sampleRate, Span<AacPacketInfo>.Empty, false);
        if (summary.PacketCount <= 0)
        {
            throw new InvalidDataException("Fragmented AAC file contains no packets for the selected audio track.");
        }

        var owner = new NativeBufferOwner<AacPacketInfo>(summary.PacketCount);
        try
        {
            owner.PreTouch();
            var populated = ScanFragmentedPackets(
                fs, trackId, defaults, mediaTimescale, sampleRate,
                owner.Span.Slice(0, summary.PacketCount), true);
            if (populated != summary)
            {
                throw new InvalidDataException("Fragmented AAC packet table changed while it was being read.");
            }

            packetCount = summary.PacketCount;
            maxPacketBytes = summary.MaxPacketBytes;
            totalPcmFrames = summary.TotalPcmFrames;
            return owner;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    private static FragmentScanResult ScanFragmentedPackets(
        FileStream fs,
        uint trackId,
        TrexDefaults defaults,
        uint mediaTimescale,
        int sampleRate,
        Span<AacPacketInfo> destination,
        bool writePackets)
    {
        var packetCount = 0;
        uint maxPacketBytes = 0;
        long maximumMediaEnd = 0;
        long carriedMediaTimestamp = 0;
        var topLevelCursor = 0L;
        while (topLevelCursor < fs.Length)
        {
            if (!TryReadBoxHeader(fs, topLevelCursor, fs.Length, out var topLevel))
            {
                throw new InvalidDataException("Invalid top-level MP4 box while scanning fragments.");
            }

            topLevelCursor = topLevel.EndOffset;
            if (topLevel.Type != "moof")
            {
                continue;
            }

            var trafCursor = topLevel.PayloadOffset;
            while (trafCursor < topLevel.EndOffset)
            {
                if (!TryReadBoxHeader(fs, trafCursor, topLevel.EndOffset, out var traf))
                {
                    throw new InvalidDataException("Invalid box inside fragmented MP4 moof.");
                }

                trafCursor = traf.EndOffset;
                if (traf.Type != "traf" ||
                    !TryFindChildBox(fs, traf, "tfhd", out var tfhdBox) ||
                    !TryReadTfhd(fs, tfhdBox, out var tfhd) ||
                    tfhd.TrackId != trackId)
                {
                    continue;
                }

                var sampleDescriptionIndex = tfhd.SampleDescriptionIndex ?? defaults.SampleDescriptionIndex;
                if (sampleDescriptionIndex != 1)
                {
                    throw new NotSupportedException($"Fragmented AAC sample-description changes are not supported. index={sampleDescriptionIndex}");
                }

                var mediaTimestamp = carriedMediaTimestamp;
                if (TryFindChildBox(fs, traf, "tfdt", out var tfdt) && !TryReadTfdt(fs, tfdt, out mediaTimestamp))
                {
                    throw new InvalidDataException("Invalid fragmented AAC tfdt box.");
                }

                var baseDataOffset = tfhd.BaseDataOffset ?? topLevel.StartOffset;
                long nextRunDataOffset = -1;
                var childCursor = traf.PayloadOffset;
                while (childCursor < traf.EndOffset)
                {
                    if (!TryReadBoxHeader(fs, childCursor, traf.EndOffset, out var child))
                    {
                        throw new InvalidDataException("Invalid box inside fragmented MP4 traf.");
                    }

                    childCursor = child.EndOffset;
                    if (child.Type != "trun")
                    {
                        continue;
                    }

                    ReadFragmentRun(
                        fs, child, baseDataOffset, ref nextRunDataOffset,
                        tfhd.DefaultSampleDuration ?? defaults.DefaultSampleDuration,
                        tfhd.DefaultSampleSize ?? defaults.DefaultSampleSize,
                        mediaTimescale, sampleRate, destination, writePackets,
                        ref packetCount, ref maxPacketBytes, ref mediaTimestamp);
                }

                carriedMediaTimestamp = Math.Max(carriedMediaTimestamp, mediaTimestamp);
                maximumMediaEnd = Math.Max(maximumMediaEnd, mediaTimestamp);
            }
        }

        return new FragmentScanResult(
            packetCount,
            maxPacketBytes,
            ScaleTimestamp(maximumMediaEnd, mediaTimescale, sampleRate));
    }

    private static void ReadFragmentRun(
        FileStream fs,
        BoxHeader trun,
        long baseDataOffset,
        ref long nextRunDataOffset,
        uint defaultSampleDuration,
        uint defaultSampleSize,
        uint mediaTimescale,
        int sampleRate,
        Span<AacPacketInfo> destination,
        bool writePackets,
        ref int packetCount,
        ref uint maxPacketBytes,
        ref long mediaTimestamp)
    {
        if (trun.PayloadLength < 8)
        {
            throw new InvalidDataException("Invalid fragmented AAC trun box.");
        }

        Span<byte> field = stackalloc byte[8];
        fs.Position = trun.PayloadOffset;
        fs.ReadExactly(field);
        var versionAndFlags = BinaryPrimitives.ReadUInt32BigEndian(field.Slice(0, 4));
        var flags = versionAndFlags & 0x00FF_FFFF;
        var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(field.Slice(4, 4));
        var bytesPerSample = 0;
        if ((flags & 0x000100) != 0) bytesPerSample += 4;
        if ((flags & 0x000200) != 0) bytesPerSample += 4;
        if ((flags & 0x000400) != 0) bytesPerSample += 4;
        if ((flags & 0x000800) != 0) bytesPerSample += 4;
        var optionalBytes = ((flags & 0x000001) != 0 ? 4 : 0) + ((flags & 0x000004) != 0 ? 4 : 0);
        if ((long)sampleCount * bytesPerSample + optionalBytes > trun.PayloadLength - 8)
        {
            throw new InvalidDataException("Fragmented AAC trun entries exceed the box length.");
        }

        if ((flags & 0x000001) != 0)
        {
            fs.ReadExactly(field.Slice(0, 4));
            var relativeOffset = BinaryPrimitives.ReadInt32BigEndian(field.Slice(0, 4));
            nextRunDataOffset = checked(baseDataOffset + relativeOffset);
        }
        else if (nextRunDataOffset < 0)
        {
            throw new NotSupportedException("The first AAC trun in a traf must provide data_offset.");
        }

        if ((flags & 0x000004) != 0)
        {
            fs.ReadExactly(field.Slice(0, 4));
        }

        for (var sample = 0U; sample < sampleCount; sample++)
        {
            var duration = defaultSampleDuration;
            var size = defaultSampleSize;
            if ((flags & 0x000100) != 0)
            {
                fs.ReadExactly(field.Slice(0, 4));
                duration = BinaryPrimitives.ReadUInt32BigEndian(field.Slice(0, 4));
            }

            if ((flags & 0x000200) != 0)
            {
                fs.ReadExactly(field.Slice(0, 4));
                size = BinaryPrimitives.ReadUInt32BigEndian(field.Slice(0, 4));
            }

            if ((flags & 0x000400) != 0)
            {
                fs.ReadExactly(field.Slice(0, 4));
            }

            if ((flags & 0x000800) != 0)
            {
                fs.ReadExactly(field.Slice(0, 4));
            }

            if (duration == 0 || size == 0 || size > int.MaxValue)
            {
                throw new InvalidDataException("Fragmented AAC sample is missing a valid duration or size.");
            }

            var sampleEndOffset = checked(nextRunDataOffset + size);
            if (nextRunDataOffset < 0 || sampleEndOffset > fs.Length)
            {
                throw new InvalidDataException("Fragmented AAC sample exceeds the file length.");
            }

            var startFrame = ScaleTimestamp(mediaTimestamp, mediaTimescale, sampleRate);
            mediaTimestamp = checked(mediaTimestamp + duration);
            var endFrame = ScaleTimestamp(mediaTimestamp, mediaTimescale, sampleRate);
            if (writePackets)
            {
                if ((uint)packetCount >= (uint)destination.Length)
                {
                    throw new InvalidDataException("Fragmented AAC packet count exceeds the allocated table.");
                }

                destination[packetCount] = new AacPacketInfo(
                    nextRunDataOffset,
                    checked((int)size),
                    startFrame,
                    checked((int)(endFrame - startFrame)));
            }

            packetCount = checked(packetCount + 1);
            maxPacketBytes = Math.Max(maxPacketBytes, size);
            nextRunDataOffset = sampleEndOffset;
        }
    }

    private static bool TryReadTrackId(FileStream fs, BoxHeader trak, out uint trackId)
    {
        trackId = 0;
        if (!TryFindChildBox(fs, trak, "tkhd", out var tkhd) || tkhd.PayloadLength < 16)
        {
            return false;
        }

        fs.Position = tkhd.PayloadOffset;
        var version = fs.ReadByte();
        var trackIdOffset = version switch { 0 => 12, 1 => 20, _ => -1 };
        if (trackIdOffset < 0 || tkhd.PayloadLength < trackIdOffset + 4)
        {
            return false;
        }

        Span<byte> value = stackalloc byte[4];
        fs.Position = tkhd.PayloadOffset + trackIdOffset;
        fs.ReadExactly(value);
        trackId = BinaryPrimitives.ReadUInt32BigEndian(value);
        return trackId != 0;
    }

    private static bool TryReadTrexDefaults(FileStream fs, BoxHeader moov, uint trackId, out TrexDefaults defaults)
    {
        defaults = default;
        if (!TryFindChildBox(fs, moov, "mvex", out var mvex))
        {
            return false;
        }

        var cursor = mvex.PayloadOffset;
        Span<byte> payload = stackalloc byte[24];
        while (cursor < mvex.EndOffset)
        {
            if (!TryReadBoxHeader(fs, cursor, mvex.EndOffset, out var child))
            {
                return false;
            }

            cursor = child.EndOffset;
            if (child.Type != "trex" || child.PayloadLength < payload.Length)
            {
                continue;
            }

            fs.Position = child.PayloadOffset;
            fs.ReadExactly(payload);
            if (BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4)) != trackId)
            {
                continue;
            }

            defaults = new TrexDefaults(
                BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(8, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(16, 4)));
            return true;
        }

        return false;
    }

    private static bool TryReadTfhd(FileStream fs, BoxHeader tfhd, out TfhdInfo info)
    {
        info = default;
        if (tfhd.PayloadLength < 8 || tfhd.PayloadLength > 36)
        {
            return false;
        }

        Span<byte> payload = stackalloc byte[36];
        var length = checked((int)tfhd.PayloadLength);
        fs.Position = tfhd.PayloadOffset;
        fs.ReadExactly(payload.Slice(0, length));
        var flags = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(0, 4)) & 0x00FF_FFFF;
        var trackId = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
        var offset = 8;
        long? baseDataOffset = null;
        uint? sampleDescriptionIndex = null;
        uint? defaultDuration = null;
        uint? defaultSize = null;

        if ((flags & 0x000001) != 0)
        {
            if (offset + 8 > length) return false;
            var value = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(offset, 8));
            if (value > long.MaxValue) return false;
            baseDataOffset = (long)value;
            offset += 8;
        }

        if ((flags & 0x000002) != 0)
        {
            if (offset + 4 > length) return false;
            sampleDescriptionIndex = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, 4));
            offset += 4;
        }

        if ((flags & 0x000008) != 0)
        {
            if (offset + 4 > length) return false;
            defaultDuration = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, 4));
            offset += 4;
        }

        if ((flags & 0x000010) != 0)
        {
            if (offset + 4 > length) return false;
            defaultSize = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(offset, 4));
            offset += 4;
        }

        if ((flags & 0x000020) != 0)
        {
            if (offset + 4 > length) return false;
        }

        info = new TfhdInfo(trackId, baseDataOffset, sampleDescriptionIndex, defaultDuration, defaultSize);
        return trackId != 0;
    }

    private static bool TryReadTfdt(FileStream fs, BoxHeader tfdt, out long mediaTimestamp)
    {
        mediaTimestamp = 0;
        if (tfdt.PayloadLength < 8)
        {
            return false;
        }

        Span<byte> payload = stackalloc byte[12];
        fs.Position = tfdt.PayloadOffset;
        var version = fs.ReadByte();
        var length = version == 1 ? 12 : version == 0 ? 8 : 0;
        if (length == 0 || tfdt.PayloadLength < length)
        {
            return false;
        }

        fs.Position = tfdt.PayloadOffset;
        fs.ReadExactly(payload.Slice(0, length));
        if (version == 0)
        {
            mediaTimestamp = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
            return true;
        }

        var value = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(4, 8));
        if (value > long.MaxValue)
        {
            return false;
        }

        mediaTimestamp = (long)value;
        return true;
    }


    private static bool TryReadBoxHeader(FileStream fs, long offset, long limit, out BoxHeader header)
    {
        header = default;

        if (offset < 0 || offset + 8 > limit)
        {
            return false;
        }

        Span<byte> basic = stackalloc byte[8];
        fs.Position = offset;
        fs.ReadExactly(basic);

        var size32 = BinaryPrimitives.ReadUInt32BigEndian(basic.Slice(0, 4));
        var type = ReadFourCc(basic.Slice(4, 4));

        long headerSize;
        long boxSize;

        if (size32 == 1)
        {
            if (offset + 16 > limit)
            {
                return false;
            }

            Span<byte> large = stackalloc byte[8];
            fs.ReadExactly(large);
            var size64 = BinaryPrimitives.ReadUInt64BigEndian(large);
            if (size64 < 16 || size64 > long.MaxValue)
            {
                return false;
            }

            headerSize = 16;
            boxSize = (long)size64;
        }
        else if (size32 == 0)
        {
            headerSize = 8;
            boxSize = limit - offset;
        }
        else
        {
            if (size32 < 8)
            {
                return false;
            }

            headerSize = 8;
            boxSize = size32;
        }

        var end = offset + boxSize;
        if (end > limit || end < offset + headerSize)
        {
            return false;
        }

        header = new BoxHeader(type, offset + headerSize, end, offset);
        return true;
    }

    private static string ReadFourCc(ReadOnlySpan<byte> bytes)
    {
        return Encoding.ASCII.GetString(bytes);
    }

    private ref struct AacConfigBitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bitOffset;

        internal AacConfigBitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bitOffset = 0;
        }

        internal int ReadBits(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            var result = 0;
            for (var i = 0; i < count; i++)
            {
                result <<= 1;
                if (_bitOffset < (_data.Length * 8))
                {
                    var byteIndex = _bitOffset >> 3;
                    var bitIndex = 7 - (_bitOffset & 7);
                    result |= (_data[byteIndex] >> bitIndex) & 1;
                }

                _bitOffset++;
            }

            return result;
        }
    }

    internal sealed class AacDecodeInfo : IDisposable
    {
        private NativeBufferOwner<AacPacketInfo>? _packets;
        private readonly int _packetCount;

        internal AacDecodeInfo(
            AacContainerInfo container,
            AacEsdsInfo esds,
            NativeBufferOwner<AacPacketInfo> packets,
            int packetCount)
        {
            Container = container;
            Esds = esds;
            _packets = packets;
            _packetCount = packetCount;
        }

        internal AacContainerInfo Container { get; }
        internal AacEsdsInfo Esds { get; }
        internal Span<AacPacketInfo> Packets => (_packets ?? throw new ObjectDisposedException(nameof(AacDecodeInfo)))
            .Span.Slice(0, _packetCount);

        public void Dispose()
        {
            _packets?.Dispose();
            _packets = null;
        }
    }


    private static StszSummary ReadStszSummary(FileStream fs, BoxHeader stsz)
    {
        if (stsz.PayloadLength < 12)
        {
            throw new InvalidDataException("Invalid stsz box.");
        }

        Span<byte> header = stackalloc byte[12];
        fs.Position = stsz.PayloadOffset;
        fs.ReadExactly(header);
        var sampleSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
        var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4));
        if (sampleSize != 0)
        {
            return new StszSummary(sampleCount, sampleSize);
        }

        if ((long)sampleCount * 4 > stsz.PayloadLength - 12)
        {
            throw new InvalidDataException("Invalid stsz packet sizes.");
        }

        uint maxPacketBytes = 0;
        Span<byte> sizeBytes = stackalloc byte[4];
        for (var i = 0U; i < sampleCount; i++)
        {
            fs.ReadExactly(sizeBytes);
            maxPacketBytes = Math.Max(maxPacketBytes, BinaryPrimitives.ReadUInt32BigEndian(sizeBytes));
        }

        return new StszSummary(sampleCount, maxPacketBytes);
    }

    internal readonly record struct AacContainerInfo(
        int Channels,
        int SampleRate,
        int BitsPerSample,
        uint PacketCount,
        uint MaxPacketBytes,
        long TotalPcmFrames,
        int PrimingFrames,
        int PaddingFrames,
        long TrimmedPcmFrames,
        string CodecFourCc);

    internal readonly record struct AacEsdsInfo(
        int ObjectTypeIndication,
        int StreamType,
        int BufferSizeDb,
        uint MaxBitrate,
        uint AvgBitrate,
        byte[] DecoderSpecificInfo,
        AacAudioSpecificConfig Config);

    internal readonly record struct AacAudioSpecificConfig(
        int AudioObjectType,
        int EffectiveAudioObjectType,
        int SamplingFrequencyIndex,
        int SampleRate,
        int ChannelConfiguration,
        bool HasSbrExtension,
        int ExtensionAudioObjectType,
        int ExtensionSamplingFrequencyIndex,
        int ExtensionSampleRate);

    internal readonly record struct AacPacketInfo(
        long Offset,
        int Size,
        long DecodeStartFrame = 0,
        int DurationFrames = 0);

    private readonly record struct StsdMp4aInfo(
        string CodecFourCc,
        int SampleEntryChannels,
        int SampleEntryBitsPerSample,
        int SampleEntrySampleRate,
        AacEsdsInfo Config);

    private sealed class StszInfo : IDisposable
    {
        private NativeBufferOwner<int>? _packetSizes;

        internal StszInfo(uint packetCount, uint maxPacketBytes, NativeBufferOwner<int> packetSizes)
        {
            PacketCount = packetCount;
            MaxPacketBytes = maxPacketBytes;
            _packetSizes = packetSizes;
        }

        internal uint PacketCount { get; }
        internal uint MaxPacketBytes { get; }
        internal Span<int> PacketSizes => (_packetSizes ?? throw new ObjectDisposedException(nameof(StszInfo)))
            .Span.Slice(0, checked((int)PacketCount));

        public void Dispose()
        {
            _packetSizes?.Dispose();
            _packetSizes = null;
        }
    }


    private readonly record struct StscInfo(StscEntry[] Entries);

    private readonly record struct StszSummary(uint PacketCount, uint MaxPacketBytes);

    private readonly record struct StscEntry(uint FirstChunk, uint SamplesPerChunk, uint SampleDescriptionIndex);

    private readonly record struct ItunesSmpbInfo(int PrimingFrames, int PaddingFrames, long TrimmedFrames);

    private readonly record struct TrimInfo(int PrimingFrames, int PaddingFrames, long TrimmedFrames);

    private readonly record struct TrexDefaults(uint SampleDescriptionIndex, uint DefaultSampleDuration, uint DefaultSampleSize);

    private readonly record struct TfhdInfo(
        uint TrackId,
        long? BaseDataOffset,
        uint? SampleDescriptionIndex,
        uint? DefaultSampleDuration,
        uint? DefaultSampleSize);

    private readonly record struct FragmentScanResult(int PacketCount, uint MaxPacketBytes, long TotalPcmFrames);

    private readonly record struct ProbeFormat(int SampleRate, int Channels, int FramesPerChannel);

    private readonly record struct BoxHeader(string Type, long PayloadOffset, long EndOffset, long StartOffset = -1)
    {
        internal long PayloadLength => EndOffset - PayloadOffset;
    }
}
