using System.Buffers.Binary;
using No_Glass_Player.PlayerHost.Playback;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Opus;

internal sealed class OpusReaderWorkspace : IDisposable
{
    private const int PageHeaderBytes = 27;
    private const int MaximumSegments = 255;
    private const int MaximumPagePayloadBytes = 255 * 255;
    private const int MaximumPacketBytes = 1024 * 1024;

    private NativeBufferOwner<short>? _decoded;
    private NativeBufferOwner<int>? _pcm32;
    private NativeBufferOwner<byte>? _pagePayload;
    private NativeBufferOwner<byte>? _packetAssembly;
    private NativeBufferOwner<byte>? _segmentTable;
    private NativeBufferOwner<long>? _seekPageOffsets;
    private NativeBufferOwner<long>? _seekPageStartFrames;
    private FileStream? _stream;
    private int _pagePayloadLength;
    private int _pagePayloadOffset;
    private int _segmentCount;
    private int _segmentIndex;
    private int _packetLength;
    private uint _serial;
    private uint _previousSequence;
    private bool _haveSerial;
    private bool _havePreviousSequence;
    private bool _endOfStream;
    private int _headerPackets;
    private int _seekPageCount;
    private uint _seekSerial;

    internal Span<short> Decoded => GetSpan(_decoded, "Opus decoded samples");
    internal Span<int> Pcm32 => GetSpan(_pcm32, "Opus PCM32 output");

    internal void EnsureCapacity(int sampleCount)
    {
        EnsureOwner(ref _decoded, sampleCount);
        EnsureOwner(ref _pcm32, sampleCount);
    }

    internal void Open(string path)
    {
        CloseStream();
        EnsureOwner(ref _pagePayload, MaximumPagePayloadBytes);
        EnsureOwner(ref _packetAssembly, MaximumPacketBytes);
        EnsureOwner(ref _segmentTable, MaximumSegments);
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        ResetCursorState();
        if (!TryReadPacket(out var head) || OpusManualDecoder.IdentifyFirstPacket(head) != OggAudioCodecKind.Opus
            || !TryReadPacket(out var tags) || tags.Length < 8 || !tags[..8].SequenceEqual("OpusTags"u8))
        {
            CloseStream();
            throw new InvalidDataException("Ogg stream is missing OpusHead or OpusTags.");
        }

        _headerPackets = 2;
        BuildSeekIndex(path);
    }

