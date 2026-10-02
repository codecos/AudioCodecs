using System.Buffers.Binary;
using System.Text;
using No_Glass_Player.AudioDecodeCore.Decode.Pcm.Opus;

internal enum OggAudioCodecKind
{
    Unknown,
    Vorbis,
    Opus
}

internal enum OpusCodingMode
{
    Silk,
    Hybrid,
    Celt
}

internal readonly record struct OpusStreamInfo(
    int Channels,
    uint InputSampleRate,
    int PreSkip,
    short OutputGainQ8,
    int MappingFamily,
    long TotalPcmFrames,
    int AudioPacketCount,
    int MaxPacketBytes,
    int PageCount,
    uint SerialNumber,
    string Vendor);

internal readonly record struct OpusPacketLayout(
    int Configuration,
    bool Stereo,
    OpusCodingMode Mode,
    int FrameSamples,
    int FrameCount,
    int PayloadOffset,
    int PayloadBytes,
    bool HasPadding);

internal readonly record struct OpusFrameSlice(int Offset, int Length);

internal static class OpusManualDecoder
{
    private const int OggPageHeaderBytes = 27;
    private const int MaximumOggSegments = 255;
    // An Opus packet can span multiple Ogg pages. Keep this bounded, but do not
    // mistake the maximum payload of one page for the maximum packet size.
    private const int MaximumPacketBytes = 1024 * 1024;
    private const int MaximumOpusFramesPerPacket = 48;
    private static readonly byte[] OggSignature = "OggS"u8.ToArray();
    private static readonly byte[] OpusHeadSignature = "OpusHead"u8.ToArray();
    private static readonly byte[] OpusTagsSignature = "OpusTags"u8.ToArray();
    private static readonly byte[] VorbisSignature = "vorbis"u8.ToArray();

