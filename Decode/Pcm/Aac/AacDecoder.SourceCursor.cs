using System.IO;
using System.Runtime.InteropServices;
using No_Glass_Player.AudioDecodeCore.Decode.Pcm;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Aac;

internal static partial class AacDecoder
{
    internal static bool TryCreateFloat32ChunkCursor(string path, out Float32ChunkCursor? cursor, out string error)
        => TryCreateFloat32ChunkCursor(path, openReadStream: null, out cursor, out error);

    internal static bool TryCreateFloat32ChunkCursor(
        string path,
        Func<Stream>? openReadStream,
        out Float32ChunkCursor? cursor,
        out string error,
        AacReaderWorkspace? workspace = null)
    {
        cursor = null;
        error = string.Empty;

        try
        {
            cursor = new Float32ChunkCursor(path, openReadStream, workspace);
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

    internal static bool TryCreatePcm32ChunkCursor(string path, out Pcm32ChunkCursor? cursor, out string error)
        => TryCreatePcm32ChunkCursor(path, openReadStream: null, out cursor, out error);

    internal static bool TryCreatePcm32ChunkCursor(
        string path,
        Func<Stream>? openReadStream,
        out Pcm32ChunkCursor? cursor,
        out string error,
        AacReaderWorkspace? workspace = null)
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
        private readonly AacMp4Demuxer.AacDecodeInfo _decodeInfo = null!;
        private readonly Stream _stream = null!;
        private readonly AacReaderWorkspace _workspace = null!;
        private readonly bool _ownsWorkspace;
        private readonly bool _ownsDecodeInfo;
        private readonly AacDecoderContext _context = null!;
        private readonly int _channels;
        private SharpJaad.AAC.Decoder _decoder;
        private readonly SharpJaad.AAC.SampleBuffer _sampleBuffer;
        private int _nextPacketIndex;
        private int _stagedSampleOffset;
        private int _stagedSampleCount;
        private long _positionFrames;
        private bool _disposed;

        internal Float32ChunkCursor(
            string path,
            Func<Stream>? openReadStream = null,
            AacReaderWorkspace? workspace = null)
        {
            var contextAcquired = false;
            try
            {
                _ownsDecodeInfo = workspace is null;
                _decodeInfo = workspace?.AcquireDecodeInfo(path) ?? AacMp4Demuxer.ReadDecodeInfo(path);
                ValidateSupportedConfig(_decodeInfo);
                _stream = (openReadStream ?? (() => OpenStream(path)))();
                _channels = Math.Max(1, _decodeInfo.Container.Channels);
                _workspace = workspace ?? new AacReaderWorkspace();
                _ownsWorkspace = workspace is null;
                _workspace.EnsureCapacity((int)Math.Max(256u, _decodeInfo.Container.MaxPacketBytes), MaximumFramePcmSamples, _channels);
                _context = _workspace.AcquireContext(_decodeInfo.Esds.DecoderSpecificInfo);
                contextAcquired = true;
                _decoder = _context.Decoder;
                _sampleBuffer = _context.SampleBuffer;
                ConfigureSampleBuffer();
            }
            catch
            {
                if (contextAcquired)
                {
                    _workspace.ReleaseContext(_context);
                }
                _stream?.Dispose();
                if (_ownsDecodeInfo)
                {
                    _decodeInfo?.Dispose();
                }
                if (_ownsWorkspace)
                {
                    _workspace?.Dispose();
                }
                throw;
            }
        }

        internal AacMp4Demuxer.AacContainerInfo Info => _decodeInfo.Container;

        internal double DurationSeconds => _decodeInfo.Container.SampleRate > 0
            ? _decodeInfo.Container.TrimmedPcmFrames / (double)_decodeInfo.Container.SampleRate
            : 0d;

        internal void Seek(TimeSpan position)
        {
            ThrowIfDisposed();

            var targetFrames = ClampFrames(position, _decodeInfo.Container.SampleRate, _decodeInfo.Container.TrimmedPcmFrames);
            _positionFrames = targetFrames;
            _stagedSampleOffset = 0;
            _stagedSampleCount = 0;
            ResetDecoderState();

            if (targetFrames >= _decodeInfo.Container.TrimmedPcmFrames || _decodeInfo.Packets.Length == 0)
            {
                _nextPacketIndex = _decodeInfo.Packets.Length;
                return;
            }

            _nextPacketIndex = GetPacketIndexForOutputFrame(targetFrames);
            PreRollPacketIfNeeded(_nextPacketIndex);
            if (!TryStageNextPacket())
            {
                _nextPacketIndex = _decodeInfo.Packets.Length;
                return;
            }

            var packetOutputFrameStart = GetPacketOutputFrameStart(_nextPacketIndex - 1);
            var localSkipFrames = Math.Max(0L, targetFrames - packetOutputFrameStart);
            var skipSamples = (int)Math.Min(_stagedSampleCount, localSkipFrames * _channels);
            _stagedSampleOffset = skipSamples;
            _stagedSampleCount -= skipSamples;
        }

        internal int ReadBytes(Span<byte> destination)
        {
            ThrowIfDisposed();

            var frameBytes = _channels * sizeof(float);
            var writableFrames = destination.Length / frameBytes;
            if (writableFrames <= 0)
            {
                return 0;
            }

            var writableSamples = writableFrames * _channels;
            var destinationFloats = MemoryMarshal.Cast<byte, float>(destination.Slice(0, writableSamples * sizeof(float)));
            var writtenSamples = 0;
            while (writtenSamples < writableSamples)
            {
                if (_stagedSampleCount <= 0 && !TryStageNextPacket())
                {
                    break;
                }

                var chunkSamples = Math.Min(writableSamples - writtenSamples, _stagedSampleCount);
                _sampleBuffer.FloatDataSpan.Slice(_stagedSampleOffset, chunkSamples)
                    .CopyTo(destinationFloats.Slice(writtenSamples, chunkSamples));
                _stagedSampleOffset += chunkSamples;
                _stagedSampleCount -= chunkSamples;
                writtenSamples += chunkSamples;
                _positionFrames += chunkSamples / _channels;
            }

            return writtenSamples * sizeof(float);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stream.Dispose();
            if (_ownsDecodeInfo)
            {
                _decodeInfo.Dispose();
            }
            _workspace.ReleaseContext(_context);
            if (_ownsWorkspace)
            {
                _workspace.Dispose();
            }
        }

        private void ResetDecoderState()
        {
            _context.Reset();
            _decoder = _context.Decoder;
            ConfigureSampleBuffer();
        }

        private void ConfigureSampleBuffer()
        {
            _sampleBuffer.PreferPcm32Output = false;
            _sampleBuffer.SetExternalFloatBuffer(_workspace.FloatOutputPointer, _workspace.FloatOutput.Length);
        }

        private bool TryStageNextPacket()
        {
            while (_nextPacketIndex < _decodeInfo.Packets.Length)
            {
                var packetIndex = _nextPacketIndex++;
                if (!TryDecodePacketIntoStage(packetIndex))
                {
                    continue;
                }

                return true;
            }

            _stagedSampleOffset = 0;
            _stagedSampleCount = 0;
            return false;
        }

        private bool TryDecodePacketIntoStage(int packetIndex)
        {
            var packet = _decodeInfo.Packets[packetIndex];
            SeekIfNeeded(_stream, packet.Offset);
            var packetBuffer = _workspace.PacketBuffer.Slice(0, packet.Size);
            _stream.ReadExactly(packetBuffer);

            _decoder.DecodeFrame(packetBuffer, _sampleBuffer);

            if (_sampleBuffer.Channels != _channels)
            {
                throw new InvalidDataException($"SharpJaad AAC channel mismatch. expected={_channels} actual={_sampleBuffer.Channels} packet={packetIndex}");
            }

            if (_sampleBuffer.SampleRate != _decodeInfo.Container.SampleRate)
            {
                throw new InvalidDataException($"JAAD AAC sample-rate mismatch. expected={_decodeInfo.Container.SampleRate} actual={_sampleBuffer.SampleRate} packet={packetIndex}");
            }

            var decodedSamples = _sampleBuffer.FloatDataSpan;
            if (decodedSamples.Length == 0)
            {
                return false;
            }

            var decodedFrames = decodedSamples.Length / _channels;
            var decodedFrameStart = packet.DecodeStartFrame;
            var decodedFrameEnd = decodedFrameStart + decodedFrames;
            var absoluteValidStart = Math.Max(decodedFrameStart, _decodeInfo.Container.PrimingFrames);
            var absoluteValidEnd = Math.Min(
                decodedFrameEnd,
                _decodeInfo.Container.TotalPcmFrames - Math.Max(0, _decodeInfo.Container.PaddingFrames));
            if (absoluteValidEnd <= absoluteValidStart)
            {
                return false;
            }

            var localStartFrame = (int)(absoluteValidStart - decodedFrameStart);
            var localFrameCount = (int)(absoluteValidEnd - absoluteValidStart);
            _stagedSampleOffset = localStartFrame * _channels;
            _stagedSampleCount = localFrameCount * _channels;
            return true;
        }

        private void PreRollPacketIfNeeded(int targetPacketIndex)
        {
            if (targetPacketIndex <= 0 || _decodeInfo.Packets.Length == 0)
            {
                return;
            }

            var preRollPackets = _decodeInfo.Esds.Config.HasSbrExtension ? 16 : 1;
            var firstPacketIndex = Math.Max(0, targetPacketIndex - preRollPackets);
            for (var packetIndex = firstPacketIndex; packetIndex < targetPacketIndex; packetIndex++)
            {
                var packet = _decodeInfo.Packets[packetIndex];
                SeekIfNeeded(_stream, packet.Offset);
                var packetBuffer = _workspace.PacketBuffer.Slice(0, packet.Size);
                _stream.ReadExactly(packetBuffer);
                _decoder.DecodeFrame(packetBuffer, _sampleBuffer);
            }
        }

        private long GetPacketOutputFrameStart(int packetIndex)
        {
            var decodedFrameStart = _decodeInfo.Packets[packetIndex].DecodeStartFrame;
            var validFrameStart = Math.Max(decodedFrameStart, _decodeInfo.Container.PrimingFrames);
            return Math.Max(0, validFrameStart - _decodeInfo.Container.PrimingFrames);
        }

        private int GetPacketIndexForOutputFrame(long outputFrame)
        {
            var absoluteFrame = Math.Max(0L, outputFrame + _decodeInfo.Container.PrimingFrames);
            var packets = _decodeInfo.Packets;
            var low = 0;
            var high = packets.Length;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (packets[middle].DecodeStartFrame <= absoluteFrame)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return Math.Clamp(low - 1, 0, Math.Max(0, packets.Length - 1));
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
        private readonly AacMp4Demuxer.AacDecodeInfo _decodeInfo = null!;
        private readonly Stream _stream = null!;
        private readonly AacReaderWorkspace _workspace = null!;
        private readonly bool _ownsWorkspace;
        private readonly bool _ownsDecodeInfo;
        private readonly AacDecoderContext _context = null!;
        private readonly int _channels;
        private SharpJaad.AAC.Decoder _decoder;
        private readonly SharpJaad.AAC.SampleBuffer _sampleBuffer;
        private int _nextPacketIndex;
        private int _stagedFrameOffset;
        private int _stagedFrameCount;
        private long _positionFrames;
        private bool _disposed;

        internal Pcm32ChunkCursor(
            string path,
            Func<Stream>? openReadStream = null,
            AacReaderWorkspace? workspace = null)
        {
            var contextAcquired = false;
            try
            {
                _ownsDecodeInfo = workspace is null;
                _decodeInfo = workspace?.AcquireDecodeInfo(path) ?? AacMp4Demuxer.ReadDecodeInfo(path);
                ValidateSupportedConfig(_decodeInfo);
                _stream = (openReadStream ?? (() => OpenStream(path)))();
                _channels = Math.Max(1, _decodeInfo.Container.Channels);
                _workspace = workspace ?? new AacReaderWorkspace();
                _ownsWorkspace = workspace is null;
                _workspace.EnsureCapacity((int)Math.Max(256u, _decodeInfo.Container.MaxPacketBytes), MaximumFramePcmSamples, _channels);
                _context = _workspace.AcquireContext(_decodeInfo.Esds.DecoderSpecificInfo);
                contextAcquired = true;
                _decoder = _context.Decoder;
                _sampleBuffer = _context.SampleBuffer;
                ConfigureSampleBuffer();
            }
            catch
            {
                if (contextAcquired)
                {
                    _workspace.ReleaseContext(_context);
                }
                _stream?.Dispose();
                if (_ownsDecodeInfo)
                {
                    _decodeInfo?.Dispose();
                }
                if (_ownsWorkspace)
                {
                    _workspace?.Dispose();
                }
                throw;
            }
        }

        internal AacMp4Demuxer.AacContainerInfo Info => _decodeInfo.Container;

        internal double DurationSeconds => _decodeInfo.Container.SampleRate > 0
            ? _decodeInfo.Container.TrimmedPcmFrames / (double)_decodeInfo.Container.SampleRate
            : 0d;

        internal void Seek(TimeSpan position)
        {
            ThrowIfDisposed();

            var targetFrames = ClampFrames(position, _decodeInfo.Container.SampleRate, _decodeInfo.Container.TrimmedPcmFrames);
            _positionFrames = targetFrames;
            _stagedFrameOffset = 0;
            _stagedFrameCount = 0;
            ResetDecoderState();

            if (targetFrames >= _decodeInfo.Container.TrimmedPcmFrames || _decodeInfo.Packets.Length == 0)
            {
                _nextPacketIndex = _decodeInfo.Packets.Length;
                return;
            }

            _nextPacketIndex = GetPacketIndexForOutputFrame(targetFrames);
            PreRollPacketIfNeeded(_nextPacketIndex);
            if (!TryStageNextPacket())
            {
                _nextPacketIndex = _decodeInfo.Packets.Length;
                return;
            }

            var packetOutputFrameStart = GetPacketOutputFrameStart(_nextPacketIndex - 1);
            var localSkipFrames = (int)Math.Min(_stagedFrameCount, Math.Max(0L, targetFrames - packetOutputFrameStart));
            _stagedFrameOffset = localSkipFrames;
            _stagedFrameCount -= localSkipFrames;
        }

        internal int ReadStereoPcm32Frames(Span<byte> destination)
        {
            ThrowIfDisposed();

            var writableFrames = destination.Length / (sizeof(int) * 2);
            if (writableFrames <= 0)
            {
                return 0;
            }

            return ReadStereoPcm32Frames(MemoryMarshal.Cast<byte, int>(destination.Slice(0, writableFrames * sizeof(int) * 2)));
        }

        internal int ReadStereoPcm32Frames(Span<int> destination)
        {
            ThrowIfDisposed();

            var writableFrames = destination.Length / 2;
            if (writableFrames <= 0)
            {
                return 0;
            }

            var writtenFrames = 0;
            while (writtenFrames < writableFrames)
            {
                if (_stagedFrameCount <= 0 && !TryStageNextPacket())
                {
                    break;
                }

                var chunkFrames = Math.Min(writableFrames - writtenFrames, _stagedFrameCount);
                var sourceOffset = _stagedFrameOffset * 2;
                var destinationOffset = writtenFrames * 2;
                _workspace.StereoPcm32Output.Slice(sourceOffset, chunkFrames * 2)
                    .CopyTo(destination.Slice(destinationOffset, chunkFrames * 2));
                _stagedFrameOffset += chunkFrames;
                _stagedFrameCount -= chunkFrames;
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

            var writtenFrames = 0;
            while (writtenFrames < writableFrames)
            {
                if (_stagedFrameCount <= 0 && !TryStageNextPacket())
                {
                    break;
                }

                var chunkFrames = Math.Min(writableFrames - writtenFrames, _stagedFrameCount);
                var sourceOffset = _stagedFrameOffset * 2;
                var sampleCount = chunkFrames * 2;
                var destinationOffset = writtenFrames * 2;
                var source = _workspace.StereoPcm32Output.Slice(sourceOffset, sampleCount);
                var target = destination.Slice(destinationOffset, sampleCount);
                for (var i = 0; i < sampleCount; i++)
                {
                    target[i] = (long)source[i] << workFractionalBits;
                }

                _stagedFrameOffset += chunkFrames;
                _stagedFrameCount -= chunkFrames;
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
            _stream.Dispose();
            if (_ownsDecodeInfo)
            {
                _decodeInfo.Dispose();
            }
            _workspace.ReleaseContext(_context);
            if (_ownsWorkspace)
            {
                _workspace.Dispose();
            }
        }

        private void ResetDecoderState()
        {
            _context.Reset();
            _decoder = _context.Decoder;
            ConfigureSampleBuffer();
        }

        private void ConfigureSampleBuffer()
        {
            _sampleBuffer.PreferPcm32Output = true;
            _sampleBuffer.SetExternalPcm32Buffer(_workspace.Pcm32OutputPointer, _workspace.Pcm32Output.Length);
        }

        private bool TryStageNextPacket()
        {
            while (_nextPacketIndex < _decodeInfo.Packets.Length)
            {
                var packetIndex = _nextPacketIndex++;
                if (!TryDecodePacketIntoStage(packetIndex))
                {
                    continue;
                }

                return true;
            }

            _stagedFrameOffset = 0;
            _stagedFrameCount = 0;
            return false;
        }

        private bool TryDecodePacketIntoStage(int packetIndex)
        {
            var packet = _decodeInfo.Packets[packetIndex];
            SeekIfNeeded(_stream, packet.Offset);
            var packetBuffer = _workspace.PacketBuffer.Slice(0, packet.Size);
            _stream.ReadExactly(packetBuffer);

            _decoder.DecodeFrame(packetBuffer, _sampleBuffer);

            if (_sampleBuffer.Channels != _channels)
            {
                throw new InvalidDataException($"SharpJaad AAC channel mismatch. expected={_channels} actual={_sampleBuffer.Channels} packet={packetIndex}");
            }

            if (_sampleBuffer.SampleRate != _decodeInfo.Container.SampleRate)
            {
                throw new InvalidDataException($"JAAD AAC sample-rate mismatch. expected={_decodeInfo.Container.SampleRate} actual={_sampleBuffer.SampleRate} packet={packetIndex}");
            }

            var decodedSamples = _sampleBuffer.Pcm32DataSpan;
            if (decodedSamples.Length == 0)
            {
                return false;
            }

            var decodedFrames = decodedSamples.Length / _channels;
            var decodedFrameStart = packet.DecodeStartFrame;
            var decodedFrameEnd = decodedFrameStart + decodedFrames;
            var absoluteValidStart = Math.Max(decodedFrameStart, _decodeInfo.Container.PrimingFrames);
            var absoluteValidEnd = Math.Min(
                decodedFrameEnd,
                _decodeInfo.Container.TotalPcmFrames - Math.Max(0, _decodeInfo.Container.PaddingFrames));
            if (absoluteValidEnd <= absoluteValidStart)
            {
                return false;
            }

            var localStartFrame = (int)(absoluteValidStart - decodedFrameStart);
            var localFrameCount = (int)(absoluteValidEnd - absoluteValidStart);
            var sourceSampleOffset = localStartFrame * _channels;
            if (_channels == 1)
            {
                var sourceIndex = sourceSampleOffset;
                var destinationIndex = 0;
                for (var frame = 0; frame < localFrameCount; frame++)
                {
                    var sample = decodedSamples[sourceIndex];
                    _workspace.StereoPcm32Output[destinationIndex] = sample;
                    _workspace.StereoPcm32Output[destinationIndex + 1] = sample;
                    sourceIndex++;
                    destinationIndex += 2;
                }
            }
            else
            {
                decodedSamples.Slice(sourceSampleOffset, localFrameCount * 2)
                    .CopyTo(_workspace.StereoPcm32Output.Slice(0, localFrameCount * 2));
            }

            _stagedFrameOffset = 0;
            _stagedFrameCount = localFrameCount;
            return true;
        }

        private void PreRollPacketIfNeeded(int targetPacketIndex)
        {
            if (targetPacketIndex <= 0 || _decodeInfo.Packets.Length == 0)
            {
                return;
            }

            var preRollPackets = _decodeInfo.Esds.Config.HasSbrExtension ? 16 : 1;
            var firstPacketIndex = Math.Max(0, targetPacketIndex - preRollPackets);
            for (var packetIndex = firstPacketIndex; packetIndex < targetPacketIndex; packetIndex++)
            {
                var packet = _decodeInfo.Packets[packetIndex];
                SeekIfNeeded(_stream, packet.Offset);
                var packetBuffer = _workspace.PacketBuffer.Slice(0, packet.Size);
                _stream.ReadExactly(packetBuffer);
                _decoder.DecodeFrame(packetBuffer, _sampleBuffer);
            }
        }

        private long GetPacketOutputFrameStart(int packetIndex)
        {
            var decodedFrameStart = _decodeInfo.Packets[packetIndex].DecodeStartFrame;
            var validFrameStart = Math.Max(decodedFrameStart, _decodeInfo.Container.PrimingFrames);
            return Math.Max(0, validFrameStart - _decodeInfo.Container.PrimingFrames);
        }

        private int GetPacketIndexForOutputFrame(long outputFrame)
        {
            var absoluteFrame = Math.Max(0L, outputFrame + _decodeInfo.Container.PrimingFrames);
            var packets = _decodeInfo.Packets;
            var low = 0;
            var high = packets.Length;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (packets[middle].DecodeStartFrame <= absoluteFrame)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return Math.Clamp(low - 1, 0, Math.Max(0, packets.Length - 1));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(Pcm32ChunkCursor));
            }
        }
    }

    private static Stream OpenStream(string path)
    {
        // AAC access is packet-sequential. A one-byte FileStream buffer made
        // each packet read pay an avoidable OS read boundary; keep a bounded
        // 1 MiB read-ahead without changing packet offsets or decode state.
#if OFFLINE_SCAN_DECODER
        const int bufferSize = 2 << 20;
#else
        const int bufferSize = 1 << 20;
#endif
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
    }

    private static void SeekIfNeeded(Stream stream, long offset)
    {
        if (stream.Position != offset)
        {
            stream.Position = offset;
        }
    }

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

        return targetFrames > totalFrames ? totalFrames : targetFrames;
    }
}