    internal long SeekToFrame(long targetFrame, int prerollFrames)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Opus cursor is not open.");
        }

        var desiredStart = Math.Max(0, targetFrame - Math.Max(0, prerollFrames));
        var selected = 0;
        for (var i = 0; i < _seekPageCount; i++)
        {
            if (_seekPageStartFrames![i] > desiredStart)
            {
                break;
            }

            selected = i;
        }

        _stream.Position = _seekPageOffsets![selected];
        ResetCursorState();
        _serial = _seekSerial;
        _haveSerial = true;
        _headerPackets = 2;
        return _seekPageStartFrames[selected];
    }

    internal void ResetCursor()
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Opus cursor is not open.");
        }

        _stream.Position = 0;
        ResetCursorState();
        if (!TryReadPacket(out var head) || OpusManualDecoder.IdentifyFirstPacket(head) != OggAudioCodecKind.Opus
            || !TryReadPacket(out var tags) || tags.Length < 8 || !tags[..8].SequenceEqual("OpusTags"u8))
        {
            throw new InvalidDataException("Ogg stream is missing OpusHead or OpusTags.");
        }

        _headerPackets = 2;
    }

    internal bool TryReadAudioPacket(out ReadOnlySpan<byte> packet)
    {
        while (TryReadPacket(out packet))
        {
            if (_headerPackets < 2)
            {
                _headerPackets++;
                continue;
            }

            return true;
        }

        packet = default;
        return false;
    }

    public void Dispose()
    {
        CloseStream();
        _pcm32?.Dispose();
        _pcm32 = null;
        _decoded?.Dispose();
        _decoded = null;
        _segmentTable?.Dispose();
        _segmentTable = null;
        _packetAssembly?.Dispose();
        _packetAssembly = null;
        _pagePayload?.Dispose();
        _pagePayload = null;
        _seekPageStartFrames?.Dispose();
        _seekPageStartFrames = null;
        _seekPageOffsets?.Dispose();
        _seekPageOffsets = null;
    }

    private bool TryReadPacket(out ReadOnlySpan<byte> packet)
    {
        packet = default;
        while (!_endOfStream)
        {
            if (_segmentIndex >= _segmentCount && !TryReadPage())
            {
                break;
            }

            var segmentLength = _segmentTable![_segmentIndex++];
            _pagePayload!.AsSpan(_pagePayloadOffset, segmentLength)
                .CopyTo(_packetAssembly!.AsSpan(_packetLength, segmentLength));
            _pagePayloadOffset += segmentLength;
            _packetLength += segmentLength;
            if (segmentLength == 255)
            {
                continue;
            }

            packet = _packetAssembly.AsSpan(0, _packetLength);
            _packetLength = 0;
            return true;
        }

        if (_packetLength != 0)
        {
            throw new InvalidDataException("Ogg stream ends with an incomplete packet.");
        }

        return false;
    }

    private bool TryReadPage()
    {
        if (_stream is null || _endOfStream)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[PageHeaderBytes];
        var headerOffset = _stream.Position;
        if (!TryReadExact(_stream, header))
        {
            if (_stream.Position != headerOffset)
            {
                throw new InvalidDataException("Ogg stream ends with an incomplete page header.");
            }

            _endOfStream = true;
            return false;
        }

        if (!header[..4].SequenceEqual("OggS"u8) || header[4] != 0)
        {
            throw new InvalidDataException("Invalid Ogg page header.");
        }

        var headerType = header[5];
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]);
        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(header[18..22]);
        if (!_haveSerial)
        {
            _serial = serial;
            _haveSerial = true;
        }
        else if (serial != _serial)
        {
            throw new InvalidDataException("Ogg contains more than one logical stream.");
        }

        if (_havePreviousSequence && sequence != _previousSequence + 1)
        {
            throw new InvalidDataException("Ogg page sequence is not consecutive.");
        }

        if ((_packetLength != 0) != ((headerType & 1) != 0))
        {
            throw new InvalidDataException("Ogg packet continuation flag is inconsistent.");
        }

        _segmentCount = header[26];
        _segmentIndex = 0;
        _pagePayloadOffset = 0;
        _pagePayloadLength = 0;
        if (!TryReadExact(_stream, _segmentTable!.AsSpan(0, _segmentCount)))
        {
            throw new InvalidDataException("Ogg page has an incomplete segment table.");
        }

        for (var i = 0; i < _segmentCount; i++)
        {
            _pagePayloadLength += _segmentTable[i];
        }

        _stream.ReadExactly(_pagePayload!.AsSpan(0, _pagePayloadLength));
        _previousSequence = sequence;
        _havePreviousSequence = true;
        return true;
    }

    private void ResetCursorState()
    {
        _pagePayloadLength = 0;
        _pagePayloadOffset = 0;
        _segmentCount = 0;
        _segmentIndex = 0;
        _packetLength = 0;
        _serial = 0;
        _previousSequence = 0;
        _haveSerial = false;
        _havePreviousSequence = false;
        _endOfStream = false;
        _headerPackets = 0;
    }

    private void BuildSeekIndex(string path)
    {
        _seekPageCount = 0;
        _seekSerial = 0;
        using var scan = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        Span<byte> header = stackalloc byte[PageHeaderBytes];
        uint previousSequence = 0;
        var haveSerial = false;
        var havePreviousSequence = false;
        long lastGranule = 0;

        while (scan.Position < scan.Length)
        {
            var pageOffset = scan.Position;
            if (!TryReadExact(scan, header))
            {
                throw new InvalidDataException("Ogg stream ends with an incomplete page header.");
            }

            if (!header[..4].SequenceEqual("OggS"u8) || header[4] != 0)
            {
                throw new InvalidDataException("Invalid Ogg page header.");
            }

            var headerType = header[5];
            var granule = BinaryPrimitives.ReadInt64LittleEndian(header[6..14]);
            var serial = BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]);
            var sequence = BinaryPrimitives.ReadUInt32LittleEndian(header[18..22]);
            if (!haveSerial)
            {
                _seekSerial = serial;
                haveSerial = true;
            }
            else if (serial != _seekSerial)
            {
                throw new InvalidDataException("Ogg contains more than one logical stream.");
            }

            if (havePreviousSequence && sequence != previousSequence + 1)
            {
                throw new InvalidDataException("Ogg page sequence is not consecutive.");
            }

            var segmentCount = header[26];
            if (!TryReadExact(scan, _segmentTable!.AsSpan(0, segmentCount)))
            {
                throw new InvalidDataException("Ogg page has an incomplete segment table.");
            }

            var payloadLength = 0;
            for (var i = 0; i < segmentCount; i++)
            {
                payloadLength += _segmentTable[i];
            }

            if (payloadLength > scan.Length - scan.Position)
            {
                throw new InvalidDataException("Ogg page has an incomplete payload.");
            }

            if ((headerType & 1) == 0)
            {
                AppendSeekPage(pageOffset, lastGranule);
            }

            scan.Position += payloadLength;
            if (granule >= 0)
            {
                lastGranule = granule;
            }

            previousSequence = sequence;
            havePreviousSequence = true;
        }

        if (_seekPageCount == 0)
        {
            throw new InvalidDataException("Ogg stream does not contain a seekable page.");
        }
    }

    private void AppendSeekPage(long pageOffset, long startFrame)
    {
        EnsureSeekCapacity(_seekPageCount + 1);
        _seekPageOffsets![_seekPageCount] = pageOffset;
        _seekPageStartFrames![_seekPageCount] = startFrame;
        _seekPageCount++;
    }

    private void EnsureSeekCapacity(int requiredElements)
    {
        if (_seekPageOffsets is not null && _seekPageOffsets.CapacityElements >= requiredElements)
        {
            return;
        }

        var capacity = _seekPageOffsets is null
            ? 256
            : Math.Max(requiredElements, checked(_seekPageOffsets.CapacityElements * 2));
        var offsets = new NativeBufferOwner<long>(capacity);
        var startFrames = new NativeBufferOwner<long>(capacity);
        try
        {
            if (_seekPageCount > 0)
            {
                _seekPageOffsets!.AsSpan(0, _seekPageCount).CopyTo(offsets.AsSpan());
                _seekPageStartFrames!.AsSpan(0, _seekPageCount).CopyTo(startFrames.AsSpan());
            }
        }
        catch
        {
            offsets.Dispose();
            startFrames.Dispose();
            throw;
        }

        var oldOffsets = _seekPageOffsets;
        var oldStartFrames = _seekPageStartFrames;
        _seekPageOffsets = offsets;
        _seekPageStartFrames = startFrames;
        oldOffsets?.Dispose();
        oldStartFrames?.Dispose();
    }

    private void CloseStream()
    {
        _stream?.Dispose();
        _stream = null;
    }

    private static bool TryReadExact(FileStream stream, Span<byte> destination)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var read = stream.Read(destination[offset..]);
            if (read == 0)
            {
                return offset == 0;
            }

            offset += read;
        }

        return true;
    }

    private static Span<T> GetSpan<T>(NativeBufferOwner<T>? owner, string name)
        where T : unmanaged
        => (owner ?? throw new InvalidOperationException($"The {name} buffer has not been prepared.")).Span;

    private static void EnsureOwner<T>(ref NativeBufferOwner<T>? owner, int requiredElements)
        where T : unmanaged
    {
        if (owner is not null && owner.CapacityElements >= requiredElements)
        {
            return;
        }

        var replacement = new NativeBufferOwner<T>(Math.Max(1, requiredElements));
        try
        {
            replacement.PreTouch();
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        var current = owner;
        owner = replacement;
        current?.Dispose();
    }
}
