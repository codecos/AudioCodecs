using No_Glass_Player.PlayerHost.Playback;

namespace Minimp3;

internal readonly struct Mp3IndexEntry
{
    internal readonly ulong Sample;
    internal readonly int Offset;

    internal Mp3IndexEntry(ulong sample, int offset)
    {
        Sample = sample;
        Offset = offset;
    }
}

internal sealed class Mp3NativeIndex : IDisposable
{
    private NativeBufferOwner<Mp3IndexEntry>? _owner;
    internal int Count { get; private set; }

    internal Mp3IndexEntry this[int index] => _owner!.Span[index];

    internal void Clear() => Count = 0;

    internal void Add(ulong sample, int offset)
    {
        EnsureCapacity(Count + 1);
        _owner!.Span[Count++] = new Mp3IndexEntry(sample, offset);
    }

    internal int FindOffset(int offset)
    {
        for (var i = 0; i < Count; i++)
        {
            if (_owner!.Span[i].Offset == offset) return i;
        }

        return -1;
    }

    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
        Count = 0;
    }

    private void EnsureCapacity(int required)
    {
        if (_owner is not null && _owner.CapacityElements >= required) return;
        var capacity = _owner is null ? 4096 : _owner.CapacityElements;
        while (capacity < required) capacity = checked(capacity * 2);
        var replacement = new NativeBufferOwner<Mp3IndexEntry>(capacity);
        if (_owner is not null && Count > 0)
            _owner.Span[..Count].CopyTo(replacement.Span);
        _owner?.Dispose();
        _owner = replacement;
    }
}

public sealed class mp3dec_file_info_t
{
    public short[] buffer = Array.Empty<short>();
    public ulong samples;
    public int channels, hz, layer, avg_bitrate_kbps;
    public uint frames;
    public int delay, padding;
    public bool vbr_tag_found;
}

/// <summary>
/// Float-output counterpart of the root C <c>mp3dec_file_info_t</c> when
/// <c>MINIMP3_FLOAT_OUTPUT</c> is selected. Keeping this as a distinct type
/// avoids changing the binary shape of the short-output API at runtime.
/// </summary>
public sealed class mp3dec_file_info_f32_t
{
    public float[] buffer = Array.Empty<float>();
    public ulong samples;
    public int channels, hz, layer, avg_bitrate_kbps;
    public uint frames;
    public int delay, padding;
    public bool vbr_tag_found;
}

