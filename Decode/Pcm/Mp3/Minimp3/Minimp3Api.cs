using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Minimp3;

/// <summary>Equivalent to the public mp3dec_frame_info_t structure.</summary>
public sealed class mp3dec_frame_info_t
{
    public int frame_bytes;
    public int frame_offset;
    public int channels;
    public int hz;
    public int layer;
    public int bitrate_kbps;
}

/// <summary>Equivalent to the stateful mp3dec_t decoder handle.</summary>
public sealed class mp3dec_t
{
    internal Mp3Decoder Decoder = new();
    internal readonly FrameInfo InternalFrameInfo = new();
    internal readonly mp3dec_frame_info_t PublicInfoScratch = new();
    internal int FreeFormatBytes => Decoder.FreeFormatBytes;
    internal mp3dec_frame_info_t InfoScratch => PublicInfoScratch;
    internal short[] ShortScratch => Decoder.ScratchPcm;
    // Root compile-time switches become the default decoder policy; callers
    // can still override it per instance just like the managed API options.
    public Minimp3Options Options { get; set; } = Minimp3Build.DefaultOptions;
    public int LastError { get; internal set; }

    internal int DecodeFrameAt(byte[] mp3, int offset, int bytes, short[] pcm, mp3dec_frame_info_t info)
    {
        int samples = Decoder.DecodeFrame(mp3, offset, bytes, pcm, InternalFrameInfo);
        info.frame_bytes = InternalFrameInfo.FrameBytes;
        info.frame_offset = InternalFrameInfo.FrameOffset;
        info.channels = InternalFrameInfo.Channels;
        info.hz = InternalFrameInfo.Hz;
        info.layer = InternalFrameInfo.Layer;
        info.bitrate_kbps = InternalFrameInfo.BitrateKbps;
        return samples;
    }

    internal int DecodeFrameFloatAt(byte[] mp3, int offset, int bytes, float[] pcm, mp3dec_frame_info_t info)
    {
        int samples = Decoder.DecodeFrameFloat(mp3, offset, bytes, pcm, InternalFrameInfo);
        info.frame_bytes = InternalFrameInfo.FrameBytes;
        info.frame_offset = InternalFrameInfo.FrameOffset;
        info.channels = InternalFrameInfo.Channels;
        info.hz = InternalFrameInfo.Hz;
        info.layer = InternalFrameInfo.Layer;
        info.bitrate_kbps = InternalFrameInfo.BitrateKbps;
        return samples;
    }

    internal int DecodeFrameInt32At(byte[] mp3, int offset, int bytes, int[] pcm, mp3dec_frame_info_t info)
        => DecodeFrameInt32At(mp3, offset, bytes, pcm.AsSpan(), info);

    internal int DecodeFrameInt32At(byte[] mp3, int offset, int bytes, Span<int> pcm, mp3dec_frame_info_t info)
    {
        int samples = Decoder.DecodeFrameInt32(mp3, offset, bytes, pcm, InternalFrameInfo);
        info.frame_bytes = InternalFrameInfo.FrameBytes;
        info.frame_offset = InternalFrameInfo.FrameOffset;
        info.channels = InternalFrameInfo.Channels;
        info.hz = InternalFrameInfo.Hz;
        info.layer = InternalFrameInfo.Layer;
        info.bitrate_kbps = InternalFrameInfo.BitrateKbps;
        return samples;
    }
}

[Flags]
public enum Minimp3Options
{
    Default = 0,
    OnlyMp3 = 1,
    FloatOutput = 2,
    NoSimd = 4,
    NonstandardButLogical = 8,
    AllowMonoStereoTransition = 16,
    /// <summary>Require the managed SIMD implementation, matching MINIMP3_ONLY_SIMD.</summary>
    OnlySimd = 32
}

