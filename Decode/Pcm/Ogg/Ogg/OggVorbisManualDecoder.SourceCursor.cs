using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using No_Glass_Player.AudioDecodeCore.Decode.Pcm.Ogg;

internal static partial class OggVorbisManualDecoder
{


    internal static bool TryCreatePcm32ChunkCursor(string path, out Pcm32ChunkCursor? cursor, out string error)
        => TryCreatePcm32ChunkCursor(path, openReadStream: null, out cursor, out error);

    internal static bool TryCreatePcm32ChunkCursor(
        string path,
        Func<Stream>? openReadStream,
        out Pcm32ChunkCursor? cursor,
        out string error,
        OggReaderWorkspace? workspace = null)
    {
        cursor = null;
        error = string.Empty;

        try
        {
            cursor = new Pcm32ChunkCursor(path, openReadStream, workspace);
            return true;
        }
        catch (Exception ex)
        {
            cursor?.Dispose();
            cursor = null;
            error = ex.Message;
            return false;
        }
    }

    internal sealed class Float32ChunkCursor : IDisposable
    {
        private readonly OggVorbisInfo _info;
        private readonly OggReaderWorkspace _workspace;
        private readonly bool _ownsWorkspace;
        private readonly OggPacketCursor _cursor;
        private int _stagedByteOffset;
        private int _stagedByteLength;
        private long _positionFrames;
        private bool _disposed;

        internal Float32ChunkCursor(
            string path,
            Func<Stream>? openReadStream = null,
            OggReaderWorkspace? workspace = null)
        {
            _workspace = workspace ?? new OggReaderWorkspace();
            _ownsWorkspace = workspace is null;
            try
            {
                _info = ReadPlaybackInfoCore(path, _workspace, parseComments: false);
                _workspace.EnsureFloat32Capacity(_info.BlockSizeLarge, _info.Channels);
                _cursor = new OggPacketCursor(path, _info, OggVorbisOutputTarget.Float32, _workspace, openReadStream);
            }
            catch
            {
                if (_ownsWorkspace)
                {
                    _workspace.Dispose();
                }

                throw;
            }
        }

        internal int Channels => _info.Channels;

        internal int SampleRate => _info.SampleRate;

        internal double DurationSeconds => _info.SampleRate > 0 ? _info.TotalPcmFrames / (double)_info.SampleRate : 0d;

        internal void Seek(TimeSpan position)
        {
            ThrowIfDisposed();

            var targetFrames = ClampFrames(position, _info.SampleRate, _info.TotalPcmFrames);
            _cursor.Reset();
            _positionFrames = 0;
            _stagedByteOffset = 0;
            _stagedByteLength = 0;

            if (targetFrames <= 0)
            {
                return;
            }

            SkipFrames(targetFrames, Channels * sizeof(float));
        }

        internal int ReadBytes(Span<byte> destination)
        {
            ThrowIfDisposed();

            var frameBytes = Channels * sizeof(float);
            var writableBytes = destination.Length - (destination.Length % frameBytes);
            if (writableBytes <= 0)
            {
                return 0;
            }

            var totalWritten = 0;
            while (totalWritten < writableBytes)
            {
                if (_stagedByteLength <= 0 && !TryStageNextPacket())
                {
                    break;
                }

                var chunkBytes = Math.Min(writableBytes - totalWritten, _stagedByteLength);
                var alignedChunkBytes = chunkBytes - (chunkBytes % frameBytes);
                if (alignedChunkBytes <= 0)
                {
                    break;
                }

                GetStagedBytes().Slice(_stagedByteOffset, alignedChunkBytes)
                    .CopyTo(destination.Slice(totalWritten, alignedChunkBytes));
                _stagedByteOffset += alignedChunkBytes;
                _stagedByteLength -= alignedChunkBytes;
                totalWritten += alignedChunkBytes;
                _positionFrames += alignedChunkBytes / frameBytes;
            }

            return totalWritten;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cursor.Dispose();
            if (_ownsWorkspace)
            {
                _workspace.Dispose();
            }
        }

        private bool TryStageNextPacket()
        {
            var sampleCount = _cursor.DecodeNextFloat32Packet();
            if (sampleCount <= 0)
            {
                _stagedByteOffset = 0;
                _stagedByteLength = 0;
                return false;
            }

            _stagedByteOffset = 0;
            _stagedByteLength = sampleCount * sizeof(float);
            return true;
        }

        private ReadOnlySpan<byte> GetStagedBytes()
            => MemoryMarshal.AsBytes(_workspace.FloatOutput);