public sealed class mp3dec_ex_t : IDisposable
{
    internal mp3dec_t decoder = new();
    internal byte[] data = Array.Empty<byte>();
    internal int offset;
    internal int startOffset;
    internal int endOffset;
    internal readonly short[] frameBuffer = new short[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
    internal readonly int[] int32FrameBuffer = new int[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
    internal readonly float[] floatFrameBuffer = new float[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
    internal readonly mp3dec_frame_info_t frameInfo = new();
    internal readonly mp3dec_t probeDecoder = new();
    internal readonly short[] probePcm = new short[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
    internal readonly mp3dec_frame_info_t probeInfo = new();
    internal int bufferedSamples;
    internal int bufferConsumed;
    internal int int32BufferedSamples;
    internal int int32BufferConsumed;
    internal int floatBufferedSamples;
    internal int floatBufferConsumed;
    internal int lastChannels, lastHz, lastLayer;
    internal bool opened;
    // 索引容量随 decoder 复用；关闭时只清空元素，避免丢失旧容量并在 ZeroGC 下泄漏。
    internal readonly Mp3NativeIndex index = new();
    // Reused by callback seek-index scans; these must not be allocated per seek.
    internal readonly mp3dec_t indexScanState = new();
    internal readonly short[] indexScanPcm = new short[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
    internal readonly mp3dec_frame_info_t indexScanInfo = new();
    internal byte[] indexScanBuffer = new byte[128 * 1024];
    internal byte[] indexScanScratch = new byte[64 * 1024];
    internal int flags;
    internal ulong currentSample;
    internal int toSkip;
    internal Func<byte[], int>? readCallback;
    internal Func<long, int>? seekCallback;
    internal byte[] callbackBuffer = new byte[128 * 1024];
    // 回调读取复用固定 scratch，避免每次 IOCP/流短读都创建临时 byte[]。
    // scratch 只承载一次 read 的数据，随后复制进 callbackBuffer，保持现有窗口和 seek 语义。
    internal byte[] callbackScratch = new byte[64 * 1024];
    internal int callbackCount;
    internal long callbackPosition;
    internal long callbackStart;
    internal bool callbackEof;
    internal bool callbackError;
    internal bool callbackTagsTrimmed;
    internal bool callbackMode;
    internal bool metadataOnlyTag;

    public ulong samples;
    public bool vbr_tag_found;
    public uint vbr_frames;
    public int start_delay, end_padding;
    public int last_error;
    public mp3dec_frame_info_t info => frameInfo;
    /// <summary>Decoder policy used by the embedded low-level decoder.</summary>
    public Minimp3Options Options { get => decoder.Options; set => decoder.Options = value; }

    public void Dispose()
    {
        minimp3_ex.mp3dec_ex_close(this);
        index.Dispose();
    }
}

/// <summary>Managed equivalents of the buffer-oriented minimp3_ex functions.</summary>
public static class minimp3_ex
{
    private static readonly int[] Mpeg1Layer1Bitrates = { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 };
    private static readonly int[] Mpeg1Layer2Bitrates = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 };
    private static readonly int[] Mpeg1Layer3Bitrates = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
    private static readonly int[] Mpeg2Layer1Bitrates = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 };
    private static readonly int[] Mpeg2Layer23Bitrates = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };
    public const int MP3D_SEEK_TO_BYTE = 0;
    public const int MP3D_SEEK_TO_SAMPLE = 1;
    public const int MP3D_DO_NOT_SCAN = 2;
    public const int MP3D_ALLOW_MONO_STEREO_TRANSITION = 4;
#if MINIMP3_ALLOW_MONO_STEREO_TRANSITION
    public const int MP3D_FLAGS_MASK = MP3D_SEEK_TO_SAMPLE | MP3D_DO_NOT_SCAN | MP3D_ALLOW_MONO_STEREO_TRANSITION;
#else
    public const int MP3D_FLAGS_MASK = MP3D_SEEK_TO_SAMPLE | MP3D_DO_NOT_SCAN;
#endif
    public const int MP3D_E_PARAM = -1;
    public const int MP3D_E_MEMORY = -2;
    public const int MP3D_E_IOERROR = -3;
    public const int MP3D_E_USER = -4;
    public const int MP3D_E_DECODE = -5;
    public delegate int MP3D_PROGRESS_CB(object? userData, long fileSize, long offset, mp3dec_frame_info_t info);
    public delegate int MP3D_ITERATE_CB(object? userData, byte[] frame, int frameSize, int freeFormatBytes, long offset, mp3dec_frame_info_t info);
    /// <summary>Full root callback shape, including the remaining input window.</summary>
    public delegate int MP3D_ITERATE_CB_EX(object? userData, byte[] frame, int frameSize,
        int freeFormatBytes, int bufferSize, long offset, mp3dec_frame_info_t info);
    public sealed class mp3dec_io_t
    {
        public required Func<byte[], int> read;
        public required Func<long, int> seek;
    }

    private static ReadOnlySpan<byte> SkipTags(byte[] input)
    {
        int start = Math.Min(input.Length, SkipId3v2(input, input.Length));
        int length = input.Length - start;
        length = SkipId3v1AndApe(input, start, length);
        return input.AsSpan(start, length);
    }

    private static int Id3v2PrefixSize(byte[] input, int count)
        => SkipId3v2(input, count);

    // Direct counterpart of minimp3_ex.h:mp3dec_skip_id3v2.
    private static int SkipId3v2(byte[] input, int count)
    {
#if MINIMP3_NOSKIP_ID3V2
        return 0;
#else
        if (count < 10 || input[0] != (byte)'I' || input[1] != (byte)'D' || input[2] != (byte)'3' ||
            (input[5] & 0x0f) != 0 || (input[6] & 0x80) != 0 || (input[7] & 0x80) != 0 ||
            (input[8] & 0x80) != 0 || (input[9] & 0x80) != 0) return 0;
        int size = ((input[6] & 0x7f) << 21) | ((input[7] & 0x7f) << 14) |
                   ((input[8] & 0x7f) << 7) | (input[9] & 0x7f);
        long total = 10L + size + ((input[5] & 0x10) != 0 ? 10 : 0);
        return total > int.MaxValue ? 0 : (int)total;
#endif
    }

    // Direct counterpart of minimp3_ex.h:mp3dec_skip_id3v1, including the
    // APEv2 footer branch that follows it in the root implementation.
    private static int SkipId3v1AndApe(byte[] input, int start, int length)
    {
#if !MINIMP3_NOSKIP_ID3V1
        if (length >= 128 && input[start + length - 128] == (byte)'T' &&
            input[start + length - 127] == (byte)'A' && input[start + length - 126] == (byte)'G')
        {
            length -= 128;
            if (length >= 227 && input[start + length - 227] == (byte)'T' &&
                input[start + length - 226] == (byte)'A' && input[start + length - 225] == (byte)'G' &&
                input[start + length - 224] == (byte)'+')
                length -= 227;
        }
#endif
#if !MINIMP3_NOSKIP_APEV2
        if (length > 32 && input[start + length - 32] == (byte)'A' &&
            input[start + length - 31] == (byte)'P' && input[start + length - 30] == (byte)'E' &&
            input[start + length - 29] == (byte)'T' && input[start + length - 28] == (byte)'A' &&
            input[start + length - 27] == (byte)'G' && input[start + length - 26] == (byte)'E' &&
            input[start + length - 25] == (byte)'X')
        {
            int footer = start + length - 32;
            uint tagSize = (uint)(input[footer + 12] | (input[footer + 13] << 8) |
                                  (input[footer + 14] << 16) | (input[footer + 15] << 24));
            length -= 32;
            if (tagSize <= (uint)length) length -= (int)tagSize;
        }
#endif
        return length;
    }

    private static int TrimCallbackTrailer(byte[] buffer, int count)
        => SkipId3v1AndApe(buffer, 0, count);

    private enum VbrTagResult
    {
        None,
        NoFrames,
        Valid
    }

    private static VbrTagResult CheckVbrTag(byte[] frame, int frameSize, out uint frames, out int delay, out int padding)
    {
        frames = 0; delay = padding = 0;
        // The tag follows the Layer III side information. Searching arbitrary
        // payload bytes (as the earlier implementation did) accepts false tags.
        if (frameSize < 12 || frame[0] != 0xff || (frame[1] & 0xe0) != 0xe0) return VbrTagResult.None;
        int layer = 4 - ((frame[1] >> 1) & 3);
        if (layer != 3) return VbrTagResult.None;
        if (!Mp3Decoder.TryGetVbrTagOffset(frame, frameSize, out int tag)) return VbrTagResult.None;
        bool xing = frame[tag] == (byte)'X' && frame[tag + 1] == (byte)'i' && frame[tag + 2] == (byte)'n' && frame[tag + 3] == (byte)'g';
        bool info = frame[tag] == (byte)'I' && frame[tag + 1] == (byte)'n' && frame[tag + 2] == (byte)'f' && frame[tag + 3] == (byte)'o';
        if (!xing && !info) return VbrTagResult.None;
        // minimp3_ex.h intentionally treats this as the low byte.  The
        // preceding bytes are padding in the Xing/Info layout.
        int flags = frame[tag + 7];
        int p = tag + 8;
        if ((flags & 1) == 0) return VbrTagResult.NoFrames;
        if (p + 4 > frameSize) return VbrTagResult.None;
        frames = (uint)((frame[p] << 24) | (frame[p + 1] << 16) | (frame[p + 2] << 8) | frame[p + 3]);
        p += 4;
        if ((flags & 2) != 0) { if (p + 4 > frameSize) return VbrTagResult.None; p += 4; }
        if ((flags & 4) != 0) { if (p + 100 > frameSize) return VbrTagResult.None; p += 100; }
        if ((flags & 8) != 0) { if (p + 4 > frameSize) return VbrTagResult.None; p += 4; }
        // LAME/Lavc extension: the delay/padding triplet is 21 bytes after
        // the extension marker, encoded as 12-bit delay and padding fields.
        if (p >= frameSize) return VbrTagResult.None;
        if (frame[p] != 0)
        {
            if (frameSize - p <= 35) return VbrTagResult.None;
            p += 21;
            delay = ((frame[p] << 4) | (frame[p + 1] >> 4)) + 529;
            padding = (((frame[p + 1] & 0x0f) << 8) | frame[p + 2]) - 529;
        }
        // The C helper returns success whenever FRAMES_FLAG is present; a
        // zero frame count is still a valid (empty) Xing/Info declaration.
        return VbrTagResult.Valid;
    }

    private static void CopyFrameInfo(FrameInfo source, mp3dec_frame_info_t target)
    {
        target.frame_bytes = source.FrameBytes;
        target.frame_offset = source.FrameOffset;
        target.channels = source.Channels;
        target.hz = source.Hz;
        target.layer = source.Layer;
        target.bitrate_kbps = source.BitrateKbps;
    }

    public static int mp3dec_load_buf(mp3dec_t dec, byte[] buf, mp3dec_file_info_t info)
        => LoadBuffer(dec, buf, info, null, null, 0);

    public static int mp3dec_load_buf(mp3dec_t dec, byte[] buf, int bufSize,
        mp3dec_file_info_t info, MP3D_PROGRESS_CB? progress = null, object? userData = null)
    {
        if (buf is null || (uint)bufSize > (uint)buf.Length) return MP3D_E_PARAM;
        return LoadBuffer(dec, buf.AsSpan(0, bufSize).ToArray(), info, progress, userData, bufSize);
    }

    private static int LoadBuffer(mp3dec_t dec, byte[] buf, mp3dec_file_info_t info,
        MP3D_PROGRESS_CB? progress, object? userData, long progressFileSize)
    {
        if (dec is null || buf is null || info is null) return MP3D_E_PARAM;
        info.buffer = Array.Empty<short>();
        info.samples = 0;
        info.channels = info.hz = info.layer = info.avg_bitrate_kbps = 0;
        info.frames = 0;
        info.delay = info.padding = 0;
        info.vbr_tag_found = false;
        var output = new List<short>();
        int sourcePrefix = Id3v2PrefixSize(buf, buf.Length);
        int startOffset = Math.Min(sourcePrefix, buf.Length);
        int endOffset = Math.Min(buf.Length, startOffset + SkipTags(buf).Length);
        minimp3.mp3dec_init(dec);
        var pcm = new short[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
        var frame = new mp3dec_frame_info_t();
        int offset = startOffset;
        int decodedFrames = 0;
        long bitrateSum = 0;
        int streamChannels = 0, streamHz = 0, streamLayer = 0;
        bool channelTransition = false;
        byte[]? firstFrame = null;
        int firstFrameOutputSamples = 0;
        while (offset < endOffset)
        {
            int count = dec.DecodeFrameAt(buf, offset, endOffset - offset, pcm, frame);
            if (frame.frame_bytes <= 0) break;
            bool isFirstFrame = firstFrame is null && frame.frame_offset >= 0 && offset + frame.frame_offset + frame.frame_bytes <= endOffset;
            if (isFirstFrame)
                firstFrame = buf.AsSpan(offset + frame.frame_offset, frame.frame_bytes).ToArray();
            offset += frame.frame_bytes;
            if (count > 0)
            {
                if (decodedFrames != 0 &&
                    (frame.hz != streamHz || frame.layer != streamLayer ||
                     (frame.channels != streamChannels &&
                      (dec.Options & Minimp3Options.AllowMonoStereoTransition) == 0)))
                    return MP3D_E_DECODE;
                if (decodedFrames == 0)
                {
                    streamChannels = frame.channels;
                    streamHz = frame.hz;
                    streamLayer = frame.layer;
                }
                else if (frame.channels != streamChannels &&
                         (dec.Options & Minimp3Options.AllowMonoStereoTransition) != 0)
                {
                    streamChannels = 0;
                    channelTransition = true;
                }
                int frameOutputSamples = count * Math.Max(1, frame.channels);
                for (int i = 0; i < frameOutputSamples; i++) output.Add(pcm[i]);
                if (isFirstFrame) firstFrameOutputSamples = frameOutputSamples;
                decodedFrames++;
                bitrateSum += frame.bitrate_kbps;
            }
            if (!channelTransition) info.channels = frame.channels;
            info.hz = frame.hz;
            info.layer = frame.layer;
            info.avg_bitrate_kbps = frame.bitrate_kbps;
            if (progress is not null)
            {
                int callbackResult = progress(userData, progressFileSize, sourcePrefix + offset - startOffset, frame);
                if (callbackResult != 0) return callbackResult;
            }
        }
        info.frames = (uint)decodedFrames;
        if (decodedFrames != 0) info.avg_bitrate_kbps = (int)(bitrateSum / decodedFrames);
        if (channelTransition) info.channels = 0;
        if (firstFrame is not null)
        {
            var vbrResult = CheckVbrTag(firstFrame, firstFrame.Length, out uint vbrFrames, out int delay, out int padding);
            if (vbrResult == VbrTagResult.Valid)
            {
                info.frames = vbrFrames; info.delay = delay; info.padding = padding; info.vbr_tag_found = true;
                int channels = Math.Max(1, info.channels);
                int tagSamples = Math.Min(output.Count, firstFrameOutputSamples);
                if (tagSamples > 0) output.RemoveRange(0, tagSamples);
                int trimStart = Math.Min(output.Count, Math.Max(0, delay) * channels);
                if (trimStart > 0) output.RemoveRange(0, trimStart);
                int trimEnd = Math.Min(output.Count, Math.Max(0, padding) * channels);
                if (trimEnd > 0) output.RemoveRange(output.Count - trimEnd, trimEnd);
            }
            else if (vbrResult == VbrTagResult.NoFrames && firstFrameOutputSamples > 0)
            {
                output.RemoveRange(0, Math.Min(output.Count, firstFrameOutputSamples));
                decodedFrames = Math.Max(0, decodedFrames - 1);
            }
        }
        info.buffer = output.ToArray();
        info.samples = (ulong)info.buffer.Length;
        // A Xing/Info frame without a FRAMES field is metadata-only. The C
        // loader accepts a stream consisting solely of that frame and reports
        // an empty, successful result rather than MP3D_E_DECODE.
        return 0;
    }

    /// <summary>Loads a buffer using the decoder's native float sample path.</summary>
    public static int mp3dec_load_buf_f32(mp3dec_t dec, byte[] buf, mp3dec_file_info_f32_t info)
    {
        if (dec is null || buf is null || info is null) return MP3D_E_PARAM;
        info.buffer = Array.Empty<float>();
        info.samples = 0;
        info.channels = info.hz = info.layer = info.avg_bitrate_kbps = 0;
        info.frames = 0;
        info.delay = info.padding = 0;
        info.vbr_tag_found = false;

        int sourceStart = Math.Min(Id3v2PrefixSize(buf, buf.Length), buf.Length);
        int sourceEnd = Math.Min(buf.Length, sourceStart + SkipTags(buf).Length);
        minimp3.mp3dec_init(dec);
        var output = new List<float>();
        var pcm = new float[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
        var frame = new mp3dec_frame_info_t();
        byte[]? firstFrame = null;
        int firstFrameOutputSamples = 0;
        int offset = sourceStart;
        int decodedFrames = 0;
        long bitrateSum = 0;
        while (offset < sourceEnd)
        {
            int count = dec.DecodeFrameFloatAt(buf, offset, sourceEnd - offset, pcm, frame);
            if (frame.frame_bytes <= 0) break;
            bool frameInSource = frame.frame_offset >= 0 && frame.frame_offset <= sourceEnd - offset &&
                                 frame.frame_bytes <= sourceEnd - offset - frame.frame_offset;
            int frameStart = frameInSource ? offset + frame.frame_offset : -1;
            if (firstFrame is null && frameInSource)
                firstFrame = buf.AsSpan(frameStart, frame.frame_bytes).ToArray();
            offset += frame.frame_bytes;
            if (count > 0)
            {
                int n = count * Math.Max(1, frame.channels);
                for (int i = 0; i < n; i++) output.Add(pcm[i]);
                if (decodedFrames == 0) firstFrameOutputSamples = n;
                decodedFrames++;
                bitrateSum += frame.bitrate_kbps;
            }
            info.channels = frame.channels;
            info.hz = frame.hz;
            info.layer = frame.layer;
        }
        info.frames = (uint)decodedFrames;
        if (decodedFrames != 0) info.avg_bitrate_kbps = (int)(bitrateSum / decodedFrames);
        if (firstFrame is not null)
        {
            var result = CheckVbrTag(firstFrame, firstFrame.Length, out uint frames, out int delay, out int padding);
            if (result == VbrTagResult.Valid)
            {
                info.frames = frames;
                info.delay = delay;
                info.padding = padding;
                info.vbr_tag_found = true;
                int channels = Math.Max(1, info.channels);
                output.RemoveRange(0, Math.Min(output.Count, firstFrameOutputSamples));
                output.RemoveRange(0, Math.Min(output.Count, Math.Max(0, delay) * channels));
                int trim = Math.Min(output.Count, Math.Max(0, padding) * channels);
                if (trim != 0) output.RemoveRange(output.Count - trim, trim);
            }
            else if (result == VbrTagResult.NoFrames)
            {
                output.RemoveRange(0, Math.Min(output.Count, firstFrameOutputSamples));
            }
        }
        info.buffer = output.ToArray();
        info.samples = (ulong)info.buffer.Length;
        return 0;
    }

    public static int mp3dec_load_f32(mp3dec_t dec, string fileName, mp3dec_file_info_f32_t info)
    {
        if (dec is null || fileName is null || info is null) return MP3D_E_PARAM;
        try { return mp3dec_load_buf_f32(dec, File.ReadAllBytes(fileName), info); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return MP3D_E_IOERROR; }
    }

    /// <summary>Float-output callback loader using the incremental ex reader.</summary>
    public static int mp3dec_load_cb_f32(mp3dec_t dec, mp3dec_io_t io,
        mp3dec_file_info_f32_t info, MP3D_PROGRESS_CB? progress = null, object? userData = null)
    {
        if (dec is null || io is null || info is null) return MP3D_E_PARAM;
        info.buffer = Array.Empty<float>();
        info.samples = 0;
        info.channels = info.hz = info.layer = info.avg_bitrate_kbps = 0;
        info.frames = 0;
        info.delay = info.padding = 0;
        info.vbr_tag_found = false;
        using var ex = new mp3dec_ex_t();
        int open = OpenCallback(ex, io, MP3D_SEEK_TO_BYTE, null);
        if (open != 0) return open;
        var output = new List<float>();
        int frameCount = 0;
        long bitrateSum = 0;
        int streamChannels = 0, streamHz = 0, streamLayer = 0;
        bool transition = false;
        while (true)
        {
            long offset = ex.callbackPosition;
            int count = mp3dec_ex_read_frame_f32(ex, out var frame, out var frameInfo);
            if (count == 0) break;
            if (frameCount != 0 && (frameInfo.hz != streamHz || frameInfo.layer != streamLayer))
            {
                ex.last_error = MP3D_E_DECODE;
                break;
            }
            if (frameCount == 0)
            {
                streamChannels = frameInfo.channels;
                streamHz = frameInfo.hz;
                streamLayer = frameInfo.layer;
            }
            else if (frameInfo.channels != streamChannels)
            {
                if ((dec.Options & Minimp3Options.AllowMonoStereoTransition) == 0)
                {
                    ex.last_error = MP3D_E_DECODE;
                    break;
                }
                transition = true;
            }
            output.AddRange(frame);
            frameCount++;
            bitrateSum += frameInfo.bitrate_kbps;
            info.channels = frameInfo.channels;
            info.hz = frameInfo.hz;
            info.layer = frameInfo.layer;
            if (progress is not null)
            {
                int callbackResult = progress(userData, ex.callbackBuffer.Length, offset, frameInfo);
                if (callbackResult != 0) return callbackResult;
            }
        }
        info.frames = (uint)frameCount;
        if (frameCount != 0) info.avg_bitrate_kbps = (int)(bitrateSum / frameCount);
        if (transition) info.channels = 0;
        info.delay = ex.start_delay;
        info.padding = ex.end_padding;
        info.vbr_tag_found = ex.vbr_tag_found;
        info.buffer = output.ToArray();
        info.samples = (ulong)info.buffer.Length;
        return ex.last_error == MP3D_E_DECODE ? MP3D_E_DECODE : 0;
    }

    public static int mp3dec_load(mp3dec_t dec, string fileName, mp3dec_file_info_t info, MP3D_PROGRESS_CB? progress = null, object? userData = null)
    {
        if (dec is null || fileName is null || info is null) return MP3D_E_PARAM;
        try
        {
            byte[] data = File.ReadAllBytes(fileName);
            return LoadBuffer(dec, data, info, progress, userData, data.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return MP3D_E_IOERROR; }
    }

    public static int mp3dec_iterate_buf(byte[] buf, MP3D_ITERATE_CB callback, object? userData = null)
    {
        if (buf is null || callback is null) return MP3D_E_PARAM;
        return mp3dec_iterate_buf(buf, buf.Length, callback, userData);
    }

    public static int mp3dec_iterate_buf(byte[] buf, int bufSize, MP3D_ITERATE_CB callback, object? userData = null)
    {
        if (buf is null || callback is null || (uint)bufSize > (uint)buf.Length) return MP3D_E_PARAM;
        return mp3dec_iterate_buf(buf, bufSize,
            (userData2, frame, frameSize, freeFormatBytes, _, offset, info) =>
                callback(userData2, frame, frameSize, freeFormatBytes, offset, info), userData);
    }

    /// <summary>Root-compatible iterate callback with the remaining-window argument.</summary>
    public static int mp3dec_iterate_buf(byte[] buf, int bufSize, MP3D_ITERATE_CB_EX callback, object? userData = null)
    {
        if (buf is null || callback is null || (uint)bufSize > (uint)buf.Length) return MP3D_E_PARAM;
        byte[] source = buf.AsSpan(0, bufSize).ToArray();
        int prefix = Id3v2PrefixSize(source, source.Length);
        int start = Math.Min(prefix, source.Length);
        int end = start + SkipTags(source).Length;
        var decoder = new mp3dec_t();
        minimp3.mp3dec_init(decoder);
        int cursor = start;
        while (cursor < end)
        {
            var info = decoder.InfoScratch;
            int probe = decoder.DecodeFrameAt(source, cursor, end - cursor, decoder.ShortScratch, info);
            if (info.frame_bytes <= 0) break;
            int frameOffset = Math.Clamp(info.frame_offset, 0, info.frame_bytes);
            int frameSize = info.frame_bytes - frameOffset;
            if (frameSize <= 0 || cursor + frameOffset + frameSize > end) break;
            int result = callback(userData, source.AsSpan(cursor + frameOffset, frameSize).ToArray(), frameSize,
                decoder.FreeFormatBytes, end - cursor - frameOffset, cursor + frameOffset, info);
            if (result != 0) return result;
            cursor += info.frame_bytes;
            if (probe <= 0 && cursor >= end) break;
        }
        return 0;
    }

    public static int mp3dec_iterate(string fileName, MP3D_ITERATE_CB callback, object? userData = null)
    {
        if (fileName is null || callback is null) return MP3D_E_PARAM;
        try { return mp3dec_iterate_buf(File.ReadAllBytes(fileName), callback, userData); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return MP3D_E_IOERROR; }
    }

    public static int mp3dec_iterate_cb(mp3dec_io_t io, MP3D_ITERATE_CB callback, object? userData = null)
        => IterateCallback(io, null, callback, null, userData);

    public static int mp3dec_iterate_cb(mp3dec_io_t io, MP3D_ITERATE_CB_EX callback, object? userData = null)
        => IterateCallback(io, null, null, callback, userData);

    private static int IterateCallback(mp3dec_io_t io, byte[]? workBuffer,
        MP3D_ITERATE_CB? callback, MP3D_ITERATE_CB_EX? callbackEx, object? userData = null)
    {
        if (io is null || (callback is null && callbackEx is null)) return MP3D_E_PARAM;
        if (!TrySeek(io, 0)) return MP3D_E_IOERROR;
        int freeFormatBytes = 0;
        byte[] buffer = workBuffer is { Length: >= 16 * 1024 } ? workBuffer : new byte[128 * 1024];
        int countInBuffer = 0;
        long absoluteOffset = 0;
        bool eof = false;
        var chunk = new byte[64 * 1024];
        mp3dec_t? iterateHeaderDecoder = null;
        mp3dec_frame_info_t? iterateInfo = null;
        var iterateScratchPcm = new short[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
        while (true)
        {
            if (!eof && countInBuffer < buffer.Length)
            {
                int room = buffer.Length - countInBuffer;
                if (room < chunk.Length)
                {
                    Array.Resize(ref buffer, checked(buffer.Length + chunk.Length));
                    room = buffer.Length - countInBuffer;
                }
                int read;
                try { read = io.read(chunk); }
                catch { return MP3D_E_IOERROR; }
                if (read < 0 || read > room) return MP3D_E_IOERROR;
                if (read == 0) eof = true;
                else chunk.AsSpan(0, read).CopyTo(buffer.AsSpan(countInBuffer));
                countInBuffer += Math.Max(0, read);
            }
            if (countInBuffer == 0) break;
            int frameOffset = Mp3Decoder.FindFrameForIterate(buffer, countInBuffer,
                ref freeFormatBytes, out int frameSize);
            if (frameSize <= 0 || frameOffset >= countInBuffer || frameOffset + frameSize > countInBuffer)
            {
                if (eof) break;
                // Keep the candidate bytes intact until the next read supplies
                // enough data to distinguish a partial frame from garbage.
                if (countInBuffer == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
                continue;
            }

            // Populate frame metadata without advancing any decoder state. The
            // root iterate path only inspects the validated header here.
            // 迭代扫描只需要头信息；复用同一个 decoder 和输入 scratch，避免每帧
            // 创建包含 reservoir/滤波状态的完整 Mp3Decoder。
            iterateHeaderDecoder ??= new mp3dec_t();
            minimp3.mp3dec_init(iterateHeaderDecoder);
            var info = iterateInfo ??= new mp3dec_frame_info_t();
            iterateHeaderDecoder.DecodeFrameAt(buffer, frameOffset, countInBuffer - frameOffset,
                iterateScratchPcm, info);
            info.frame_offset = frameOffset;
            info.frame_bytes = frameOffset + frameSize;
            int result = callbackEx is not null
                ? callbackEx(userData, buffer.AsSpan(frameOffset, frameSize).ToArray(), frameSize,
                    freeFormatBytes, countInBuffer - frameOffset, absoluteOffset + frameOffset, info)
                : callback!(userData, buffer.AsSpan(frameOffset, frameSize).ToArray(), frameSize,
                    freeFormatBytes, absoluteOffset + frameOffset, info);
            if (result != 0) return result;
            int consumed = info.frame_bytes;
            buffer.AsSpan(consumed, countInBuffer - consumed).CopyTo(buffer);
            countInBuffer -= consumed;
            absoluteOffset += consumed;
        }
        return 0;
    }

    /// <summary>
    /// C-compatible overload that accepts the caller-owned streaming work
    /// buffer. The managed implementation uses the same incremental reader;
    /// the buffer is retained as the minimum-capacity contract required by
    /// minimp3_ex.h.
    /// </summary>
    public static int mp3dec_iterate_cb(mp3dec_io_t io, byte[] buffer,
        MP3D_ITERATE_CB callback, object? userData = null)
    {
        if (buffer is null || buffer.Length < 16 * 1024) return MP3D_E_PARAM;
        return IterateCallback(io, buffer, callback, null, userData);
    }

    public static int mp3dec_iterate_cb(mp3dec_io_t io, byte[] buffer,
        MP3D_ITERATE_CB_EX callback, object? userData = null)
    {
        if (buffer is null || buffer.Length < 16 * 1024) return MP3D_E_PARAM;
        return IterateCallback(io, buffer, null, callback, userData);
    }

    public static int mp3dec_ex_open(mp3dec_ex_t dec, string fileName, int flags = MP3D_SEEK_TO_BYTE)
    {
        if (dec is null || fileName is null) return MP3D_E_PARAM;
        try { return mp3dec_ex_open_buf(dec, File.ReadAllBytes(fileName), flags); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return MP3D_E_IOERROR; }
    }

    public static int mp3dec_detect_cb(mp3dec_io_t io, byte[] buffer)
    {
        if (io is null || buffer is null) return MP3D_E_PARAM;
        if (buffer.Length < 16 * 1024) return MP3D_E_PARAM;
        if (!TrySeek(io, 0)) return MP3D_E_IOERROR;
        // The root implementation reads the ID3 detection header separately,
        // then fills the remainder of the fixed detection window.  Preserving
        // that sequence matters for callbacks which intentionally short-read.
        var header = new byte[10];
        int first;
        try { first = io.read(header); }
        catch { return MP3D_E_IOERROR; }
        if (first < 0 || first > header.Length) return MP3D_E_IOERROR;
        if (first < header.Length) return MP3D_E_USER;
        header.CopyTo(buffer, 0);
        int filled = header.Length;
        if (Id3v2PrefixSize(header, header.Length) > 0)
            return 0;

        var tail = new byte[buffer.Length - header.Length];
        int read;
        try { read = io.read(tail); }
        catch { return MP3D_E_IOERROR; }
        if (read < 0 || read > tail.Length) return MP3D_E_IOERROR;
        tail.AsSpan(0, read).CopyTo(buffer.AsSpan(header.Length));
        filled += read;
        if (filled < 16 * 1024)
        {
            // Match mp3dec_skip_id3v1's only effect needed by detection: a
            // trailing TAG is excluded from the frame search window.
            if (filled >= 128 && buffer.AsSpan(filled - 128, 3).SequenceEqual("TAG"u8))
                filled -= 128;
        }
        return mp3dec_detect_buf(buffer.AsSpan(0, filled).ToArray());
    }

    public static int mp3dec_load_cb(mp3dec_t dec, mp3dec_io_t io, mp3dec_file_info_t info, MP3D_PROGRESS_CB? progress = null, object? userData = null)
        => LoadCallback(dec, io, null, info, progress, userData);

    private static int LoadCallback(mp3dec_t dec, mp3dec_io_t io, byte[]? workBuffer,
        mp3dec_file_info_t info, MP3D_PROGRESS_CB? progress = null, object? userData = null)
    {
        if (dec is null || io is null || info is null) return MP3D_E_PARAM;
        info.buffer = Array.Empty<short>();
        info.samples = 0;
        info.channels = info.hz = info.layer = info.avg_bitrate_kbps = 0;
        info.frames = 0;
        info.delay = info.padding = 0;
        info.vbr_tag_found = false;
        var ex = new mp3dec_ex_t();
        int open;
        try
        {
            open = OpenCallback(ex, io, MP3D_SEEK_TO_BYTE, workBuffer);
        }
        catch
        {
            // 打开阶段失败时没有进入下面的 finally；这里显式断开回调并清理窗口，
            // 避免异常路径把 callback delegate、输入窗口和索引挂在临时 decoder 上。
            mp3dec_ex_close(ex);
            throw;
        }
        if (open != 0)
        {
            mp3dec_ex_close(ex);
            return open;
        }
        var output = new List<short>();
        int frameCount = 0;
        long bitrateSum = 0;
        long progressFileSize = ex.callbackBuffer.Length;
        int streamChannels = 0, streamHz = 0, streamLayer = 0;
        bool channelTransition = false;
        int savedDelay = 0, savedPadding = 0, savedError = 0;
        bool savedVbr = false;
        try
        {
            while (true)
            {
                long frameOffset = ex.callbackPosition;
                int count = mp3dec_ex_read_frame(ex, out var frame, out var frameInfo);
                if (count == 0) break;
                output.AddRange(frame);
                if (frameCount != 0 && (frameInfo.hz != streamHz || frameInfo.layer != streamLayer))
                {
                    ex.last_error = MP3D_E_DECODE;
                    break;
                }
                if (frameCount == 0)
                {
                    streamChannels = frameInfo.channels;
                    streamHz = frameInfo.hz;
                    streamLayer = frameInfo.layer;
                }
                else if (frameInfo.channels != streamChannels)
                {
                    if ((dec.Options & Minimp3Options.AllowMonoStereoTransition) == 0)
                    {
                        ex.last_error = MP3D_E_DECODE;
                        break;
                    }
                    channelTransition = true;
                }
                frameCount++;
                bitrateSum += frameInfo.bitrate_kbps;
                info.channels = frameInfo.channels;
                info.hz = frameInfo.hz;
                info.layer = frameInfo.layer;
                info.avg_bitrate_kbps += frameInfo.bitrate_kbps;
                if (progress is not null)
                {
                    int result = progress(userData, progressFileSize, frameOffset, frameInfo);
                    if (result != 0) return result;
                }
            }
        }
        finally
        {
            // Capture public result metadata before close clears the native
            // equivalent of the whole mp3dec_ex_t with memset.
            savedDelay = ex.start_delay;
            savedPadding = ex.end_padding;
            savedVbr = ex.vbr_tag_found;
            savedError = ex.last_error;
            mp3dec_ex_close(ex);
        }
        info.frames = (uint)frameCount;
        if (frameCount != 0) info.avg_bitrate_kbps = (int)(bitrateSum / frameCount);
        if (channelTransition) info.channels = 0;
        info.buffer = output.ToArray();
        info.samples = (ulong)info.buffer.Length;
        info.delay = savedDelay;
        info.padding = savedPadding;
        info.vbr_tag_found = savedVbr;
        return savedError == MP3D_E_DECODE ? MP3D_E_DECODE : 0;
    }

    /// <summary>C-compatible load_cb overload with caller-provided work storage.</summary>
    public static int mp3dec_load_cb(mp3dec_t dec, mp3dec_io_t io, byte[] buffer,
        mp3dec_file_info_t info, MP3D_PROGRESS_CB? progress = null, object? userData = null)
    {
        if (buffer is null || buffer.Length < 16 * 1024) return MP3D_E_PARAM;
        return LoadCallback(dec, io, buffer, info, progress, userData);
    }

    public static int mp3dec_load_cb(mp3dec_t dec, mp3dec_io_t io, byte[] buffer, int bufferSize,
        mp3dec_file_info_t info, MP3D_PROGRESS_CB? progress = null, object? userData = null)
    {
        if (buffer is null || (uint)bufferSize > (uint)buffer.Length || bufferSize < 16 * 1024)
            return MP3D_E_PARAM;
        if (io is null) return MP3D_E_PARAM;
        return LoadCallback(dec, io, buffer.AsSpan(0, bufferSize).ToArray(), info, progress, userData);
    }

    public static int mp3dec_ex_open_cb(mp3dec_ex_t dec, mp3dec_io_t io, int flags = MP3D_SEEK_TO_BYTE)
        => OpenCallback(dec, io, flags, null);

    private static int OpenCallback(mp3dec_ex_t dec, mp3dec_io_t io, int flags, byte[]? workBuffer)
    {
        if (dec is null || io is null) return MP3D_E_PARAM;
        if ((flags & ~MP3D_FLAGS_MASK) != 0) return MP3D_E_PARAM;
        if (!TrySeek(io, 0)) return MP3D_E_IOERROR;
        dec.readCallback = io.read;
        dec.seekCallback = io.seek;
        dec.data = Array.Empty<byte>();
        dec.offset = 0;
        dec.startOffset = dec.endOffset = 0;
        dec.callbackBuffer = workBuffer is { Length: >= 16 * 1024 }
            ? workBuffer
            : dec.callbackBuffer;
        dec.callbackCount = 0;
        dec.callbackPosition = 0;
        dec.callbackStart = 0;
        dec.callbackEof = false;
        dec.callbackError = false;
        dec.callbackTagsTrimmed = false;
        dec.callbackMode = true;
        dec.samples = 0;
        dec.currentSample = 0;
        dec.toSkip = 0;
        dec.bufferedSamples = 0;
        dec.bufferConsumed = 0;
        dec.int32BufferedSamples = 0;
        dec.int32BufferConsumed = 0;
        dec.floatBufferedSamples = 0;
        dec.floatBufferConsumed = 0;
        dec.opened = true;
        dec.flags = flags;
        dec.index.Dispose();
        dec.index.Add(0, 0);
        dec.vbr_tag_found = false;
        dec.metadataOnlyTag = false;
        dec.vbr_frames = 0;
        dec.start_delay = dec.end_padding = 0;
        dec.last_error = 0;
        dec.lastChannels = dec.lastHz = dec.lastLayer = 0;
        minimp3.mp3dec_init(dec.decoder);

        // Probe only the first frame. The remaining source stays in the callback.
        int probeWindow = Math.Min(dec.callbackBuffer.Length, 16 * 1024);
        if (!EnsureCallbackData(dec, probeWindow) && dec.callbackError) return MP3D_E_IOERROR;
        // The C callback loader treats a clean short/empty EOF as an empty
        // stream. Only a non-EOF inability to obtain a header is a decode
        // failure.
        if (dec.callbackCount < 4) return dec.callbackEof ? 0 : MP3D_E_DECODE;
        int id3Size = Id3v2PrefixSize(dec.callbackBuffer, dec.callbackCount);
        if (id3Size > 0)
        {
            if (!TrySeek(dec, id3Size)) return MP3D_E_IOERROR;
            dec.callbackCount = 0;
            dec.callbackPosition = id3Size;
            dec.callbackStart = id3Size;
            dec.callbackEof = false;
            if (!EnsureCallbackData(dec, probeWindow) && dec.callbackCount < 4)
                return dec.callbackEof ? 0 : MP3D_E_DECODE;
        }
        var probe = dec.probeDecoder;
        var probePcm = dec.probePcm;
        var probeInfo = dec.probeInfo;
        int probeCount = probe.Decoder.DecodeFrame(dec.callbackBuffer, 0, dec.callbackCount, probePcm, probe.InternalFrameInfo);
        CopyFrameInfo(probe.InternalFrameInfo, probeInfo);
        if (probeInfo.frame_bytes <= 0)
        {
            while (!dec.callbackEof && probeInfo.frame_bytes <= 0 && EnsureCallbackData(dec, dec.callbackCount + 4096))
            {
                probeInfo.frame_bytes = 0;
                probeInfo.frame_offset = 0;
                probeInfo.channels = 0;
                probeInfo.hz = 0;
                probeInfo.layer = 0;
                probeInfo.bitrate_kbps = 0;
                probeCount = probe.Decoder.DecodeFrame(dec.callbackBuffer, 0, dec.callbackCount, probePcm, probe.InternalFrameInfo);
                CopyFrameInfo(probe.InternalFrameInfo, probeInfo);
                if (probeCount == 0 && probeInfo.frame_bytes <= 0 && dec.callbackEof) break;
            }
        }
        VbrTagResult callbackVbrResult = VbrTagResult.None;
        uint vf = 0; int vd = 0, vp = 0;
        if (probeInfo.frame_bytes > 0 && probeInfo.frame_offset >= 0 && probeInfo.frame_offset + probeInfo.frame_bytes <= dec.callbackCount)
        {
            callbackVbrResult = CheckVbrTag(dec.callbackBuffer.AsSpan(probeInfo.frame_offset, probeInfo.frame_bytes).ToArray(), probeInfo.frame_bytes, out vf, out vd, out vp);
        }
        if (callbackVbrResult == VbrTagResult.Valid)
        {
            int perFrame = probeInfo.layer == 1 ? 384 : probeInfo.layer == 2 ? 1152 : (probeInfo.hz <= 24000 ? 576 : 1152);
            int channels = Math.Max(1, probeInfo.channels);
            dec.vbr_tag_found = true;
            dec.vbr_frames = vf;
            dec.start_delay = vd * channels;
            dec.end_padding = vp * channels;
            ulong total = (ulong)vf * (ulong)channels * (ulong)perFrame;
            ulong trim = (ulong)dec.start_delay + (ulong)Math.Max(0, dec.end_padding);
            dec.samples = total > trim ? total - trim : 0;
            dec.toSkip = dec.start_delay;
            int tagEnd = probeInfo.frame_offset + probeInfo.frame_bytes;
            // Root minimp3 scans the tag frame through the real decoder before
            // moving start_offset past it, preserving reservoir/QMF state.
            var tagInfo = new mp3dec_frame_info_t();
            minimp3.mp3dec_decode_frame(dec.decoder,
                dec.callbackBuffer.AsSpan(probeInfo.frame_offset, probeInfo.frame_bytes).ToArray(),
                probeInfo.frame_bytes, dec.frameBuffer, tagInfo);
            ConsumeCallbackData(dec, tagEnd);
            dec.callbackStart = dec.callbackPosition;
            minimp3.mp3dec_init(dec.decoder);
        }
        else if (callbackVbrResult == VbrTagResult.NoFrames && probeInfo.frame_bytes > 0)
        {
            dec.metadataOnlyTag = true;
            int skip = probeInfo.frame_offset + probeInfo.frame_bytes;
            ConsumeCallbackData(dec, skip);
            dec.callbackStart = dec.callbackPosition;
            if (!TrySeek(dec, dec.callbackStart)) return MP3D_E_IOERROR;
            dec.callbackCount = 0;
            dec.callbackEof = false;
            dec.offset = 0;
        }
        // Callback/IOCP input must return from open promptly.  Its full seek
        // map is built lazily by mp3dec_ex_seek, outside the open IPC request;
        // scanning here can block PlayerHost long enough to trigger IPC timeout.
        if (!dec.callbackMode && (flags & MP3D_DO_NOT_SCAN) == 0)
        {
            BuildIndex(dec);
            if (dec.callbackError) return MP3D_E_IOERROR;
            if (!TrySeek(dec, dec.callbackStart)) return MP3D_E_IOERROR;
            dec.callbackCount = 0;
            dec.callbackPosition = dec.callbackStart;
            dec.callbackEof = false;
            dec.callbackTagsTrimmed = false;
            dec.offset = 0;
            dec.currentSample = 0;
            dec.toSkip = 0;
            minimp3.mp3dec_init(dec.decoder);
        }
        if (probeInfo.frame_bytes > 0)
        {
            dec.frameInfo.frame_bytes = probeInfo.frame_bytes;
            dec.frameInfo.frame_offset = probeInfo.frame_offset;
            dec.frameInfo.channels = probeInfo.channels;
            dec.frameInfo.hz = probeInfo.hz;
            dec.frameInfo.layer = probeInfo.layer;
            dec.frameInfo.bitrate_kbps = probeInfo.bitrate_kbps;
        }
        return 0;
    }

    public static int mp3dec_ex_open_buf(mp3dec_ex_t dec, byte[] buf, int flags = MP3D_SEEK_TO_BYTE)
    {
        if (dec is null || buf is null) return MP3D_E_PARAM;
        if ((flags & ~MP3D_FLAGS_MASK) != 0) return MP3D_E_PARAM;
        // Retain the original byte coordinate space. The C implementation
        // keeps metadata in the backing buffer and tracks audio boundaries
        // separately, so byte seeks and frame indexes include ID3 offsets.
        int prefix = Id3v2PrefixSize(buf, buf.Length);
        int audioLength = SkipTags(buf).Length;
        dec.startOffset = Math.Min(prefix, buf.Length);
        dec.endOffset = Math.Min(buf.Length, dec.startOffset + audioLength);
        // open_buf 接管调用方传入的数组；避免文件打开阶段再复制一份完整 MP3。
        dec.data = buf;
        dec.callbackMode = false;
        dec.callbackCount = 0;
        dec.callbackEof = false;
        dec.callbackError = false;
        dec.callbackStart = 0;
        dec.metadataOnlyTag = false;
        dec.readCallback = null;
        dec.seekCallback = null;
        dec.offset = dec.startOffset;
        dec.samples = 0;
        dec.currentSample = 0;
        dec.toSkip = 0;
        dec.bufferedSamples = 0;
        dec.bufferConsumed = 0;
        dec.int32BufferedSamples = 0;
        dec.int32BufferConsumed = 0;
        dec.floatBufferedSamples = 0;
        dec.floatBufferConsumed = 0;
        dec.opened = true;
        dec.flags = flags;
        dec.index.Clear();
        dec.index.Add(0, dec.startOffset);
        dec.vbr_tag_found = false;
        dec.metadataOnlyTag = false;
        dec.vbr_frames = 0;
        dec.start_delay = dec.end_padding = 0;
        dec.last_error = 0;
        dec.lastChannels = dec.lastHz = dec.lastLayer = 0;
        minimp3.mp3dec_init(dec.decoder);
        var probe = new mp3dec_t();
        var probePcm = new short[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
        var probeInfo = new mp3dec_frame_info_t();
        if (dec.endOffset - dec.startOffset >= 4)
        {
            int audioBytes = dec.endOffset - dec.startOffset;
            probe.DecodeFrameAt(dec.data, dec.startOffset, audioBytes, probePcm, probeInfo);
            if (probeInfo.frame_bytes > 0 && probeInfo.frame_offset >= 0 &&
                probeInfo.frame_offset <= audioBytes &&
                probeInfo.frame_bytes <= audioBytes - probeInfo.frame_offset)
            {
                int absoluteProbeOffset = dec.startOffset + probeInfo.frame_offset;
                var result = CheckVbrTag(dec.data.AsSpan(absoluteProbeOffset, probeInfo.frame_bytes).ToArray(), probeInfo.frame_bytes, out uint vf, out int vd, out int vp);
                if (result == VbrTagResult.Valid)
                { int perFrame = probeInfo.layer == 1 ? 384 : probeInfo.layer == 2 ? 1152 : (probeInfo.hz <= 24000 ? 576 : 1152); int channels = Math.Max(1, probeInfo.channels); dec.vbr_tag_found = true; dec.vbr_frames = vf; dec.start_delay = vd * channels; dec.end_padding = vp * channels; dec.toSkip = dec.start_delay; ulong total = (ulong)vf * (ulong)channels * (ulong)perFrame; ulong trim = (ulong)dec.start_delay + (ulong)Math.Max(0, dec.end_padding); dec.samples = total > trim ? total - trim : 0; var tagInfo = new mp3dec_frame_info_t(); minimp3.mp3dec_decode_frame(dec.decoder, dec.data.AsSpan(absoluteProbeOffset, probeInfo.frame_bytes).ToArray(), probeInfo.frame_bytes, dec.frameBuffer, tagInfo); dec.offset = absoluteProbeOffset + probeInfo.frame_bytes; }
                else if (result == VbrTagResult.NoFrames)
                {
                    dec.metadataOnlyTag = true;
                    dec.offset = absoluteProbeOffset + probeInfo.frame_bytes;
                }
            }
        }
        minimp3.mp3dec_init(dec.decoder);
        if (!dec.vbr_tag_found && (flags & MP3D_DO_NOT_SCAN) == 0)
        {
            BuildIndex(dec);
            if (dec.index.Count > 1)
            {
                dec.currentSample = 0;
                dec.offset = dec.index[0].Offset;
                minimp3.mp3dec_init(dec.decoder);
            }
        }
        // mp3dec_ex_open_buf probes the first frame for VBR metadata, but the
        // public ex handle's info structure is only updated by a read call in
        // the frame loop. Preserve the probe header so callers can validate
        // sample rate and channel count immediately after open, as the C API does.
        if (probeInfo.frame_bytes > 0)
        {
            dec.frameInfo.frame_bytes = probeInfo.frame_bytes;
            dec.frameInfo.frame_offset = dec.startOffset + probeInfo.frame_offset;
            dec.frameInfo.channels = probeInfo.channels;
            dec.frameInfo.hz = probeInfo.hz;
            dec.frameInfo.layer = probeInfo.layer;
            dec.frameInfo.bitrate_kbps = probeInfo.bitrate_kbps;
        }
        return 0;
    }

    public static void mp3dec_ex_close(mp3dec_ex_t dec)
    {
        if (dec is null) return;
        // Root mp3dec_ex_close frees the dynamically built frame index and
        // zeroes the embedded mp3dec_t.  Clear both here so close/reopen and
        // repeated Dispose calls cannot retain a stale seek trajectory.
        dec.index.Clear();
        dec.decoder.Decoder.Reset();
        dec.data = Array.Empty<byte>();
        dec.offset = 0;
        dec.startOffset = 0;
        dec.endOffset = 0;
        dec.opened = false;
        dec.currentSample = 0;
        dec.toSkip = 0;
        dec.samples = 0;
        dec.vbr_tag_found = false;
        dec.vbr_frames = 0;
        dec.start_delay = 0;
        dec.end_padding = 0;
        dec.last_error = 0;
        dec.lastChannels = 0;
        dec.lastHz = 0;
        dec.lastLayer = 0;
        dec.flags = 0;
        // Keep managed callback scratch owned by the decoder. In ZeroGC mode
        // replacing it with Array.Empty would only orphan the old arrays;
        // reuse is the only deterministic lifetime available for this API.
        dec.callbackCount = 0;
        dec.callbackMode = false;
        dec.callbackEof = false;
        dec.callbackError = false;
        dec.callbackStart = 0;
        dec.bufferedSamples = 0;
        dec.bufferConsumed = 0;
        dec.int32BufferedSamples = 0;
        dec.int32BufferConsumed = 0;
        dec.floatBufferedSamples = 0;
        dec.floatBufferConsumed = 0;
        dec.callbackTagsTrimmed = false;
        dec.metadataOnlyTag = false;
        dec.readCallback = null;
        dec.seekCallback = null;
        dec.frameInfo.frame_bytes = 0;
        dec.frameInfo.frame_offset = 0;
        dec.frameInfo.channels = 0;
        dec.frameInfo.hz = 0;
        dec.frameInfo.layer = 0;
        dec.frameInfo.bitrate_kbps = 0;
    }

    public static int mp3dec_ex_seek(mp3dec_ex_t dec, ulong position)
    {
        if (dec is null) return MP3D_E_PARAM;
        // A zero-initialized C mp3dec_ex_t accepts byte seeks as a no-op.
        if (!dec.opened)
        {
            if (dec.flags == MP3D_SEEK_TO_BYTE) return 0;
            dec.last_error = MP3D_E_PARAM;
            return MP3D_E_PARAM;
        }
        if (dec.callbackMode && (dec.flags & MP3D_SEEK_TO_SAMPLE) == 0)
        {
            ulong target = Math.Min(position, long.MaxValue);
            if (!TrySeek(dec, (long)target)) { dec.last_error = MP3D_E_IOERROR; return MP3D_E_IOERROR; }
            dec.callbackCount = 0;
            dec.callbackPosition = (long)target;
        dec.callbackEof = false;
        dec.callbackError = false;
        dec.callbackTagsTrimmed = false;
            dec.offset = 0;
            dec.currentSample = 0;
            dec.toSkip = 0;
            dec.bufferedSamples = 0;
            dec.bufferConsumed = 0;
            dec.int32BufferedSamples = 0;
            dec.int32BufferConsumed = 0;
            dec.floatBufferedSamples = 0;
            dec.floatBufferConsumed = 0;
        }
        else if ((dec.flags & MP3D_SEEK_TO_SAMPLE) != 0)
        {
            if (dec.samples != 0 && position > dec.samples) position = dec.samples;
            // A successful seek resets a prior callback IO error, matching the
            // root ex API. BuildIndex may already be complete, so this reset
            // must happen before it rather than only inside the scanner.
            if (dec.callbackMode)
            {
                dec.callbackError = false;
                dec.callbackEof = false;
                dec.last_error = 0;
            }
            // Root mp3dec_ex_seek skips index construction when the logical
            // target is zero (and there is no encoder delay). This is also
            // required for MP3D_DO_NOT_SCAN streams: seeking to zero must be
            // a local state reset, not an implicit full scan.
            if (position == 0 && dec.start_delay == 0)
            {
                if (dec.callbackMode)
                {
                    if (!TrySeek(dec, dec.callbackStart))
                    {
                        dec.last_error = MP3D_E_IOERROR;
                        return MP3D_E_IOERROR;
                    }
                    dec.callbackCount = 0;
                    dec.callbackPosition = dec.callbackStart;
                    dec.callbackEof = false;
                    dec.callbackError = false;
                    dec.callbackTagsTrimmed = false;
                    dec.offset = 0;
                }
                else
                {
                    dec.offset = dec.startOffset;
                }
                dec.currentSample = 0;
                dec.toSkip = 0;
                dec.bufferedSamples = 0;
                dec.bufferConsumed = 0;
                dec.int32BufferedSamples = 0;
                dec.int32BufferConsumed = 0;
                dec.floatBufferedSamples = 0;
                dec.floatBufferConsumed = 0;
                minimp3.mp3dec_init(dec.decoder);
                return 0;
            }
            ulong target = position + (ulong)Math.Max(0, dec.start_delay);
            // A bounded callback index scan is only a seek aid.  It must not
            // replace the VBR/Xing total sample count with the scan stop
            // position; doing so makes the very next read look like EOF.
            ulong knownTotalSamples = dec.samples;
            BuildIndex(dec, target);
            if (knownTotalSamples != 0)
            {
                dec.samples = knownTotalSamples;
            }
            if (dec.callbackMode && dec.callbackError) { dec.last_error = MP3D_E_IOERROR; return MP3D_E_IOERROR; }
            // Select the last indexed frame not after the requested logical
            // sample. The index records zero-output reservoir frames too;
            // choosing the closest predecessor matches mp3dec_idx_binary_search.
            var entry = dec.index.Count == 0 ? new Mp3IndexEntry(0, 0) : dec.index[0];
#if MINIMP3_SEEK_IDX_LINEAR_SEARCH
            for (int n = 1; n < dec.index.Count; n++)
            {
                if (dec.index[n].Sample > target) break;
                entry = dec.index[n];
            }
#else
            // Direct counterpart of mp3dec_idx_binary_search.  Keeping the
            // search branch explicit makes the root compile-time switch
            // observable without changing the selected frame.
            int lo = 0, hi = dec.index.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (dec.index[mid].Sample <= target)
                {
                    entry = dec.index[mid];
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
#endif
            if (dec.callbackMode && dec.index.Count > 0)
            {
                // The bounded scan already produced a predecessor frame near
                // the target. Start there and let the decoder consume the
                // short reservoir pre-roll instead of replaying the complete
                // file from byte zero.
                int targetIndex = dec.index.FindOffset(entry.Offset);
                if (targetIndex > 0)
                {
                    // Layer III needs both bit-reservoir bytes and synthesis
                    // history.  Nine frames is insufficient for files whose
                    // main_data_begin reaches farther back; use a bounded
                    // pre-roll matching the reservoir history, then discard
                    // the samples before the requested position.
                    entry = dec.index[Math.Max(0, targetIndex - 64)];
                }
            }
            // The C implementation advances over the consecutive zero-sample
            // prefix after selecting the predecessor. Those frames still
            // consume compressed samples while rebuilding Layer III's
            // reservoir; read_frame subtracts them again as it consumes them.
            int zeroPrefixSkip = 0;
            if (!dec.callbackMode)
            {
                int entryIndex = dec.index.FindOffset(entry.Offset);
                while (entryIndex >= 0 && entryIndex + 1 < dec.index.Count &&
                       dec.index[entryIndex].Sample == 0 && dec.index[entryIndex + 1].Sample == 0)
                {
                    int frameOffset = dec.index[entryIndex].Offset;
                    int layer = dec.lastLayer != 0 ? dec.lastLayer : 3;
                    int channels = Math.Max(1, dec.lastChannels != 0 ? dec.lastChannels : 1);
                    int frameSamples = FrameSamplesFromHeader(dec.data, frameOffset, layer) * channels;
                    if (zeroPrefixSkip > int.MaxValue - frameSamples) zeroPrefixSkip = int.MaxValue;
                    else zeroPrefixSkip += frameSamples;
                    entryIndex++;
                    entry = dec.index[entryIndex];
                }
            }
            if (dec.callbackMode)
            {
                if (!TrySeek(dec, entry.Offset)) { dec.last_error = MP3D_E_IOERROR; return MP3D_E_IOERROR; }
                dec.callbackCount = 0;
                dec.callbackPosition = entry.Offset;
        dec.callbackEof = false;
        dec.callbackTagsTrimmed = false;
                dec.offset = 0;
            }
            else dec.offset = entry.Offset;
            dec.currentSample = position;
            ulong skipFromEntry = target > entry.Sample ? target - entry.Sample : 0;
            dec.toSkip = (int)Math.Min((ulong)int.MaxValue, skipFromEntry);
            if (zeroPrefixSkip > int.MaxValue - dec.toSkip) dec.toSkip = int.MaxValue;
            else dec.toSkip += zeroPrefixSkip;
        }
        else
        {
            dec.offset = (int)Math.Min(position, (ulong)dec.data.Length);
            if (!dec.callbackMode && dec.offset < dec.startOffset)
                dec.offset = dec.startOffset;
            dec.samples = 0;
            dec.currentSample = 0;
            dec.toSkip = 0;
        }
            dec.bufferedSamples = 0;
            dec.bufferConsumed = 0;
            dec.int32BufferedSamples = 0;
            dec.int32BufferConsumed = 0;
            dec.floatBufferedSamples = 0;
            dec.floatBufferConsumed = 0;
            dec.lastChannels = 0;
            dec.lastHz = 0;
            dec.lastLayer = 0;
        minimp3.mp3dec_init(dec.decoder);
        return 0;
    }

    private static bool TrySeek(mp3dec_io_t io, long offset)
    {
        try { return io.seek(offset) == 0; }
        catch { return false; }
    }

    private static bool TrySeek(mp3dec_ex_t dec, long offset)
    {
        if (dec.seekCallback is null) return false;
        try { return dec.seekCallback(offset) == 0; }
        catch { return false; }
    }

    private static bool EnsureCallbackData(mp3dec_ex_t dec, int needed)
    {
        if (!dec.callbackMode || dec.readCallback is null) return false;
        while (dec.callbackCount < needed && !dec.callbackEof)
        {
            int room = dec.callbackBuffer.Length - dec.callbackCount;
            if (room < dec.callbackScratch.Length)
            {
                // The callback buffer is fixed after decoder construction.
                // Never grow it from the playback path; the caller must
                // consume/compact data before requesting more input.
                return dec.callbackCount >= needed;
            }
            int count;
            try { count = dec.readCallback(dec.callbackScratch); }
            catch { dec.callbackError = true; return false; }
            if (count < 0 || count > room || count > dec.callbackScratch.Length) { dec.callbackError = true; return false; }
            if (count == 0) { dec.callbackEof = true; break; }
            dec.callbackScratch.AsSpan(0, count).CopyTo(dec.callbackBuffer.AsSpan(dec.callbackCount));
            dec.callbackCount += count;
        }
        if (dec.callbackEof && !dec.callbackTagsTrimmed)
        {
            int trimmed = TrimCallbackTrailer(dec.callbackBuffer, dec.callbackCount);
            if (trimmed < dec.callbackCount) dec.callbackCount = trimmed;
            dec.callbackTagsTrimmed = true;
        }
        return dec.callbackCount >= needed;
    }

    private static void ConsumeCallbackData(mp3dec_ex_t dec, int count)
    {
        if (count <= 0) return;
        if (count >= dec.callbackCount) { dec.callbackCount = 0; dec.callbackPosition += count; return; }
        dec.callbackBuffer.AsSpan(count, dec.callbackCount - count).CopyTo(dec.callbackBuffer);
        dec.callbackCount -= count;
        dec.callbackPosition += count;
    }

    private static void BuildIndex(mp3dec_ex_t dec, ulong stopSample = ulong.MaxValue)
    {
        if (dec.index.Count > 1 && dec.index[dec.index.Count - 1].Sample >= stopSample) return;
        if (dec.callbackMode && dec.index.Count == 1)
            dec.index.Clear();
            dec.index.Add(0, (int)dec.callbackStart);
        if (dec.callbackMode)
        {
            if (BuildHeaderIndexCallback(dec, stopSample))
            {
                return;
            }
            if (dec.seekCallback is null || dec.readCallback is null || !TrySeek(dec, dec.callbackStart))
                return;
            var scanState = dec.indexScanState;
            minimp3.mp3dec_init(scanState);
            var scanPcm = dec.indexScanPcm;
            var scanInfo = dec.indexScanInfo;
            var buffer = dec.indexScanBuffer;
            var readScratch = dec.indexScanScratch;
            int filled = 0;
            long absolute = dec.callbackStart;
            ulong scanSample = 0;
            bool scanStarted = false;
            bool eof = false;
            while (true)
            {
                if (!eof && filled < buffer.Length)
                {
                    int room = buffer.Length - filled;
                    if (room < readScratch.Length)
                    {
                        dec.callbackError = true;
                        return;
                    }
                    int count;
                    try { count = dec.readCallback(readScratch); }
                    catch { dec.callbackError = true; return; }
                    if (count < 0 || count > room) { dec.callbackError = true; return; }
                    if (count == 0) eof = true;
                    else readScratch.AsSpan(0, count).CopyTo(buffer.AsSpan(filled));
                    filled += Math.Max(0, count);
                }
                if (filled == 0) break;
                // Reuse the scan metadata object. This loop runs once per
                // indexed frame; allocating a new info object here made
                // callback-mode seek scans grow the managed heap rapidly.
                scanInfo.frame_bytes = 0;
                scanInfo.frame_offset = 0;
                scanInfo.channels = 0;
                scanInfo.hz = 0;
                scanInfo.layer = 0;
                scanInfo.bitrate_kbps = 0;
                int decoded = scanState.DecodeFrameAt(buffer, 0, filled, scanPcm, scanInfo);
                if (decoded <= 0 && scanInfo.frame_bytes <= 0)
                {
                    if (eof) break;
                    if (filled == buffer.Length)
                    {
                        dec.callbackError = true;
                        return;
                    }
                    continue;
                }
                if (scanInfo.frame_bytes <= 0 || scanInfo.frame_bytes > filled)
                {
                    if (eof) break;
                    continue;
                }
                int frameStart = Math.Clamp(scanInfo.frame_offset, 0, filled);
                int frameSamples = decoded > 0 ? decoded : FrameSamplesFromHeader(buffer, frameStart, scanInfo.layer);
                int absoluteFrameStart = (int)(absolute + frameStart);
                if (dec.index[dec.index.Count - 1].Offset != absoluteFrameStart)
                    dec.index.Add(scanSample, absoluteFrameStart);
                if (decoded > 0)
                {
                    scanSample += (ulong)decoded * (ulong)Math.Max(1, scanInfo.channels);
                    scanStarted = true;
                }
                if (scanSample >= stopSample) break;
                else if (scanStarted)
                {
                    scanSample += (ulong)frameSamples * (ulong)Math.Max(1, scanInfo.channels);
                }
                // frame_bytes is the MPEG frame length; frame_offset is the
                // sync position inside the scan window.  Advancing only by
                // frame_bytes loses any skipped tag/junk prefix and makes
                // later seek entries point into the middle of a frame.
                int consumed = frameStart + scanInfo.frame_bytes;
                if (consumed > filled)
                {
                    if (eof) break;
                    continue;
                }
                buffer.AsSpan(consumed, filled - consumed).CopyTo(buffer);
                filled -= consumed;
                absolute += consumed;
            }
            if (scanSample > 0 && (eof || stopSample == ulong.MaxValue)) dec.samples = scanSample;
            dec.callbackCount = 0;
            // The callback cursor is now positioned at the same logical byte
            // as the source seek.  Keeping it at zero makes the next read
            // report offsets from the file start and breaks a later seek.
            dec.callbackPosition = dec.callbackStart;
            dec.callbackEof = false;
            return;
        }
        var state = new mp3dec_t();
        var pcm = new short[minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME];
        var info = new mp3dec_frame_info_t();
        int offset = dec.startOffset;
        ulong sample = 0;
        bool started = false;
        while (offset < dec.endOffset)
        {
            // Decode directly from the retained file buffer.  The previous
            // implementation created a new byte[] for every indexed frame;
            // long VBR files therefore produced sustained managed-heap growth.
            int count = state.DecodeFrameAt(dec.data, offset, dec.endOffset - offset, pcm, info);
            if (info.frame_bytes <= 0) break;
            int frameStart = offset + Math.Clamp(info.frame_offset, 0, dec.endOffset - offset);
            // C stores an index entry for every frame, including frames whose
            // Layer III reservoir is not filled yet and therefore decode zero
            // samples during the scan.
            if (dec.index[dec.index.Count - 1].Offset != frameStart)
                dec.index.Add(sample, frameStart);
            int channels = Math.Max(1, info.channels);
            int frameSamples = count > 0 ? count : FrameSamplesFromHeader(dec.data, frameStart, info.layer);
            if (count > 0)
            {
                sample += (ulong)count * (ulong)channels;
                started = true;
            }
            else if (started)
            {
                sample += (ulong)frameSamples * (ulong)channels;
            }
            if (sample >= stopSample) break;
            offset += info.frame_bytes;
        }
        if (sample > 0 && (offset >= dec.endOffset || stopSample == ulong.MaxValue)) dec.samples = sample;
    }

    private static bool BuildHeaderIndexCallback(mp3dec_ex_t dec, ulong stopSample)
    {
        if (dec.seekCallback is null || dec.readCallback is null || !TrySeek(dec, dec.callbackStart))
            return false;

        // Match the storage prefetch window so a long resume seek needs only
        // a handful of sequential callback reads instead of one read per
        // small decoder window.
        // 索引扫描是顺序解析，保留有界窗口即可；4 MiB + 2 MiB 的临时数组
        // 会在每次远距离 seek 时制造 LOH 峰值，并让 PlayerHost 工作集迟迟不降。
        var buffer = dec.indexScanBuffer;
        var scratch = dec.indexScanScratch;
        var filled = 0;
        long absolute = dec.callbackStart;
        ulong sample = 0;
        var eof = false;
        var needRead = true;

        while (true)
        {
            if (!eof && needRead)
            {
                int count;
                try
                {
                    count = dec.readCallback(scratch);
                }
                catch
                {
                    dec.callbackError = true;
                    return true;
                }
                if (count < 0 || count > scratch.Length)
                {
                    dec.callbackError = true;
                    return true;
                }

                if (count == 0)
                {
                    eof = true;
                }
                else
                {
                    if (filled + count > buffer.Length)
                    {
                        dec.callbackError = true;
                        return true;
                    }

                    scratch.AsSpan(0, count).CopyTo(buffer.AsSpan(filled));
                    filled += count;
                }
                needRead = false;
            }

            if (filled < 4)
                break;

            var sync = 0;
            var frameBytes = 0;
            var frameSamples = 0;
            var channels = 0;
            while (sync + 4 <= filled && !TryParseMpegHeader(buffer, sync, out frameBytes, out frameSamples, out channels))
                sync++;

            if (sync + 4 > filled)
            {
                if (eof) break;
                buffer.AsSpan(Math.Max(0, filled - 3), Math.Min(3, filled)).CopyTo(buffer);
                absolute += Math.Max(0, filled - 3);
                filled = Math.Min(3, filled);
                needRead = true;
                continue;
            }

            if (sync + frameBytes > filled)
            {
                if (eof) break;
                if (sync > 0)
                {
                    buffer.AsSpan(sync, filled - sync).CopyTo(buffer);
                    absolute += sync;
                    filled -= sync;
                }
                needRead = true;
                continue;
            }

            var frameOffset = checked((int)(absolute + sync));
            if (dec.index.Count == 0 || dec.index[dec.index.Count - 1].Offset != frameOffset)
                dec.index.Add(sample, frameOffset);

            sample += (ulong)frameSamples * (ulong)Math.Max(1, channels);
            var consumed = sync + frameBytes;
            buffer.AsSpan(consumed, filled - consumed).CopyTo(buffer);
            filled -= consumed;
            absolute += consumed;
            if (filled < 4096)
                needRead = true;

            if (sample >= stopSample)
                break;
        }

        dec.callbackCount = 0;
        dec.callbackPosition = dec.callbackStart;
        dec.callbackEof = false;
        return true;
    }

    private static bool TryParseMpegHeader(
        byte[] data,
        int offset,
        out int frameBytes,
        out int frameSamples,
        out int channels)
    {
        frameBytes = frameSamples = channels = 0;
        if (offset < 0 || offset + 4 > data.Length)
            return false;

        var h = (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
        if ((h & 0xFFE00000u) != 0xFFE00000u)
            return false;

        var version = (int)((h >> 19) & 3);
        var layerBits = (int)((h >> 17) & 3);
        // The MPEG header encodes Layer III as bits=1, while minimp3's
        // public frame info uses layer=3 for Layer III (and layer=1 for I).
        var layer = 4 - layerBits;
        var bitrateIndex = (int)((h >> 12) & 15);
        var sampleIndex = (int)((h >> 10) & 3);
        if (version == 1 || layerBits == 0 || bitrateIndex is 0 or 15 || sampleIndex == 3)
            return false;

        var rate = version == 3
            ? sampleIndex switch { 0 => 44100, 1 => 48000, _ => 32000 }
            : version == 2
                ? sampleIndex switch { 0 => 22050, 1 => 24000, _ => 16000 }
                : sampleIndex switch { 0 => 11025, 1 => 12000, _ => 8000 };
        var mpeg1 = version == 3;
        var bitrates = layer == 3
            ? (mpeg1 ? Mpeg1Layer3Bitrates : Mpeg2Layer23Bitrates)
            : layer == 2
                ? (mpeg1 ? Mpeg1Layer2Bitrates : Mpeg2Layer23Bitrates)
                : (mpeg1 ? Mpeg1Layer1Bitrates : Mpeg2Layer1Bitrates);
        var bitrate = bitrates[bitrateIndex];
        var padding = (int)((h >> 9) & 1);
        frameBytes = layer == 3
            ? (mpeg1 ? 144 : 72) * bitrate * 1000 / rate + padding
            : layer == 2
                ? (mpeg1 ? 144 : 72) * bitrate * 1000 / rate + padding
                : 12 * bitrate * 1000 / rate + padding * 4;
        frameSamples = layer == 1 ? 384 : layer == 2 ? 1152 : (mpeg1 ? 1152 : 576);
        channels = ((h >> 6) & 3) == 3 ? 1 : 2;
        return frameBytes >= 4;
    }

    private static int FrameSamplesFromHeader(byte[] data, int offset, int layer)
    {
        if (layer == 1) return 384;
        if (layer == 3 && offset + 2 < data.Length && (data[offset + 1] & 0x0e) == 2) return 576;
        return 1152;
    }

    public static int mp3dec_ex_read_frame(mp3dec_ex_t dec, out short[] pcm, out mp3dec_frame_info_t info, int max_samples = minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME)
    {
        pcm = Array.Empty<short>();
        info = dec?.frameInfo ?? new mp3dec_frame_info_t();
        // Root C has no object on which to store last_error for a null
        // decoder, so the read operation is a zero-sample no-op.
        if (dec is null) return 0;
        if (max_samples < 0) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        if (!dec.opened) { dec.last_error = MP3D_E_IOERROR; return 0; }
        if (dec.samples != 0 && dec.currentSample >= dec.samples) return 0;
        if (dec.last_error != 0) return 0;
        if (dec.bufferConsumed < dec.bufferedSamples)
        {
            if (max_samples == 0) return 0;
            int available = dec.bufferedSamples - dec.bufferConsumed;
            int length = Math.Min(available, max_samples);
            if (dec.samples != 0)
                length = (int)Math.Min((ulong)length, dec.samples - dec.currentSample);
            // 将剩余样本搬回固定缓冲区起点，保证旧的 out-array API 也不创建切片数组。
            // Span.CopyTo 对重叠区域安全；调用方始终从样本零开始读取。
            if (dec.bufferConsumed != 0)
            {
                dec.frameBuffer.AsSpan(dec.bufferConsumed, length).CopyTo(dec.frameBuffer);
            }
            pcm = dec.frameBuffer;
            dec.bufferConsumed += length;
            dec.currentSample += (ulong)length;
            return length;
        }
        int count;
        int reservoirFrameSamples = 0;
        if (dec.callbackMode)
        {
            // The C implementation keeps a minimum multi-frame window before
            // decoding. This is important for short-read callbacks: attempting
            // to parse a 4-byte prefix can discard a free-format candidate
            // before its complete frame is available.
            int minimum = Math.Min(dec.callbackBuffer.Length, 16 * 1024);
            if (!EnsureCallbackData(dec, minimum) && dec.callbackCount < 4)
            {
                if (dec.callbackError) dec.last_error = MP3D_E_IOERROR;
                return 0;
            }
            count = dec.decoder.DecodeFrameAt(dec.callbackBuffer, 0, dec.callbackCount, dec.frameBuffer, info);
            while (info.frame_bytes <= 0 && !dec.callbackEof)
            {
                int before = dec.callbackCount;
                EnsureCallbackData(dec, before + 4096);
                count = dec.decoder.DecodeFrameAt(dec.callbackBuffer, 0, dec.callbackCount, dec.frameBuffer, info);
                if (dec.callbackCount == before) break;
            }
            if (info.frame_bytes <= 0)
            {
                if (dec.callbackError) dec.last_error = MP3D_E_IOERROR;
                return 0;
            }
            if (count == 0 && info.frame_offset >= 0 &&
                info.frame_offset + 2 < dec.callbackCount)
            {
                reservoirFrameSamples = FrameSamplesFromHeader(dec.callbackBuffer, info.frame_offset, info.layer) *
                    Math.Max(1, info.channels);
            }
        }
        else
        {
            if (dec.offset >= dec.endOffset) return 0;
            int available = dec.endOffset - dec.offset;
            count = dec.decoder.DecodeFrameAt(dec.data, dec.offset, available, dec.frameBuffer, info);
            if (count == 0 && info.frame_offset >= 0 &&
                dec.offset + info.frame_offset + 2 < dec.endOffset)
            {
                reservoirFrameSamples = FrameSamplesFromHeader(dec.data, dec.offset + info.frame_offset, info.layer) *
                    Math.Max(1, info.channels);
            }
        }
        if (info.frame_bytes <= 0) return 0;
        if (dec.callbackMode && dec.callbackEof)
        {
            int trimmed = TrimCallbackTrailer(dec.callbackBuffer, dec.callbackCount);
            if (trimmed < dec.callbackCount)
            {
                dec.callbackCount = trimmed;
                dec.callbackTagsTrimmed = true;
                if (dec.callbackCount == 0) return 0;
                return mp3dec_ex_read_frame(dec, out pcm, out info, max_samples);
            }
        }
        if (dec.lastHz != 0 && (info.hz != dec.lastHz || info.layer != dec.lastLayer ||
            (info.channels != dec.lastChannels && (dec.flags & MP3D_ALLOW_MONO_STEREO_TRANSITION) == 0)))
        {
            // A callback may expose the final bytes of an ID3/APE trailer as
            // a pseudo-frame. Once EOF is known and the candidate has no
            // valid MPEG parameters, treat it as metadata termination.
            if (dec.callbackMode && dec.callbackEof && info.hz == 0 && info.layer == 0 && info.channels == 0)
                return 0;
            dec.last_error = MP3D_E_DECODE; return 0;
        }
        dec.lastChannels = info.channels; dec.lastHz = info.hz; dec.lastLayer = info.layer;
        if (dec.callbackMode)
        {
            // frame_offset is the sync position within the callback window;
            // consume the skipped prefix together with the MPEG frame.
            ConsumeCallbackData(dec, info.frame_offset + info.frame_bytes);
        }
        else dec.offset += info.frame_bytes;
        if (count == 0 && reservoirFrameSamples > 0)
        {
            // Root minimp3 consumes undecodable reservoir frames inside the
            // read loop. They advance the logical compressed sample position
            // but produce no PCM, so keep reading until a PCM frame exists.
            dec.toSkip = Math.Max(0, dec.toSkip - Math.Min(dec.toSkip, reservoirFrameSamples));
            return mp3dec_ex_read_frame(dec, out pcm, out info, max_samples);
        }
        if (count <= 0) return 0;
        dec.bufferedSamples = Math.Min(count * Math.Max(1, info.channels), dec.frameBuffer.Length);
        int skip = Math.Min(dec.toSkip, dec.bufferedSamples);
        dec.toSkip -= skip;
        dec.bufferConsumed = skip;
        if (max_samples == 0) return 0;
        return mp3dec_ex_read_frame(dec, out pcm, out info, max_samples);
    }

    public static int mp3dec_ex_read(mp3dec_ex_t dec, short[] output, int samples)
    {
        if (dec is null) return 0;
        if (output is null) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        if (samples < 0) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        int written = 0;
        while (written < samples && written < output.Length)
        {
            int count = mp3dec_ex_read_frame(dec, out var frame, out _, samples - written);
            if (count == 0) break;
            frame.AsSpan(0, Math.Min(count, output.Length - written)).CopyTo(output.AsSpan(written));
            written += Math.Min(count, output.Length - written);
        }
        return written;
    }

    /// <summary>Reads signed 32-bit PCM written directly by the synthesis stage.</summary>
    public static int mp3dec_ex_read_frame_i32(mp3dec_ex_t dec, out int[] pcm,
        out mp3dec_frame_info_t info, int max_samples = minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME)
    {
        pcm = Array.Empty<int>();
        info = dec?.frameInfo ?? new mp3dec_frame_info_t();
        if (dec is null) return 0;
        if (max_samples < 0) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        if (!dec.opened) { dec.last_error = MP3D_E_IOERROR; return 0; }
        if (dec.callbackMode && dec.int32BufferConsumed >= dec.int32BufferedSamples)
        {
            // Keep the callback reservoir path identical to the validated
            // short-output implementation while the direct callback target is
            // being validated against the reference decoder.
            var shortCount = mp3dec_ex_read_frame(dec, out var shortPcm, out info, max_samples);
            if (shortCount <= 0) return shortCount;
            var converted = Math.Min(shortCount, dec.int32FrameBuffer.Length);
            for (var i = 0; i < converted; i++)
                dec.int32FrameBuffer[i] = shortPcm[i] << 16;
            pcm = dec.int32FrameBuffer;
            return converted;
        }
        if (dec.samples != 0 && dec.currentSample >= dec.samples) return 0;
        if (dec.last_error != 0) return 0;
        if (dec.int32BufferConsumed < dec.int32BufferedSamples)
        {
            if (max_samples == 0) return 0;
            int available = dec.int32BufferedSamples - dec.int32BufferConsumed;
            int length = Math.Min(available, max_samples);
            if (dec.samples != 0)
                length = (int)Math.Min((ulong)length, dec.samples - dec.currentSample);
            // 将剩余片段搬到固定 buffer 的起点再返回，避免新增 slice 数组；
            // Span.CopyTo 对重叠区域安全，返回的 PCM 始终从样本零开始。
            if (dec.int32BufferConsumed != 0)
            {
                dec.int32FrameBuffer.AsSpan(dec.int32BufferConsumed, length)
                    .CopyTo(dec.int32FrameBuffer);
            }
            pcm = dec.int32FrameBuffer;
            dec.int32BufferConsumed += length;
            dec.currentSample += (ulong)length;
            return length;
        }
        if (dec.offset >= dec.endOffset) return 0;
        int count = dec.decoder.DecodeFrameInt32At(dec.data, dec.offset,
            dec.endOffset - dec.offset, dec.int32FrameBuffer, info);
        if (info.frame_bytes <= 0) return 0;
        if (dec.lastHz != 0 && (info.hz != dec.lastHz || info.layer != dec.lastLayer ||
            (info.channels != dec.lastChannels && (dec.flags & MP3D_ALLOW_MONO_STEREO_TRANSITION) == 0)))
        {
            dec.last_error = MP3D_E_DECODE;
            return 0;
        }
        dec.lastChannels = info.channels;
        dec.lastHz = info.hz;
        dec.lastLayer = info.layer;
        dec.offset += info.frame_bytes;
        if (count == 0)
        {
            int reservoirSamples = FrameSamplesFromHeader(dec.data,
                Math.Min(dec.endOffset - 3, dec.offset - info.frame_bytes + info.frame_offset), info.layer) *
                Math.Max(1, info.channels);
            dec.toSkip = Math.Max(0, dec.toSkip - Math.Min(dec.toSkip, reservoirSamples));
            return mp3dec_ex_read_frame_i32(dec, out pcm, out info, max_samples);
        }
        dec.int32BufferedSamples = Math.Min(count * Math.Max(1, info.channels), dec.int32FrameBuffer.Length);
        int skip = Math.Min(dec.toSkip, dec.int32BufferedSamples);
        dec.toSkip -= skip;
        dec.int32BufferConsumed = skip;
        if (max_samples == 0) return 0;
        return mp3dec_ex_read_frame_i32(dec, out pcm, out info, max_samples);
    }

    public static int mp3dec_ex_read_i32(mp3dec_ex_t dec, int[] output, int samples)
    {
        if (dec is null) return 0;
        if (output is null) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        if (samples < 0) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        int written = 0;
        while (written < samples && written < output.Length)
        {
            int count = mp3dec_ex_read_frame_i32(dec, out var frame, out _, samples - written);
            if (count <= 0) break;
            int copy = Math.Min(count, output.Length - written);
            frame.AsSpan(0, copy).CopyTo(output.AsSpan(written));
            written += copy;
            if (copy < count) break;
        }
        return written;
    }

    /// <summary>Span output boundary for native PCM32 workspaces.</summary>
    public static int mp3dec_ex_read_i32(mp3dec_ex_t dec, Span<int> output)
    {
        if (dec is null) return 0;
        var written = 0;
        while (written < output.Length)
        {
            var count = mp3dec_ex_read_frame_i32(dec, out var frame, out _, output.Length - written);
            if (count <= 0) break;
            var copy = Math.Min(count, output.Length - written);
            frame.AsSpan(0, copy).CopyTo(output[written..]);
            written += copy;
            if (copy < count) break;
        }
        return written;
    }

    /// <summary>
    /// Float high-level read counterpart. This uses the decoder's synthesis
    /// float target directly; it does not quantize to Int16 first.
    /// </summary>
    public static int mp3dec_ex_read_frame_f32(mp3dec_ex_t dec, out float[] pcm,
        out mp3dec_frame_info_t info, int max_samples = minimp3.MINIMP3_MAX_SAMPLES_PER_FRAME)
    {
        pcm = Array.Empty<float>();
        info = dec?.frameInfo ?? new mp3dec_frame_info_t();
        if (dec is null) return 0;
        if (max_samples < 0) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        if (!dec.opened) { dec.last_error = MP3D_E_IOERROR; return 0; }
        if (dec.samples != 0 && dec.currentSample >= dec.samples) return 0;
        if (dec.last_error != 0) return 0;
        if (dec.floatBufferConsumed < dec.floatBufferedSamples)
        {
            if (max_samples == 0) return 0;
            int available = dec.floatBufferedSamples - dec.floatBufferConsumed;
            int length = Math.Min(available, max_samples);
            if (dec.samples != 0)
                length = (int)Math.Min((ulong)length, dec.samples - dec.currentSample);
            if (dec.floatBufferConsumed != 0)
            {
                dec.floatFrameBuffer.AsSpan(dec.floatBufferConsumed, length).CopyTo(dec.floatFrameBuffer);
            }
            pcm = dec.floatFrameBuffer;
            dec.floatBufferConsumed += length;
            dec.currentSample += (ulong)length;
            return length;
        }

        int count;
        int reservoirFrameSamples = 0;
        if (dec.callbackMode)
        {
            int minimum = Math.Min(dec.callbackBuffer.Length, 16 * 1024);
            if (!EnsureCallbackData(dec, minimum) && dec.callbackCount < 4)
            {
                if (dec.callbackError) dec.last_error = MP3D_E_IOERROR;
                return 0;
            }
            count = dec.decoder.DecodeFrameFloatAt(dec.callbackBuffer, 0, dec.callbackCount,
                dec.floatFrameBuffer, info);
            while (info.frame_bytes <= 0 && !dec.callbackEof)
            {
                int before = dec.callbackCount;
                EnsureCallbackData(dec, before + 4096);
                count = dec.decoder.DecodeFrameFloatAt(dec.callbackBuffer, 0, dec.callbackCount,
                    dec.floatFrameBuffer, info);
                if (dec.callbackCount == before) break;
            }
            if (info.frame_bytes <= 0)
            {
                if (dec.callbackError) dec.last_error = MP3D_E_IOERROR;
                return 0;
            }
            if (count == 0 && info.frame_offset >= 0 && info.frame_offset + 2 < dec.callbackCount)
                reservoirFrameSamples = FrameSamplesFromHeader(dec.callbackBuffer, info.frame_offset, info.layer) * Math.Max(1, info.channels);
        }
        else
        {
            if (dec.offset >= dec.endOffset) return 0;
            count = dec.decoder.DecodeFrameFloatAt(dec.data, dec.offset, dec.endOffset - dec.offset,
                dec.floatFrameBuffer, info);
            if (count == 0 && info.frame_offset >= 0 && dec.offset + info.frame_offset + 2 < dec.endOffset)
                reservoirFrameSamples = FrameSamplesFromHeader(dec.data, dec.offset + info.frame_offset, info.layer) * Math.Max(1, info.channels);
        }
        if (info.frame_bytes <= 0) return 0;
        if (dec.callbackMode && dec.callbackEof)
        {
            int trimmed = TrimCallbackTrailer(dec.callbackBuffer, dec.callbackCount);
            if (trimmed < dec.callbackCount)
            {
                dec.callbackCount = trimmed;
                dec.callbackTagsTrimmed = true;
                if (dec.callbackCount == 0) return 0;
            }
        }
        if (dec.lastHz != 0 && (info.hz != dec.lastHz || info.layer != dec.lastLayer ||
            (info.channels != dec.lastChannels && (dec.flags & MP3D_ALLOW_MONO_STEREO_TRANSITION) == 0)))
        {
            dec.last_error = MP3D_E_DECODE;
            return 0;
        }
        dec.lastChannels = info.channels; dec.lastHz = info.hz; dec.lastLayer = info.layer;
        if (dec.callbackMode) ConsumeCallbackData(dec, info.frame_bytes);
        else dec.offset += info.frame_bytes;
        if (count == 0 && reservoirFrameSamples > 0)
        {
            dec.toSkip = Math.Max(0, dec.toSkip - Math.Min(dec.toSkip, reservoirFrameSamples));
            return mp3dec_ex_read_frame_f32(dec, out pcm, out info, max_samples);
        }
        if (count <= 0) return 0;
        dec.floatBufferedSamples = Math.Min(count * Math.Max(1, info.channels), dec.floatFrameBuffer.Length);
        int skip = Math.Min(dec.toSkip, dec.floatBufferedSamples);
        dec.toSkip -= skip;
        dec.floatBufferConsumed = skip;
        if (max_samples == 0) return 0;
        return mp3dec_ex_read_frame_f32(dec, out pcm, out info, max_samples);
    }

    public static int mp3dec_ex_read_f32(mp3dec_ex_t dec, float[] output, int samples)
    {
        if (dec is null) return 0;
        if (output is null) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        if (samples < 0) { dec.last_error = MP3D_E_PARAM; return MP3D_E_PARAM; }
        int written = 0;
        while (written < samples && written < output.Length)
        {
            int count = mp3dec_ex_read_frame_f32(dec, out float[] frame, out _, samples - written);
            if (count <= 0) break;
            int copy = Math.Min(count, Math.Min(frame.Length, output.Length - written));
            frame.AsSpan(0, copy).CopyTo(output.AsSpan(written));
            written += copy;
            if (copy < count) break;
        }
        return written;
    }

    public static int mp3dec_detect_buf(byte[] buf)
    {
        if (buf is null) return MP3D_E_PARAM;
        if (buf.Length < 10) return MP3D_E_USER;
        if (Id3v2PrefixSize(buf, buf.Length) > 0) return 0;
        return Mp3Decoder.HasValidFrameSequence(SkipTags(buf).ToArray()) ? 0 : MP3D_E_USER;
    }

    public static int mp3dec_detect_buf(byte[] buf, int bufSize)
    {
        if (buf is null || (uint)bufSize > (uint)buf.Length) return MP3D_E_PARAM;
        return mp3dec_detect_buf(buf.AsSpan(0, bufSize).ToArray());
    }

    public static int mp3dec_detect_cb(mp3dec_io_t io, byte[] buffer, int bufferSize)
    {
        if (io is null || buffer is null || (uint)bufferSize > (uint)buffer.Length || bufferSize < 16 * 1024)
            return MP3D_E_PARAM;
        var work = bufferSize == buffer.Length ? buffer : new byte[bufferSize];
        int result = mp3dec_detect_cb(io, work);
        if (!ReferenceEquals(work, buffer)) work.CopyTo(buffer, 0);
        return result;
    }

    public static int mp3dec_detect(string fileName)
    {
        if (fileName is null) return MP3D_E_PARAM;
        try { return mp3dec_detect_buf(File.ReadAllBytes(fileName)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return MP3D_E_IOERROR; }
    }

    // The C _w entry points take wchar_t paths. System.String is UTF-16, so
    // these aliases preserve the same API surface without a second path
    // implementation.
    public static int mp3dec_detect_w(string fileName) => mp3dec_detect(fileName);
    public static int mp3dec_load_w(mp3dec_t dec, string fileName, mp3dec_file_info_t info,
        MP3D_PROGRESS_CB? progress = null, object? userData = null) => mp3dec_load(dec, fileName, info, progress, userData);
    public static int mp3dec_iterate_w(string fileName, MP3D_ITERATE_CB callback, object? userData = null)
        => mp3dec_iterate(fileName, callback, userData);
    public static int mp3dec_ex_open_w(mp3dec_ex_t dec, string fileName, int flags = MP3D_SEEK_TO_BYTE)
        => mp3dec_ex_open(dec, fileName, flags);
}
