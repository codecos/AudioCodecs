using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.InteropServices;
using Concentus.Structs;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Opus;

internal sealed class OpusPcm32Reader : ISelfHostedPcm32Reader
{
    private const int MaximumFrameSamples = 5760;
    private readonly int _channels;
    private readonly int _preSkip;
    private readonly long _totalFrames;
    private readonly OpusDecoder _decoder;
    private readonly OpusReaderWorkspace _workspace;
    private readonly bool _ownsWorkspace;
    private int _packetIndex;
    private int _decodedFrameOffset;
    private int _decodedFrameCount;
    private long _position;
    private bool _disposed;

    private OpusPcm32Reader(
        string sourcePath,
        OpusStreamInfo info,
        OpusReaderWorkspace? workspace)
    {
        _channels = info.Channels;
        _preSkip = info.PreSkip;
        _totalFrames = info.TotalPcmFrames;
        _decoder = new OpusDecoder(48000, _channels);
        _workspace = workspace ?? new OpusReaderWorkspace();
        _ownsWorkspace = workspace is null;
        _workspace.EnsureCapacity(MaximumFrameSamples * _channels);
        _workspace.Open(sourcePath);
        _packetIndex = 0;
        _decodedFrameOffset = 0;
        _decodedFrameCount = 0;
        _position = 0;
    }

    public int SampleRate => 48000;

    public double DurationSeconds => _totalFrames / 48000.0;

    internal static OpusPcm32Reader Create(string sourcePath, OpusReaderWorkspace? workspace = null)
    {
        if (!OpusManualDecoder.TryReadInfo(sourcePath, out var info, out var error))
        {
            throw new InvalidDataException($"Self-hosted Opus probe failed: {error}");
        }

        var reader = new OpusPcm32Reader(sourcePath, info, workspace);
        reader.SkipSourceFrames(reader._preSkip);
        return reader;
    }

    public void Seek(TimeSpan position)
    {
        ThrowIfDisposed();
        var target = _preSkip + Math.Clamp((long)Math.Round(position.TotalSeconds * SampleRate), 0, _totalFrames);
        _decoder.ResetState();
        _packetIndex = 0;
        _decodedFrameOffset = 0;
        _decodedFrameCount = 0;
        _position = _workspace.SeekToFrame(target, 5760);

        SkipSourceFrames(target);
    }

    public int ReadStereoPcm32Frames(Span<byte> destination)
    {
        var frames = destination.Length / (sizeof(int) * 2);
        if (frames <= 0)
        {
            return 0;
        }

        return ReadStereoPcm32Frames(MemoryMarshal.Cast<byte, int>(destination)[..(frames * 2)]);
    }

    public int ReadStereoPcm32Frames(Span<int> destination)
    {
        ThrowIfDisposed();
        var capacity = destination.Length / 2;
        var written = 0;
        while (written < capacity)
        {
            if (_decodedFrameOffset >= _decodedFrameCount && !DecodeNextPacket())
            {
                break;
            }

            var available = Math.Min(capacity - written, _decodedFrameCount - _decodedFrameOffset);
            available = (int)Math.Min(available, _preSkip + _totalFrames - _position);
            if (available <= 0)
            {
                break;
            }
            var sourceOffset = _decodedFrameOffset * _channels;
            var destinationOffset = written * 2;
            if (_channels == 2)
            {
                _workspace.Pcm32.Slice(sourceOffset, available * 2)
                    .CopyTo(destination[destinationOffset..]);
            }
            else
            {
                for (var frame = 0; frame < available; frame++)
                {
                    var sample = _workspace.Pcm32[sourceOffset + frame];
                    var output = destinationOffset + frame * 2;
                    destination[output] = sample;
                    destination[output + 1] = sample;
                }
            }

            _decodedFrameOffset += available;
            _position += available;
            written += available;
        }

        return written;
    }

    public int ReadStereoWorkFrames(Span<long> destination)
    {
        var capacity = destination.Length / 2;
        Span<int> temp = stackalloc int[Math.Min(MaximumFrameSamples * 2, Math.Max(2, capacity * 2))];
        var read = ReadStereoPcm32Frames(temp[..(Math.Min(capacity, MaximumFrameSamples) * 2)]);
        for (var i = 0; i < read * 2; i++)
        {
            destination[i] = temp[i];
        }

        return read;
    }

    private bool DecodeNextPacket()
    {
        if (!_workspace.TryReadAudioPacket(out var packet))
        {
            _decodedFrameOffset = 0;
            _decodedFrameCount = 0;
            return false;
        }

        _packetIndex++;
        var decodedFrames = _decoder.Decode(packet, _workspace.Decoded, MaximumFrameSamples);
        if (decodedFrames <= 0)
        {
            throw new InvalidDataException($"Opus packet {_packetIndex - 1} decoded to {decodedFrames} frames.");
        }

        var samples = decodedFrames * _channels;
        ConvertInt16ToPcm32(_workspace.Decoded[..samples], _workspace.Pcm32[..samples]);

        _decodedFrameOffset = 0;
        _decodedFrameCount = decodedFrames;
        return true;
    }

    private static void ConvertInt16ToPcm32(ReadOnlySpan<short> source, Span<int> destination)
    {
        // Concentus 保持 Int16 解码输出，播放器边界统一要求 PCM32。
        // AVX2 路径一次扩展 8 个样本，标量尾部保证无 SIMD 时结果一致。
        // 该转换只改变数值容器和缩放，不改变 Opus packet 解码语义。
        var i = 0;
        if (Avx2.IsSupported)
        {
            for (; i <= source.Length - 8; i += 8)
            {
                var input = Unsafe.ReadUnaligned<Vector128<short>>(
                    ref Unsafe.As<short, byte>(ref MemoryMarshal.GetReference(source[i..])));
                var widened = Avx2.ConvertToVector256Int32(input);
                var shifted = Avx2.ShiftLeftLogical(widened.AsUInt32(), 16).AsInt32();
                Unsafe.WriteUnaligned(
                    ref Unsafe.As<int, byte>(ref MemoryMarshal.GetReference(destination[i..])),
                    shifted);
            }
        }

        for (; i < source.Length; i++)
        {
            destination[i] = source[i] << 16;
        }
    }

    private void SkipSourceFrames(long target)
    {
        while (_position < target && DecodeNextPacket())
        {
            var skip = (int)Math.Min(target - _position, _decodedFrameCount - _decodedFrameOffset);
            _decodedFrameOffset += skip;
            _position += skip;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _decoder.Dispose();
        if (_ownsWorkspace)
        {
            _workspace.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OpusPcm32Reader));
        }
    }
}
