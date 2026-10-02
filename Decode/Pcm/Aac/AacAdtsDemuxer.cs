using No_Glass_Player.PlayerHost.Playback;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Aac;

internal static class AacAdtsDemuxer
{
    private static ReadOnlySpan<int> SamplingFrequencyTable =>
        [96_000, 88_200, 64_000, 48_000, 44_100, 32_000, 24_000, 22_050, 16_000, 12_000, 11_025, 8_000, 7_350];

    internal static bool IsAdtsFile(string path)
    {
        try
        {
            using var stream = Open(path);
            var offset = SkipLeadingId3v2(stream);
            return TryReadHeader(stream, offset, out _);
        }
        catch
        {
            return false;
        }
    }

    internal static AacMp4Demuxer.AacContainerInfo ReadInfo(string path)
    {
        using var info = ReadDecodeInfo(path);
        return info.Container;
    }

    internal static AacMp4Demuxer.AacDecodeInfo ReadDecodeInfo(string path)
    {
        using var stream = Open(path);
        var audioStart = SkipLeadingId3v2(stream);
        var summary = ScanFrames(stream, audioStart, Span<AacMp4Demuxer.AacPacketInfo>.Empty, writePackets: false);
        if (summary.PacketCount == 0)
        {
            throw new InvalidDataException("ADTS file contains no AAC frames.");
        }

        var packets = new NativeBufferOwner<AacMp4Demuxer.AacPacketInfo>(summary.PacketCount);
        try
        {
            packets.PreTouch();
            var populated = ScanFrames(stream, audioStart, packets.Span.Slice(0, summary.PacketCount), writePackets: true);
            if (populated != summary)
            {
                throw new InvalidDataException("ADTS frame table changed while it was being read.");
            }

            var asc = CreateAudioSpecificConfig(summary.AudioObjectType, summary.SamplingFrequencyIndex, summary.ChannelConfiguration);
            var output = ProbeOutputFormat(stream, packets.Span.Slice(0, summary.PacketCount), asc);
            var hasParametricStereo = summary.ChannelConfiguration == 1 && output.Channels == 2;
            if (output.Channels != summary.ChannelConfiguration && !hasParametricStereo)
            {
                throw new NotSupportedException(
                    $"ADTS AAC changed its channel layout during SBR/PS processing. header={summary.ChannelConfiguration} output={output.Channels}");
            }

            var hasSbr = output.SampleRate != summary.CoreSampleRate;
            if (output.SampleRate != summary.CoreSampleRate && output.SampleRate != summary.CoreSampleRate * 2)
            {
                throw new NotSupportedException(
                    $"ADTS AAC output rate is not supported. core={summary.CoreSampleRate} output={output.SampleRate}");
            }

            var expectedFrameLength = hasSbr ? 2048 : 1024;
            if (output.FramesPerChannel != expectedFrameLength)
            {
                throw new NotSupportedException(
                    $"ADTS AAC frame length is not supported. expected={expectedFrameLength} actual={output.FramesPerChannel}");
            }

            var packetSpan = packets.Span.Slice(0, summary.PacketCount);
            for (var index = 0; index < packetSpan.Length; index++)
            {
                packetSpan[index] = packetSpan[index] with
                {
                    DecodeStartFrame = checked((long)index * expectedFrameLength),
                    DurationFrames = expectedFrameLength
                };
            }

            var totalFrames = checked((long)summary.PacketCount * expectedFrameLength);
            var outputFrequencyIndex = FindSamplingFrequencyIndex(output.SampleRate);
            var config = new AacMp4Demuxer.AacAudioSpecificConfig(
                hasParametricStereo ? 29 : summary.AudioObjectType,
                summary.AudioObjectType,
                summary.SamplingFrequencyIndex,
                summary.CoreSampleRate,
                summary.ChannelConfiguration,
                hasSbr,
                hasSbr ? 5 : 0,
                hasSbr ? outputFrequencyIndex : -1,
                hasSbr ? output.SampleRate : 0);
            var esds = new AacMp4Demuxer.AacEsdsInfo(0x40, 5, 0, 0, 0, asc, config);
            var container = new AacMp4Demuxer.AacContainerInfo(
                output.Channels,
                output.SampleRate,
                16,
                checked((uint)summary.PacketCount),
                summary.MaxPayloadBytes,
                totalFrames,
                0,
                0,
                totalFrames,
                "ADTS");

            var result = new AacMp4Demuxer.AacDecodeInfo(container, esds, packets, summary.PacketCount);
            packets = null!;
            return result;
        }
        finally
        {
            packets?.Dispose();
        }
    }