    internal static OggAudioCodecKind IdentifyFirstPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length >= 7
            && packet[0] == 1
            && packet.Slice(1, VorbisSignature.Length).SequenceEqual(VorbisSignature))
        {
            return OggAudioCodecKind.Vorbis;
        }

        if (packet.Length >= OpusHeadSignature.Length
            && packet[..OpusHeadSignature.Length].SequenceEqual(OpusHeadSignature))
        {
            return OggAudioCodecKind.Opus;
        }

        return OggAudioCodecKind.Unknown;
    }

    internal static bool TryReadInfo(string path, out OpusStreamInfo info, out string error)
    {
        try
        {
            info = ReadInfo(path);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            info = default;
            error = ex.Message;
            return false;
        }
    }

    internal static OpusStreamInfo ReadInfo(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        var pageHeader = new byte[OggPageHeaderBytes + MaximumOggSegments];
        var payload = new byte[MaximumPacketBytes];
        var packet = new byte[MaximumPacketBytes];

        var codec = OggAudioCodecKind.Unknown;
        var header = default(OpusStreamInfo);
        var haveHeader = false;
        var haveTags = false;
        var packetLength = 0;
        var pageCount = 0;
        var audioPacketCount = 0;
        var maxPacketBytes = 0;
        var serial = 0u;
        var haveSerial = false;
        var lastGranule = -1L;
        var previousSequence = -1u;

        while (TryReadPage(stream, pageHeader, payload, out var page))
        {
            pageCount++;
            if (!haveSerial)
            {
                serial = page.SerialNumber;
                haveSerial = true;
            }
            else if (serial != page.SerialNumber)
            {
                throw new InvalidDataException("Ogg contains more than one logical stream.");
            }

            if (previousSequence >= 0 && page.SequenceNumber != previousSequence + 1)
            {
                throw new InvalidDataException("Ogg page sequence is not consecutive.");
            }

            var continued = (page.HeaderType & 0x01) != 0;
            if (packetLength != 0 && !continued)
            {
                throw new InvalidDataException("Ogg packet continuation flag is missing.");
            }

            if (packetLength == 0 && continued && page.SegmentCount != 0)
            {
                throw new InvalidDataException("Ogg packet continuation flag has no preceding packet.");
            }

            previousSequence = page.SequenceNumber;
            if (page.GranulePosition >= 0)
            {
                lastGranule = page.GranulePosition;
            }

            var segmentOffset = 0;
            for (var i = 0; i < page.SegmentCount; i++)
            {
                var segmentLength = page.SegmentTable[i];
                if (packetLength + segmentLength > packet.Length)
                {
                    throw new InvalidDataException("Ogg packet exceeds the supported size.");
                }

                payload.AsSpan(segmentOffset, segmentLength).CopyTo(packet.AsSpan(packetLength));
                packetLength += segmentLength;
                segmentOffset += segmentLength;

                if (segmentLength == 255)
                {
                    continue;
                }

                var completePacket = packet.AsSpan(0, packetLength);
                if (!haveHeader)
                {
                    codec = IdentifyFirstPacket(completePacket);
                    if (codec != OggAudioCodecKind.Opus)
                    {
                        throw new InvalidDataException(codec == OggAudioCodecKind.Vorbis
                            ? "Ogg stream is Vorbis, not Opus."
                            : "Ogg stream is not an Opus logical stream.");
                    }

                    header = ParseOpusHead(completePacket);
                    haveHeader = true;
                }
                else if (!haveTags)
                {
                    ParseOpusTags(completePacket, out var vendor);
                    header = header with { Vendor = vendor };
                    haveTags = true;
                }
                else
                {
                    if (!TryParsePacketLayout(completePacket, out var layout, out var packetError))
                    {
                        throw new InvalidDataException($"Invalid Opus audio packet {audioPacketCount}: {packetError}");
                    }

                    audioPacketCount++;
                    maxPacketBytes = Math.Max(maxPacketBytes, completePacket.Length);
                    _ = layout;
                }

                packetLength = 0;
            }
        }

        if (!haveHeader || !haveTags || pageCount == 0)
        {
            throw new InvalidDataException("Ogg Opus stream is missing OpusHead or OpusTags.");
        }

        var totalFrames = lastGranule < 0
            ? 0
            : Math.Max(0, lastGranule - header.PreSkip);
        return header with
        {
            TotalPcmFrames = totalFrames,
            AudioPacketCount = audioPacketCount,
            MaxPacketBytes = maxPacketBytes,
            PageCount = pageCount,
            SerialNumber = serial
        };
    }

    internal static List<byte[]> ReadAudioPackets(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        var pageHeader = new byte[OggPageHeaderBytes + MaximumOggSegments];
        var payload = new byte[MaximumPacketBytes];
        var packet = new byte[MaximumPacketBytes];
        var packets = new List<byte[]>();
        var packetLength = 0;
        var logicalPacketIndex = 0;

        while (TryReadPage(stream, pageHeader, payload, out var page))
        {
            var segmentOffset = 0;
            for (var i = 0; i < page.SegmentCount; i++)
            {
                var segmentLength = page.SegmentTable[i];
                if (packetLength + segmentLength > packet.Length)
                {
                    throw new InvalidDataException("Ogg packet exceeds the supported size.");
                }

                payload.AsSpan(segmentOffset, segmentLength).CopyTo(packet.AsSpan(packetLength));
                packetLength += segmentLength;
                segmentOffset += segmentLength;
                if (segmentLength == 255)
                {
                    continue;
                }

                if (logicalPacketIndex >= 2)
                {
                    packets.Add(packet.AsSpan(0, packetLength).ToArray());
                }

                logicalPacketIndex++;
                packetLength = 0;
            }
        }

        if (packetLength != 0)
        {
            throw new InvalidDataException("Ogg stream ends with an incomplete packet.");
        }

        return packets;
    }

    internal static bool TryParsePacketLayout(
        ReadOnlySpan<byte> packet,
        out OpusPacketLayout layout,
        out string error)
    {
        layout = default;
        error = string.Empty;
        if (packet.Length < 1)
        {
            error = "packet is empty";
            return false;
        }

        var toc = packet[0];
        var configuration = toc >> 3;
        var stereo = (toc & 0x04) != 0;
        var code = toc & 0x03;
        var mode = configuration switch
        {
            <= 11 => OpusCodingMode.Silk,
            <= 15 => OpusCodingMode.Hybrid,
            _ => OpusCodingMode.Celt
        };
        var frameSamples = ResolveFrameSamples(configuration);
        var offset = 1;
        var payloadEnd = packet.Length;
        var frameCount = 1;
        var hasPadding = false;
        var firstLengthForValidation = -1;

        if (frameSamples <= 0 || frameSamples * 1 > 5760)
        {
            error = "packet frame duration is outside the 120 ms limit";
            return false;
        }

        if (code == 1)
        {
            frameCount = 2;
            if (((packet.Length - 1) & 1) != 0)
            {
                error = "code 1 packet length must be odd";
                return false;
            }
        }
        else if (code == 2)
        {
            frameCount = 2;
            if (!TryReadFrameLength(packet, ref offset, out var firstLength)
                || firstLength > packet.Length - offset)
            {
                error = "code 2 first frame length is invalid";
                return false;
            }

            firstLengthForValidation = firstLength;
            offset += firstLength;
        }
        else if (code == 3)
        {
            if (packet.Length < 2)
            {
                error = "code 3 packet has no frame count byte";
                return false;
            }

            var frameCountByte = packet[offset++];
            // RFC 6716 diagrams number bit 0 as the most significant bit:
            // v is 0x80, p is 0x40, and M occupies the low six bits.
            hasPadding = (frameCountByte & 0x40) != 0;
            frameCount = frameCountByte & 0x3F;
            if (frameCount is < 1 or > MaximumOpusFramesPerPacket)
            {
                error = "code 3 frame count is outside 1..48";
                return false;
            }

            if (frameCount * frameSamples > 5760)
            {
                error = $"code 3 packet duration exceeds 120 ms (frames={frameCount}, frame_samples={frameSamples})";
                return false;
            }

            if (hasPadding)
            {
                var paddingBytes = 0;
                byte paddingByte;
                do
                {
                    if ((uint)offset >= (uint)packet.Length)
                    {
                        error = "padding length is truncated";
                        return false;
                    }

                    paddingByte = packet[offset++];
                    paddingBytes = checked(paddingBytes + (paddingByte == 255 ? 254 : paddingByte));
                } while (paddingByte == 255);

                if (paddingBytes > packet.Length - offset)
                {
                    error = "padding exceeds packet length";
                    return false;
                }

                payloadEnd -= paddingBytes;
            }

            var vbr = (frameCountByte & 0x80) != 0;
            if (vbr)
            {
                var framePayloadBytes = 0;
                for (var i = 0; i < frameCount - 1; i++)
                {
                    var lengthOffset = offset;
                    if (!TryReadFrameLength(packet, ref offset, out var frameLength))
                    {
                        error = $"code 3 VBR frame length {i} is truncated at offset {lengthOffset}";
                        return false;
                    }

                    if (frameLength > payloadEnd - offset
                        || framePayloadBytes > payloadEnd - offset - frameLength)
                    {
                        error = $"code 3 VBR frame length {i} is invalid (value={frameLength}, offset={lengthOffset}, remaining={packet.Length - offset})";
                        return false;
                    }

                    framePayloadBytes += frameLength;
                }
            }
            else
            {
                var cbrPayloadBytes = payloadEnd - offset;
                if (cbrPayloadBytes < 0 || cbrPayloadBytes % frameCount != 0)
                {
                    error = $"code 3 CBR payload is not divisible by frame count (payload={cbrPayloadBytes}, frames={frameCount})";
                    return false;
                }

                if (cbrPayloadBytes / frameCount > 1275)
                {
                    error = "code 3 CBR frame exceeds 1275 bytes";
                    return false;
                }
            }
        }

        var payloadBytes = payloadEnd - offset;
        if (payloadBytes < 0)
        {
            error = $"packet has invalid payload (toc=0x{toc:X2}, code={code}, config={configuration}, length={packet.Length}, offset={offset})";
            return false;
        }

        if ((code == 0 && payloadBytes > 1275)
            || (code == 1 && payloadBytes / 2 > 1275)
            || (code == 2 && (firstLengthForValidation > 1275 || payloadBytes - firstLengthForValidation > 1275)))
        {
            error = "implicit frame length exceeds 1275 bytes";
            return false;
        }

        layout = new OpusPacketLayout(
            configuration,
            stereo,
            mode,
            frameSamples,
            frameCount,
            offset,
            payloadBytes,
            hasPadding);
        return true;
    }

    internal static bool TryGetFrameSlices(
        ReadOnlySpan<byte> packet,
        Span<OpusFrameSlice> frames,
        out int frameCount,
        out OpusPacketLayout layout,
        out string error)
    {
        frameCount = 0;
        layout = default;
        error = string.Empty;
        if (!TryParsePacketLayout(packet, out layout, out error))
        {
            return false;
        }

        var toc = packet[0];
        var code = toc & 0x03;
        var count = layout.FrameCount;
        if (frames.Length < count)
        {
            error = $"frame destination is too small (required={count})";
            return false;
        }

        var payloadEnd = packet.Length;
        var offset = layout.PayloadOffset;
        if (layout.HasPadding)
        {
            var frameCountByte = packet[1];
            var paddingOffset = 2;
            var paddingBytes = 0;
            byte paddingByte;
            do
            {
                paddingByte = packet[paddingOffset++];
                paddingBytes = checked(paddingBytes + (paddingByte == 255 ? 254 : paddingByte));
            } while (paddingByte == 255);

            payloadEnd -= paddingBytes;
            _ = frameCountByte;
        }

        if (code == 0)
        {
            frames[0] = new OpusFrameSlice(offset, payloadEnd - offset);
            frameCount = 1;
            return true;
        }

        if (code == 1)
        {
            var length = (payloadEnd - offset) / 2;
            frames[0] = new OpusFrameSlice(offset, length);
            frames[1] = new OpusFrameSlice(offset + length, length);
            frameCount = 2;
            return true;
        }

        if (code == 2)
        {
            var lengthCursor = 1;
            if (!TryReadFrameLength(packet, ref lengthCursor, out var firstLength))
            {
                error = "code 2 first frame length is truncated";
                return false;
            }

            var firstOffset = lengthCursor;
            var secondOffset = firstOffset + firstLength;
            if (secondOffset > payloadEnd)
            {
                error = "code 2 first frame exceeds packet payload";
                return false;
            }

            frames[0] = new OpusFrameSlice(firstOffset, firstLength);
            frames[1] = new OpusFrameSlice(secondOffset, payloadEnd - secondOffset);
            frameCount = 2;
            return true;
        }

        var frameCountByteForVbr = packet[1];
        var vbr = (frameCountByteForVbr & 0x80) != 0;
        if (!vbr)
        {
            var length = (payloadEnd - offset) / count;
            for (var i = 0; i < count; i++)
            {
                frames[i] = new OpusFrameSlice(offset + i * length, length);
            }

            frameCount = count;
            return true;
        }

        var lengthCursorForVbr = offset;
        Span<int> frameLengths = stackalloc int[count - 1];
        var usedBytes = 0;
        for (var i = 0; i < count - 1; i++)
        {
            if (!TryReadFrameLength(packet, ref lengthCursorForVbr, out var length))
            {
                error = $"code 3 VBR frame length {i} is truncated";
                return false;
            }

            frameLengths[i] = length;
            usedBytes = checked(usedBytes + length);
        }

        var frameDataOffset = lengthCursorForVbr;
        for (var i = 0; i < count - 1; i++)
        {
            var length = frameLengths[i];
            if (length > payloadEnd - frameDataOffset)
            {
                error = "code 3 VBR frame data exceeds packet payload";
                return false;
            }

            frames[i] = new OpusFrameSlice(frameDataOffset, length);
            frameDataOffset += length;
        }

        var finalLength = payloadEnd - frameDataOffset;
        if (finalLength < 0 || usedBytes + finalLength > payloadEnd - offset)
        {
            error = "code 3 VBR frame data exceeds packet payload";
            return false;
        }

        frames[count - 1] = new OpusFrameSlice(frameDataOffset, finalLength);
        frameCount = count;
        return true;
    }

    private static OpusStreamInfo ParseOpusHead(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 19 || !packet[..8].SequenceEqual(OpusHeadSignature))
        {
            throw new InvalidDataException("Invalid OpusHead packet.");
        }

        var version = packet[8];
        var channels = packet[9];
        var preSkip = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(10, 2));
        var inputRate = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(12, 4));
        var gain = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(16, 2));
        var mappingFamily = packet[18];
        if (version != 1 || channels is < 1 or > 2 || mappingFamily != 0 || inputRate == 0)
        {
            throw new InvalidDataException("Only Opus mapping family 0 mono/stereo streams are supported.");
        }

        return new OpusStreamInfo(channels, inputRate, preSkip, gain, mappingFamily, 0, 0, 0, 0, 0, string.Empty);
    }

    private static void ParseOpusTags(ReadOnlySpan<byte> packet, out string vendor)
    {
        vendor = string.Empty;
        if (packet.Length < 16 || !packet[..8].SequenceEqual(OpusTagsSignature))
        {
            throw new InvalidDataException("Invalid OpusTags packet.");
        }

        var offset = 8;
        var vendorLength = ReadBoundedStringLength(packet, ref offset, "vendor");
        vendor = Encoding.UTF8.GetString(packet.Slice(offset, vendorLength));
        offset += vendorLength;
        var commentCount = ReadUInt32(packet, ref offset, "comment count");
        for (var i = 0u; i < commentCount; i++)
        {
            var commentLength = ReadBoundedStringLength(packet, ref offset, "comment");
            offset += commentLength;
        }
    }

    private static int ReadBoundedStringLength(ReadOnlySpan<byte> packet, ref int offset, string field)
    {
        var length = ReadUInt32(packet, ref offset, field);
        if (length > int.MaxValue || length > (uint)(packet.Length - offset))
        {
            throw new InvalidDataException($"OpusTags {field} is truncated.");
        }

        return (int)length;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> packet, ref int offset, string field)
    {
        if (packet.Length - offset < 4)
        {
            throw new InvalidDataException($"OpusTags {field} is truncated.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static int ResolveFrameSamples(int configuration)
    {
        var durationMs = configuration switch
        {
            <= 11 => (configuration & 3) switch { 0 => 10, 1 => 20, 2 => 40, _ => 60 },
            12 or 14 => 10,
            13 or 15 => 20,
            _ => ((configuration - 16) / 4) switch { 0 => 2.5, 1 => 5, 2 => 10, _ => 20 }
        };
        return (int)(durationMs * 48);
    }

    private static bool TryReadFrameLength(ReadOnlySpan<byte> packet, ref int offset, out int length)
    {
        length = 0;
        if ((uint)offset >= (uint)packet.Length)
        {
            return false;
        }

        var first = packet[offset++];
        if (first < 252)
        {
            length = first;
            return true;
        }

        if ((uint)offset >= (uint)packet.Length)
        {
            return false;
        }

        // RFC 6716: the second byte carries the high part; values 252..255
        // therefore cover the full 252..1275-byte frame-length range.
        length = packet[offset++] * 4 + first;
        return true;
    }

    private static bool TryReadPage(
        FileStream stream,
        byte[] pageHeader,
        byte[] payload,
        out OggPage page)
    {
        page = default;
        var firstRead = ReadAtMost(stream, pageHeader.AsSpan(0, OggPageHeaderBytes));
        if (firstRead == 0)
        {
            return false;
        }

        if (firstRead != OggPageHeaderBytes || !pageHeader.AsSpan(0, 4).SequenceEqual(OggSignature))
        {
            throw new InvalidDataException("Invalid Ogg page header.");
        }

        if (pageHeader[4] != 0)
        {
            throw new InvalidDataException("Unsupported Ogg bitstream version.");
        }

        var segmentCount = pageHeader[26];
        if (ReadAtMost(stream, pageHeader.AsSpan(OggPageHeaderBytes, segmentCount)) != segmentCount)
        {
            throw new InvalidDataException("Truncated Ogg segment table.");
        }

        var payloadLength = 0;
        for (var i = 0; i < segmentCount; i++)
        {
            payloadLength = checked(payloadLength + pageHeader[OggPageHeaderBytes + i]);
        }

        if (payloadLength > payload.Length || ReadAtMost(stream, payload.AsSpan(0, payloadLength)) != payloadLength)
        {
            throw new InvalidDataException("Truncated Ogg page payload.");
        }

        var granule = BinaryPrimitives.ReadInt64LittleEndian(pageHeader.AsSpan(6, 8));
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(pageHeader.AsSpan(14, 4));
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(pageHeader.AsSpan(18, 4));
        var segmentTable = new byte[segmentCount];
        pageHeader.AsSpan(OggPageHeaderBytes, segmentCount).CopyTo(segmentTable);
        page = new OggPage(pageHeader[5], granule, serial, sequence, segmentCount, segmentTable);
        return true;
    }

    private static int ReadAtMost(Stream stream, Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            var read = stream.Read(destination[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private readonly record struct OggPage(
        byte HeaderType,
        long GranulePosition,
        uint SerialNumber,
        uint SequenceNumber,
        int SegmentCount,
        byte[] SegmentTable);
}
