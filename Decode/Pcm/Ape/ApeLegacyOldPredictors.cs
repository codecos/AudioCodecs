namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Ape;

// 3.92 及以前的 HIGH / EXTRA HIGH 使用多代专用反预测器。
// 这些实现逐段对应 SDK Old/AntiPredictorHighOld.cpp 与 AntiPredictorExtraHighOld.cpp，
// 保持原有整数溢出、移位和帧内系数读取顺序。
internal static partial class ApeLegacyDecoder
{
    private static void DecodeLegacyChannel(LegacyBitReader reader, LegacyRangeReader? rangeReader,
        int[] residual, int[] output, int count, int version, int compression, int bitsPerSample,
        LegacyFrameWorkspace workspace)
    {
        int[]? coefficientsA = null;
        int[]? coefficientsB = null;
        var iterations = -1;
        if (compression == CompressionExtraHigh && version < 3800)
        {
            var countBits = version < 3320 ? 4 : 3;
            var valueBits = version < 3600 ? 5 : 6;
            if (version < 3320) valueBits = 6;
            iterations = (int)reader.ReadBits(countBits);
            coefficientsA = workspace.CoefficientsA;
            coefficientsB = workspace.CoefficientsB;
            for (var i = 0; i <= iterations; i++)
            {
                coefficientsA[i] = (int)reader.ReadBits(valueBits);
                coefficientsB[i] = (int)reader.ReadBits(valueBits);
            }
        }

        if (rangeReader is null) DecodeArray(reader, residual, count, version);
        else DecodeRangeArray(rangeReader, residual, count);

        if (version >= 3930)
        {
            UndoPrediction(residual, output, count, version, compression, bitsPerSample, workspace);
            return;
        }

        switch (compression)
        {
            case CompressionFast:
            case CompressionNormal:
                UndoPrediction(residual, output, count, version, compression, bitsPerSample, workspace);
                break;
            case CompressionHigh:
                HighLegacy(residual, output, count, version, workspace);
                break;
            case CompressionExtraHigh:
                ExtraHighLegacy(residual, output, count, version, iterations, coefficientsA, coefficientsB, workspace);
                break;
            default:
                throw new NotSupportedException($"APE legacy compression level is invalid: {compression}");
        }
    }

    private static void HighLegacy(int[] input, int[] output, int count, int version, LegacyFrameWorkspace workspace)
    {
        if (version < 3320) High0000To3320(input, output, count);
        else if (version < 3600) High3320To3600(input, output, count);
        else if (version < 3700) High3600To3700(input, output, count, workspace);
        else if (version < 3800) High3700To3800(input, output, count, workspace);
        else High3800To3929(input, output, count, workspace);
    }

    private static void High0000To3320(int[] input, int[] output, int count)
    {
        if (count < 32) { Array.Copy(input, output, count); return; }
        Array.Copy(input, output, 8);
        var m = 0;
        for (var i = 8; i < count; i++)
        {
            var p = 5 * output[i - 1] - 10 * output[i - 2] + 12 * output[i - 3] - 7 * output[i - 4] + output[i - 5];
            output[i] = input[i] + ((p * m) >> 12);
            if (input[i] > 0) m += p > 0 ? 1 : -1; else if (input[i] < 0) m += p > 0 ? -1 : 1;
        }
        Array.Copy(output, input, 8); m = 0;
        for (var i = 8; i < count; i++)
        {
            var p = 4 * input[i - 1] - 6 * input[i - 2] + 4 * input[i - 3] - input[i - 4];
            input[i] = output[i] + ((p * m) >> 12);
            if (output[i] > 0) m += p > 0 ? 2 : -2; else if (output[i] < 0) m += p > 0 ? -2 : 2;
        }
        Normal0000To3320(input, output, count);
    }

    private static void High3320To3600(int[] input, int[] output, int count)
    {
        if (count < 8) { Array.Copy(input, output, count); return; }
        OffsetPredict(input, output, count, 2, 12); OffsetPredict(output, input, count, 3, 12);
        OffsetPredict(input, output, count, 4, 12); OffsetPredict(output, input, count, 5, 12);
        OffsetPredict(input, output, count, 6, 12); OffsetPredict(output, input, count, 7, 12);
        Normal3320To3800(input, output, count);
    }

    private static void OffsetPredict(int[] input, int[] output, int count, int offset, int delta)
    {
        Array.Copy(input, output, Math.Min(offset, count));
        var m = 0;
        for (var i = offset; i < count; i++)
        {
            output[i] = input[i] + ((output[i - offset] * m) >> 12);
            m += ((output[i - offset] ^ input[i]) > 0) ? delta : -delta;
        }
    }

