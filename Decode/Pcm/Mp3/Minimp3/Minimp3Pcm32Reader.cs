using System.IO;
using System.Runtime.InteropServices;
using Minimp3;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Mp3;

/// <summary>
/// minimp3_ex 的 PCM32 游标适配。解码核心直接写入 Int32；这里仅负责
/// 文件游标、seek、单声道扩展和现有 PlayerHost reader 契约，不再经过 Float32 或 PCM16。
/// </summary>
internal sealed class Minimp3Pcm32Reader : IDisposable
{
    private readonly mp3dec_ex_t _decoder;
    private readonly Mp3ReaderWorkspace _workspace;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly bool _ownsWorkspace;
    private readonly Stream? _inputStream;
    private bool _disposed;

    private Minimp3Pcm32Reader(mp3dec_ex_t decoder, int sampleRate, int channels, Mp3ReaderWorkspace workspace, bool ownsWorkspace, Stream? inputStream)
    {
        _decoder = decoder;
        _sampleRate = sampleRate;
        _channels = Math.Max(1, channels);
        _workspace = workspace;
        _ownsWorkspace = ownsWorkspace;
        _inputStream = inputStream;
        _workspace.EnsurePcm32Capacity(0);
    }

    internal int SampleRate => _sampleRate;
    internal int Channels => _channels;
    internal long DurationFrames => _sampleRate > 0
        ? (long)Math.Max(0d, Math.Round(DurationSeconds * _sampleRate, MidpointRounding.ToZero))
        : 0L;
    internal double DurationSeconds => _sampleRate > 0
        ? _decoder.samples / (double)_channels / _sampleRate
        : 0d;

    // 路径入口用于离线探测和非 IOCP 调用方；生产流式播放使用下面的 Stream 入口。
    internal static bool TryCreate(string path, Mp3ReaderWorkspace? workspace,
        out Minimp3Pcm32Reader? cursor, out string? error)
    {
        cursor = null;
        error = null;
        mp3dec_ex_t? decoder = null;
        Mp3ReaderWorkspace? ownedWorkspace = null;
        try
        {
            decoder = new mp3dec_ex_t();
            var result = minimp3_ex.mp3dec_ex_open(decoder, path, minimp3_ex.MP3D_SEEK_TO_SAMPLE);
            if (result != 0)
            {
                decoder.Dispose();
                error = $"minimp3 open failed: {result}";
                return false;
            }

            var info = decoder.info;
            if (info.hz <= 0 || info.channels <= 0)
            {
                decoder.Dispose();
                error = "minimp3 did not return valid stream information.";
                return false;
            }

            var actualWorkspace = workspace ?? (ownedWorkspace = new Mp3ReaderWorkspace());
            cursor = new Minimp3Pcm32Reader(decoder, info.hz, info.channels, actualWorkspace, workspace is null, inputStream: null);
            ownedWorkspace = null;
            return true;
        }
        catch (Exception ex)
        {
            decoder?.Dispose();
            ownedWorkspace?.Dispose();
            error = ex.Message;
            return false;
        }
    }

    internal static bool TryCreate(Func<Stream> openReadStream, Mp3ReaderWorkspace? workspace,
        out Minimp3Pcm32Reader? cursor, out string? error)
    {
        cursor = null;
        error = null;
        Stream? input = null;
        mp3dec_ex_t? decoder = null;
        Mp3ReaderWorkspace? ownedWorkspace = null;
        try
        {
            input = openReadStream();
            var io = new minimp3_ex.mp3dec_io_t
            {
                read = buffer => input.Read(buffer, 0, buffer.Length),
                seek = offset =>
                {
                    input.Seek(offset, SeekOrigin.Begin);
                    return 0;
                }
            };
            var actualWorkspace = workspace ?? (ownedWorkspace = new Mp3ReaderWorkspace());
            decoder = actualWorkspace.AcquireDecoder();
            var result = minimp3_ex.mp3dec_ex_open_cb(decoder, io, minimp3_ex.MP3D_SEEK_TO_SAMPLE);
            if (result != 0)
            {
                decoder.Dispose();
                input.Dispose();
                error = $"minimp3 open failed: {result}";
                return false;
            }

            var info = decoder.info;
            if (info.hz <= 0 || info.channels <= 0)
            {
                decoder.Dispose();
                input.Dispose();
                error = "minimp3 did not return valid stream information.";
                return false;
            }

            cursor = new Minimp3Pcm32Reader(decoder, info.hz, info.channels, actualWorkspace, workspace is null, input);
            ownedWorkspace = null;
            return true;
        }
        catch (Exception ex)
        {
            if (decoder is not null) minimp3_ex.mp3dec_ex_close(decoder);
            input?.Dispose();
            ownedWorkspace?.Dispose();
            error = ex.Message;
            return false;
        }
    }

    internal void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = (ulong)Math.Max(0d, Math.Round(position.TotalSeconds * _sampleRate * _channels, MidpointRounding.ToZero));
        var result = minimp3_ex.mp3dec_ex_seek(_decoder, target);
        if (result != 0) throw new InvalidOperationException($"minimp3 sample seek failed: {result}");
    }

    internal int ReadStereoPcm32Frames(Span<int> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var requested = destination.Length / 2;
        var stage = _workspace.Pcm32Stage;
        var stageFrames = Math.Min(requested, stage.Length / 2);
        var written = 0;
        while (written < stageFrames)
        {
            var sourceSamples = Math.Min(minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME, (stageFrames - written) * _channels);
            var count = minimp3_ex.mp3dec_ex_read_i32(_decoder, stage.Slice(written * 2, sourceSamples));
            if (count <= 0) break;
            var frames = count / _channels;
            if (_channels == 1)
            {
                for (var frame = frames - 1; frame >= 0; frame--)
                {
                    var left = stage[written * 2 + frame];
                    var target = (written + frame) * 2;
                    stage[target] = left;
                    stage[target + 1] = left;
                }
            }
            else
            {
                for (var frame = 0; frame < frames; frame++)
                {
                    var source = frame * 2;
                    var target = (written + frame) * 2;
                    stage[target] = stage[written * 2 + source];
                    stage[target + 1] = stage[written * 2 + source + 1];
                }
            }
            written += frames;
            if (frames == 0) break;
        }
        stage[..(written * 2)].CopyTo(destination);
        return written;
    }

    internal int ReadStereoPcm32Frames(Span<byte> destination)
    {
        // 与 Groovy 游标一样按块读取；逐帧调用会把解码器调用次数放大到
        // 音频帧数，并在 seek/恢复时拖住 PlayerHost 的命令和输出循环。
        var samples = MemoryMarshal.Cast<byte, int>(destination);
        return ReadStereoPcm32Frames(samples);
    }

    internal int ReadStereoWorkFrames(Span<long> destination)
    {
        var requestedFrames = destination.Length / 2;
        var stage = _workspace.Pcm32Stage;
        var total = 0;
        while (total < requestedFrames)
        {
            var chunkFrames = Math.Min(requestedFrames - total, stage.Length / 2);
            var read = ReadStereoPcm32Frames(stage[..(chunkFrames * 2)]);
            if (read <= 0)
            {
                break;
            }

            var source = stage[..(read * 2)];
            var target = destination.Slice(total * 2, read * 2);
            for (var i = 0; i < source.Length; i++)
            {
                target[i] = (long)source[i] << 28;
            }

            total += read;
            if (read < chunkFrames)
            {
                break;
            }
        }

        return total;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        minimp3_ex.mp3dec_ex_close(_decoder);
        _inputStream?.Dispose();
        if (_ownsWorkspace) _workspace.Dispose();
    }
}