public static class Minimp3Capabilities
{
    public static bool SimdAvailable => Minimp3Build.SimdEnabled && Sse.IsSupported && Sse2.IsSupported;
    /// <summary>Compile-time default option set corresponding to root macros.</summary>
    public static Minimp3Options DefaultOptions => Minimp3Build.DefaultOptions;
    public const bool SupportsFloatOutput = true;
#if MINIMP3_ONLY_MP3
    public const bool SupportsLayer1 = false;
    public const bool SupportsLayer2 = false;
#else
    public const bool SupportsLayer1 = true;
    public const bool SupportsLayer2 = true;
#endif
    public const bool SupportsLayer3 = true;
}

internal static class Minimp3Build
{
#if MINIMP3_ONLY_MP3
    internal const Minimp3Options DefaultOptions = Minimp3Options.OnlyMp3
#else
    internal const Minimp3Options DefaultOptions = Minimp3Options.Default
#endif
#if MINIMP3_NONSTANDARD_BUT_LOGICAL
        | Minimp3Options.NonstandardButLogical
#endif
#if MINIMP3_ALLOW_MONO_STEREO_TRANSITION
        | Minimp3Options.AllowMonoStereoTransition
#endif
#if MINIMP3_NO_SIMD
        | Minimp3Options.NoSimd
#endif
#if MINIMP3_ONLY_SIMD
        | Minimp3Options.OnlySimd
#endif
#if MINIMP3_FLOAT_OUTPUT
        | Minimp3Options.FloatOutput
#endif
        ;
#if MINIMP3_NO_SIMD
    internal const bool SimdEnabled = false;
#else
    internal const bool SimdEnabled = true;
#endif
}

/// <summary>Managed translation of the public low-level minimp3 API.</summary>
public static class minimp3
{
    public const int MINIMP3_MAX_SAMPLES_PER_FRAME = 1152 * 2;

    public static void mp3dec_init(mp3dec_t dec)
    {
        ArgumentNullException.ThrowIfNull(dec);
        dec.Decoder.Options = dec.Options | Minimp3Build.DefaultOptions;
        dec.Decoder.InitHeaderOnly();
    }

    /// <summary>
    /// Decodes one frame from the beginning of <paramref name="mp3"/>.
    /// The return value is the number of samples per channel, matching C minimp3.
    /// PCM is interleaved in <paramref name="pcm"/>.
    /// </summary>
    public static int mp3dec_decode_frame(mp3dec_t dec, byte[] mp3, int mp3_bytes,
        short[]? pcm, mp3dec_frame_info_t info)
    {
        ArgumentNullException.ThrowIfNull(dec);
        ArgumentNullException.ThrowIfNull(mp3);
        ArgumentNullException.ThrowIfNull(info);
        if ((uint)mp3_bytes > (uint)mp3.Length)
            throw new ArgumentOutOfRangeException(nameof(mp3_bytes));
        if (pcm is not null && pcm.Length < MINIMP3_MAX_SAMPLES_PER_FRAME)
            throw new ArgumentException("PCM buffer must hold MINIMP3_MAX_SAMPLES_PER_FRAME samples.", nameof(pcm));

        dec.Decoder.Options = dec.Options | Minimp3Build.DefaultOptions;

        var frame = dec.InternalFrameInfo;
        int samples = dec.Decoder.DecodeFrame(mp3, mp3_bytes, pcm, frame);
        if (samples == 0 && frame.FrameBytes == 0) dec.LastError = -5;
        info.frame_bytes = frame.FrameBytes;
        info.frame_offset = frame.FrameOffset;
        info.channels = frame.Channels;
        info.hz = frame.Hz;
        info.layer = frame.Layer;
        info.bitrate_kbps = frame.BitrateKbps;
        return samples;
    }