    private static void High3600To3700(int[] input, int[] output, int count, LegacyFrameWorkspace workspace)
    {
        if (count < 16) { Array.Copy(input, output, count); return; }
        Array.Copy(input, output, 13);
        var bm = workspace.Bm; var bp = workspace.Bp;
        Array.Clear(bm, 0, 13); Array.Clear(bp, 0, 13);
        for (var i = 0; i < 13; i++) bp[i] = output[12 - i];
        var m2 = 64; var m3 = 28; var m4 = 16;
        var p4 = input[12]; var p3 = (input[12] - input[11]) << 1; var p2 = input[12] + ((input[10] - input[11]) << 3);
        for (var i = 13; i < count; i++)
        {
            var original = --input[i];
            var value = original;
            for (var j = 0; j < 13; j++) value += (j is 1 or 9 or 10 or 11 or 12 ? 1 : -1) * ((bp[j] * bm[j]) >> 8);
            for (var j = 0; j < 13; j++)
            {
                var positiveRule = (j & 1) == 0 ? bp[j] > 0 : bp[j] >= 0;
                var sign = original > 0 ? (positiveRule ? 1 : -1) : original < 0 ? ((j & 1) == 0 ? bp[j] <= 0 : bp[j] < 0) ? 1 : -1 : 0;
                bm[j] += (j is 1 or 9 or 10 or 11 or 12 ? 1 : -1) * sign;
            }
            for (var j = 12; j > 0; j--) bp[j] = bp[j - 1]; bp[0] = value;
            input[i] = value + ((p2 * m2) >> 11) + ((p3 * m3) >> 9) + ((p4 * m4) >> 9);
            if (value != 0) { var direction = value > 0 ? -1 : 1; m2 += direction * (p2 > 0 ? -1 : 1); m3 += direction * (p3 > 0 ? -1 : 1); m4 += direction * (p4 > 0 ? -1 : 1); }
            p2 = input[i] + ((input[i - 2] - input[i - 1]) << 3); p3 = (input[i] - input[i - 1]) << 1; p4 = input[i]; output[i] = input[i];
        }
        for (var i = 1; i < 13; i++) output[i] = input[i] + output[i - 1];
        m4 = 370; p4 = 2 * input[12] - input[11]; var p6 = 0; var p5 = output[12]; var previous = input[12]; var m6 = 0;
        for (var i = 13; i < count; i++)
        {
            var source = output[i]; var current = source + ((p4 * m4) >> 9) - ((p6 * m6) >> 10);
            m4 += (source ^ p4) >= 0 ? 1 : -1; m6 += (source ^ p6) >= 0 ? -1 : 1;
            p4 = 2 * current - previous; p6 = current; output[i] = current + ((p5 * 31) >> 5); p5 = output[i]; previous = current;
        }
    }

    private static void High3700To3800(int[] input, int[] output, int count, LegacyFrameWorkspace workspace)
    {
        const int first = 16;
        if (count < 20) { Array.Copy(input, output, count); return; }
        Array.Copy(input, output, first); var bm = workspace.Bm; Array.Clear(bm, 0, first);
        var m2 = 64; var m3 = 115; var m4 = 64; var m5 = 740; var m6 = 0;
        var p4 = input[15]; var p3 = (input[15] - input[14]) << 1; var p2 = input[15] + ((input[13] - input[14]) << 3);
        var ipp2 = input[14]; var ipp1 = input[15]; var p7 = 2 * input[15] - input[14]; var previous = output[15];
        for (var i = 1; i < first; i++) output[i] += output[i - 1];
        for (var i = first; i < count; i++)
        {
            var original = input[i] - 1; var dot = 0;
            for (var j = 0; j < first; j++)
            {
                var history = input[i - 1 - j]; dot = unchecked(dot + history * bm[j]);
                if (original > 0) bm[j] += (j & 1) == 0 ? -(history > 0 ? 1 : -1) : ((history >> 30) & 2) - 1;
                else if (original < 0) bm[j] += (j & 1) == 0 ? -(history <= 0 ? 1 : -1) : -(((history >> 30) & 2) - 1);
            }
            input[i] = original - (dot >> 8);
            var stage = input[i] + ((p2 * m2 + p3 * m3 + p4 * m4) >> 11);
            if (input[i] > 0) { m2 += p2 > 0 ? 1 : -1; m3 += p3 > 0 ? 4 : -4; m4 += p4 > 0 ? 4 : -4; }
            else if (input[i] < 0) { m2 += p2 > 0 ? -1 : 1; m3 += p3 > 0 ? -4 : 4; m4 += p4 > 0 ? -4 : 4; }
            p4 = stage; p2 = p4 + ((ipp2 - ipp1) << 3); p3 = (p4 - ipp1) << 1; ipp2 = ipp1; ipp1 = p4;
            stage += ((p7 * m5 - previous * m6) >> 10); m5 += (ipp1 ^ p7) >= 0 ? 2 : -2; m6 += (ipp1 ^ previous) >= 0 ? -1 : 1;
            p7 = 2 * stage - previous; previous = stage; output[i] = stage + ((output[i - 1] * 31) >> 5);
        }
    }

