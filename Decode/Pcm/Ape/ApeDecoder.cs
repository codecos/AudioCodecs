using System.Buffers;
using System.Buffers.Binary;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Ape;

internal readonly record struct ApeInfo(
    int Version,
    int CompressionLevel,
    int FormatFlags,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    long TotalPcmFrames,
    int TotalFrames,
    int BlocksPerFrame,
    int FinalFrameBlocks,
    long[] FrameOffsets,
    long FrameDataEnd,
    int MaxFrameBytes,
    byte[]? LegacySeekBits = null)
{
    internal int GetFrameBlocks(int frameIndex)
        => frameIndex == TotalFrames - 1 ? FinalFrameBlocks : BlocksPerFrame;

    internal int GetFrameByteLength(int frameIndex)
    {
        var nextOffset = frameIndex + 1 < TotalFrames
            ? FrameOffsets[frameIndex + 1]
            : FrameDataEnd;
        return checked((int)(nextOffset - FrameOffsets[frameIndex]));
    }
}

internal static class ApeDecoder
{
    private const int CompressionFast = 1000;
    private const int CompressionNormal = 2000;
    private const int CompressionHigh = 3000;
    private const int CompressionExtraHigh = 4000;
    private const int CompressionInsane = 5000;
    private const int FormatFlagBigEndian = 1 << 9;
    private const int FormatFlagSigned8Bit = 1 << 11;
    private const int FormatFlagFloatingPoint = 1 << 12;

    private const int SpecialFrameMonoSilence = 1;
    private const int SpecialFrameLeftSilence = 1;
    private const int SpecialFrameRightSilence = 2;
    private const int SpecialFramePseudoStereo = 4;

    private const uint RangeOverflowShift = 16;
    private const uint CodeBits = 32;
    private const uint TopValue = 1u << ((int)CodeBits - 1);
    private const int ExtraBits = ((int)CodeBits - 2) % 8 + 1;
    private const uint BottomValue = TopValue >> 8;
    private const uint OverflowSignal = 1;
    private const uint OverflowPivotValue = 32768;
    private const int ModelElements = 64;

    private static readonly uint[] PowersOfTwoMinusOne =
    {
        0, 1, 3, 7, 15, 31, 63, 127, 255, 511, 1023, 2047, 4095, 8191, 16383, 32767, 65535,
        131071, 262143, 524287, 1048575, 2097151, 4194303, 8388607, 16777215, 33554431,
        67108863, 134217727, 268435455, 536870911, 1073741823, 2147483647, 4294967295
    };

    private static readonly uint[] RangeTotal2 =
    {
        0, 19578, 36160, 48417, 56323, 60899, 63265, 64435, 64971, 65232, 65351, 65416, 65447,
        65466, 65476, 65482, 65485, 65488, 65490, 65491, 65492, 65493, 65494, 65495, 65496,
        65497, 65498, 65499, 65500, 65501, 65502, 65503, 65504, 65505, 65506, 65507, 65508,
        65509, 65510, 65511, 65512, 65513, 65514, 65515, 65516, 65517, 65518, 65519, 65520,
        65521, 65522, 65523, 65524, 65525, 65526, 65527, 65528, 65529, 65530, 65531, 65532,
        65533, 65534, 65535, 65536
    };

    private static readonly uint[] RangeWidth2 =
    {
        19578, 16582, 12257, 7906, 4576, 2366, 1170, 536, 261, 119, 65, 31, 19, 10, 6, 3, 3,
        2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
    };

    private static readonly uint[] KSumMinBoundary =
    {
        0, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144,
        524288, 1048576, 2097152, 4194304, 8388608, 16777216, 33554432, 67108864, 134217728,
        268435456, 536870912, 1073741824, 2147483648, 0, 0, 0, 0
    };

    private static readonly byte[] RangeOverflowLookup = BuildRangeOverflowLookup();

    internal static ApeInfo ReadInfo(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return ReadInfo(stream);
    }

    internal static DecodeResult DecodeToPcm32(string path)
    {
        var samples = DecodeToPcm32Samples(path);
        long sink = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sink = unchecked(sink ^ samples[i]);
        }

