using System.Buffers.Binary;

namespace Minimp3;

internal readonly record struct Minimp3BasicInfo(int SampleRate, int Channels, long DurationFrames);

internal static class Minimp3InfoReader
{
    private static readonly int[] Rates = { 44100, 48000, 32000, 22050, 24000, 16000, 11025, 12000, 8000 };
    private static readonly int[] Mpeg1Bitrates = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 };
    private static readonly int[] Mpeg2Bitrates = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 };

    internal static bool TryRead(string path, out Minimp3BasicInfo info)
    {
        info = default;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan);
            Span<byte> id3Header = stackalloc byte[10];
            var start = SkipId3v2(stream, id3Header);
            stream.Position = start;
            Span<byte> frame = stackalloc byte[4096];
            Span<byte> frameHeader = stackalloc byte[4];
            while (stream.Read(frameHeader) == 4)
            {
                if (!TryHeader(frameHeader, out var rate, out var channels, out var samples, out var frameBytes, out var bitrate, out var version, out var crc))
                {
                    stream.Position = ++start;
                    continue;
                }

                if (frameBytes > frame.Length) return false;
                frameHeader.CopyTo(frame);
                stream.ReadExactly(frame[4..frameBytes]);
                var frames = TryReadVbrFrameCount(frame[..frameBytes], version, channels, crc, out var vbrFrames)
                    ? vbrFrames
                    : bitrate > 0 && frameBytes > 0
                    ? Math.Max(1L, (stream.Length - start) / frameBytes)
                    : 0;
                if (frames <= 0) return false;
                info = new Minimp3BasicInfo(rate, channels, frames * samples);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static long SkipId3v2(FileStream stream, Span<byte> header)
    {
        try
        {
            stream.ReadExactly(header);
        }
        catch (EndOfStreamException)
        {
            return 0;
        }

        if (header[0] != 'I' || header[1] != 'D' || header[2] != '3') return 0;
        var size = (header[6] & 0x7f) << 21 | (header[7] & 0x7f) << 14 | (header[8] & 0x7f) << 7 | (header[9] & 0x7f);
        return Math.Min(stream.Length, 10L + size + ((header[5] & 0x10) != 0 ? 10 : 0));
    }

    private static bool TryHeader(ReadOnlySpan<byte> h, out int rate, out int channels, out int samples, out int frameBytes, out int bitrate, out int version, out bool crc)
    {
        rate = channels = samples = frameBytes = bitrate = version = 0;
        crc = false;
        if (h[0] != 0xff || (h[1] & 0xe0) != 0xe0) return false;
        version = (h[1] >> 3) & 3;
        var layer = (h[1] >> 1) & 3;
        var rateIndex = (h[2] >> 2) & 3;
        var bitrateIndex = h[2] >> 4;
        if (version == 1 || layer == 0 || rateIndex == 3 || bitrateIndex == 0 || bitrateIndex == 15) return false;
        var baseRate = Rates[rateIndex];
        rate = version switch { 3 => baseRate, 2 => baseRate / 2, _ => baseRate / 4 };
        bitrate = (version == 3 ? Mpeg1Bitrates : Mpeg2Bitrates)[bitrateIndex];
        crc = (h[1] & 1) == 0;
        samples = layer == 1 ? (version == 3 ? 1152 : 576) : 1152;
        frameBytes = (layer == 3 ? 12 : version == 3 ? 144 : 72) * bitrate * 1000 / rate;
        channels = (h[3] & 0xc0) == 0xc0 ? 1 : 2;
        return rate > 0 && frameBytes >= 4;
    }

    private static bool TryReadVbrFrameCount(ReadOnlySpan<byte> frame, int version, int channels, bool crc, out long frames)
    {
        frames = 0;
        var sideInfo = version == 3 ? (channels == 1 ? 17 : 32) : (channels == 1 ? 9 : 17);
        var offset = 4 + (crc ? 2 : 0) + sideInfo;
        if (offset + 12 <= frame.Length && IsTag(frame, offset))
        {
            var flags = BinaryPrimitives.ReadUInt32BigEndian(frame[(offset + 4)..]);
            if ((flags & 1) != 0 && offset + 12 <= frame.Length)
            {
                frames = BinaryPrimitives.ReadUInt32BigEndian(frame[(offset + 8)..]);
                return frames > 0;
            }
        }

        // VBRI 通常位于 MPEG 头之后 32 字节处，头本身占 4 字节。
        // 读取标准 VBRI frame count，避免扫描或创建完整解码器。
        const int vbriOffset = 36;
        if (vbriOffset + 18 <= frame.Length && frame.Slice(vbriOffset, 4).SequenceEqual("VBRI"u8))
        {
            frames = BinaryPrimitives.ReadUInt32BigEndian(frame[(vbriOffset + 14)..]);
            return frames > 0;
        }

        return false;
    }

    private static bool IsTag(ReadOnlySpan<byte> frame, int offset)
        => frame.Slice(offset, 4).SequenceEqual("Xing"u8) || frame.Slice(offset, 4).SequenceEqual("Info"u8);
}