    private static void High3800To3929(int[] input, int[] output, int count, LegacyFrameWorkspace workspace)
        => HighAdaptive16(input, output, count, workspace);

    private static void HighAdaptive16(int[] input, int[] output, int count, LegacyFrameWorkspace workspace)
    {
        const int first = 16;
        if (count < 20) { Array.Copy(input, output, count); return; }
        Array.Copy(input, output, first); var bm = workspace.Bm; Array.Clear(bm, 0, first);
        var m2 = 64; var m3 = 115; var m4 = 64; var m5 = 740; var m6 = 0;
        var p4 = input[15]; var p3 = (input[15] - input[14]) << 1; var p2 = input[15] + ((input[13] - input[14]) << 3);
        var ipp2 = input[14]; var ipp1 = input[15]; var p7 = 2 * input[15] - input[14]; var previousOutput = output[15];
        for (var i = 1; i < first; i++) output[i] += output[i - 1];
        for (var i = first; i < count; i++)
        {
            var original = input[i]; var dot = 0;
            for (var j = 0; j < first; j++)
            {
                var history = input[i - first + j]; dot = unchecked(dot + history * bm[j]);
                if (original > 0) bm[j] += ((history >> 30) & 2) - 1;
                else if (original < 0) bm[j] -= ((history >> 30) & 2) - 1;
            }
            input[i] = original - (dot >> 9);
            var stage = input[i] + ((p2 * m2 + p3 * m3 + p4 * m4) >> 11);
            if (input[i] > 0) { m2 -= ((p2 >> 30) & 2) - 1; m3 -= ((p3 >> 28) & 8) - 4; m4 -= ((p4 >> 28) & 8) - 4; }
            else if (input[i] < 0) { m2 += ((p2 >> 30) & 2) - 1; m3 += ((p3 >> 28) & 8) - 4; m4 += ((p4 >> 28) & 8) - 4; }
            p2 = stage + ((ipp2 - p4) << 3); p3 = (stage - p4) << 1; ipp2 = p4; ipp1 = stage; p4 = stage;
            stage += ((p7 * m5 - previousOutput * m6) >> 10);
            if (p4 > 0) { m5 -= ((p7 >> 29) & 4) - 2; m6 += ((previousOutput >> 30) & 2) - 1; }
            else if (p4 < 0) { m5 += ((p7 >> 29) & 4) - 2; m6 -= ((previousOutput >> 30) & 2) - 1; }
            p7 = 2 * stage - previousOutput; previousOutput = stage; output[i] = stage + ((output[i - 1] * 31) >> 5);
        }
    }

    private static void ExtraHighLegacy(int[] input, int[] output, int count, int version, int iterations, int[]? a, int[]? b,
        LegacyFrameWorkspace workspace)
    {
        if (version >= 3800) { ExtraHigh3800To3929(input, output, count, version, workspace); return; }
        if (a is null || b is null) throw new InvalidDataException("APE EXTRA HIGH frame coefficients are missing.");
        for (var z = iterations; z >= 0;)
        {
            if (version < 3600)
            {
                ExtraOffsetSingle(input, output, count, b[z], -1, version < 3320 ? 64 : 32, version < 3320);
                ExtraOffsetSingle(output, input, count, a[z], 1, version < 3320 ? 64 : 32, version < 3320); z--;
            }
            else
            {
                ExtraOffsetPair(input, output, count, a[z], b[z]); z--;
                if (z >= 0) { ExtraOffsetPair(output, input, count, a[z], b[z]); z--; }
                else Array.Copy(output, input, count);
            }
        }
        // SDK 的 3.32-3.59 EXTRA HIGH 只更新了前置 offset 阶段，末级仍沿用最早的 HIGH 反预测器。
        // 3.60 起末级才随文件版本切换到对应的 HIGH 实现，不能直接对所有版本调用 HighLegacy。
        if (version < 3600) High0000To3320(input, output, count);
        else HighLegacy(input, output, count, version, workspace);
    }

    private static void ExtraOffsetSingle(int[] input, int[] output, int count, int offset, int direction, int maxOrder, bool fixedGain)
    {
        if (offset == 0 || count <= maxOrder) { Array.Copy(input, output, count); return; }
        Array.Copy(input, output, maxOrder); var m = 512;
        for (var i = maxOrder; i < count; i++)
        {
            output[i] = fixedGain ? input[i] + direction * (output[i - offset] >> 3) : input[i] + direction * ((output[i - offset] * m) >> 12);
            if (!fixedGain) m += ((input[i] ^ output[i - offset]) > 0 ? 8 : -8) * direction;
        }
    }

