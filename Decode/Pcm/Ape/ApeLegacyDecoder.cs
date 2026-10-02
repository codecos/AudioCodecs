using System.Buffers;
using System.Buffers.Binary;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Ape;

// 旧版 APE（3.97 及更早）使用不同的头、Rice/范围位流和反预测器。
// 这里保持 SDK 的帧边界、位序和校验顺序，再统一输出交错有符号整数样本。
// 新版范围编码仍由 ApeDecoder 处理，避免两套格式逻辑相互污染。
internal static partial class ApeLegacyDecoder
{
    private const int CompressionFast = 1000;
    private const int CompressionNormal = 2000;
    private const int CompressionHigh = 3000;
    private const int CompressionExtraHigh = 4000;
    private const int CompressionInsane = 5000;
    private const int FormatFlag8Bit = 1 << 0;
    private const int FormatFlagCrc = 1 << 1;
    private const int FormatFlagPeak = 1 << 2;
    private const int FormatFlag24Bit = 1 << 3;
    private const int FormatFlagHasSeekElements = 1 << 4;
    private const int FormatFlagCreateWavHeader = 1 << 5;
    private const int SpecialFrameMonoSilence = 1;
    private const int SpecialFrameLeftSilence = 1;
    private const int SpecialFrameRightSilence = 2;
    private const int SpecialFramePseudoStereo = 4;

    private static readonly uint[] KMinOld =
    {
        0, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072,
        262144, 524288, 1048576, 2097152, 4194304, 8388608, 16777216, 33554432,
        67108864, 134217728, 268435456, 536870912, 1073741824, 2147483648u, 0, 0, 0, 0, 0, 0
    };

    private static readonly uint[] KMaxOld =
    {
        128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144,
        524288, 1048576, 2097152, 4194304, 8388608, 16777216, 33554432, 67108864,
        134217728, 268435456, 536870912, 1073741824, 2147483648u, 0, 0, 0, 0, 0, 0, 0
    };

    private static readonly uint[] KMin =
    {
        0, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768,
        65536, 131072, 262144, 524288, 1048576, 2097152, 4194304, 8388608,
        16777216, 33554432, 67108864, 134217728, 268435456, 536870912,
        1073741824, 2147483648u, 0, 0, 0, 0
    };

    private static readonly uint[] KMax =
    {
        32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536,
        131072, 262144, 524288, 1048576, 2097152, 4194304, 8388608, 16777216,
        33554432, 67108864, 134217728, 268435456, 536870912, 1073741824,
        2147483648u, 0, 0, 0, 0, 0
    };

    private static readonly uint[] RangeTotal1 =
    {
        0,14824,28224,39348,47855,53994,58171,60926,62682,63786,64463,64878,65126,65276,
        65365,65419,65450,65469,65480,65487,65491,65493,65494,65495,65496,65497,65498,65499,
        65500,65501,65502,65503,65504,65505,65506,65507,65508,65509,65510,65511,65512,65513,
        65514,65515,65516,65517,65518,65519,65520,65521,65522,65523,65524,65525,65526,65527,
        65528,65529,65530,65531,65532,65533,65534,65535,65536
    };
    private static readonly uint[] RangeWidth1 =
    {
        14824,13400,11124,8507,6139,4177,2755,1756,1104,677,415,248,150,89,54,31,19,11,
        7,4,2,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,
        1,1,1,1,1,1,1,1
    };
    private static readonly byte[] RangeOverflowLookup1 = BuildRangeOverflowLookup1();