    private static AdtsScanSummary ScanFrames(
        FileStream stream,
        long startOffset,
        Span<AacMp4Demuxer.AacPacketInfo> destination,
        bool writePackets)
    {
        var offset = startOffset;
        var packetCount = 0;
        uint maxPayloadBytes = 0;
        AdtsHeader first = default;
        while (offset < stream.Length)
        {
            if (!TryReadHeader(stream, offset, out var header))
            {
                if (packetCount > 0 && IsTrailingId3v1(stream, offset))
                {
                    offset = stream.Length;
                    break;
                }

                throw new InvalidDataException($"Invalid ADTS frame header at byte offset {offset}.");
            }

            if (packetCount == 0)
            {
                first = header;
            }
            else if (header.AudioObjectType != first.AudioObjectType ||
                     header.SamplingFrequencyIndex != first.SamplingFrequencyIndex ||
                     header.ChannelConfiguration != first.ChannelConfiguration)
            {
                throw new NotSupportedException("ADTS streams that change AAC profile, sample rate, or channel layout are not supported.");
            }

            if (header.RawDataBlockCount != 1)
            {
                throw new NotSupportedException("ADTS frames containing multiple AAC raw_data_blocks are not supported.");
            }

            if (writePackets)
            {
                if ((uint)packetCount >= (uint)destination.Length)
                {
                    throw new InvalidDataException("ADTS packet count exceeds the allocated table.");
                }

                destination[packetCount] = new AacMp4Demuxer.AacPacketInfo(
                    checked(offset + header.HeaderLength),
                    header.PayloadLength);
            }

            packetCount = checked(packetCount + 1);
            maxPayloadBytes = Math.Max(maxPayloadBytes, checked((uint)header.PayloadLength));
            offset = checked(offset + header.FrameLength);
        }

        if (offset != stream.Length)
        {
            throw new InvalidDataException("ADTS frame table does not cover the complete file.");
        }

        return new AdtsScanSummary(
            packetCount,
            maxPayloadBytes,
            first.AudioObjectType,
            first.SamplingFrequencyIndex,
            first.SampleRate,
            first.ChannelConfiguration);
    }

    private static ProbeFormat ProbeOutputFormat(
        FileStream stream,
        Span<AacMp4Demuxer.AacPacketInfo> packets,
        byte[] asc)
    {
        var decoder = new SharpJaad.AAC.Decoder(asc);
        var sampleBuffer = new SharpJaad.AAC.SampleBuffer();
        sampleBuffer.SetBigEndian(false);
        var packetBuffer = new byte[packets[0].Size];
        stream.Position = packets[0].Offset;
        stream.ReadExactly(packetBuffer);
        decoder.DecodeFrame(packetBuffer, sampleBuffer);
        if (sampleBuffer.SampleRate <= 0 || sampleBuffer.Channels <= 0 || sampleBuffer.FramesPerChannel <= 0)
        {
            throw new InvalidDataException("JAAD produced no output while probing the first ADTS frame.");
        }

        return new ProbeFormat(sampleBuffer.SampleRate, sampleBuffer.Channels, sampleBuffer.FramesPerChannel);
    }