    private static void ExtraOffsetPair(int[] input, int[] output, int count, int offset1, int offset2)
    {
        const int maxOrder = 64;
        if (offset1 == 0 || offset2 == 0 || count <= maxOrder) { Array.Copy(input, output, count); return; }
        Array.Copy(input, output, maxOrder); var m1 = 64; var m2 = 64;
        for (var i = maxOrder; i < count; i++)
        {
            output[i] = input[i] + ((output[i - offset1] * m1) >> 9) - ((output[i - offset2] * m2) >> 9);
            m1 += (input[i] ^ output[i - offset1]) > 0 ? 1 : -1; m2 += (input[i] ^ output[i - offset2]) > 0 ? -1 : 1;
        }
    }

    private static void ExtraHigh3800To3929(int[] input, int[] output, int count, int version, LegacyFrameWorkspace workspace)
    {
        var order = version < 3830 ? 128 : 256; var shift = version < 3830 ? 11 : 12; var stageShift = version < 3830 ? 10 : 11;
        var minimum = version < 3830 ? 134 : 262;
        if (count < minimum) { Array.Copy(input, output, count); return; }
        Array.Copy(input, output, order);
        var coefficients = workspace.ShortCoefficients; var shortInput = workspace.ShortInput; var adapt = workspace.Adapt;
        Array.Clear(coefficients, 0, order); Array.Clear(shortInput, 0, count); Array.Clear(adapt, 0, count);
        for (var i = 0; i < order; i++) { shortInput[i] = (short)input[i]; adapt[i] = (short)(((input[i] >> 30) & 2) - 1); }
        var fm = workspace.Fm; var fp = workspace.Fp; Array.Clear(fm); Array.Clear(fp);
        var m2 = 64; var m3 = 115; var m4 = 64; var m5 = 740; var m6 = 0;
        var p4 = input[order - 1]; var p3 = (input[order - 1] - input[order - 2]) << 1; var p2 = input[order - 1] + ((input[order - 3] - input[order - 2]) << 3);
        var ipp2 = input[order - 2]; var p7 = 2 * input[order - 1] - input[order - 2]; var previous = output[order - 1];
        for (var i = 1; i < order; i++) output[i] += output[i - 1];
        for (var i = order; i < count; i++)
        {
            if (version >= 3830)
            {
                fp[0] = input[i]; var dot8 = 0;
                for (var j = 8; j > 0; j--) { dot8 += fp[j] * fm[j]; if (fp[0] > 0) fm[j] += ((fp[j] >> 30) & 2) - 1; else if (fp[0] < 0) fm[j] -= ((fp[j] >> 30) & 2) - 1; fp[j] = fp[j - 1]; }
                input[i] -= dot8 >> 9;
            }
            var original = input[i]; var dot = 0;
            for (var j = 0; j < order; j++) { dot += shortInput[i - order + j] * coefficients[j]; if (original > 0) coefficients[j] += adapt[i - order + j]; else if (original < 0) coefficients[j] -= adapt[i - order + j]; }
            shortInput[i] = (short)input[i]; adapt[i] = (short)(((input[i] >> 30) & 2) - 1); input[i] -= dot >> shift;
            shortInput[i] = (short)input[i]; adapt[i] = (short)(((input[i] >> 30) & 2) - 1);
            var stage = input[i] + ((p2 * m2 + p3 * m3 + p4 * m4) >> 11);
            if (input[i] > 0) { m2 -= ((p2 >> 30) & 2) - 1; m3 -= ((p3 >> 28) & 8) - 4; m4 -= ((p4 >> 28) & 8) - 4; } else if (input[i] < 0) { m2 += ((p2 >> 30) & 2) - 1; m3 += ((p3 >> 28) & 8) - 4; m4 += ((p4 >> 28) & 8) - 4; }
            p2 = stage + ((ipp2 - p4) << 3); p3 = (stage - p4) << 1; ipp2 = p4; p4 = stage;
            stage += ((p7 * m5 - previous * m6) >> stageShift);
            if (p4 > 0) { m5 -= ((p7 >> 29) & 4) - 2; m6 += ((previous >> 30) & 2) - 1; } else if (p4 < 0) { m5 += ((p7 >> 29) & 4) - 2; m6 -= ((previous >> 30) & 2) - 1; }
            p7 = 2 * stage - previous; previous = stage; output[i] = stage + ((output[i - 1] * 31) >> 5);
        }
    }
}