    internal static ApeInfo ReadInfo(FileStream stream, int descriptorOffset)
    {
        stream.Position = descriptorOffset;
        Span<byte> header = stackalloc byte[32];
        stream.ReadExactly(header);
        var version = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
        var compression = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8, 2));
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(10, 2));
        var sampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4)));
        var wavHeaderBytes = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(16, 4)));
        var terminatingBytes = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(20, 4)));
        var totalFrames = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(24, 4)));
        var finalFrameBlocks = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(28, 4)));

        if (channels is < 1 or > 2 || totalFrames <= 0 || finalFrameBlocks <= 0)
            throw new InvalidDataException("APE legacy header contains invalid channel or frame geometry.");
        if (compression is not (CompressionFast or CompressionNormal or CompressionHigh or CompressionExtraHigh or CompressionInsane))
            throw new NotSupportedException($"APE compression level is invalid: level={compression}");
        if (version < 3930 && compression == CompressionInsane)
            throw new NotSupportedException("APE INSANE compression is not defined for this legacy predictor generation.");
        if (version < 3950 && compression == CompressionInsane)
            throw new NotSupportedException("APE 3.93-3.94 does not define INSANE compression.");
        if ((flags & FormatFlagPeak) != 0)
            stream.Position += 4;
        var seekElements = (flags & FormatFlagHasSeekElements) != 0
            ? checked((int)ReadUInt32(stream))
            : totalFrames;
        if (seekElements < totalFrames)
            throw new InvalidDataException("APE legacy seek table is smaller than the frame count.");
        if ((flags & FormatFlagCreateWavHeader) == 0)
            stream.Position += wavHeaderBytes;

        var seekTable = new byte[checked(seekElements * 4)];
        stream.ReadExactly(seekTable);
        var offsets = new long[totalFrames];
        long add = 0;
        uint previous = 0;
        for (var i = 0; i < totalFrames; i++)
        {
            var current = BinaryPrimitives.ReadUInt32LittleEndian(seekTable.AsSpan(i * 4, 4));
            if (current < previous)
                add += 0x1_0000_0000L;
            offsets[i] = descriptorOffset + add + current;
            previous = current;
        }

        var seekBits = version <= 3800 ? new byte[seekElements] : Array.Empty<byte>();
        if (seekBits.Length != 0)
            stream.ReadExactly(seekBits);

        var blocksPerFrame = (version >= 3900 || (version >= 3800 && compression == 4000)) ? 73728 : 9216;
        if (version >= 3950)
            blocksPerFrame *= 4;
        var bits = (flags & FormatFlag8Bit) != 0 ? 8 : (flags & FormatFlag24Bit) != 0 ? 24 : 16;
        var frameDataEnd = stream.Length - terminatingBytes;
        frameDataEnd = TrimLegacyTrailingTag(stream, frameDataEnd);
        var maxFrameBytes = 0;
        for (var i = 0; i < totalFrames; i++)
        {
            var next = i + 1 < totalFrames ? offsets[i + 1] : frameDataEnd;
            var length = checked((int)(next - offsets[i]));
            if (length <= 0)
                throw new InvalidDataException($"APE legacy frame span is invalid: frame={i}");
            maxFrameBytes = Math.Max(maxFrameBytes, length);
        }

        return new ApeInfo(version, compression, flags, sampleRate, channels, bits,
            ((long)(totalFrames - 1) * blocksPerFrame) + finalFrameBlocks, totalFrames,
            blocksPerFrame, finalFrameBlocks, offsets, frameDataEnd, maxFrameBytes, seekBits);
    }

    internal static int[] DecodeToPcm32Samples(string path, ApeInfo info)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        var totalSamples = checked(info.TotalPcmFrames * info.Channels);
        if (totalSamples > int.MaxValue)
            throw new NotSupportedException("APE sample capture exceeds supported size.");
        var output = new int[totalSamples];
        var maxBytes = Math.Max(64, info.MaxFrameBytes + 8);
        var frameBytes = ArrayPool<byte>.Shared.Rent(maxBytes);
        var residual = new int[info.BlocksPerFrame];
        var residual2 = new int[info.BlocksPerFrame];
        var decoded = new int[info.BlocksPerFrame];
        var decoded2 = new int[info.BlocksPerFrame];
        var workspace = new LegacyFrameWorkspace(info);
        try
        {
            var write = 0;
            for (var frame = 0; frame < info.TotalFrames; frame++)
            {
                var length = info.GetFrameByteLength(frame);
                var offset = info.FrameOffsets[frame];
                stream.Position = offset;
                stream.ReadExactly(frameBytes.AsSpan(0, length));
                var bitStart = info.LegacySeekBits is { Length: > 0 } && frame < info.LegacySeekBits.Length
                    ? info.LegacySeekBits[frame]
                    : 0;
                var reader = new LegacyBitReader(frameBytes.AsSpan(0, length), bitStart);
                var stored = (info.FormatFlags & FormatFlagCrc) != 0
                    ? reader.ReadBits(32)
                    : reader.ReadRice(30);
                var special = 0;
                if ((info.FormatFlags & FormatFlagCrc) != 0 && info.Version > 3820 && (stored & 0x80000000u) != 0)
                {
                    special = unchecked((int)reader.ReadBits(32));
                    stored &= 0x7FFFFFFFu;
                }
                if ((info.FormatFlags & FormatFlagCrc) == 0 && stored == 0)
                    special = info.Channels == 1
                        ? SpecialFrameMonoSilence
                        : SpecialFrameLeftSilence | SpecialFrameRightSilence;

                var blocks = info.GetFrameBlocks(frame);
                var rangeReader = info.Version >= 3891
                    ? new LegacyRangeReader(frameBytes.AsSpan(0, length), reader.Position, info.Version)
                    : null;
                var isStereoSilence = info.Channels == 2 &&
                    (special & (SpecialFrameLeftSilence | SpecialFrameRightSilence)) ==
                    (SpecialFrameLeftSilence | SpecialFrameRightSilence);
                var isMonoSilence = info.Channels == 1 && (special & SpecialFrameMonoSilence) != 0;
                if (!isStereoSilence && !isMonoSilence)
                {
                    if (info.Version >= 3950 && info.Channels == 2 && (special & SpecialFramePseudoStereo) == 0)
                    {
                        // 3.95 以后位流先存 Y 再存 X，两个预测器还共享上一帧 X 状态。
                        if (rangeReader is null) throw new InvalidDataException("APE 3.95+ requires range-coded frame data.");
                        DecodeRangeArray(rangeReader, residual, blocks);
                        DecodeRangeArray(rangeReader, residual2, blocks);
                        UndoPredictionModernStereo(residual2, residual, decoded, decoded2, blocks, info.BitsPerSample, info.CompressionLevel, info.Version, workspace);
                    }
                    else
                    {
                        DecodeLegacyChannel(reader, rangeReader, residual, decoded, blocks, info.Version, info.CompressionLevel, info.BitsPerSample, workspace);
                    }
                }
                if (info.Channels == 2 && !isStereoSilence && (special & SpecialFramePseudoStereo) == 0 && info.Version < 3950)
                {
                    DecodeLegacyChannel(reader, rangeReader, residual, decoded2, blocks, info.Version, info.CompressionLevel, info.BitsPerSample, workspace);
                }

                var crc = new LegacyCrc32();
                uint oldChecksum = 0;
                for (var i = 0; i < blocks; i++)
                {
                    int left;
                    int right;
                    if (info.Channels == 1)
                    {
                        left = (special & SpecialFrameMonoSilence) != 0 ? 0 : decoded[i];
                        output[write++] = NormalizeLegacySample(left, info.BitsPerSample, info.Version);
                        crc.Update(left, info.BitsPerSample, info.FormatFlags, info.Version);
                        oldChecksum = unchecked(oldChecksum + AbsoluteToUInt(left));
                    }
                    else if ((special & SpecialFrameLeftSilence) != 0 && (special & SpecialFrameRightSilence) != 0)
                    {
                        output[write++] = 0;
                        output[write++] = 0;
                        crc.UpdateInterleaved(0, 0, info.BitsPerSample, info.FormatFlags, info.Version);
                    }
                    else if ((special & SpecialFramePseudoStereo) != 0)
                    {
                        left = decoded[i];
                        var normalized = NormalizeLegacySample(left, info.BitsPerSample, info.Version);
                        output[write++] = normalized;
                        output[write++] = normalized;
                        crc.UpdateInterleaved(left, left, info.BitsPerSample, info.FormatFlags, info.Version);
                        oldChecksum = unchecked(oldChecksum + AbsoluteToUInt(left) + AbsoluteToUInt(left));
                    }
                    else
                    {
                        var x = decoded[i];
                        var y = decoded2[i];
                        right = x - y / 2;
                        left = right + y;
                        output[write++] = NormalizeLegacySample(right, info.BitsPerSample, info.Version);
                        output[write++] = NormalizeLegacySample(left, info.BitsPerSample, info.Version);
                        crc.UpdateInterleaved(right, left, info.BitsPerSample, info.FormatFlags, info.Version);
                        oldChecksum = unchecked(oldChecksum + AbsoluteToUInt(right) + AbsoluteToUInt(left));
                    }
                }

                if ((info.FormatFlags & FormatFlagCrc) != 0)
                {
                    var expected = stored;
                    if (crc.FinalizeValue(info.Version > 3820) != expected)
                        throw new InvalidDataException($"APE legacy frame CRC mismatch at frame={frame}.");
                }
                else if (oldChecksum != stored)
                {
                    throw new InvalidDataException($"APE legacy frame checksum mismatch at frame={frame}.");
                }
            }
            return output;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frameBytes);
        }
    }

    // 旧格式单帧解码保留 seekBits、历史校验和及各代 predictor 的原始顺序。
    // 大块残差与输出数组由流式 reader 长期复用，避免内存随歌曲长度增长。
    internal static int DecodeFrameToPcm32(FileStream stream, ApeInfo info, int frame,
        byte[] frameBytes, int[] output, int[] residual, int[] residual2, int[] decoded, int[] decoded2,
        LegacyFrameWorkspace workspace)
    {
        var length = info.GetFrameByteLength(frame);
        stream.Position = info.FrameOffsets[frame];
        stream.ReadExactly(frameBytes.AsSpan(0, length));
        var bitStart = info.LegacySeekBits is { Length: > 0 } && frame < info.LegacySeekBits.Length
            ? info.LegacySeekBits[frame] : 0;
        var reader = workspace.BitReader;
        reader.Reset(frameBytes.AsSpan(0, length), bitStart);
        var stored = (info.FormatFlags & FormatFlagCrc) != 0 ? reader.ReadBits(32) : reader.ReadRice(30);
        var special = 0;
        if ((info.FormatFlags & FormatFlagCrc) != 0 && info.Version > 3820 && (stored & 0x80000000u) != 0)
        {
            special = unchecked((int)reader.ReadBits(32));
            stored &= 0x7FFFFFFFu;
        }
        if ((info.FormatFlags & FormatFlagCrc) == 0 && stored == 0)
            special = info.Channels == 1 ? SpecialFrameMonoSilence : SpecialFrameLeftSilence | SpecialFrameRightSilence;

        var blocks = info.GetFrameBlocks(frame);
        LegacyRangeReader? rangeReader = null;
        if (info.Version >= 3891)
        {
            rangeReader = workspace.RangeReader;
            rangeReader.Reset(frameBytes.AsSpan(0, length), reader.Position, info.Version);
        }
        var stereoSilence = info.Channels == 2 &&
            (special & (SpecialFrameLeftSilence | SpecialFrameRightSilence)) ==
            (SpecialFrameLeftSilence | SpecialFrameRightSilence);
        var monoSilence = info.Channels == 1 && (special & SpecialFrameMonoSilence) != 0;
        if (!stereoSilence && !monoSilence)
        {
            if (info.Version >= 3950 && info.Channels == 2 && (special & SpecialFramePseudoStereo) == 0)
            {
                if (rangeReader is null) throw new InvalidDataException("APE 3.95+ requires range-coded frame data.");
                DecodeRangeArray(rangeReader, residual, blocks);
                DecodeRangeArray(rangeReader, residual2, blocks);
                UndoPredictionModernStereo(residual2, residual, decoded, decoded2, blocks,
                    info.BitsPerSample, info.CompressionLevel, info.Version, workspace);
            }
            else DecodeLegacyChannel(reader, rangeReader, residual, decoded, blocks,
                info.Version, info.CompressionLevel, info.BitsPerSample, workspace);
        }
        if (info.Channels == 2 && !stereoSilence && (special & SpecialFramePseudoStereo) == 0 && info.Version < 3950)
            DecodeLegacyChannel(reader, rangeReader, residual, decoded2, blocks,
                info.Version, info.CompressionLevel, info.BitsPerSample, workspace);

        var crc = workspace.Crc;
        crc.Reset();
        uint oldChecksum = 0;
        var write = 0;
        for (var i = 0; i < blocks; i++)
        {
            int first;
            int second = 0;
            if (info.Channels == 1)
            {
                first = (special & SpecialFrameMonoSilence) != 0 ? 0 : decoded[i];
                output[write++] = ScaleToPcm32(NormalizeLegacySample(first, info.BitsPerSample, info.Version), info.BitsPerSample);
                crc.Update(first, info.BitsPerSample, info.FormatFlags, info.Version);
                oldChecksum = unchecked(oldChecksum + AbsoluteToUInt(first));
            }
            else
            {
                if (stereoSilence) first = second = 0;
                else if ((special & SpecialFramePseudoStereo) != 0) first = second = decoded[i];
                else
                {
                    var x = decoded[i];
                    var y = decoded2[i];
                    first = x - y / 2;
                    second = first + y;
                }
                output[write++] = ScaleToPcm32(NormalizeLegacySample(first, info.BitsPerSample, info.Version), info.BitsPerSample);
                output[write++] = ScaleToPcm32(NormalizeLegacySample(second, info.BitsPerSample, info.Version), info.BitsPerSample);
                crc.UpdateInterleaved(first, second, info.BitsPerSample, info.FormatFlags, info.Version);
                oldChecksum = unchecked(oldChecksum + AbsoluteToUInt(first) + AbsoluteToUInt(second));
            }
        }
        if ((info.FormatFlags & FormatFlagCrc) != 0)
        {
            if (crc.FinalizeValue(info.Version > 3820) != stored)
                throw new InvalidDataException($"APE legacy frame CRC mismatch at frame={frame}.");
        }
        else if (oldChecksum != stored)
            throw new InvalidDataException($"APE legacy frame checksum mismatch at frame={frame}.");
        return blocks;
    }

    // 旧格式位读取器、范围读取器和校验器由统一 workspace 持有并在每帧复位。
    internal sealed class LegacyFrameWorkspace
    {
        internal LegacyFrameWorkspace(ApeInfo info)
        {
            BitReader = new LegacyBitReader(info.MaxFrameBytes + 8);
            RangeReader = new LegacyRangeReader(info.MaxFrameBytes + 8);
            Crc = new LegacyCrc32();
            Filters = ApeDecoder.CreateInt32Filters(info.CompressionLevel, info.Version);
            Predictor0 = new FastModernPredictor(info.BitsPerSample, info.CompressionLevel, info.Version);
            Predictor1 = new FastModernPredictor(info.BitsPerSample, info.CompressionLevel, info.Version);
            ShortInput = new short[info.BlocksPerFrame];
            Adapt = new short[info.BlocksPerFrame];
        }

        internal LegacyBitReader BitReader { get; }
        internal LegacyRangeReader RangeReader { get; }
        internal LegacyCrc32 Crc { get; }
        internal ApeDecoder.ApeNnFilterInt32[] Filters { get; }
        internal FastModernPredictor Predictor0 { get; }
        internal FastModernPredictor Predictor1 { get; }
        internal int[] CoefficientsA { get; } = new int[16];
        internal int[] CoefficientsB { get; } = new int[16];
        internal int[] Bm { get; } = new int[256];
        internal int[] Bp { get; } = new int[256];
        internal int[] History { get; } = new int[264];
        internal int[] M { get; } = new int[16];
        internal int[] Fm { get; } = new int[9];
        internal int[] Fp { get; } = new int[9];
        internal short[] ShortCoefficients { get; } = new short[256];
        internal short[] ShortInput { get; }
        internal short[] Adapt { get; }
    }

    private static void DecodeArray(LegacyBitReader reader, int[] output, int count, int version)
    {
        if (version < 3860)
        {
            var sum = 0u;
            var first = Math.Min(count, 5);
            for (var i = 0; i < first; i++)
            {
                output[i] = (int)reader.ReadRice(10);
                sum += (uint)output[i];
            }
            if (count <= 5)
            {
                for (var i = 0; i < count; i++) output[i] = ZigZag(output[i]);
                return;
            }

            var k = GetK(sum / 10u);
            var warmupEnd = Math.Min(count, 64);
            for (var i = 5; i < warmupEnd; i++)
            {
                output[i] = (int)reader.ReadRice(k);
                sum += (uint)output[i];
                k = GetK(sum / (uint)(i + 1) / 2u);
            }
            if (count <= 64)
            {
                for (var i = 0; i < count; i++) output[i] = ZigZag(output[i]);
                return;
            }

            k = GetK(sum >> 7);
            var kMin = KMinOld[k];
            var kMax = KMaxOld[k];
            for (var i = 64; i < count; i++)
            {
                var encoded = (uint)reader.ReadRice(k);
                sum += encoded - (uint)output[i - 64];
                output[i] = unchecked((int)encoded);
                output[i - 64] = ZigZag(output[i - 64]);
                if (sum < kMin)
                {
                    do { k--; } while (sum < KMinOld[k]);
                }
                else if (k + 1 < KMaxOld.Length && KMaxOld[k + 1] != 0 && sum >= kMax)
                {
                    do { k++; } while (k + 1 < KMaxOld.Length && KMaxOld[k + 1] != 0 && sum >= KMaxOld[k]);
                }
                kMin = KMinOld[k];
                kMax = KMaxOld[k];
            }
            for (var i = count - 64; i < count; i++) output[i] = ZigZag(output[i]);
            return;
        }

        var currentK = 10u;
        var kSum = 1024u * 16u;
        for (var i = 0; i < count; i++)
        {
            output[i] = reader.DecodeRiceNew(ref currentK, ref kSum, version <= 3880);
        }
    }

    private static void DecodeRangeArray(LegacyRangeReader reader, int[] output, int count)
    {
        reader.FlushRange();
        var k = 10u;
        var kSum = 1024u * 16u;
        for (var i = 0; i < count; i++)
            output[i] = reader.DecodeValueRange(ref k, ref kSum);
        reader.FinalizeRange();
    }

    private static byte[] BuildRangeOverflowLookup1()
    {
        var table = new byte[65536];
        var overflow = 0;
        for (var i = 0; i < table.Length; i++)
        {
            if (i >= RangeTotal1[overflow + 1]) overflow++;
            table[i] = (byte)overflow;
        }
        return table;
    }

    private static uint GetK(uint value)
    {
        var k = 0;
        while (k < 31 && KMaxOld[k] != 0 && value >= KMaxOld[k])
            k++;
        return (uint)k;
    }

    private static int ZigZag(int value)
        => (value & 1) != 0 ? (value >> 1) + 1 : -(value >> 1);

    private static void UndoPrediction(int[] input, int[] output, int count, int version, int compression, int bitsPerSample,
        LegacyFrameWorkspace workspace)
    {
        if (version >= 3930)
        {
            // 3.93-3.94 的旧 predictor 不包含 NNFilter，需先在外层逆滤波。
            // 3.95 起现代 predictor 自己持有滤波链，外层重复处理会破坏单声道和伪立体声。
            if (version < 3950)
            {
                ApplyLegacyNnFilters(input, count, workspace.Filters);
                Fast3930To3950(input, output, count, workspace);
            }
            else Fast3950ToCurrent(input, output, count, workspace.Predictor0);
            return;
        }

        if (compression == CompressionFast)
        {
            if (version < 3320) Fast0000To3320(input, output, count);
            else
            {
                Fast3320To3930(input, count);
                Array.Copy(input, output, count);
            }
            return;
        }

        if (compression == CompressionNormal)
        {
            if (version < 3320) Normal0000To3320(input, output, count);
            else if (version < 3800) Normal3320To3800(input, output, count);
            else Normal3800To3929(input, output, count);
            return;
        }

        throw new NotSupportedException($"APE legacy compression level is not implemented: {compression}");
    }

    private static void UndoPredictionModernStereo(int[] residualX, int[] residualY, int[] outputX, int[] outputY,
        int count, int bitsPerSample, int compression, int version, LegacyFrameWorkspace workspace)
    {
        var predictorX = workspace.Predictor0;
        var predictorY = workspace.Predictor1;
        predictorX.Flush();
        predictorY.Flush();
        var lastX = 0;
        for (var i = 0; i < count; i++)
        {
            var y = predictorY.Decompress(residualY[i], lastX);
            var x = predictorX.Decompress(residualX[i], y);
            outputX[i] = x;
            outputY[i] = y;
            lastX = x;
        }
    }

    private static void ApplyLegacyNnFilters(int[] values, int count, ApeDecoder.ApeNnFilterInt32[] filters)
    {
        for (var i = 0; i < filters.Length; i++) filters[i].Flush();
        for (var sample = 0; sample < count; sample++)
        {
            var value = values[sample];
            for (var filter = filters.Length - 1; filter >= 0; filter--)
                value = filters[filter].Decompress(value);
            values[sample] = value;
        }
    }

    private static void Fast3930To3950(int[] input, int[] output, int count, LegacyFrameWorkspace workspace)
    {
        if (count == 0) return;
        var history = workspace.History; Array.Clear(history);
        var m = workspace.M; Array.Clear(m); m[0] = 360; m[1] = 317; m[2] = -109; m[3] = 98;
        var index = 8;
        var last = 0;
        for (var i = 0; i < count; i++)
        {
            if (index == 264)
            {
                Array.Copy(history, 256, history, 0, 8);
                index = 8;
            }

            var value = input[i];
            var p1 = history[index - 1];
            var p2 = history[index - 1] - history[index - 2];
            var p3 = history[index - 2] - history[index - 3];
            var p4 = history[index - 3] - history[index - 4];
            var filtered = value + ((p1 * m[0] + p2 * m[1] + p3 * m[2] + p4 * m[3]) >> 9);
            var direction = (value < 0 ? 1 : 0) - (value > 0 ? 1 : 0);
            m[0] += (((p1 >> 30) & 2) - 1) * direction;
            m[1] += (((p2 >> 30) & 2) - 1) * direction;
            m[2] += (((p3 >> 30) & 2) - 1) * direction;
            m[3] += (((p4 >> 30) & 2) - 1) * direction;
            var result = filtered + ((last * 31) >> 5);
            history[index] = filtered;
            output[i] = result;
            last = result;
            index++;
        }
    }

    private static void Fast3950ToCurrent(int[] input, int[] output, int count, FastModernPredictor predictor)
    {
        predictor.Flush();
        for (var i = 0; i < count; i++)
            output[i] = predictor.Decompress(input[i], 0);
    }

    internal sealed class FastModernPredictor
    {
        private readonly long[] _predictionA = new long[264];
        private readonly long[] _predictionB = new long[264];
        private readonly long[] _adaptA = new long[264];
        private readonly long[] _adaptB = new long[264];
        private readonly long[] _ma = new long[8];
        private readonly long[] _mb = new long[8];
        private readonly bool _wide;
        private readonly ApeDecoder.ApeNnFilterInt32[] _filters;
        private int _index;
        private int _lastValue;
        private int _stageA;
        private int _stageB;

        internal FastModernPredictor(int bitsPerSample, int compression, int version)
        {
            _wide = bitsPerSample > 16;
            _filters = ApeDecoder.CreateInt32Filters(compression, version);
        }

        internal void Flush()
        {
            Array.Clear(_predictionA); Array.Clear(_predictionB); Array.Clear(_adaptA); Array.Clear(_adaptB);
            Array.Clear(_ma); Array.Clear(_mb);
            _ma[0] = 360; _ma[1] = 317; _ma[2] = -109; _ma[3] = 98;
            _index = 8; _lastValue = 0; _stageA = 0; _stageB = 0;
            for (var i = 0; i < _filters.Length; i++) _filters[i].Flush();
        }

        internal int Decompress(int residual, int reference)
        {
            for (var i = _filters.Length - 1; i >= 0; i--) residual = _filters[i].Decompress(residual);
            if (_index == 264)
            {
                Array.Copy(_predictionA, 256, _predictionA, 0, 8);
                Array.Copy(_predictionB, 256, _predictionB, 0, 8);
                Array.Copy(_adaptA, 256, _adaptA, 0, 8);
                Array.Copy(_adaptB, 256, _adaptB, 0, 8);
                _index = 8;
            }

            var predictionA0 = _lastValue;
            var predictionA1 = predictionA0 - _predictionA[_index - 1];
            var predictionB0 = reference - ((_stageB * 31) >> 5);
            _stageB = reference;
            var predictionB1 = predictionB0 - _predictionB[_index - 1];
            _predictionA[_index] = predictionA0;
            _predictionA[_index - 1] = predictionA1;
            _predictionB[_index] = predictionB0;
            _predictionB[_index - 1] = predictionB1;

            var pa = predictionA0 * _ma[0] + predictionA1 * _ma[1] + _predictionA[_index - 2] * _ma[2] + _predictionA[_index - 3] * _ma[3];
            var pb = predictionB0 * _mb[0] + predictionB1 * _mb[1] + _predictionB[_index - 2] * _mb[2] + _predictionB[_index - 3] * _mb[3] + _predictionB[_index - 4] * _mb[4];
            var current = _wide
                ? unchecked(residual + (int)(((int)pa + ((int)pb >> 1)) >> 10))
                : unchecked(residual + (int)((pa + (pb >> 1)) >> 10));

            _adaptA[_index] = Adapt(predictionA0); _adaptA[_index - 1] = Adapt(predictionA1);
            _adaptB[_index] = Adapt(predictionB0); _adaptB[_index - 1] = Adapt(predictionB1);
            var direction = (residual < 0 ? 1L : 0L) - (residual > 0 ? 1L : 0L);
            _ma[0] += _adaptA[_index] * direction; _ma[1] += _adaptA[_index - 1] * direction;
            _ma[2] += _adaptA[_index - 2] * direction; _ma[3] += _adaptA[_index - 3] * direction;
            _mb[0] += _adaptB[_index] * direction; _mb[1] += _adaptB[_index - 1] * direction;
            _mb[2] += _adaptB[_index - 2] * direction; _mb[3] += _adaptB[_index - 3] * direction;
            _mb[4] += _adaptB[_index - 4] * direction;

            _stageA = current + ((_stageA * 31) >> 5);
            _lastValue = current;
            _index++;
            return _stageA;
        }

        private static long Adapt(long value) => value != 0 ? (((value >> 30) & 2L) - 1L) : 0L;
    }

    private static void Fast0000To3320(int[] input, int[] output, int n)
    {
        if (n < 32) { Array.Copy(input, output, n); return; }
        output[0] = input[0];
        for (var i = 1; i < 8; i++) output[i] = input[i] + output[i - 1];
        var m = 4000;
        var p = output[7] * 2 - output[6];
        for (var i = 8; i < n; i++)
        {
            output[i] = input[i] + ((p * m) >> 12);
            if (input[i] > 0) m += p > 0 ? 4 : -4;
            else if (input[i] < 0) m += p > 0 ? -4 : 4;
            p = output[i] * 2 - output[i - 1];
        }
    }

    private static void Fast3320To3930(int[] a, int n)
    {
        if (n < 3) return;
        var m = 375;
        var ip2 = a[1];
        var ip3 = a[0];
        var op1 = a[1];
        for (var i = 2; i < n; i++)
        {
            var p = ip2 * 2 - ip3;
            ip3 = ip2;
            ip2 = a[i] + ((p * m) >> 9);
            m += ((a[i] ^ p) > 0) ? 1 : -1;
            a[i] = ip2 + op1;
            op1 = a[i];
        }
    }

    private static void Normal0000To3320(int[] input, int[] output, int n)
    {
        if (n < 32) { Array.Copy(input, output, n); return; }
        Array.Copy(input, output, 8);
        var m = 300;
        for (var i = 8; i < n; i++)
        {
            var p = output[i - 1] * 3 - output[i - 2] * 3 + output[i - 3];
            output[i] = input[i] + ((p * m) >> 12);
            if (input[i] > 0) m += p > 0 ? 4 : -4; else if (input[i] < 0) m += p > 0 ? -4 : 4;
        }
        Array.Copy(output, input, 8);
        m = 3000;
        for (var i = 8; i < n; i++)
        {
            var p = input[i - 1] * 2 - input[i - 2];
            input[i] = output[i] + ((p * m) >> 12);
            if (output[i] > 0) m += p > 0 ? 12 : -12; else if (output[i] < 0) m += p > 0 ? -12 : 12;
        }
        Array.Copy(input, output, 8);
        m = 3900;
        for (var i = 8; i < n; i++)
        {
            var p = output[i - 1];
            output[i] = input[i] + ((p * m) >> 12);
            if (input[i] > 0) m += p > 0 ? 1 : -1; else if (input[i] < 0) m += p > 0 ? -1 : 1;
        }
    }

    private static void Normal3320To3800(int[] input, int[] output, int n)
    {
        if (n < 8) { Array.Copy(input, output, n); return; }
        Array.Copy(input, output, 5);
        var m1 = 0; var m2 = 64; var m3 = 28;
        var p3 = 3 * (output[4] - output[3]) + output[2];
        var p2 = input[4] + ((input[2] - input[3]) << 3) - input[1] + input[0];
        var p1 = output[4];
        for (var i = 5; i < n; i++)
        {
            var op0 = input[i] + ((p1 * m1) >> 8); m1 += ((input[i] ^ p1) > 0) ? 1 : -1; p1 = op0;
            input[i] = op0 + ((p2 * m2) >> 11); m2 += ((op0 ^ p2) > 0) ? 1 : -1;
            p2 = input[i] + ((input[i - 2] - input[i - 1]) << 3) - input[i - 3] + input[i - 4];
            output[i] = input[i] + ((p3 * m3) >> 9); m3 += ((input[i] ^ p3) > 0) ? 1 : -1;
            p3 = 3 * (output[i] - output[i - 1]) + output[i - 2];
        }
        var m4 = 370; var m5 = 3900;
        for (var i = 1; i < 5; i++) output[i] = input[i] + output[i - 1];
        var p4 = 2 * input[4] - input[3]; var p5 = output[4]; var ip1 = input[4];
        for (var i = 5; i < n; i++)
        {
            var ip0 = output[i] + ((p4 * m4) >> 9); m4 += ((ip0 ^ p4) > 0) ? 1 : -1; p4 = 2 * ip0 - ip1;
            output[i] = ip0 + ((p5 * m5) >> 12); m5 += ((ip0 ^ p5) > 0) ? 1 : -1; p5 = output[i]; ip1 = ip0;
        }
    }

    private static void Normal3800To3929(int[] input, int[] output, int n)
    {
        if (n < 8) { Array.Copy(input, output, n); return; }
        Array.Copy(input, output, 4);
        var m2 = 64; var m3 = 115; var m4 = 64; var m5 = 740; var m6 = 0;
        var p4 = input[3]; var p3 = (input[3] - input[2]) << 1; var p2 = input[3] + ((input[1] - input[2]) << 3);
        for (var i = 1; i < 4; i++) output[i] += output[i - 1];
        var ipp2 = input[2]; var p7 = 2 * input[3] - input[2]; var opp = output[3];
        for (var i = 4; i < n; i++)
        {
            var value = input[i] + ((p2 * m2 + p3 * m3 + p4 * m4) >> 11);
            if (input[i] > 0) { m2 -= ((p2 >> 30) & 2) - 1; m3 -= ((p3 >> 28) & 8) - 4; m4 -= ((p4 >> 28) & 8) - 4; }
            else if (input[i] < 0) { m2 += ((p2 >> 30) & 2) - 1; m3 += ((p3 >> 28) & 8) - 4; m4 += ((p4 >> 28) & 8) - 4; }
            p2 = value + ((ipp2 - p4) << 3); p3 = (value - p4) << 1; ipp2 = p4; p4 = value;
            value += ((p7 * m5 - opp * m6) >> 10);
            if (p4 > 0) { m5 -= ((p7 >> 29) & 4) - 2; m6 += ((opp >> 30) & 2) - 1; }
            else if (p4 < 0) { m5 += ((p7 >> 29) & 4) - 2; m6 -= ((opp >> 30) & 2) - 1; }
            p7 = 2 * value - opp; opp = value; output[i] = value + ((output[i - 1] * 31) >> 5);
        }
    }

    private static uint ReadUInt32(FileStream stream)
    {
        Span<byte> buffer = stackalloc byte[4]; stream.ReadExactly(buffer); return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private static long TrimLegacyTrailingTag(FileStream stream, long frameDataEnd)
    {
        // 旧文件的帧表不记录标签长度，读取尾部签名以免把 APE/ID3 标签误当成最后一帧。
        var original = stream.Position;
        try
        {
            if (stream.Length >= 32)
            {
                stream.Position = stream.Length - 32;
                Span<byte> footer = stackalloc byte[32];
                stream.ReadExactly(footer);
                if (footer[..8].SequenceEqual("APETAGEX"u8))
                {
                    var tagBytes = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(12, 4));
                    if (tagBytes >= 32 && tagBytes <= stream.Length)
                        frameDataEnd = Math.Min(frameDataEnd, stream.Length - tagBytes);
                }
            }

            if (stream.Length >= 128)
            {
                stream.Position = stream.Length - 128;
                Span<byte> id3 = stackalloc byte[3];
                stream.ReadExactly(id3);
                if (id3.SequenceEqual("TAG"u8))
                    frameDataEnd = Math.Min(frameDataEnd, stream.Length - 128);
            }
        }
        finally
        {
            stream.Position = original;
        }

        return frameDataEnd;
    }

    private static int ScaleToPcm32(int value, int bits)
        => bits switch { 8 => value << 24, 16 => value << 16, 24 => value << 8, 32 => value, _ => throw new NotSupportedException() };

    private static int NormalizeLegacySample(int value, int bits, int version)
        => bits == 8 && version <= 3830 ? unchecked((byte)value) - 128 : value;

    private static uint AbsoluteToUInt(int value)
        => value >= 0 ? (uint)value : unchecked((uint)(-(long)value));

    internal sealed class LegacyBitReader
    {
        private readonly uint[] _words;
        private int _bits;
        private int _index;
        internal LegacyBitReader(ReadOnlySpan<byte> data, int startBit)
            : this(data.Length)
        {
            Reset(data, startBit);
        }
        internal LegacyBitReader(int maxBytes)
        {
            _words = new uint[(maxBytes + 3) / 4 + 1];
        }
        internal void Reset(ReadOnlySpan<byte> data, int startBit)
        {
            if (data.Length > (_words.Length - 1) * 4) throw new ArgumentOutOfRangeException(nameof(data));
            _bits = data.Length * 8;
            _index = startBit;
            for (var i = 0; i < data.Length; i += 4)
            {
                var count = Math.Min(4, data.Length - i);
                _words[i / 4] = count switch
                {
                    4 => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4)),
                    3 => (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16),
                    2 => (uint)(data[i] | data[i + 1] << 8),
                    _ => data[i]
                };
            }
            _words[(data.Length + 3) / 4] = 0;
        }
        internal uint ReadBits(int count)
        {
            if (count == 0) return 0;
            if (_index + count > _bits) throw new EndOfStreamException("APE legacy bitstream ended.");
            var word = _index >> 5; var shift = _index & 31; _index += count;
            if (shift + count <= 32) return (_words[word] >> (32 - shift - count)) & (count == 32 ? uint.MaxValue : (1u << count) - 1);
            var first = 32 - shift; var second = count - first;
            return ((_words[word] & ((1u << first) - 1)) << second) | (_words[word + 1] >> (32 - second));
        }
        internal int Position => _index;
        internal uint ReadRice(uint k)
        {
            var overflow = 0u; while (ReadBits(1) == 0) overflow++;
            return (overflow << (int)k) | ReadBits((int)k);
        }
        internal int DecodeRiceNew(ref uint k, ref uint kSum, bool uncapped)
        {
            var overflow = 0u; while (ReadBits(1) == 0) overflow++;
            if (!uncapped) while (overflow >= 16) { k += 4; overflow -= 16; }
            var value = (overflow << (int)Math.Min(k, 31)) | (k == 0 ? 0 : ReadBits((int)Math.Min(k, 31)));
            kSum += value - ((kSum + 8) >> 4);
            if (k > 31) k = 31;
            if (k > 0 && kSum < KMin[k]) k--; else if (k + 1 < KMax.Length && KMax[k] != 0 && kSum >= KMax[k]) k++;
            return ZigZag(unchecked((int)value));
        }
    }

    internal sealed class LegacyRangeReader
    {
        private readonly uint[] _words;
        private int _bits;
        private int _version;
        private int _index;
        private uint _low;
        private uint _range;
        private uint _buffer;

        internal LegacyRangeReader(ReadOnlySpan<byte> data, int startBit, int version)
            : this(data.Length)
        {
            Reset(data, startBit, version);
        }
        internal LegacyRangeReader(int maxBytes)
        {
            _words = new uint[(maxBytes + 3) / 4 + 1];
        }
        internal void Reset(ReadOnlySpan<byte> data, int startBit, int version)
        {
            if (data.Length > (_words.Length - 1) * 4) throw new ArgumentOutOfRangeException(nameof(data));
            _version = version;
            _bits = data.Length * 8;
            _index = startBit;
            for (var i = 0; i < data.Length; i += 4)
            {
                var count = Math.Min(4, data.Length - i);
                _words[i / 4] = count switch
                {
                    4 => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4)),
                    3 => (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16),
                    2 => (uint)(data[i] | data[i + 1] << 8),
                    _ => data[i]
                };
            }
            _words[(data.Length + 3) / 4] = 0;
            _low = 0;
            _range = 0;
            _buffer = 0;
        }

        internal void FlushRange()
        {
            _index += (8 - (_index & 7)) & 7;
            _ = ReadBits(8);
            _buffer = ReadBits(8);
            _low = _buffer >> 1;
            _range = 1u << 7;
        }

        internal int DecodeValueRange(ref uint k, ref uint kSum)
        {
            var total = DecodeFast(16);
            if (total >= 65536) throw new InvalidDataException("APE legacy range symbol is invalid.");
            var overflow = RangeOverflowLookup1[total];
            _low -= _range * RangeTotal1[overflow];
            _range *= RangeWidth1[overflow];
            uint tempK;
            if (overflow == 63)
            {
                tempK = DecodeFastWithUpdate(5);
                overflow = 0;
            }
            else
            {
                tempK = k == 0 ? 0 : k - 1;
            }

            uint value;
            if (tempK <= 16 || _version < 3910)
            {
                value = DecodeFastWithUpdate((int)tempK);
            }
            else
            {
                var low = DecodeFastWithUpdate(16);
                var high = DecodeFastWithUpdate((int)tempK - 16);
                value = low | (high << 16);
            }
            value += (uint)((ulong)overflow << (int)tempK);
            kSum += ((value + 1) / 2) - ((kSum + 16) >> 5);
            if (k > 0 && kSum < KMin[k]) k--;
            else if (k + 1 < KMin.Length && KMin[k + 1] != 0 && kSum >= KMin[k + 1]) k++;
            return (value & 1) != 0 ? (int)(value >> 1) + 1 : -(int)(value >> 1);
        }

        internal void FinalizeRange()
        {
            while (_range <= (1u << 23))
            {
                _index += 8;
                _range <<= 8;
                if (_range == 0) break;
            }
            if (_version <= 3950)
                _index -= 16;
        }

        private uint DecodeFast(int shift)
        {
            Normalize();
            _range >>= shift;
            return _low / _range;
        }

        private uint DecodeFastWithUpdate(int shift)
        {
            Normalize();
            _range >>= shift;
            if (_range == 0) throw new InvalidDataException("APE legacy range decoder entered zero range.");
            var value = _low / _range;
            _low %= _range;
            return value;
        }

        private void Normalize()
        {
            while (_range <= (1u << 23))
            {
                _buffer = (_buffer << 8) | ReadBits(8);
                _low = (_low << 8) | ((_buffer >> 1) & 0xFF);
                _range <<= 8;
                if (_range == 0) throw new InvalidDataException("APE legacy range decoder overflowed.");
            }
        }

        private uint ReadBits(int count)
        {
            if (_index + count > _bits) throw new EndOfStreamException("APE legacy range bitstream ended.");
            var word = _index >> 5; var shift = _index & 31; _index += count;
            if (shift + count <= 32) return (_words[word] >> (32 - shift - count)) & (count == 32 ? uint.MaxValue : (1u << count) - 1);
            var first = 32 - shift; var second = count - first;
            return ((_words[word] & ((1u << first) - 1)) << second) | (_words[word + 1] >> (32 - second));
        }
    }

    internal sealed class LegacyCrc32
    {
        private static readonly uint[] Table = BuildTable(); private uint _value = 0xFFFFFFFFu;
        internal void Reset() => _value = 0xFFFFFFFFu;
        internal void UpdateInterleaved(int a, int b, int bits, int flags, int version) { Update(a, bits, flags, version); Update(b, bits, flags, version); }
        internal void Update(int sample, int bits, int flags, int version)
        {
            var value = unchecked((uint)sample);
            var bytes = bits / 8;
            if (bits == 8)
                value = version <= 3830 ? unchecked((byte)sample) : unchecked((byte)(sample + 128));
            for (var i = 0; i < bytes; i++) { var b = (byte)(value >> (i * 8)); _value = Table[(_value ^ b) & 0xFF] ^ (_value >> 8); }
        }
        internal uint FinalizeValue(bool shifted) { var value = _value ^ 0xFFFFFFFFu; return shifted ? value >> 1 : value; }
        private static uint[] BuildTable() { var t = new uint[256]; for (uint i = 0; i < t.Length; i++) { var c = i; for (var j = 0; j < 8; j++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; } return t; }
    }
}