        private void SkipFrames(long targetFrames, int frameBytes)
        {
            while (_positionFrames < targetFrames)
            {
                if (!TryStageNextPacket())
                {
                    _stagedByteOffset = 0;
                    _stagedByteLength = 0;
                    return;
                }

                var stagedFrames = _stagedByteLength / frameBytes;
                var remainingFrames = targetFrames - _positionFrames;
                if (remainingFrames >= stagedFrames)
                {
                    _stagedByteOffset = 0;
                    _stagedByteLength = 0;
                    _positionFrames += stagedFrames;
                    continue;
                }

                var skipBytes = (int)(remainingFrames * frameBytes);
                _stagedByteOffset = skipBytes;
                _stagedByteLength -= skipBytes;
                _positionFrames = targetFrames;
                return;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Float32ChunkCursor));
            }
        }
    }

    internal sealed class Pcm32ChunkCursor : IDisposable
    {
        private readonly OggVorbisInfo _info;
        private readonly OggReaderWorkspace _workspace;
        private readonly bool _ownsWorkspace;
        private readonly OggPacketCursor _cursor;
        private int _stagedByteOffset;
        private int _stagedByteLength;
        private long _positionFrames;
        private bool _disposed;

        internal Pcm32ChunkCursor(
            string path,
            Func<Stream>? openReadStream = null,
            OggReaderWorkspace? workspace = null)
        {
            _workspace = workspace ?? new OggReaderWorkspace();
            _ownsWorkspace = workspace is null;
            try
            {
                _info = ReadPlaybackInfoCore(path, _workspace, parseComments: false);
                _workspace.EnsurePcm32Capacity(_info.BlockSizeLarge, _info.Channels);
                _cursor = new OggPacketCursor(path, _info, OggVorbisOutputTarget.Pcm32, _workspace, openReadStream);
            }
            catch
            {
                if (_ownsWorkspace)
                {
                    _workspace.Dispose();
                }

                throw;
            }
        }

        internal int SampleRate => _info.SampleRate;

        internal double DurationSeconds => _info.SampleRate > 0 ? _info.TotalPcmFrames / (double)_info.SampleRate : 0d;

        internal void Seek(TimeSpan position)
        {
            ThrowIfDisposed();

            var targetFrames = ClampFrames(position, _info.SampleRate, _info.TotalPcmFrames);
            _cursor.Reset();
            _positionFrames = 0;
            _stagedByteOffset = 0;
            _stagedByteLength = 0;

            if (targetFrames <= 0)
            {
                return;
            }

            SkipFrames(targetFrames);
        }

        internal int ReadStereoPcm32Frames(Span<byte> destination)
        {
            ThrowIfDisposed();

            var writableFrames = destination.Length / (sizeof(int) * 2);
            if (writableFrames <= 0)
            {
                return 0;
            }

            return ReadStereoPcm32Frames(MemoryMarshal.Cast<byte, int>(destination[..(writableFrames * 2 * sizeof(int))]));
        }

        internal int ReadStereoPcm32Frames(Span<int> destination)
        {
            ThrowIfDisposed();

            var writableFrames = destination.Length / 2;
            if (writableFrames <= 0)
            {
                return 0;
            }

            const int frameBytes = sizeof(int) * 2;
            var writtenFrames = 0;
            while (writtenFrames < writableFrames)
            {
                if (_stagedByteLength <= 0 && !TryStageNextPacket())
                {
                    break;
                }

                var stagedFrames = _stagedByteLength / frameBytes;
                var chunkFrames = Math.Min(writableFrames - writtenFrames, stagedFrames);
                if (chunkFrames <= 0)
                {
                    break;
                }

                var sourceSampleOffset = _stagedByteOffset / sizeof(int);
                var sampleCount = chunkFrames * 2;
                _workspace.StereoPcm32Output.Slice(sourceSampleOffset, sampleCount)
                    .CopyTo(destination.Slice(writtenFrames * 2, sampleCount));
                _stagedByteOffset += sampleCount * sizeof(int);
                _stagedByteLength -= sampleCount * sizeof(int);
                writtenFrames += chunkFrames;
                _positionFrames += chunkFrames;
            }

            return writtenFrames;
        }

        internal int ReadStereoWorkFrames(Span<long> destination)
        {
            const int workFractionalBits = 28;

            ThrowIfDisposed();

            var writableFrames = destination.Length / 2;
            if (writableFrames <= 0)
            {
                return 0;
            }

            const int frameBytes = sizeof(int) * 2;
            var writtenFrames = 0;
            while (writtenFrames < writableFrames)
            {
                if (_stagedByteLength <= 0 && !TryStageNextPacket())
                {
                    break;
                }

                var stagedFrames = _stagedByteLength / frameBytes;
                var chunkFrames = Math.Min(writableFrames - writtenFrames, stagedFrames);
                if (chunkFrames <= 0)
                {
                    break;
                }

                var sourceSampleOffset = _stagedByteOffset / sizeof(int);
                var sampleCount = chunkFrames * 2;
                var source = _workspace.StereoPcm32Output.Slice(sourceSampleOffset, sampleCount);
                var target = destination.Slice(writtenFrames * 2, sampleCount);
                for (var i = 0; i < sampleCount; i++)
                {
                    target[i] = (long)source[i] << workFractionalBits;
                }

                _stagedByteOffset += sampleCount * sizeof(int);
                _stagedByteLength -= sampleCount * sizeof(int);
                writtenFrames += chunkFrames;
                _positionFrames += chunkFrames;
            }

            return writtenFrames;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cursor.Dispose();
            if (_ownsWorkspace)
            {
                _workspace.Dispose();
            }
        }

        private bool TryStageNextPacket()
        {
            var sampleCount = _cursor.DecodeNextPcm32Packet();
            if (sampleCount <= 0)
            {
                _stagedByteOffset = 0;
                _stagedByteLength = 0;
                return false;
            }

            var frames = sampleCount / Math.Max(1, _info.Channels);
            var stereoSpan = _workspace.StereoPcm32Output[..(frames * 2)];
            if (_info.Channels == 1)
            {
                var sourceSpan = _workspace.Pcm32SourceOutput[..frames];
                for (var frame = 0; frame < frames; frame++)
                {
                    var sample = sourceSpan[frame];
                    var offset = frame * 2;
                    stereoSpan[offset] = sample;
                    stereoSpan[offset + 1] = sample;
                }
            }
            else
            {
                _workspace.Pcm32SourceOutput[..(frames * 2)].CopyTo(stereoSpan);
            }

            _stagedByteOffset = 0;
            _stagedByteLength = frames * 2 * sizeof(int);
            return true;
        }

        private void SkipFrames(long targetFrames)
        {
            const int frameBytes = sizeof(int) * 2;
            while (_positionFrames < targetFrames)
            {
                if (!TryStageNextPacket())
                {
                    _stagedByteOffset = 0;
                    _stagedByteLength = 0;
                    return;
                }

                var stagedFrames = _stagedByteLength / frameBytes;
                var remainingFrames = targetFrames - _positionFrames;
                if (remainingFrames >= stagedFrames)
                {
                    _stagedByteOffset = 0;
                    _stagedByteLength = 0;
                    _positionFrames += stagedFrames;
                    continue;
                }

                var skipBytes = (int)(remainingFrames * frameBytes);
                _stagedByteOffset = skipBytes;
                _stagedByteLength -= skipBytes;
                _positionFrames = targetFrames;
                return;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Pcm32ChunkCursor));
            }
        }
    }

    private sealed class OggPacketCursor : IDisposable
    {
        private readonly OggVorbisInfo _info;
        private readonly OggVorbisOutputTarget _outputTarget;
        private readonly OggReaderWorkspace _workspace;
        private readonly Stream _stream;
        private readonly OggVorbisFloat32Context _context;
        private int _packetLength;
        private int _pageSegmentCount;
        private int _pageSegmentIndex;
        private int _pagePayloadOffset;
        private int _lastCompletedPacketSegment;
        private long _pageGranulePosition;
        private int _packetIndex;
        private bool _disposed;

        internal OggPacketCursor(
            string path,
            OggVorbisInfo info,
            OggVorbisOutputTarget outputTarget,
            OggReaderWorkspace workspace,
            Func<Stream>? openReadStream = null)
        {
            _info = info;
            _outputTarget = outputTarget;
            _workspace = workspace;
            _context = workspace.AcquireDecoderContext(
                info.Channels,
                info.BlockSizeSmall,
                info.BlockSizeLarge,
                info.Setup,
                outputTarget);
            _stream = openReadStream?.Invoke() ?? OpenStream(path);
        }

        internal int DecodeNextFloat32Packet()
        {
            if (_outputTarget != OggVorbisOutputTarget.Float32)
            {
                throw new InvalidOperationException("OGG cursor output target is not Float32.");
            }

            return ReadNextPacket();
        }

        internal int DecodeNextPcm32Packet()
        {
            if (_outputTarget != OggVorbisOutputTarget.Pcm32)
            {
                throw new InvalidOperationException("OGG cursor output target is not PCM32.");
            }

            return ReadNextPacket();
        }

        internal void Reset()
        {
            ThrowIfDisposed();
            if (!_stream.CanSeek)
            {
                throw new NotSupportedException("OGG seek requires a seekable source stream.");
            }

            _stream.Position = 0;
            _packetLength = 0;
            _context.ResetState();
            _pageSegmentCount = 0;
            _pageSegmentIndex = 0;
            _pagePayloadOffset = 0;
            _lastCompletedPacketSegment = -1;
            _pageGranulePosition = 0;
            _packetIndex = 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context.ResetState();
            _stream.Dispose();
        }

        private int ReadNextPacket()
        {
            ThrowIfDisposed();

            while (true)
            {
                if (_pageSegmentIndex >= _pageSegmentCount)
                {
                    if (!TryReadPage())
                    {
                        if (_packetLength != 0)
                        {
                            throw new InvalidDataException("Ogg stream ended inside a continued packet.");
                        }

                        return 0;
                    }
                }

                var segmentIndex = _pageSegmentIndex++;
                var segmentBytes = _workspace.SegmentTable[segmentIndex];
                if (segmentBytes > 0)
                {
                    var requiredPacketBytes = checked(_packetLength + segmentBytes);
                    _workspace.EnsurePacketCapacity(requiredPacketBytes);
                    _workspace.PagePayload.Slice(_pagePayloadOffset, segmentBytes)
                        .CopyTo(_workspace.PacketBuffer.Slice(_packetLength, segmentBytes));
                    _packetLength = requiredPacketBytes;
                    _pagePayloadOffset += segmentBytes;
                }

                if (segmentBytes >= 255)
                {
                    continue;
                }

                var packet = _workspace.PacketBuffer[.._packetLength];
                var packetGranulePosition = segmentIndex == _lastCompletedPacketSegment
                    ? _pageGranulePosition
                    : -1;
                var sampleCount = DecodeCompletedPacket(packet, packetGranulePosition);
                _packetLength = 0;
                _packetIndex++;
                if (sampleCount > 0)
                {
                    return sampleCount;
                }
            }
        }

        private bool TryReadPage()
        {
            Span<byte> header = stackalloc byte[27];
            if (!TryReadExact(_stream, header))
            {
                return false;
            }

            if (header[0] != (byte)'O' || header[1] != (byte)'g' || header[2] != (byte)'g' || header[3] != (byte)'S')
            {
                throw new InvalidDataException($"Invalid Ogg capture pattern at stream offset {_stream.Position - header.Length}.");
            }

            if (header[4] != 0)
            {
                throw new InvalidDataException($"Unsupported Ogg bitstream version: {header[4]}.");
            }

            _pageGranulePosition = BinaryPrimitives.ReadInt64LittleEndian(header[6..14]);
            _pageSegmentCount = header[26];
            _pageSegmentIndex = 0;
            _pagePayloadOffset = 0;
            _lastCompletedPacketSegment = -1;

            var segmentSpan = _workspace.SegmentTable[.._pageSegmentCount];
            _stream.ReadExactly(segmentSpan);

            var payloadBytes = 0;
            for (var i = 0; i < segmentSpan.Length; i++)
            {
                payloadBytes += segmentSpan[i];
                if (segmentSpan[i] < 255)
                {
                    _lastCompletedPacketSegment = i;
                }
            }

            _stream.ReadExactly(_workspace.PagePayload[..payloadBytes]);
            return true;
        }

        private int DecodeCompletedPacket(ReadOnlySpan<byte> packet, long granulePosition)
        {
            if (_packetIndex < 2)
            {
                return 0;
            }

            if (_packetIndex == 2)
            {
                return 0;
            }

            _context.ResetCapturedOutput();
            _context.DecodePacket(packet, granulePosition);
            return _context.CapturedSampleCount;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(OggPacketCursor));
            }
        }
    }

    private static Stream OpenStream(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);

    private static long ClampFrames(TimeSpan position, int sampleRate, long totalFrames)
    {
        if (position <= TimeSpan.Zero)
        {
            return 0;
        }

        var targetFrames = (long)(position.TotalSeconds * sampleRate);
        if (targetFrames < 0)
        {
            return 0;
        }

        if (totalFrames > 0 && targetFrames > totalFrames)
        {
            return totalFrames;
        }

        return targetFrames;
    }
}