    public static void mp3dec_f32_to_s16(ReadOnlySpan<float> input, Span<short> output)
    {
        int count = Math.Min(input.Length, output.Length);
        int i = 0;
        if (Minimp3Build.SimdEnabled && Sse.IsSupported && Sse2.IsSupported)
        {
            var scale = Vector128.Create(32768f);
            var min = Vector128.Create(-32768f);
            var max = Vector128.Create(32767f);
            for (; i + 8 <= count; i += 8)
            {
                var a = Vector128.Create(input[i], input[i + 1], input[i + 2], input[i + 3]);
                var b = Vector128.Create(input[i + 4], input[i + 5], input[i + 6], input[i + 7]);
                a = Sse.Max(Sse.Min(Sse.Multiply(a, scale), max), min);
                b = Sse.Max(Sse.Min(Sse.Multiply(b, scale), max), min);
                var packed = Sse2.PackSignedSaturate(Sse2.ConvertToVector128Int32(a), Sse2.ConvertToVector128Int32(b));
                output[i] = packed.GetElement(0);
                output[i + 1] = packed.GetElement(1);
                output[i + 2] = packed.GetElement(2);
                output[i + 3] = packed.GetElement(3);
                output[i + 4] = packed.GetElement(4);
                output[i + 5] = packed.GetElement(5);
                output[i + 6] = packed.GetElement(6);
                output[i + 7] = packed.GetElement(7);
            }
        }
        for (; i < count; i++)
        {
            float sample = input[i] * 32768f;
            if (sample >= 32766.5f) output[i] = 32767;
            else if (sample <= -32767.5f) output[i] = -32768;
            else
            {
                short value = (short)(sample + 0.5f);
                if (value < 0) value = (short)(value - 1);
                output[i] = value;
            }
        }
    }

    /// <summary>Managed float-output counterpart used when FloatOutput is selected.</summary>
    public static int mp3dec_decode_frame_f32(mp3dec_t dec, byte[] mp3, int mp3_bytes,
        float[] pcm, mp3dec_frame_info_t info)
    {
        ArgumentNullException.ThrowIfNull(dec);
        ArgumentNullException.ThrowIfNull(mp3);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(pcm);
        if ((uint)mp3_bytes > (uint)mp3.Length)
            throw new ArgumentOutOfRangeException(nameof(mp3_bytes));
        if (pcm.Length < MINIMP3_MAX_SAMPLES_PER_FRAME)
            throw new ArgumentException("PCM buffer must hold MINIMP3_MAX_SAMPLES_PER_FRAME samples.", nameof(pcm));
        dec.Decoder.Options = dec.Options | Minimp3Build.DefaultOptions;
        int count = dec.Decoder.DecodeFrameFloat(mp3, mp3_bytes, pcm, dec.InternalFrameInfo);
        info.frame_bytes = dec.InternalFrameInfo.FrameBytes;
        info.frame_offset = dec.InternalFrameInfo.FrameOffset;
        info.channels = dec.InternalFrameInfo.Channels;
        info.hz = dec.InternalFrameInfo.Hz;
        info.layer = dec.InternalFrameInfo.Layer;
        info.bitrate_kbps = dec.InternalFrameInfo.BitrateKbps;
        return count;
    }

    /// <summary>Decodes one frame directly into signed 32-bit PCM.</summary>
    public static int mp3dec_decode_frame_i32(mp3dec_t dec, byte[] mp3, int mp3_bytes,
        int[] pcm, mp3dec_frame_info_t info)
    {
        ArgumentNullException.ThrowIfNull(dec);
        ArgumentNullException.ThrowIfNull(mp3);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(pcm);
        if ((uint)mp3_bytes > (uint)mp3.Length)
            throw new ArgumentOutOfRangeException(nameof(mp3_bytes));
        if (pcm.Length < MINIMP3_MAX_SAMPLES_PER_FRAME)
            throw new ArgumentException("PCM buffer must hold MINIMP3_MAX_SAMPLES_PER_FRAME samples.", nameof(pcm));
        dec.Decoder.Options = dec.Options | Minimp3Build.DefaultOptions;
        int count = dec.Decoder.DecodeFrameInt32(mp3, mp3_bytes, pcm, dec.InternalFrameInfo);
        info.frame_bytes = dec.InternalFrameInfo.FrameBytes;
        info.frame_offset = dec.InternalFrameInfo.FrameOffset;
        info.channels = dec.InternalFrameInfo.Channels;
        info.hz = dec.InternalFrameInfo.Hz;
        info.layer = dec.InternalFrameInfo.Layer;
        info.bitrate_kbps = dec.InternalFrameInfo.BitrateKbps;
        return count;
    }

}