        return new DecodeResult(samples.LongLength, sink);
    }

    internal static DecodeResult DecodeToFloat32(string path)
    {
        var samples = DecodeToFloat32Samples(path);
        long sink = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sink = unchecked(sink ^ BitConverter.SingleToInt32Bits(samples[i]));
        }

        return new DecodeResult(samples.LongLength, sink);
    }

    internal static int[] DecodeToPcm32Samples(string path)
    {
        var raw = DecodeToRawIntSamples(path, out var info);
        var scaled = new int[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            scaled[i] = ScaleToPcm32(raw[i], info.BitsPerSample);
        }

        return scaled;
    }

    internal static float[] DecodeToFloat32Samples(string path)
    {
        var raw = DecodeToRawIntSamples(path, out var info);
        var scaled = new float[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            scaled[i] = ScaleToFloat32(raw[i], info.BitsPerSample);
        }

        return scaled;
    }

    private static ApeInfo ReadInfo(FileStream stream)
    {
        var descriptorOffset = FindDescriptorOffset(stream);
        if (descriptorOffset < 0)
        {
            throw new InvalidDataException("APE descriptor not found.");
        }

        stream.Position = descriptorOffset;
        Span<byte> commonHeader = stackalloc byte[8];
        stream.ReadExactly(commonHeader);
        var commonVersion = BinaryPrimitives.ReadUInt16LittleEndian(commonHeader.Slice(4, 2));
        if (commonVersion < 3980)
        {
            // 3.97 及更早版本采用旧头和旧位流，交给 SDK 兼容实现。
            return ApeLegacyDecoder.ReadInfo(stream, descriptorOffset);
        }

        stream.Position = descriptorOffset;
        Span<byte> descriptorBuffer = stackalloc byte[52];
        stream.ReadExactly(descriptorBuffer);

        var version = BinaryPrimitives.ReadUInt16LittleEndian(descriptorBuffer.Slice(4, 2));
        var descriptorBytes = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(descriptorBuffer.Slice(8, 4)));
        var headerBytes = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(descriptorBuffer.Slice(12, 4)));
        var seekTableBytes = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(descriptorBuffer.Slice(16, 4)));
        var headerDataBytes = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(descriptorBuffer.Slice(20, 4)));
        var frameDataBytesLow = BinaryPrimitives.ReadUInt32LittleEndian(descriptorBuffer.Slice(24, 4));
        var frameDataBytesHigh = BinaryPrimitives.ReadUInt32LittleEndian(descriptorBuffer.Slice(28, 4));
        var frameDataBytes = ((long)frameDataBytesHigh << 32) | frameDataBytesLow;

        if (descriptorBytes < descriptorBuffer.Length)
        {
            throw new InvalidDataException($"APE descriptor size is invalid: {descriptorBytes}");
        }

        if (descriptorBytes > descriptorBuffer.Length)
        {
            stream.Position += descriptorBytes - descriptorBuffer.Length;
        }

        Span<byte> headerBuffer = stackalloc byte[24];
        stream.ReadExactly(headerBuffer);

        var compressionLevel = BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.Slice(0, 2));
        var formatFlags = BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.Slice(2, 2));
        var blocksPerFrame = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.Slice(4, 4)));
        var finalFrameBlocks = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.Slice(8, 4)));
        var totalFrames = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.Slice(12, 4)));
        var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.Slice(16, 2));
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(headerBuffer.Slice(18, 2));
        var sampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.Slice(20, 4)));

        if (headerBytes < headerBuffer.Length)
        {
            throw new InvalidDataException($"APE header size is invalid: {headerBytes}");
        }

        if (headerBytes > headerBuffer.Length)
        {
            stream.Position += headerBytes - headerBuffer.Length;
        }

        if (version < 3990)
        {
            throw new NotSupportedException($"APE manual decoder currently supports modern files only: version={version}");
        }

        if (compressionLevel is not (CompressionFast or CompressionNormal or CompressionHigh or CompressionExtraHigh or CompressionInsane))
        {
            throw new NotSupportedException($"APE compression level is invalid: level={compressionLevel}");
        }

        if ((formatFlags & FormatFlagBigEndian) != 0)
        {
            throw new NotSupportedException("APE big-endian integer decode is not implemented.");
        }

        if ((formatFlags & FormatFlagFloatingPoint) != 0)
        {
            throw new NotSupportedException("APE floating-point decode is not implemented.");
        }

        if (channels is < 1 or > 2)
        {
            throw new NotSupportedException($"APE manual decoder currently supports mono/stereo only: channels={channels}");
        }

        if (bitsPerSample is not (8 or 16 or 24 or 32))
        {
            throw new NotSupportedException($"APE manual decoder currently supports integer 8/16/24/32-bit only: bits={bitsPerSample}");
        }

        if (totalFrames <= 0 || blocksPerFrame <= 0 || finalFrameBlocks <= 0)
        {
            throw new InvalidDataException(
                $"APE frame geometry is invalid: totalFrames={totalFrames} blocksPerFrame={blocksPerFrame} finalFrameBlocks={finalFrameBlocks}");
        }

        if (seekTableBytes < totalFrames * sizeof(uint))
        {
            throw new InvalidDataException($"APE seek table is too small: seekBytes={seekTableBytes} frames={totalFrames}");
        }

        var seekTableBuffer = new byte[seekTableBytes];
        stream.ReadExactly(seekTableBuffer);
        var frameOffsets = ConvertSeekTableToOffsets(seekTableBuffer, totalFrames, descriptorOffset);
        var frameDataStart = descriptorOffset + descriptorBytes + headerBytes + seekTableBytes + headerDataBytes;
        var frameDataEnd = frameDataStart + frameDataBytes;
        var maxFrameBytes = 0;
        for (var frameIndex = 0; frameIndex < totalFrames; frameIndex++)
        {
            var nextOffset = frameIndex + 1 < totalFrames ? frameOffsets[frameIndex + 1] : frameDataEnd;
            var frameByteLength = checked((int)(nextOffset - frameOffsets[frameIndex]));
            if (frameByteLength <= 0)
            {
                throw new InvalidDataException($"APE frame span is invalid: frame={frameIndex} bytes={frameByteLength}");
            }

            if (frameByteLength > maxFrameBytes)
            {
                maxFrameBytes = frameByteLength;
            }
        }

        var totalPcmFrames = ((long)(totalFrames - 1) * blocksPerFrame) + finalFrameBlocks;
        return new ApeInfo(
            Version: version,
            CompressionLevel: compressionLevel,
            FormatFlags: formatFlags,
            SampleRate: sampleRate,
            Channels: channels,
            BitsPerSample: bitsPerSample,
            TotalPcmFrames: totalPcmFrames,
            TotalFrames: totalFrames,
            BlocksPerFrame: blocksPerFrame,
            FinalFrameBlocks: finalFrameBlocks,
            FrameOffsets: frameOffsets,
            FrameDataEnd: frameDataEnd,
            MaxFrameBytes: maxFrameBytes);
    }

    private static int[] DecodeToRawIntSamples(string path, out ApeInfo info)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        info = ReadInfo(stream);

        if (info.Version < 3980)
        {
            return ApeLegacyDecoder.DecodeToPcm32Samples(path, info);
        }

        var totalSamples = checked(info.TotalPcmFrames * info.Channels);
        if (totalSamples > int.MaxValue)
        {
            throw new NotSupportedException($"APE sample capture exceeds supported size. samples={totalSamples}");
        }

        var output = new int[totalSamples];
        var frameBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, info.MaxFrameBytes + 8));
        try
        {
            IApePredictor predictor0 = CreatePredictor(info);
            IApePredictor predictor1 = CreatePredictor(info);
            var crc = new ApeCrc32();
            var write = 0;
            var frameBaseOffset = info.FrameOffsets[0];

            for (var frameIndex = 0; frameIndex < info.TotalFrames; frameIndex++)
            {
                var frameByteLength = info.GetFrameByteLength(frameIndex);
                var frameOffset = info.FrameOffsets[frameIndex];
                var seekRemainder = (int)((frameOffset - frameBaseOffset) & 3);
                var readOffset = frameOffset - seekRemainder;
                var readLength = frameByteLength + seekRemainder + 4;
                var available = checked((int)Math.Min(int.MaxValue, stream.Length - readOffset));
                if (available < frameByteLength + seekRemainder)
                {
                    throw new InvalidDataException($"APE frame data is truncated: frame={frameIndex} bytes={available} required={frameByteLength + seekRemainder}");
                }

                stream.Position = readOffset;
                stream.ReadExactly(frameBuffer.AsSpan(0, Math.Min(available, readLength)));

                var bitArray = new ApeBitArray(frameBuffer.AsSpan(0, Math.Min(available, readLength)), seekRemainder * 8);
                var storedCrc = bitArray.ReadUnsignedInt32();
                var specialCodes = 0;
                if ((storedCrc & 0x80000000u) != 0)
                {
                    specialCodes = unchecked((int)bitArray.ReadUnsignedInt32());
                    storedCrc &= 0x7FFFFFFFu;
                }

                predictor0.Flush();
                predictor1.Flush();
                bitArray.FlushState(out var bitState0);
                bitArray.FlushState(out var bitState1);
                bitArray.FlushRangeDecoder();
                crc.Reset();

                var frameBlocks = info.GetFrameBlocks(frameIndex);
                var lastX = 0;

                for (var blockIndex = 0; blockIndex < frameBlocks; blockIndex++)
                {
                    try
                    {
                        if (info.Channels == 2)
                        {
                            int first;
                            int second;
                            if ((specialCodes & SpecialFrameLeftSilence) != 0 && (specialCodes & SpecialFrameRightSilence) != 0)
                            {
                                first = 0;
                                second = 0;
                            }
                            else if ((specialCodes & SpecialFramePseudoStereo) != 0)
                            {
                                var x = predictor0.DecompressValue(bitArray.DecodeValueRange(ref bitState0), 0);
                                first = x;
                                second = x;
                            }
                            else
                            {
                                var y = predictor1.DecompressValue(bitArray.DecodeValueRange(ref bitState1), lastX);
                                var x = predictor0.DecompressValue(bitArray.DecodeValueRange(ref bitState0), y);
                                lastX = x;

                                first = x - (y / 2);
                                second = first + y;
                            }

                            output[write++] = first;
                            output[write++] = second;
                            crc.UpdateInterleavedFrame(first, second, info.BitsPerSample, info.FormatFlags);
                        }
                        else
                        {
                            var sample = (specialCodes & SpecialFrameMonoSilence) != 0
                                ? 0
                                : predictor0.DecompressValue(bitArray.DecodeValueRange(ref bitState0), 0);
                            output[write++] = sample;
                            crc.UpdateMonoFrame(sample, info.BitsPerSample, info.FormatFlags);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
                    {
                        throw new InvalidDataException(
                            $"APE decode failed at frame={frameIndex} block={blockIndex} bitIndex={bitArray.CurrentBitIndex} rangeLow=0x{bitArray.RangeLow:X8} rangeValue=0x{bitArray.RangeValue:X8} rangeBuffer=0x{bitArray.RangeBuffer:X8}",
                            ex);
                    }
                }

                bitArray.FinalizeRangeDecoder();
                var frameCrc = crc.FinalizeForFrame();
                if (frameCrc != storedCrc)
                {
                    throw new InvalidDataException(
                        $"APE frame CRC mismatch at frame={frameIndex}: expected=0x{storedCrc:X8} actual=0x{frameCrc:X8}");
                }
            }

            return output;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(frameBuffer);
        }
    }

    // 单帧入口复用调用方提供的压缩/PCM 缓冲，供流式播放和随机 seek 使用。
    // predictor、范围解码状态及 CRC 均严格在 APE 帧边界内创建和验证。
    internal static int DecodeModernFrameToPcm32(FileStream stream, ApeInfo info, int frameIndex,
        byte[] frameBuffer, int[] output, ModernFrameWorkspace workspace)
    {
        var frameByteLength = info.GetFrameByteLength(frameIndex);
        var frameOffset = info.FrameOffsets[frameIndex];
        var frameBaseOffset = info.FrameOffsets[0];
        var seekRemainder = (int)((frameOffset - frameBaseOffset) & 3);
        var readOffset = frameOffset - seekRemainder;
        var readLength = frameByteLength + seekRemainder + 4;
        var available = checked((int)Math.Min(int.MaxValue, stream.Length - readOffset));
        if (available < frameByteLength + seekRemainder)
            throw new InvalidDataException($"APE frame data is truncated: frame={frameIndex}.");

        stream.Position = readOffset;
        var actualRead = Math.Min(available, readLength);
        stream.ReadExactly(frameBuffer.AsSpan(0, actualRead));
        var bitArray = workspace.BitArray;
        bitArray.Reset(frameBuffer.AsSpan(0, actualRead), seekRemainder * 8);
        var storedCrc = bitArray.ReadUnsignedInt32();
        var specialCodes = 0;
        if ((storedCrc & 0x80000000u) != 0)
        {
            specialCodes = unchecked((int)bitArray.ReadUnsignedInt32());
            storedCrc &= 0x7FFFFFFFu;
        }

        var predictor0 = workspace.Predictor0;
        var predictor1 = workspace.Predictor1;
        predictor0.Flush();
        predictor1.Flush();
        bitArray.FlushState(out var bitState0);
        bitArray.FlushState(out var bitState1);
        bitArray.FlushRangeDecoder();
        var crc = workspace.Crc;
        crc.Reset();
        var blocks = info.GetFrameBlocks(frameIndex);
        var write = 0;
        var lastX = 0;
        for (var block = 0; block < blocks; block++)
        {
            int first;
            int second = 0;
            if (info.Channels == 2)
            {
                if ((specialCodes & SpecialFrameLeftSilence) != 0 && (specialCodes & SpecialFrameRightSilence) != 0)
                    first = second = 0;
                else if ((specialCodes & SpecialFramePseudoStereo) != 0)
                    first = second = predictor0.DecompressValue(bitArray.DecodeValueRange(ref bitState0), 0);
                else
                {
                    var y = predictor1.DecompressValue(bitArray.DecodeValueRange(ref bitState1), lastX);
                    var x = predictor0.DecompressValue(bitArray.DecodeValueRange(ref bitState0), y);
                    lastX = x;
                    first = x - y / 2;
                    second = first + y;
                }
                output[write++] = ScaleToPcm32(first, info.BitsPerSample);
                output[write++] = ScaleToPcm32(second, info.BitsPerSample);
                crc.UpdateInterleavedFrame(first, second, info.BitsPerSample, info.FormatFlags);
            }
            else
            {
                first = (specialCodes & SpecialFrameMonoSilence) != 0
                    ? 0 : predictor0.DecompressValue(bitArray.DecodeValueRange(ref bitState0), 0);
                output[write++] = ScaleToPcm32(first, info.BitsPerSample);
                crc.UpdateMonoFrame(first, info.BitsPerSample, info.FormatFlags);
            }
        }
        bitArray.FinalizeRangeDecoder();
        var actualCrc = crc.FinalizeForFrame();
        if (actualCrc != storedCrc)
            throw new InvalidDataException($"APE frame CRC mismatch at frame={frameIndex}: expected=0x{storedCrc:X8} actual=0x{actualCrc:X8}");
        return blocks;
    }

    // 现代格式的位数组、predictor、NNFilter 与 CRC 统一预创建，帧边界只 Reset 状态。
    internal sealed class ModernFrameWorkspace
    {
        internal ModernFrameWorkspace(ApeInfo info)
        {
            BitArray = new ApeBitArray(info.MaxFrameBytes + 8);
            Predictor0 = CreatePredictor(info);
            Predictor1 = CreatePredictor(info);
            Crc = new ApeCrc32();
        }

        internal ApeBitArray BitArray { get; }
        internal IApePredictor Predictor0 { get; }
        internal IApePredictor Predictor1 { get; }
        internal ApeCrc32 Crc { get; }
    }

    private static int FindDescriptorOffset(FileStream stream)
    {
        stream.Position = 0;
        Span<byte> id3 = stackalloc byte[10];
        var bytesRead = stream.Read(id3);
        var junkBytes = 0;
        if (bytesRead == id3.Length &&
            id3[0] == (byte)'I' &&
            id3[1] == (byte)'D' &&
            id3[2] == (byte)'3')
        {
            var syncSafeLength =
                ((id3[6] & 0x7F) << 21) |
                ((id3[7] & 0x7F) << 14) |
                ((id3[8] & 0x7F) << 7) |
                (id3[9] & 0x7F);
            var hasFooter = (id3[5] & 0x10) != 0;
            junkBytes = syncSafeLength + (hasFooter ? 20 : 10);
            stream.Position = junkBytes;
            if (!hasFooter)
            {
                while (stream.Position < stream.Length)
                {
                    var value = stream.ReadByte();
                    if (value != 0)
                    {
                        stream.Position--;
                        break;
                    }

                    junkBytes++;
                }
            }
        }

        stream.Position = junkBytes;
        var scanBytes = 0;
        while (stream.Position + 4 <= stream.Length && scanBytes < 1024 * 1024)
        {
            Span<byte> tag = stackalloc byte[4];
            stream.ReadExactly(tag);
            if (tag.SequenceEqual("MAC "u8) || tag.SequenceEqual("MACF"u8))
            {
                stream.Position -= 4;
                return checked((int)stream.Position);
            }

            stream.Position -= 3;
            junkBytes++;
            scanBytes++;
        }

        return -1;
    }

    private static long[] ConvertSeekTableToOffsets(ReadOnlySpan<byte> seekTableBuffer, int totalFrames, int descriptorOffset)
    {
        var offsets = new long[totalFrames];
        long seekAdd = 0;
        uint previous = 0;
        for (var i = 0; i < totalFrames; i++)
        {
            var current = BinaryPrimitives.ReadUInt32LittleEndian(seekTableBuffer.Slice(i * sizeof(uint), sizeof(uint)));
            if (current < previous)
            {
                seekAdd += (long)uint.MaxValue + 1L;
            }

            offsets[i] = descriptorOffset + seekAdd + current;
            previous = current;
        }

        return offsets;
    }

    private static int ScaleToPcm32(int value, int bitsPerSample)
        => bitsPerSample switch
        {
            8 => value << 24,
            16 => value << 16,
            24 => value << 8,
            32 => value,
            _ => throw new NotSupportedException($"Unsupported integer bit depth for PCM32 scaling: {bitsPerSample}")
        };

    private static float ScaleToFloat32(int value, int bitsPerSample)
        => bitsPerSample switch
        {
            8 => value / 128f,
            16 => value / 32768f,
            24 => value / 8388608f,
            32 => value / 2147483648f,
            _ => throw new NotSupportedException($"Unsupported integer bit depth for Float32 scaling: {bitsPerSample}")
        };

    private static byte[] BuildRangeOverflowLookup()
    {
        var lookup = new byte[65536];
        var overflow = 0;
        for (var i = 0; i < lookup.Length; i++)
        {
            if (i >= RangeTotal2[overflow + 1])
            {
                overflow++;
            }

            lookup[i] = (byte)overflow;
        }

        return lookup;
    }

    internal readonly struct UnbitArrayState
    {
        internal UnbitArrayState(uint k, uint kSum)
        {
            K = k;
            KSum = kSum;
        }

        internal uint K { get; init; }
        internal uint KSum { get; init; }
    }

    internal sealed class ApeBitArray
    {
        private readonly uint[] _bitArray;
        private int _goodBytes;
        private uint _currentBitIndex;
        private uint _rangeLow;
        private uint _rangeValue;
        private uint _rangeBuffer;

        internal ApeBitArray(ReadOnlySpan<byte> data, int startBitIndex = 0)
            : this(data.Length)
        {
            Reset(data, startBitIndex);
        }

        internal ApeBitArray(int maxBytes)
        {
            _bitArray = new uint[(maxBytes + 3) / 4 + 1];
        }

        internal void Reset(ReadOnlySpan<byte> data, int startBitIndex = 0)
        {
            if (data.Length > (_bitArray.Length - 1) * 4)
                throw new ArgumentOutOfRangeException(nameof(data), "APE bit workspace is too small.");
            _goodBytes = data.Length;
            var words = (data.Length + 3) / 4;
            for (var index = 0; index < words; index++)
            {
                var byteOffset = index * 4;
                if (byteOffset >= data.Length)
                {
                    break;
                }

                var remaining = Math.Min(4, data.Length - byteOffset);
                _bitArray[index] = remaining switch
                {
                    4 => BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(byteOffset, 4)),
                    3 => (uint)(data[byteOffset] | (data[byteOffset + 1] << 8) | (data[byteOffset + 2] << 16)),
                    2 => (uint)(data[byteOffset] | (data[byteOffset + 1] << 8)),
                    1 => data[byteOffset],
                    _ => 0
                };
            }
            _bitArray[words] = 0;

            _currentBitIndex = (uint)startBitIndex;
            _rangeLow = 0;
            _rangeValue = 0;
            _rangeBuffer = 0;
        }

        internal uint ReadUnsignedInt32() => DecodeValueXBits(32);

        internal void FlushState(out UnbitArrayState state)
            => state = new UnbitArrayState(10, (1u << 10) * 16u);

        internal void FlushRangeDecoder()
        {
            AdvanceToByteBoundary();
            _ = DecodeValueXBits(8);
            _rangeBuffer = DecodeValueXBits(8);
            _rangeLow = _rangeBuffer >> (8 - ExtraBits);
            _rangeValue = 1u << ExtraBits;
        }

        internal void FinalizeRangeDecoder()
        {
            while (_rangeValue <= BottomValue)
            {
                _currentBitIndex += 8;
                _rangeValue <<= 8;
                if (_rangeValue == 0)
                {
                    return;
                }
            }
        }

        internal uint CurrentBitIndex => _currentBitIndex;
        internal uint RangeLow => _rangeLow;
        internal uint RangeValue => _rangeValue;
        internal uint RangeBuffer => _rangeBuffer;

        internal long DecodeValueRange(ref UnbitArrayState state)
        {
            var pivotValue = Math.Max(state.KSum / 32u, 1u);
            var overflow = DecodeOverflow(ref pivotValue);
            uint baseValue;

            if (pivotValue >= (1u << 16))
            {
                var pivotBits = 0u;
                while ((pivotValue >> (int)pivotBits) > 0)
                {
                    pivotBits++;
                }

                var shift = pivotBits >= 16 ? pivotBits - 16 : 0;
                var splitFactor = 1u << (int)shift;
                var pivotA = (pivotValue / splitFactor) + 1;
                var pivotB = splitFactor;

                NormalizeRange();
                _rangeValue /= pivotA;
                var baseA = _rangeLow / _rangeValue;
                _rangeLow %= _rangeValue;

                NormalizeRange();
                _rangeValue /= pivotB;
                var baseB = _rangeLow / _rangeValue;
                _rangeLow %= _rangeValue;

                baseValue = baseA * splitFactor + baseB;
            }
            else
            {
                NormalizeRange();
                _rangeValue /= pivotValue;
                baseValue = _rangeLow / _rangeValue;
                _rangeLow %= _rangeValue;
            }

            var value = (long)baseValue + ((long)overflow * pivotValue);
            var newKSum = state.KSum + (uint)((value + 1) / 2) - ((state.KSum + 16u) >> 5);
            var newK = state.K;
            if (newK > 0 && newKSum < KSumMinBoundary[newK])
            {
                newK--;
            }
            else if (newK + 1 < KSumMinBoundary.Length &&
                     KSumMinBoundary[newK + 1] != 0 &&
                     newKSum >= KSumMinBoundary[newK + 1])
            {
                newK++;
            }

            state = new UnbitArrayState(newK, newKSum);
            return (value & 1) != 0 ? (value >> 1) + 1 : -(value >> 1);
        }

        private void AdvanceToByteBoundary()
        {
            var mod = _currentBitIndex & 7u;
            if (mod != 0)
            {
                _currentBitIndex += 8u - mod;
            }
        }

        private uint DecodeOverflow(ref uint pivotValue)
        {
            var rangeTotal = RangeDecodeFast(RangeOverflowShift);
            if (rangeTotal >= 65536)
            {
                throw new InvalidDataException($"APE range symbol is invalid: {rangeTotal}");
            }

            var overflow = RangeOverflowLookup[rangeTotal];
            _rangeLow -= _rangeValue * RangeTotal2[overflow];
            _rangeValue *= RangeWidth2[overflow];

            if (overflow == ModelElements - 1)
            {
                var value = RangeDecodeFastWithUpdate(16);
                value <<= 16;
                value |= RangeDecodeFastWithUpdate(16);
                if (value == OverflowSignal)
                {
                    pivotValue = OverflowPivotValue;
                    return DecodeOverflow(ref pivotValue);
                }

                return value;
            }

            return overflow;
        }

        private uint RangeDecodeFast(uint shift)
        {
            NormalizeRange();
            _rangeValue >>= (int)shift;
            return _rangeLow / _rangeValue;
        }

        private uint RangeDecodeFastWithUpdate(uint shift)
        {
            NormalizeRange();
            _rangeValue >>= (int)shift;
            if (_rangeValue == 0)
            {
                throw new InvalidDataException("APE range decoder entered an invalid zero-range state.");
            }

            var result = _rangeLow / _rangeValue;
            _rangeLow %= _rangeValue;
            return result;
        }

        private void NormalizeRange()
        {
            while (_rangeValue <= BottomValue)
            {
                _rangeBuffer = (_rangeBuffer << 8) | DecodeByte();
                _rangeLow = (_rangeLow << 8) | ((_rangeBuffer >> 1) & 0xFF);
                _rangeValue <<= 8;
                if (_rangeValue == 0)
                {
                    throw new InvalidDataException("APE range decoder overflowed while normalizing.");
                }
            }
        }

        private uint DecodeByte()
        {
            EnsureBitsAvailable(8);

            var value = (_bitArray[_currentBitIndex >> 5] >> (24 - ((int)_currentBitIndex & 31))) & 0xFFu;
            _currentBitIndex += 8;
            return value;
        }

        private uint DecodeValueXBits(uint bitCount)
        {
            EnsureBitsAvailable(bitCount);

            var leftBits = 32 - ((int)_currentBitIndex & 31);
            var wordIndex = (int)(_currentBitIndex >> 5);
            _currentBitIndex += bitCount;
            if (leftBits >= bitCount)
            {
                return (_bitArray[wordIndex] & PowersOfTwoMinusOne[leftBits]) >> (leftBits - (int)bitCount);
            }

            var rightBits = (int)bitCount - leftBits;
            var leftValue = (_bitArray[wordIndex] & PowersOfTwoMinusOne[leftBits]) << rightBits;
            var rightValue = _bitArray[wordIndex + 1] >> (32 - rightBits);
            return leftValue | rightValue;
        }

        private void EnsureBitsAvailable(uint bitCount)
        {
            if ((_currentBitIndex + bitCount) >= (uint)(_goodBytes * 8))
            {
                throw new EndOfStreamException($"APE frame bitstream ended while reading {bitCount} bits.");
            }
        }
    }

    internal interface IApePredictor
    {
        void Flush();
        int DecompressValue(long residualA, int referenceB);
    }

    private static IApePredictor CreatePredictor(ApeInfo info)
        => info.BitsPerSample == 32
            ? new FastApePredictorInt64(info.CompressionLevel, info.Version)
            : new FastApePredictor(info.BitsPerSample, info.CompressionLevel, info.Version);

    private sealed class FastApePredictor : IApePredictor
    {
        private readonly RollBuffer<int> _predictionA = new(historyElements: 8, windowElements: 256);
        private readonly RollBuffer<int> _predictionB = new(historyElements: 8, windowElements: 256);
        private readonly RollBuffer<int> _adaptA = new(historyElements: 8, windowElements: 256);
        private readonly RollBuffer<int> _adaptB = new(historyElements: 8, windowElements: 256);
        private readonly ScaledFirstOrderFilter _stage1FilterA = new();
        private readonly ScaledFirstOrderFilter _stage1FilterB = new();
        private readonly int[] _ma = new int[8];
        private readonly int[] _mb = new int[8];
        private readonly bool _useWidePredictionMath;
        private readonly ApeNnFilterInt32[] _filters;

        private int _lastValueA;
        private int _currentIndex;

        internal FastApePredictor(int bitsPerSample, int compressionLevel, int version)
        {
            _useWidePredictionMath = bitsPerSample > 16;
            _filters = CreateInt32Filters(compressionLevel, version);
        }

        public void Flush()
        {
            Array.Clear(_ma);
            Array.Clear(_mb);
            _predictionA.Flush();
            _predictionB.Flush();
            _adaptA.Flush();
            _adaptB.Flush();
            _ma[0] = 360;
            _ma[1] = 317;
            _ma[2] = -109;
            _ma[3] = 98;
            _stage1FilterA.Flush();
            _stage1FilterB.Flush();
            _lastValueA = 0;
            _currentIndex = 0;
            for (var i = 0; i < _filters.Length; i++) _filters[i].Flush();
        }

        public int DecompressValue(long residualA, int referenceB)
        {
            if (_currentIndex == 256)
            {
                _predictionA.Roll();
                _predictionB.Roll();
                _adaptA.Roll();
                _adaptB.Roll();
                _currentIndex = 0;
            }

            var nA = (int)residualA;
            var nB = referenceB;

            // SDK 解码时按编码滤波链的逆序恢复残差。
            for (var i = _filters.Length - 1; i >= 0; i--) nA = _filters[i].Decompress(nA);

            _predictionA[0] = _lastValueA;
            _predictionA[-1] = _predictionA[0] - _predictionA[-1];

            _predictionB[0] = _stage1FilterB.Compress(nB);
            _predictionB[-1] = _predictionB[0] - _predictionB[-1];

            int currentA;
            if (_useWidePredictionMath)
            {
                var predictionA =
                    ((long)_predictionA[0] * _ma[0]) +
                    ((long)_predictionA[-1] * _ma[1]) +
                    ((long)_predictionA[-2] * _ma[2]) +
                    ((long)_predictionA[-3] * _ma[3]);
                var predictionB =
                    ((long)_predictionB[0] * _mb[0]) +
                    ((long)_predictionB[-1] * _mb[1]) +
                    ((long)_predictionB[-2] * _mb[2]) +
                    ((long)_predictionB[-3] * _mb[3]) +
                    ((long)_predictionB[-4] * _mb[4]);
                currentA = unchecked(nA + (int)(((int)predictionA + (((int)predictionB) >> 1)) >> 10));
            }
            else
            {
                var predictionA =
                    ((long)_predictionA[0] * _ma[0]) +
                    ((long)_predictionA[-1] * _ma[1]) +
                    ((long)_predictionA[-2] * _ma[2]) +
                    ((long)_predictionA[-3] * _ma[3]);
                var predictionB =
                    ((long)_predictionB[0] * _mb[0]) +
                    ((long)_predictionB[-1] * _mb[1]) +
                    ((long)_predictionB[-2] * _mb[2]) +
                    ((long)_predictionB[-3] * _mb[3]) +
                    ((long)_predictionB[-4] * _mb[4]);
                currentA = unchecked(nA + (int)((predictionA + (predictionB >> 1)) >> 10));
            }
            _adaptA[0] = ComputeAdaptValue(_predictionA[0]);
            _adaptA[-1] = ComputeAdaptValue(_predictionA[-1]);
            _adaptB[0] = ComputeAdaptValue(_predictionB[0]);
            _adaptB[-1] = ComputeAdaptValue(_predictionB[-1]);

            var direction = (nA < 0 ? 1 : 0) - (nA > 0 ? 1 : 0);
            _ma[0] += _adaptA[0] * direction;
            _ma[1] += _adaptA[-1] * direction;
            _ma[2] += _adaptA[-2] * direction;
            _ma[3] += _adaptA[-3] * direction;
            _mb[0] += _adaptB[0] * direction;
            _mb[1] += _adaptB[-1] * direction;
            _mb[2] += _adaptB[-2] * direction;
            _mb[3] += _adaptB[-3] * direction;
            _mb[4] += _adaptB[-4] * direction;

            var result = _stage1FilterA.Decompress(currentA);
            _lastValueA = currentA;
            _predictionA.IncrementFast();
            _predictionB.IncrementFast();
            _adaptA.IncrementFast();
            _adaptB.IncrementFast();
            _currentIndex++;
            return result;
        }

        private static int ComputeAdaptValue(int value)
            => value != 0 ? (((value >> 30) & 2) - 1) : 0;
    }

    private sealed class FastApePredictorInt64 : IApePredictor
    {
        private readonly RollBuffer<long> _predictionA = new(historyElements: 8, windowElements: 256);
        private readonly RollBuffer<long> _predictionB = new(historyElements: 8, windowElements: 256);
        private readonly RollBuffer<long> _adaptA = new(historyElements: 8, windowElements: 256);
        private readonly RollBuffer<long> _adaptB = new(historyElements: 8, windowElements: 256);
        private readonly ScaledFirstOrderFilterInt64 _stage1FilterA = new();
        private readonly ScaledFirstOrderFilterInt64 _stage1FilterB = new();
        private readonly long[] _ma = new long[8];
        private readonly long[] _mb = new long[8];
        private readonly ApeNnFilterInt64[] _filters;

        private long _lastValueA;
        private int _currentIndex;

        internal FastApePredictorInt64(int compressionLevel, int version)
        {
            _filters = CreateInt64Filters(compressionLevel, version);
        }

        public void Flush()
        {
            Array.Clear(_ma);
            Array.Clear(_mb);
            _predictionA.Flush();
            _predictionB.Flush();
            _adaptA.Flush();
            _adaptB.Flush();
            _ma[0] = 360;
            _ma[1] = 317;
            _ma[2] = -109;
            _ma[3] = 98;
            _stage1FilterA.Flush();
            _stage1FilterB.Flush();
            _lastValueA = 0;
            _currentIndex = 0;
            for (var i = 0; i < _filters.Length; i++) _filters[i].Flush();
        }

        public int DecompressValue(long residualA, int referenceB)
        {
            if (_currentIndex == 256)
            {
                _predictionA.Roll();
                _predictionB.Roll();
                _adaptA.Roll();
                _adaptB.Roll();
                _currentIndex = 0;
            }

            var nA = residualA;
            var nB = (long)referenceB;
            for (var i = _filters.Length - 1; i >= 0; i--) nA = _filters[i].Decompress(nA);

            _predictionA[0] = _lastValueA;
            _predictionA[-1] = _predictionA[0] - _predictionA[-1];

            _predictionB[0] = _stage1FilterB.Compress(referenceB);
            _predictionB[-1] = _predictionB[0] - _predictionB[-1];

            var predictionA =
                (_predictionA[0] * _ma[0]) +
                (_predictionA[-1] * _ma[1]) +
                (_predictionA[-2] * _ma[2]) +
                (_predictionA[-3] * _ma[3]);
            var predictionB =
                (_predictionB[0] * _mb[0]) +
                (_predictionB[-1] * _mb[1]) +
                (_predictionB[-2] * _mb[2]) +
                (_predictionB[-3] * _mb[3]) +
                (_predictionB[-4] * _mb[4]);

            var currentA = nA + ((predictionA + (predictionB >> 1)) >> 10);
            _adaptA[0] = ComputeAdaptValue(_predictionA[0]);
            _adaptA[-1] = ComputeAdaptValue(_predictionA[-1]);
            _adaptB[0] = ComputeAdaptValue(_predictionB[0]);
            _adaptB[-1] = ComputeAdaptValue(_predictionB[-1]);

            var direction = (nA < 0 ? 1L : 0L) - (nA > 0 ? 1L : 0L);
            _ma[0] += _adaptA[0] * direction;
            _ma[1] += _adaptA[-1] * direction;
            _ma[2] += _adaptA[-2] * direction;
            _ma[3] += _adaptA[-3] * direction;
            _mb[0] += _adaptB[0] * direction;
            _mb[1] += _adaptB[-1] * direction;
            _mb[2] += _adaptB[-2] * direction;
            _mb[3] += _adaptB[-3] * direction;
            _mb[4] += _adaptB[-4] * direction;

            var result = _stage1FilterA.Decompress(currentA);
            _lastValueA = currentA;
            _predictionA.IncrementFast();
            _predictionB.IncrementFast();
            _adaptA.IncrementFast();
            _adaptB.IncrementFast();
            _currentIndex++;
            return result;
        }

        private static long ComputeAdaptValue(long value)
            => value != 0 ? (((value >> 30) & 2L) - 1L) : 0L;
    }

    internal static ApeNnFilterInt32[] CreateInt32Filters(int compressionLevel, int version)
        => compressionLevel switch
        {
            CompressionFast => Array.Empty<ApeNnFilterInt32>(),
            CompressionNormal => [new(16, 11, version)],
            CompressionHigh => [new(64, 11, version)],
            CompressionExtraHigh => [new(256, 13, version), new(32, 10, version)],
            CompressionInsane => [new(1280, 15, version), new(256, 13, version), new(16, 11, version)],
            _ => throw new NotSupportedException($"APE compression level is invalid: level={compressionLevel}")
        };

    private static ApeNnFilterInt64[] CreateInt64Filters(int compressionLevel, int version)
        => compressionLevel switch
        {
            CompressionFast => Array.Empty<ApeNnFilterInt64>(),
            CompressionNormal => [new(16, 11, version)],
            CompressionHigh => [new(64, 11, version)],
            CompressionExtraHigh => [new(256, 13, version), new(32, 10, version)],
            CompressionInsane => [new(1280, 15, version), new(256, 13, version), new(16, 11, version)],
            _ => throw new NotSupportedException($"APE compression level is invalid: level={compressionLevel}")
        };

    internal sealed class ApeNnFilterInt32
    {
        private readonly int _order;
        private readonly int _shift;
        private readonly int _rounding;
        private readonly bool _legacyDelta;
        private readonly short[] _coefficients;
        private readonly short[] _input;
        private readonly short[] _delta;
        private int _position;
        private int _runningAverage;

        internal ApeNnFilterInt32(int order, int shift, int version)
        {
            _order = order; _shift = shift; _rounding = 1 << (shift - 1); _legacyDelta = version < 3980;
            _coefficients = new short[order]; _input = new short[order + 512]; _delta = new short[order + 512];
            Flush();
        }

        internal void Flush()
        {
            Array.Clear(_coefficients); Array.Clear(_input); Array.Clear(_delta);
            _position = _order; _runningAverage = 0;
        }

        internal int Decompress(int value)
        {
            if (_position == _input.Length)
            {
                Array.Copy(_input, _position - _order, _input, 0, _order);
                Array.Copy(_delta, _position - _order, _delta, 0, _order);
                _position = _order;
            }
            var dot = 0;
            var start = _position - _order;
            for (var i = 0; i < _order; i++) dot = unchecked(dot + _input[start + i] * _coefficients[i]);
            var output = unchecked(value + ((dot + _rounding) >> _shift));
            var direction = value < 0 ? -1 : value > 0 ? 1 : 0;
            if (direction != 0)
                for (var i = 0; i < _order; i++) _coefficients[i] = unchecked((short)(_coefficients[i] - _delta[start + i] * direction));
            UpdateDelta(output);
            _input[_position] = SaturateToInt16(output);
            _position++;
            return output;
        }

        private void UpdateDelta(int value)
        {
            if (_legacyDelta)
            {
                _delta[_position] = value == 0 ? (short)0 : (short)(((value >> 28) & 8) - 4);
                _delta[_position - 4] >>= 1; _delta[_position - 8] >>= 1;
                return;
            }
            var absolute = value == int.MinValue ? int.MaxValue : Math.Abs(value);
            _delta[_position] = absolute > _runningAverage * 3
                ? (short)(((value >> 25) & 64) - 32)
                : absolute > (_runningAverage * 4) / 3
                    ? (short)(((value >> 26) & 32) - 16)
                    : absolute > 0 ? (short)(((value >> 27) & 16) - 8) : (short)0;
            _runningAverage += (absolute - _runningAverage) / 16;
            _delta[_position - 1] >>= 1; _delta[_position - 2] >>= 1; _delta[_position - 8] >>= 1;
        }

        private static short SaturateToInt16(int value)
            => value > short.MaxValue ? short.MaxValue : value < short.MinValue ? short.MinValue : (short)value;
    }

    internal sealed class ApeNnFilterInt64
    {
        private readonly int _order, _shift;
        private readonly long _rounding;
        private readonly bool _legacyDelta;
        private readonly int[] _coefficients, _input, _delta;
        private int _position;
        private long _runningAverage;

        internal ApeNnFilterInt64(int order, int shift, int version)
        {
            _order = order; _shift = shift; _rounding = 1L << (shift - 1); _legacyDelta = version < 3980;
            _coefficients = new int[order]; _input = new int[order + 512]; _delta = new int[order + 512]; Flush();
        }

        internal void Flush() { Array.Clear(_coefficients); Array.Clear(_input); Array.Clear(_delta); _position = _order; _runningAverage = 0; }

        internal long Decompress(long value)
        {
            if (_position == _input.Length)
            {
                Array.Copy(_input, _position - _order, _input, 0, _order); Array.Copy(_delta, _position - _order, _delta, 0, _order); _position = _order;
            }
            long dot = 0; var start = _position - _order;
            for (var i = 0; i < _order; i++) dot = unchecked(dot + (long)_input[start + i] * _coefficients[i]);
            var output = unchecked(value + ((dot + _rounding) >> _shift));
            var direction = value < 0 ? -1 : value > 0 ? 1 : 0;
            if (direction != 0) for (var i = 0; i < _order; i++) _coefficients[i] = unchecked(_coefficients[i] - _delta[start + i] * direction);
            UpdateDelta(output); _input[_position] = SaturateToInt16(output); _position++; return output;
        }

        private void UpdateDelta(long value)
        {
            if (_legacyDelta)
            {
                _delta[_position] = value == 0 ? 0 : (int)(((value >> 28) & 8) - 4); _delta[_position - 4] >>= 1; _delta[_position - 8] >>= 1; return;
            }
            var absolute = value == long.MinValue ? long.MaxValue : Math.Abs(value);
            _delta[_position] = absolute > _runningAverage * 3 ? (int)(((value >> 25) & 64) - 32)
                : absolute > (_runningAverage * 4) / 3 ? (int)(((value >> 26) & 32) - 16)
                : absolute > 0 ? (int)(((value >> 27) & 16) - 8) : 0;
            _runningAverage += (absolute - _runningAverage) / 16;
            _delta[_position - 1] >>= 1; _delta[_position - 2] >>= 1; _delta[_position - 8] >>= 1;
        }

        private static int SaturateToInt16(long value) => value > short.MaxValue ? short.MaxValue : value < short.MinValue ? short.MinValue : (short)value;
    }

    private sealed class RollBuffer<T>
    {
        private readonly T[] _data;
        private readonly int _historyElements;
        private readonly int _windowElements;
        private int _current;

        internal RollBuffer(int historyElements, int windowElements)
        {
            _historyElements = historyElements;
            _windowElements = windowElements;
            _data = new T[_historyElements + _windowElements];
            Flush();
        }

        internal T this[int index]
        {
            get => _data[_current + index];
            set => _data[_current + index] = value;
        }

        internal void Flush()
        {
            Array.Clear(_data, 0, _historyElements + 1);
            _current = _historyElements;
        }

        internal void Roll()
        {
            Array.Copy(_data, _current - _historyElements, _data, 0, _historyElements);
            _current = _historyElements;
        }

        internal void IncrementFast()
        {
            _current++;
        }
    }

    private sealed class ScaledFirstOrderFilter
    {
        private int _lastValue;

        internal void Flush()
        {
            _lastValue = 0;
        }

        internal int Compress(int input)
        {
            var result = input - ((_lastValue * 31) >> 5);
            _lastValue = input;
            return result;
        }

        internal int Decompress(int input)
        {
            _lastValue = input + ((_lastValue * 31) >> 5);
            return _lastValue;
        }
    }

    private sealed class ScaledFirstOrderFilterInt64
    {
        private int _lastValue;

        internal void Flush()
        {
            _lastValue = 0;
        }

        internal long Compress(int input)
        {
            var result = (long)input - (((long)_lastValue * 31) >> 5);
            _lastValue = input;
            return result;
        }

        internal int Decompress(long input)
        {
            _lastValue = (int)(input + (((long)_lastValue * 31) >> 5));
            return _lastValue;
        }
    }

    internal sealed class ApeCrc32
    {
        private static readonly uint[] Table = BuildTable();
        private uint _value = 0xFFFFFFFFu;

        internal void Reset()
        {
            _value = 0xFFFFFFFFu;
        }

        internal void UpdateMonoFrame(int sample, int bitsPerSample, int formatFlags)
        {
            UpdateSample(sample, bitsPerSample, formatFlags);
        }

        internal void UpdateInterleavedFrame(int first, int second, int bitsPerSample, int formatFlags)
        {
            UpdateSample(first, bitsPerSample, formatFlags);
            UpdateSample(second, bitsPerSample, formatFlags);
        }

        internal uint FinalizeForFrame()
        {
            var crc = _value ^ 0xFFFFFFFFu;
            crc >>= 1;
            return crc;
        }

        private void UpdateSample(int sample, int bitsPerSample, int formatFlags)
        {
            switch (bitsPerSample)
            {
                case 8:
                {
                    var value = (formatFlags & FormatFlagSigned8Bit) != 0
                        ? unchecked((byte)(sbyte)sample)
                        : checked((byte)(sample + 128));
                    UpdateByte(value);
                    break;
                }
                case 16:
                {
                    var value = unchecked((ushort)(short)sample);
                    UpdateByte((byte)value);
                    UpdateByte((byte)(value >> 8));
                    break;
                }
                case 24:
                {
                    var value = unchecked((uint)sample);
                    UpdateByte((byte)value);
                    UpdateByte((byte)(value >> 8));
                    UpdateByte((byte)(value >> 16));
                    break;
                }
                case 32:
                {
                    var value = unchecked((uint)sample);
                    UpdateByte((byte)value);
                    UpdateByte((byte)(value >> 8));
                    UpdateByte((byte)(value >> 16));
                    UpdateByte((byte)(value >> 24));
                    break;
                }
                default:
                    throw new NotSupportedException($"APE CRC sample update does not support bits={bitsPerSample}");
            }
        }

        private void UpdateByte(byte value)
        {
            _value = Table[(_value ^ value) & 0xFF] ^ (_value >> 8);
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var crc = i;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 1) != 0
                        ? 0xEDB88320u ^ (crc >> 1)
                        : crc >> 1;
                }

                table[i] = crc;
            }

            return table;
        }
    }
}
