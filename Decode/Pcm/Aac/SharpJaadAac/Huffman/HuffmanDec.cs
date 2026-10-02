using SharpJaad.AAC;
using SharpJaad.AAC.Syntax;
using SharpJaad.AAC.Tools;
using System;
using System.Runtime.CompilerServices;

namespace SharpJaad.AAC.Huffman
{
    public static class HuffmanDec
    {
        private const int FastBits = 12;
        private const int FastLookupSize = 1 << FastBits;
        private const int FastIndexMask = 0xFFFF;
        private const int FastLengthShift = 16;
        private static readonly bool[] _UNSIGNED = { false, false, true, true, false, false, true, true, true, true, true };
        private static readonly CodebookIndex _scaleFactorTable = BuildCodebookIndex(Codebooks.HCB_SF);
        private static readonly CodebookIndex[] _spectralCodebooks = BuildCodebookIndexes(Codebooks.CODEBOOKS);

        internal static void PrepareStaticTables()
        {
            _ = _scaleFactorTable.Entries.Length;
            _ = _spectralCodebooks.Length;
            _ = IQTable.IQ_TABLE.Length;
            _ = ScaleFactorTable.SCALEFACTOR_TABLE.Length;
            _ = TNSTables.TNS_TABLES.Length;
            _ = ISScaleTable.SCALE_TABLE.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindOffset(BitStream input, CodebookIndex codebook)
        {
            int[] fastLookup = codebook.FastLookup;
            if (fastLookup != null && input.GetBitsLeft() >= FastBits)
            {
                int packed = fastLookup[input.PeekBits(FastBits)];
                if (packed != 0)
                {
                    input.SkipBits(packed >> FastLengthShift);
                    return (packed & FastIndexMask) - 1;
                }
            }

            int[] lengths = codebook.Lengths;
            int[] starts = codebook.Starts;
            HuffmanEntry[] entries = codebook.Entries;

            int lengthGroupCount = lengths.Length;
            int len = lengths[0];
            int cw = input.ReadBits(len);

            for (int group = 0; group < lengthGroupCount; group++)
            {
                int start = starts[group];
                int end = group + 1 < lengthGroupCount ? starts[group + 1] - 1 : entries.Length - 1;
                int found = FindCodeword(entries, start, end, cw);
                if (found >= 0)
                {
                    return found;
                }

                if (group + 1 >= lengthGroupCount)
                {
                    break;
                }

                int nextLen = lengths[group + 1];
                int delta = nextLen - len;
                len = nextLen;
                cw <<= delta;
                cw |= input.ReadBits(delta);
            }

            throw new AACException("Huffman codeword not found");
        }

        private static int FindCodeword(HuffmanEntry[] entries, int start, int end, int codeword)
        {
            while (start <= end)
            {
                int mid = start + ((end - start) >> 1);
                int current = entries[mid].Codeword;
                if (current == codeword)
                {
                    return mid;
                }

                if (current < codeword)
                {
                    start = mid + 1;
                }
                else
                {
                    end = mid - 1;
                }
            }

            return -1;
        }

        private static int GetEscape(BitStream input, int s)
        {
            bool neg = s < 0;

            int i = 4;
            while (input.ReadBool())
            {
                i++;
            }
            int j = input.ReadBits(i) | 1 << i;

            return neg ? -j : j;
        }

        public static int DecodeScaleFactor(BitStream input)
        {
            return _scaleFactorTable.Entries[FindOffset(input, _scaleFactorTable)].Value0;
        }

        public static void DecodeSpectralData(BitStream input, int cb, int[] data, int off)
        {
            CodebookIndex codebook = _spectralCodebooks[cb - 1];
            ref readonly HuffmanEntry row = ref codebook.Entries[FindOffset(input, codebook)];

            int value0 = row.Value0;
            int value1 = row.Value1;
            data[off] = value0;
            data[off + 1] = value1;
            if (cb < 5)
            {
                int value2 = row.Value2;
                int value3 = row.Value3;
                data[off + 2] = value2;
                data[off + 3] = value3;

                if (cb < 11)
                {
                    if (_UNSIGNED[cb - 1])
                    {
                        if (value0 != 0 && input.ReadBool()) data[off] = -value0;
                        if (value1 != 0 && input.ReadBool()) data[off + 1] = -value1;
                        if (value2 != 0 && input.ReadBool()) data[off + 2] = -value2;
                        if (value3 != 0 && input.ReadBool()) data[off + 3] = -value3;
                    }
                    return;
                }
            }
            else if (cb < 11)
            {
                if (_UNSIGNED[cb - 1])
                {
                    if (value0 != 0 && input.ReadBool()) data[off] = -value0;
                    if (value1 != 0 && input.ReadBool()) data[off + 1] = -value1;
                }
                return;
            }

            if (cb == 11 || cb > 15)
            {
                if (value0 != 0 && input.ReadBool()) value0 = -value0;
                if (value1 != 0 && input.ReadBool()) value1 = -value1;
                if (value0 == 16 || value0 == -16) value0 = GetEscape(input, value0);
                if (value1 == 16 || value1 == -16) value1 = GetEscape(input, value1);
                data[off] = value0;
                data[off + 1] = value1;
                return;
            }

            throw new AACException("Huffman: unknown spectral codebook: " + cb);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DecodeSpectralDataToFloat(BitStream input, int cb, float[] data, int off, float scale)
        {
            CodebookIndex codebook = _spectralCodebooks[cb - 1];
            ref readonly HuffmanEntry row = ref codebook.Entries[FindOffset(input, codebook)];

            int value0 = row.Value0;
            int value1 = row.Value1;

            if (cb < 5)
            {
                int value2 = row.Value2;
                int value3 = row.Value3;
                if (_UNSIGNED[cb - 1])
                {
                    if (value0 != 0 && input.ReadBool()) value0 = -value0;
                    if (value1 != 0 && input.ReadBool()) value1 = -value1;
                    if (value2 != 0 && input.ReadBool()) value2 = -value2;
                    if (value3 != 0 && input.ReadBool()) value3 = -value3;
                }

                data[off] = ScaleSpectralValue(value0, scale);
                data[off + 1] = ScaleSpectralValue(value1, scale);
                data[off + 2] = ScaleSpectralValue(value2, scale);
                data[off + 3] = ScaleSpectralValue(value3, scale);
                return;
            }

            if (cb < 11)
            {
                if (_UNSIGNED[cb - 1])
                {
                    if (value0 != 0 && input.ReadBool()) value0 = -value0;
                    if (value1 != 0 && input.ReadBool()) value1 = -value1;
                }

                data[off] = ScaleSpectralValue(value0, scale);
                data[off + 1] = ScaleSpectralValue(value1, scale);
                return;
            }

            if (cb == 11 || cb > 15)
            {
                if (value0 != 0 && input.ReadBool()) value0 = -value0;
                if (value1 != 0 && input.ReadBool()) value1 = -value1;
                if (value0 == 16 || value0 == -16) value0 = GetEscape(input, value0);
                if (value1 == 16 || value1 == -16) value1 = GetEscape(input, value1);
                data[off] = ScaleSpectralValue(value0, scale);
                data[off + 1] = ScaleSpectralValue(value1, scale);
                return;
            }

            throw new AACException("Huffman: unknown spectral codebook: " + cb);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ScaleSpectralValue(int value, float scale)
        {
            if (value > 0) return IQTable.IQ_TABLE[value] * scale;
            if (value < 0) return -IQTable.IQ_TABLE[-value] * scale;
            return 0f;
        }

        private static CodebookIndex BuildCodebookIndex(int[][] table)
        {
            var entries = new HuffmanEntry[table.Length];
            int distinctLengthCount = 0;
            int lastLength = -1;
            for (int i = 0; i < table.Length; i++)
            {
                int[] row = table[i];
                int length = row[0];
                entries[i] = new HuffmanEntry(
                    length,
                    row[1],
                    row.Length > 2 ? row[2] : 0,
                    row.Length > 3 ? row[3] : 0,
                    row.Length > 4 ? row[4] : 0,
                    row.Length > 5 ? row[5] : 0);

                if (length != lastLength)
                {
                    distinctLengthCount++;
                    lastLength = length;
                }
            }

            var lengths = new int[distinctLengthCount];
            var starts = new int[distinctLengthCount];
            int groupIndex = -1;
            lastLength = -1;
            for (int i = 0; i < entries.Length; i++)
            {
                int length = entries[i].Length;
                if (length != lastLength)
                {
                    groupIndex++;
                    lengths[groupIndex] = length;
                    starts[groupIndex] = i;
                    lastLength = length;
                }
            }

            return new CodebookIndex(entries, lengths, starts, BuildFastLookup(entries));
        }

        private static CodebookIndex[] BuildCodebookIndexes(int[][][] tables)
        {
            var flattened = new CodebookIndex[tables.Length];
            for (int i = 0; i < tables.Length; i++)
            {
                flattened[i] = BuildCodebookIndex(tables[i]);
            }

            return flattened;
        }

        private static int[] BuildFastLookup(HuffmanEntry[] entries)
        {
            var fastLookup = new int[FastLookupSize];
            for (int i = 0; i < entries.Length; i++)
            {
                int length = entries[i].Length;
                if (length > FastBits)
                {
                    continue;
                }

                int fillCount = 1 << (FastBits - length);
                int prefix = entries[i].Codeword << (FastBits - length);
                int packed = (i + 1) | (length << FastLengthShift);
                for (int j = 0; j < fillCount; j++)
                {
                    fastLookup[prefix | j] = packed;
                }
            }

            return fastLookup;
        }

        private readonly record struct CodebookIndex(
            HuffmanEntry[] Entries,
            int[] Lengths,
            int[] Starts,
            int[] FastLookup);

        private readonly record struct HuffmanEntry(
            int Length,
            int Codeword,
            int Value0,
            int Value1,
            int Value2,
            int Value3);
    }
}
