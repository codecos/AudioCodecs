using System.Runtime.InteropServices;
using No_Glass_Player.AudioDecodeCore.Decode.Pcm;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Ape;

// 播放路径只保留一个压缩帧和一个解码帧，内存占用与歌曲时长无关。
// seek 先按 PCM 位置定位 APE 帧，再在已解码帧内移动游标。
internal sealed class ApeStreamingReader : ISelfHostedPcm32Reader
{
    private readonly FileStream _stream;
    private readonly ApeReaderWorkspace _workspace;
    private int _frameIndex = -1;
    private int _frameOffset;
    private int _frameBlocks;
    private long _positionFrames;
    private bool _disposed;

    internal ApeStreamingReader(string path)
    {
        Info = ApeDecoder.ReadInfo(path);
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
        _workspace = new ApeReaderWorkspace(Info);
    }

    internal ApeInfo Info { get; }
    internal long PositionFrames => _positionFrames;
    public int SampleRate => Info.SampleRate;
    public double DurationSeconds => Info.TotalPcmFrames / (double)Info.SampleRate;

    public void Seek(TimeSpan position)
    {
        ThrowIfDisposed();
        var target = Math.Clamp((long)(Math.Max(0d, position.TotalSeconds) * Info.SampleRate), 0, Info.TotalPcmFrames);
        _positionFrames = target;
        if (target == Info.TotalPcmFrames)
        {
            _frameIndex = Info.TotalFrames;
            _frameOffset = 0;
            _frameBlocks = 0;
            return;
        }

        var frame = (int)Math.Min(target / Info.BlocksPerFrame, Info.TotalFrames - 1L);
        LoadFrame(frame);
        _frameOffset = checked((int)(target - ((long)frame * Info.BlocksPerFrame)));
    }

    public int ReadStereoPcm32Frames(Span<byte> destination)
    {
        var samples = destination.Length / sizeof(int);
        return ReadStereoPcm32Frames(MemoryMarshal.Cast<byte, int>(destination[..(samples * sizeof(int))]));
    }

    public int ReadStereoPcm32Frames(Span<int> destination)
    {
        ThrowIfDisposed();
        var capacity = destination.Length / 2;
        var written = 0;
        while (written < capacity && _positionFrames < Info.TotalPcmFrames)
        {
            var expectedFrame = (int)Math.Min(_positionFrames / Info.BlocksPerFrame, Info.TotalFrames - 1L);
            if (_frameIndex != expectedFrame) LoadFrame(expectedFrame);
            var available = _frameBlocks - _frameOffset;
            var take = Math.Min(capacity - written, available);
            CopyStereo(destination.Slice(written * 2, take * 2), _frameOffset, take);
            written += take;
            _frameOffset += take;
            _positionFrames += take;
        }
        return written;
    }

    public int ReadStereoWorkFrames(Span<long> destination)
    {
        const int workFractionalBits = 28;
        ThrowIfDisposed();
        var capacity = destination.Length / 2;
        var written = 0;
        while (written < capacity && _positionFrames < Info.TotalPcmFrames)
        {
            var expectedFrame = (int)Math.Min(_positionFrames / Info.BlocksPerFrame, Info.TotalFrames - 1L);
            if (_frameIndex != expectedFrame) LoadFrame(expectedFrame);
            var take = Math.Min(capacity - written, _frameBlocks - _frameOffset);
            for (var i = 0; i < take; i++)
            {
                var source = (_frameOffset + i) * Info.Channels;
                var target = (written + i) * 2;
                var left = (long)_workspace.Pcm[source] << workFractionalBits;
                destination[target] = left;
                destination[target + 1] = Info.Channels == 1
                    ? left : (long)_workspace.Pcm[source + 1] << workFractionalBits;
            }
            written += take;
            _frameOffset += take;
            _positionFrames += take;
        }
        return written;
    }

    private void LoadFrame(int frameIndex)
    {
        _frameBlocks = Info.Version < 3980
            ? ApeLegacyDecoder.DecodeFrameToPcm32(_stream, Info, frameIndex, _workspace.Compressed, _workspace.Pcm,
                _workspace.LegacyResidual0!, _workspace.LegacyResidual1!, _workspace.LegacyDecoded0!, _workspace.LegacyDecoded1!,
                _workspace.Legacy!)
            : ApeDecoder.DecodeModernFrameToPcm32(_stream, Info, frameIndex, _workspace.Compressed, _workspace.Pcm, _workspace.Modern!);
        _frameIndex = frameIndex;
        _frameOffset = 0;
    }

    private void CopyStereo(Span<int> destination, int sourceFrame, int frameCount)
    {
        if (Info.Channels == 2)
        {
            _workspace.Pcm.AsSpan(sourceFrame * 2, frameCount * 2).CopyTo(destination);
            return;
        }
        for (var i = 0; i < frameCount; i++)
        {
            var sample = _workspace.Pcm[sourceFrame + i];
            destination[i * 2] = sample;
            destination[i * 2 + 1] = sample;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
        _workspace.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