    private static bool TryReadHeader(FileStream stream, long offset, out AdtsHeader header)
    {
        header = default;
        if (offset < 0 || offset > stream.Length - 7)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[7];
        stream.Position = offset;
        stream.ReadExactly(bytes);
        if (bytes[0] != 0xFF || (bytes[1] & 0xF0) != 0xF0 || (bytes[1] & 0x06) != 0)
        {
            return false;
        }

        var samplingFrequencyIndex = (bytes[2] >> 2) & 0x0F;
        if ((uint)samplingFrequencyIndex >= (uint)SamplingFrequencyTable.Length)
        {
            return false;
        }

        var channelConfiguration = ((bytes[2] & 0x01) << 2) | ((bytes[3] >> 6) & 0x03);
        if (channelConfiguration is < 1 or > 2)
        {
            return false;
        }

        var headerLength = (bytes[1] & 0x01) != 0 ? 7 : 9;
        var frameLength = ((bytes[3] & 0x03) << 11) | (bytes[4] << 3) | (bytes[5] >> 5);
        if (frameLength <= headerLength || offset > stream.Length - frameLength)
        {
            return false;
        }

        header = new AdtsHeader(
            ((bytes[2] >> 6) & 0x03) + 1,
            samplingFrequencyIndex,
            SamplingFrequencyTable[samplingFrequencyIndex],
            channelConfiguration,
            headerLength,
            frameLength,
            frameLength - headerLength,
            (bytes[6] & 0x03) + 1);
        return true;
    }

    private static long SkipLeadingId3v2(FileStream stream)
    {
        long offset = 0;
        Span<byte> header = stackalloc byte[10];
        while (offset <= stream.Length - header.Length)
        {
            stream.Position = offset;
            stream.ReadExactly(header);
            if (!header.Slice(0, 3).SequenceEqual("ID3"u8))
            {
                break;
            }

            var version = header[3];
            if (version is < 2 or > 4)
            {
                throw new InvalidDataException($"Unsupported ID3v2 version before ADTS audio. version=2.{version}");
            }

            if ((header[6] | header[7] | header[8] | header[9]) >= 0x80)
            {
                throw new InvalidDataException("Invalid ID3v2 synchsafe length before ADTS audio.");
            }

            var payloadLength = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
            var footerLength = version == 4 && (header[5] & 0x10) != 0 ? 10 : 0;
            offset = checked(offset + 10L + payloadLength + footerLength);
            if (offset > stream.Length)
            {
                throw new InvalidDataException("ID3v2 tag exceeds the ADTS file length.");
            }
        }

        return offset;
    }

    private static bool IsTrailingId3v1(FileStream stream, long offset)
    {
        if (stream.Length - offset != 128)
        {
            return false;
        }

        Span<byte> tag = stackalloc byte[3];
        stream.Position = offset;
        stream.ReadExactly(tag);
        return tag.SequenceEqual("TAG"u8);
    }

    private static byte[] CreateAudioSpecificConfig(int audioObjectType, int samplingFrequencyIndex, int channelConfiguration)
    {
        if (audioObjectType is < 1 or > 4)
        {
            throw new NotSupportedException($"ADTS AAC profile is not supported. audioObjectType={audioObjectType}");
        }

        var bits = (audioObjectType << 11) | (samplingFrequencyIndex << 7) | (channelConfiguration << 3);
        return [(byte)(bits >> 8), (byte)bits];
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

    private static FileStream Open(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

    private readonly record struct AdtsHeader(
        int AudioObjectType,
        int SamplingFrequencyIndex,
        int SampleRate,
        int ChannelConfiguration,
        int HeaderLength,
        int FrameLength,
        int PayloadLength,
        int RawDataBlockCount);

    private readonly record struct AdtsScanSummary(
        int PacketCount,
        uint MaxPayloadBytes,
        int AudioObjectType,
        int SamplingFrequencyIndex,
        int CoreSampleRate,
        int ChannelConfiguration);

    private readonly record struct ProbeFormat(int SampleRate, int Channels, int FramesPerChannel);
}
