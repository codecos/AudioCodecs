using System;
using Concentus.Celt.Structs;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Enums;

namespace Concentus.Celt;

internal class CeltCommon
{
	private static readonly byte[] inv_table = new byte[128]
	{
		255, 255, 156, 110, 86, 70, 59, 51, 45, 40,
		37, 33, 31, 28, 26, 25, 23, 22, 21, 20,
		19, 18, 17, 16, 16, 15, 15, 14, 13, 13,
		12, 12, 12, 12, 11, 11, 11, 10, 10, 10,
		9, 9, 9, 9, 9, 9, 8, 8, 8, 8,
		8, 7, 7, 7, 7, 7, 7, 6, 6, 6,
		6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
		6, 6, 6, 5, 5, 5, 5, 5, 5, 5,
		5, 5, 5, 5, 5, 4, 4, 4, 4, 4,
		4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
		4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
		3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
		3, 3, 3, 3, 3, 3, 3, 2
	};

	private static readonly short[][] gains = new short[3][]
	{
		new short[3] { 10048, 7112, 4248 },
		new short[3] { 15200, 8784, 0 },
		new short[3] { 26208, 3280, 0 }
	};

	private static readonly sbyte[][] tf_select_table = new sbyte[4][]
	{
		new sbyte[8] { 0, -1, 0, -1, 0, -1, 0, -1 },
		new sbyte[8] { 0, -1, 0, -2, 1, 0, 1, -1 },
		new sbyte[8] { 0, -2, 0, -3, 2, 0, 1, -1 },
		new sbyte[8] { 0, -2, 0, -3, 3, 0, 1, -1 }
	};

	internal static int compute_vbr(CeltMode mode, AnalysisInfo analysis, int base_target, int LM, int bitrate, int lastCodedBands, int C, int intensity, int constrained_vbr, int stereo_saving, int tot_boost, int tf_estimate, int pitch_change, int maxDepth, OpusFramesize variable_duration, int lfe, int has_surround_mask, int surround_masking, int temporal_vbr)
	{
		int nbEBands = mode.nbEBands;
		short[] eBands = mode.eBands;
		int num = ((lastCodedBands != 0) ? lastCodedBands : nbEBands);
		int num2 = eBands[num] << LM;
		if (C == 2)
		{
			num2 += eBands[Inlines.IMIN(intensity, num)] << LM;
		}
		int num3 = base_target;
		if (analysis.valid != 0 && (double)analysis.activity < 0.4)
		{
			num3 -= (int)((float)(num2 << 3) * (0.4f - analysis.activity));
		}
		if (C == 2)
		{
			int num4 = Inlines.IMIN(intensity, num);
			int num5 = (eBands[num4] << LM) - num4;
			int a = Inlines.DIV32_16(Inlines.MULT16_16(26214, num5), num2);
			stereo_saving = Inlines.MIN16(stereo_saving, 256);
			num3 -= Inlines.MIN32(Inlines.MULT16_32_Q15(a, num3), Inlines.SHR32(Inlines.MULT16_16(stereo_saving - 26, num5 << 3), 8));
		}
		num3 += tot_boost - (16 << LM);
		int num6 = ((variable_duration == OpusFramesize.OPUS_FRAMESIZE_VARIABLE) ? 328 : 655);
		num3 += Inlines.SHL32(Inlines.MULT16_32_Q15(tf_estimate - num6, num3), 1);
		if (analysis.valid != 0 && lfe == 0)
		{
			float num7 = Inlines.MAX16(0f, analysis.tonality - 0.15f) - 0.09f;
			int num8 = num3 + (int)((float)(num2 << 3) * 1.2f * num7);
			if (pitch_change != 0)
			{
				num8 += (int)((float)(num2 << 3) * 0.8f);
			}
			num3 = num8;
		}
		if (has_surround_mask != 0 && lfe == 0)
		{
			int b = num3 + Inlines.SHR32(Inlines.MULT16_16(surround_masking, num2 << 3), 10);
			num3 = Inlines.IMAX(num3 / 4, b);
		}
		int num9 = eBands[nbEBands - 2] << LM;
		int a2 = Inlines.SHR32(Inlines.MULT16_16(C * num9 << 3, maxDepth), 10);
		a2 = Inlines.IMAX(a2, num3 >> 2);
		num3 = Inlines.IMIN(num3, a2);
		if ((has_surround_mask == 0 || lfe != 0) && (constrained_vbr != 0 || bitrate < 64000))
		{
			int a3 = Inlines.MAX16(0, bitrate - 32000);
			if (constrained_vbr != 0)
			{
				a3 = Inlines.MIN16(a3, 21955);
			}
			num3 = base_target + Inlines.MULT16_32_Q15(a3, num3 - base_target);
		}
		if (has_surround_mask == 0 && tf_estimate < 3277)
		{
			int b2 = Inlines.MULT16_16_Q15(3329, Inlines.IMAX(0, Inlines.IMIN(32000, 96000 - bitrate)));
			int a4 = Inlines.SHR32(Inlines.MULT16_16(temporal_vbr, b2), 10);
			num3 += Inlines.MULT16_32_Q15(a4, num3);
		}
		return Inlines.IMIN(2 * base_target, num3);
	}

	internal static int transient_analysis(int[][] input, int len, int C, out int tf_estimate, out int tf_chan)
	{
		int num = 0;
		tf_chan = 0;
		int[] array = new int[len];
		int num2 = len / 2;
		for (int i = 0; i < C; i++)
		{
			int num3 = 0;
			int a = 0;
			int num4 = 0;
			for (int j = 0; j < len; j++)
			{
				int num5 = Inlines.SHR32(input[i][j], 12);
				int num6 = Inlines.ADD32(a, num5);
				a = num4 + num6 - Inlines.SHL32(num5, 1);
				num4 = num5 - Inlines.SHR32(num6, 1);
				array[j] = Inlines.EXTRACT16(Inlines.SHR32(num6, 2));
			}
			Arrays.MemSetInt(array, 0, 12);
			int num7 = 0;
			num7 = 14 - Inlines.celt_ilog2(1 + Inlines.celt_maxabs32(array, 0, len));
			if (num7 != 0)
			{
				for (int j = 0; j < len; j++)
				{
					array[j] = Inlines.SHL16(array[j], num7);
				}
			}
			int num8 = 0;
			a = 0;
			for (int j = 0; j < num2; j++)
			{
				int num9 = Inlines.PSHR32(Inlines.MULT16_16(array[2 * j], array[2 * j]) + Inlines.MULT16_16(array[2 * j + 1], array[2 * j + 1]), 16);
				num8 += num9;
				array[j] = a + Inlines.PSHR32(num9 - a, 4);
				a = array[j];
			}
			a = 0;
			int a2 = 0;
			for (int j = num2 - 1; j >= 0; j--)
			{
				array[j] = a + Inlines.PSHR32(array[j] - a, 3);
				a = array[j];
				a2 = Inlines.MAX16(a2, a);
			}
			num8 = Inlines.MULT16_16(Inlines.celt_sqrt(num8), Inlines.celt_sqrt(Inlines.MULT16_16(a2, num2 >> 1)));
			int b = Inlines.SHL32(num2, 20) / Inlines.ADD32(1, Inlines.SHR32(num8, 1));
			num3 = 0;
			for (int j = 12; j < num2 - 5; j += 4)
			{
				int num10 = Inlines.MAX32(0, Inlines.MIN32(127, Inlines.MULT16_32_Q15(array[j] + 1, b)));
				num3 += inv_table[num10];
			}
			num3 = 64 * num3 * 4 / (6 * (num2 - 17));
			if (num3 > num)
			{
				tf_chan = i;
				num = num3;
			}
		}
		bool result = num > 200;
		int b2 = Inlines.MAX16(0, Inlines.celt_sqrt(27 * num) - 42);
		tf_estimate = Inlines.celt_sqrt(Inlines.MAX32(0, Inlines.SHL32(Inlines.MULT16_16(113, Inlines.MIN16(163, b2)), 14) - 37312528));
		return result ? 1 : 0;
	}

	internal static int patch_transient_decision(int[][] newE, int[][] oldE, int nbEBands, int start, int end, int C)
	{
		int a = 0;
		int[] array = new int[26];
		if (C == 1)
		{
			array[start] = oldE[0][start];
			for (int i = start + 1; i < end; i++)
			{
				array[i] = Inlines.MAX16(array[i - 1] - 1024, oldE[0][i]);
			}
		}
		else
		{
			array[start] = Inlines.MAX16(oldE[0][start], oldE[1][start]);
			for (int i = start + 1; i < end; i++)
			{
				array[i] = Inlines.MAX16(array[i - 1] - 1024, Inlines.MAX16(oldE[0][i], oldE[1][i]));
			}
		}
		for (int i = end - 2; i >= start; i--)
		{
			array[i] = Inlines.MAX16(array[i], array[i + 1] - 1024);
		}
		int num = 0;
		do
		{
			for (int i = Inlines.IMAX(2, start); i < end - 1; i++)
			{
				int a2 = Inlines.MAX16(0, newE[num][i]);
				int b = Inlines.MAX16(0, array[i]);
				a = Inlines.ADD32(a, Inlines.MAX16(0, Inlines.SUB16(a2, b)));
			}
		}
		while (++num < C);
		a = Inlines.DIV32(a, C * (end - 1 - Inlines.IMAX(2, start)));
		return (a > 1024) ? 1 : 0;
	}

	/// Apply window and compute the MDCT for all sub-frames and
	///             all channels in a frame 
	internal static void compute_mdcts(CeltMode mode, int shortBlocks, int[][] input, int[][] output, int C, int CC, int LM, int upsample)
	{
		int overlap = mode.overlap;
		int num;
		int num2;
		int shift;
		if (shortBlocks != 0)
		{
			num = shortBlocks;
			num2 = mode.shortMdctSize;
			shift = mode.maxLM;
		}
		else
		{
			num = 1;
			num2 = mode.shortMdctSize << LM;
			shift = mode.maxLM - LM;
		}
		int num3 = 0;
		do
		{
			for (int i = 0; i < num; i++)
			{
				MDCT.clt_mdct_forward(mode.mdct, input[num3], i * num2, output[num3], i, mode.window, overlap, shift, num);
			}
		}
		while (++num3 < CC);
		if (CC == 2 && C == 1)
		{
			for (int j = 0; j < num * num2; j++)
			{
				output[0][j] = Inlines.ADD32(Inlines.HALF32(output[0][j]), Inlines.HALF32(output[1][j]));
			}
		}
		if (upsample == 1)
		{
			return;
		}
		num3 = 0;
		do
		{
			int num4 = num * num2 / upsample;
			for (int j = 0; j < num4; j++)
			{
				output[num3][j] *= upsample;
			}
			Arrays.MemSetWithOffset(output[num3], 0, num4, num * num2 - num4);
		}
		while (++num3 < C);
	}

	internal static void celt_preemphasis(Span<short> pcmp, int pcmp_ptr, Span<int> inp, int inp_ptr, int N, int CC, int upsample, int[] coef, ref int mem, int clip)
	{
		int a = coef[0];
		int num = mem;
		if (coef[1] == 0 && upsample == 1 && clip == 0)
		{
			for (int i = 0; i < N; i++)
			{
				int num2 = pcmp[pcmp_ptr + CC * i];
				inp[inp_ptr + i] = Inlines.SHL32(num2, 12) - num;
				num = Inlines.SHR32(Inlines.MULT16_16(a, num2), 3);
			}
			mem = num;
			return;
		}
		int num3 = N / upsample;
		if (upsample != 1)
		{
			Arrays.MemSetWithOffset(inp, 0, inp_ptr, N);
		}
		for (int i = 0; i < num3; i++)
		{
			inp[inp_ptr + i * upsample] = pcmp[pcmp_ptr + CC * i];
		}
		for (int i = 0; i < N; i++)
		{
			int num4 = inp[inp_ptr + i];
			inp[inp_ptr + i] = Inlines.SHL32(num4, 12) - num;
			num = Inlines.SHR32(Inlines.MULT16_16(a, num4), 3);
		}
		mem = num;
	}

	internal static void celt_preemphasis(short[] pcmp, Span<int> inp, int inp_ptr, int N, int CC, int upsample, int[] coef, BoxedValueInt mem, int clip)
	{
		int a = coef[0];
		int num = mem.Val;
		if (coef[1] == 0 && upsample == 1 && clip == 0)
		{
			for (int i = 0; i < N; i++)
			{
				int num2 = pcmp[CC * i];
				inp[inp_ptr + i] = Inlines.SHL32(num2, 12) - num;
				num = Inlines.SHR32(Inlines.MULT16_16(a, num2), 3);
			}
			mem.Val = num;
			return;
		}
		int num3 = N / upsample;
		if (upsample != 1)
		{
			Arrays.MemSetWithOffset(inp, 0, inp_ptr, N);
		}
		for (int i = 0; i < num3; i++)
		{
			inp[inp_ptr + i * upsample] = pcmp[CC * i];
		}
		for (int i = 0; i < N; i++)
		{
			int num4 = inp[inp_ptr + i];
			inp[inp_ptr + i] = Inlines.SHL32(num4, 12) - num;
			num = Inlines.SHR32(Inlines.MULT16_16(a, num4), 3);
		}
		mem.Val = num;
	}

	internal static int l1_metric(int[] tmp, int N, int LM, int bias)
	{
		int num = 0;
		for (int i = 0; i < N; i++)
		{
			num += Inlines.EXTEND32(Inlines.ABS32(tmp[i]));
		}
		return Inlines.MAC16_32_Q15(num, LM * bias, num);
	}

	internal static int tf_analysis(CeltMode m, int len, int isTransient, int[] tf_res, int lambda, int[][] X, int N0, int LM, out int tf_sum, int tf_estimate, int tf_chan)
	{
		int[] array = new int[2];
		int num = 0;
		int bias = Inlines.MULT16_16_Q14(1311, Inlines.MAX16(-4096, 8192 - tf_estimate));
		int[] array2 = new int[len];
		int[] array3 = new int[m.eBands[len] - m.eBands[len - 1] << LM];
		int[] array4 = new int[m.eBands[len] - m.eBands[len - 1] << LM];
		int[] array5 = new int[len];
		int[] array6 = new int[len];
		tf_sum = 0;
		for (int i = 0; i < len; i++)
		{
			int num2 = 0;
			int num3 = m.eBands[i + 1] - m.eBands[i] << LM;
			int num4 = ((m.eBands[i + 1] - m.eBands[i] == 1) ? 1 : 0);
			Arrays.MemCopy(X[tf_chan], m.eBands[i] << LM, array3, 0, num3);
			int num5 = l1_metric(array3, num3, (isTransient != 0) ? LM : 0, bias);
			int num6 = num5;
			if (isTransient != 0 && num4 == 0)
			{
				Arrays.MemCopy(array3, 0, array4, 0, num3);
				Bands.haar1ZeroOffset(array4, num3 >> LM, 1 << LM);
				num5 = l1_metric(array4, num3, LM + 1, bias);
				if (num5 < num6)
				{
					num6 = num5;
					num2 = -1;
				}
			}
			for (int j = 0; j < LM + ((isTransient == 0 && num4 == 0) ? 1 : 0); j++)
			{
				int lM = ((isTransient == 0) ? (j + 1) : (LM - j - 1));
				Bands.haar1ZeroOffset(array3, num3 >> j, 1 << j);
				num5 = l1_metric(array3, num3, lM, bias);
				if (num5 < num6)
				{
					num6 = num5;
					num2 = j + 1;
				}
			}
			if (isTransient != 0)
			{
				array2[i] = 2 * num2;
			}
			else
			{
				array2[i] = -2 * num2;
			}
			tf_sum += ((isTransient != 0) ? LM : 0) - array2[i] / 2;
			if (num4 != 0 && (array2[i] == 0 || array2[i] == -2 * LM))
			{
				array2[i]--;
			}
		}
		num = 0;
		int num7;
		int num8;
		for (int k = 0; k < 2; k++)
		{
			num7 = 0;
			num8 = ((isTransient == 0) ? lambda : 0);
			for (int i = 1; i < len; i++)
			{
				int num9 = Inlines.IMIN(num7, num8 + lambda);
				int num10 = Inlines.IMIN(num7 + lambda, num8);
				num7 = num9 + Inlines.abs(array2[i] - 2 * Tables.tf_select_table[LM][4 * isTransient + 2 * k]);
				num8 = num10 + Inlines.abs(array2[i] - 2 * Tables.tf_select_table[LM][4 * isTransient + 2 * k + 1]);
			}
			num7 = Inlines.IMIN(num7, num8);
			array[k] = num7;
		}
		if (array[1] < array[0] && isTransient != 0)
		{
			num = 1;
		}
		num7 = 0;
		num8 = ((isTransient == 0) ? lambda : 0);
		for (int i = 1; i < len; i++)
		{
			int num11 = num7;
			int num12 = num8 + lambda;
			int num13;
			if (num11 < num12)
			{
				num13 = num11;
				array5[i] = 0;
			}
			else
			{
				num13 = num12;
				array5[i] = 1;
			}
			num11 = num7 + lambda;
			num12 = num8;
			int num14;
			if (num11 < num12)
			{
				num14 = num11;
				array6[i] = 0;
			}
			else
			{
				num14 = num12;
				array6[i] = 1;
			}
			num7 = num13 + Inlines.abs(array2[i] - 2 * Tables.tf_select_table[LM][4 * isTransient + 2 * num]);
			num8 = num14 + Inlines.abs(array2[i] - 2 * Tables.tf_select_table[LM][4 * isTransient + 2 * num + 1]);
		}
		tf_res[len - 1] = ((num7 >= num8) ? 1 : 0);
		for (int i = len - 2; i >= 0; i--)
		{
			if (tf_res[i + 1] == 1)
			{
				tf_res[i] = array6[i + 1];
			}
			else
			{
				tf_res[i] = array5[i + 1];
			}
		}
		return num;
	}

	internal static void tf_encode(int start, int end, int isTransient, int[] tf_res, int LM, int tf_select, EntropyCoder enc, Span<byte> encodedData)
	{
		uint num = enc.storage * 8;
		uint num2 = (uint)enc.tell();
		int num3 = ((isTransient != 0) ? 2 : 4);
		int num4 = ((LM > 0 && num2 + num3 + 1 <= num) ? 1 : 0);
		num -= (uint)num4;
		int num6;
		int num5 = (num6 = 0);
		for (int i = start; i < end; i++)
		{
			if (num2 + num3 <= num)
			{
				enc.enc_bit_logp(encodedData, tf_res[i] ^ num5, (uint)num3);
				num2 = (uint)enc.tell();
				num5 = tf_res[i];
				num6 |= num5;
			}
			else
			{
				tf_res[i] = num5;
			}
			num3 = ((isTransient != 0) ? 4 : 5);
		}
		if (num4 != 0 && Tables.tf_select_table[LM][4 * isTransient + num6] != Tables.tf_select_table[LM][4 * isTransient + 2 + num6])
		{
			enc.enc_bit_logp(encodedData, tf_select, 1u);
		}
		else
		{
			tf_select = 0;
		}
		for (int i = start; i < end; i++)
		{
			tf_res[i] = Tables.tf_select_table[LM][4 * isTransient + 2 * tf_select + tf_res[i]];
		}
	}

	internal static int alloc_trim_analysis(CeltMode m, int[][] X, int[][] bandLogE, int end, int LM, int C, AnalysisInfo analysis, ref int stereo_saving, int tf_estimate, int intensity, int surround_trim)
	{
		int num = 0;
		int num2 = 1280;
		if (C == 2)
		{
			int num3 = 0;
			Span<int> span;
			for (int i = 0; i < 8; i++)
			{
				span = X[0].AsSpan();
				Span<int> x = span.Slice(m.eBands[i] << LM);
				span = X[1].AsSpan();
				int a = Kernels.celt_inner_prod(x, span.Slice(m.eBands[i] << LM), m.eBands[i + 1] - m.eBands[i] << LM);
				num3 = Inlines.ADD16(num3, Inlines.EXTRACT16(Inlines.SHR32(a, 18)));
			}
			num3 = Inlines.MULT16_16_Q15(4096, num3);
			num3 = Inlines.MIN16(1024, Inlines.ABS32(num3));
			int num4 = num3;
			for (int i = 8; i < intensity; i++)
			{
				span = X[0].AsSpan();
				Span<int> x2 = span.Slice(m.eBands[i] << LM);
				span = X[1].AsSpan();
				int a2 = Kernels.celt_inner_prod(x2, span.Slice(m.eBands[i] << LM), m.eBands[i + 1] - m.eBands[i] << LM);
				num4 = Inlines.MIN16(num4, Inlines.ABS16(Inlines.EXTRACT16(Inlines.SHR32(a2, 18))));
			}
			num4 = Inlines.MIN16(1024, Inlines.ABS32(num4));
			int num5 = Inlines.celt_log2(1049625 - Inlines.MULT16_16(num3, num3));
			int num6 = Inlines.MAX16(Inlines.HALF16(num5), Inlines.celt_log2(1049625 - Inlines.MULT16_16(num4, num4)));
			num5 = Inlines.PSHR32(num5 - 6144, 2);
			num6 = Inlines.PSHR32(num6 - 6144, 2);
			num2 += Inlines.MAX16(-1024, Inlines.MULT16_16_Q15(24576, num5));
			stereo_saving = Inlines.MIN16(stereo_saving + 64, -Inlines.HALF16(num6));
		}
		int num7 = 0;
		do
		{
			for (int i = 0; i < end - 1; i++)
			{
				num += bandLogE[num7][i] * (2 + 2 * i - end);
			}
		}
		while (++num7 < C);
		num /= C * (end - 1);
		num2 -= Inlines.MAX16(Inlines.NEG16((short)512), Inlines.MIN16(512, Inlines.SHR16(num + 1024, 2) / 6));
		num2 -= Inlines.SHR16(surround_trim, 2);
		num2 -= 2 * Inlines.SHR16(tf_estimate, 6);
		if (analysis.valid != 0)
		{
			num2 -= Inlines.MAX16(-512, Inlines.MIN16(512, (int)(512f * (analysis.tonality_slope + 0.05f))));
		}
		int b = Inlines.PSHR32(num2, 8);
		return Inlines.IMAX(0, Inlines.IMIN(10, b));
	}

	internal static int stereo_analysis(CeltMode m, int[][] X, int LM)
	{
		int num = 1;
		int num2 = 1;
		for (int i = 0; i < 13; i++)
		{
			for (int j = m.eBands[i] << LM; j < m.eBands[i + 1] << LM; j++)
			{
				int num3 = Inlines.EXTEND32(X[0][j]);
				int num4 = Inlines.EXTEND32(X[1][j]);
				int x = Inlines.ADD32(num3, num4);
				int x2 = Inlines.SUB32(num3, num4);
				num = Inlines.ADD32(num, Inlines.ADD32(Inlines.ABS32(num3), Inlines.ABS32(num4)));
				num2 = Inlines.ADD32(num2, Inlines.ADD32(Inlines.ABS32(x), Inlines.ABS32(x2)));
			}
		}
		num2 = Inlines.MULT16_32_Q15((short)23170, num2);
		int num5 = 13;
		if (LM <= 1)
		{
			num5 -= 8;
		}
		return (Inlines.MULT16_32_Q15((m.eBands[13] << LM + 1) + num5, num2) > Inlines.MULT16_32_Q15(m.eBands[13] << LM + 1, num)) ? 1 : 0;
	}

	internal static int median_of_5(Span<int> x, int x_ptr)
	{
		int num = x[x_ptr + 2];
		int num3;
		int num2;
		if (x[x_ptr] > x[x_ptr + 1])
		{
			num2 = x[x_ptr + 1];
			num3 = x[x_ptr];
		}
		else
		{
			num2 = x[x_ptr];
			num3 = x[x_ptr + 1];
		}
		int num4;
		int num5;
		if (x[x_ptr + 3] > x[x_ptr + 4])
		{
			num4 = x[x_ptr + 4];
			num5 = x[x_ptr + 3];
		}
		else
		{
			num4 = x[x_ptr + 3];
			num5 = x[x_ptr + 4];
		}
		if (num2 > num4)
		{
			int num6 = num4;
			num4 = num2;
			num2 = num6;
			int num7 = num5;
			num5 = num3;
			num3 = num7;
		}
		if (num > num3)
		{
			if (num3 < num4)
			{
				return Inlines.MIN16(num, num4);
			}
			return Inlines.MIN16(num5, num3);
		}
		if (num < num4)
		{
			return Inlines.MIN16(num3, num4);
		}
		return Inlines.MIN16(num, num5);
	}

	internal static int median_of_3(Span<int> x, int x_ptr)
	{
		int num;
		int num2;
		if (x[x_ptr] > x[x_ptr + 1])
		{
			num = x[x_ptr + 1];
			num2 = x[x_ptr];
		}
		else
		{
			num = x[x_ptr];
			num2 = x[x_ptr + 1];
		}
		int num3 = x[x_ptr + 2];
		if (num2 < num3)
		{
			return num2;
		}
		if (num < num3)
		{
			return num3;
		}
		return num;
	}

	internal static int dynalloc_analysis(int[][] bandLogE, int[][] bandLogE2, int nbEBands, int start, int end, int C, int[] offsets, int lsb_depth, short[] logN, int isTransient, int vbr, int constrained_vbr, short[] eBands, int LM, int effectiveBytes, out int tot_boost_, int lfe, int[] surround_dynalloc)
	{
		int num = 0;
		int[][] array = Arrays.InitTwoDimensionalArray<int>(2, nbEBands);
		int[] array2 = new int[C * nbEBands];
		Arrays.MemSetInt(offsets, 0, nbEBands);
		int num2 = -32666;
		for (int i = 0; i < end; i++)
		{
			array2[i] = Inlines.MULT16_16((short)64, logN[i]) + 512 + Inlines.SHL16(9 - lsb_depth, 10) - Inlines.SHL16(Tables.eMeans[i], 6) + Inlines.MULT16_16(6, (i + 5) * (i + 5));
		}
		int num3 = 0;
		do
		{
			for (int i = 0; i < end; i++)
			{
				num2 = Inlines.MAX16(num2, bandLogE[num3][i] - array2[i]);
			}
		}
		while (++num3 < C);
		if (effectiveBytes > 50 && LM >= 1 && lfe == 0)
		{
			int num4 = 0;
			num3 = 0;
			do
			{
				int[] array3 = array[num3];
				array3[0] = bandLogE2[num3][0];
				for (int i = 1; i < end; i++)
				{
					if (bandLogE2[num3][i] > bandLogE2[num3][i - 1] + 512)
					{
						num4 = i;
					}
					array3[i] = Inlines.MIN16(array3[i - 1] + 1536, bandLogE2[num3][i]);
				}
				for (int i = num4 - 1; i >= 0; i--)
				{
					array3[i] = Inlines.MIN16(array3[i], Inlines.MIN16(array3[i + 1] + 2048, bandLogE2[num3][i]));
				}
				int num5 = 1024;
				for (int i = 2; i < end - 2; i++)
				{
					array3[i] = Inlines.MAX16(array3[i], median_of_5(bandLogE2[num3], i - 2) - num5);
				}
				int b = median_of_3(bandLogE2[num3], 0) - num5;
				array3[0] = Inlines.MAX16(array3[0], b);
				array3[1] = Inlines.MAX16(array3[1], b);
				b = median_of_3(bandLogE2[num3], end - 3) - num5;
				array3[end - 2] = Inlines.MAX16(array3[end - 2], b);
				array3[end - 1] = Inlines.MAX16(array3[end - 1], b);
				for (int i = 0; i < end; i++)
				{
					array3[i] = Inlines.MAX16(array3[i], array2[i]);
				}
			}
			while (++num3 < C);
			if (C == 2)
			{
				for (int i = start; i < end; i++)
				{
					array[1][i] = Inlines.MAX16(array[1][i], array[0][i] - 4096);
					array[0][i] = Inlines.MAX16(array[0][i], array[1][i] - 4096);
					array[0][i] = Inlines.HALF16(Inlines.MAX16(0, bandLogE[0][i] - array[0][i]) + Inlines.MAX16(0, bandLogE[1][i] - array[1][i]));
				}
			}
			else
			{
				for (int i = start; i < end; i++)
				{
					array[0][i] = Inlines.MAX16(0, bandLogE[0][i] - array[0][i]);
				}
			}
			for (int i = start; i < end; i++)
			{
				array[0][i] = Inlines.MAX16(array[0][i], surround_dynalloc[i]);
			}
			if ((vbr == 0 || constrained_vbr != 0) && isTransient == 0)
			{
				for (int i = start; i < end; i++)
				{
					array[0][i] = Inlines.HALF16(array[0][i]);
				}
			}
			for (int i = start; i < end; i++)
			{
				if (i < 8)
				{
					array[0][i] *= 2;
				}
				if (i >= 12)
				{
					array[0][i] = Inlines.HALF16(array[0][i]);
				}
				array[0][i] = Inlines.MIN16(array[0][i], 4096);
				int num6 = C * (eBands[i + 1] - eBands[i]) << LM;
				int num7;
				int num8;
				if (num6 < 6)
				{
					num7 = Inlines.SHR32(array[0][i], 10);
					num8 = num7 * num6 << 3;
				}
				else if (num6 > 48)
				{
					num7 = Inlines.SHR32(array[0][i] * 8, 10);
					num8 = (num7 * num6 << 3) / 8;
				}
				else
				{
					num7 = Inlines.SHR32(array[0][i] * num6 / 6, 10);
					num8 = num7 * 6 << 3;
				}
				if ((vbr == 0 || (constrained_vbr != 0 && isTransient == 0)) && num + num8 >> 3 >> 3 > effectiveBytes / 4)
				{
					int num9 = effectiveBytes / 4 << 3 << 3;
					offsets[i] = num9 - num;
					num = num9;
					break;
				}
				offsets[i] = num7;
				num += num8;
			}
		}
		tot_boost_ = num;
		return num2;
	}

	internal static void deemphasis(int[][] input, int[] input_ptrs, Span<short> pcm, int pcm_ptr, int N, int C, int downsample, int[] coef, int[] mem, int accum)
	{
		if (downsample == 1 && C == 2 && accum == 0)
		{
			deemphasis_stereo_simple(input, input_ptrs, pcm, pcm_ptr, N, coef[0], mem);
			return;
		}
		int a = coef[0];
		int num3 = 0;
		do
		{
			int num4 = mem[num3];
			int[] array2 = input[num3];
			int num5 = input_ptrs[num3];
			int num6 = pcm_ptr + num3;
			if (downsample > 1)
			{
				var nextOutput = 0;
				for (int i = 0; i < N; i++)
				{
					int num7 = array2[num5 + i] + num4;
					num4 = Inlines.MULT16_32_Q15(a, num7);
					if (i == nextOutput)
					{
						pcm[num6 + (i / downsample) * C] = Inlines.SIG2WORD16(num7);
						nextOutput += downsample;
					}
				}
			}
			else if (accum != 0)
			{
				for (int i = 0; i < N; i++)
				{
					int num8 = array2[num5 + i] + num4;
					num4 = Inlines.MULT16_32_Q15(a, num8);
					pcm[num6 + i * C] = Inlines.SAT16(Inlines.ADD32(pcm[num6 + i * C], Inlines.SIG2WORD16(num8)));
				}
			}
			else
			{
				for (int i = 0; i < N; i++)
				{
					int num9 = array2[num5 + i] + num4;
					if (array2[num5 + i] > 0 && num4 > 0 && num9 < 0)
					{
						num9 = int.MaxValue;
						num4 = int.MaxValue;
					}
					else
					{
						num4 = Inlines.MULT16_32_Q15(a, num9);
					}
					pcm[num6 + i * C] = Inlines.SIG2WORD16(num9);
				}
			}
			mem[num3] = num4;
		}
		while (++num3 < C);
	}

	internal static void deemphasis_stereo_simple(int[][] input, int[] input_ptrs, Span<short> pcm, int pcm_ptr, int N, int coef0, int[] mem)
	{
		int[] array = input[0];
		int[] array2 = input[1];
		int num = input_ptrs[0];
		int num2 = input_ptrs[1];
		int num3 = mem[0];
		int num4 = mem[1];
		for (int i = 0; i < N; i++)
		{
			int num5 = array[num + i] + num3;
			int num6 = array2[num2 + i] + num4;
			num3 = Inlines.MULT16_32_Q15(coef0, num5);
			num4 = Inlines.MULT16_32_Q15(coef0, num6);
			pcm[pcm_ptr + 2 * i] = Inlines.SIG2WORD16(num5);
			pcm[pcm_ptr + 2 * i + 1] = Inlines.SIG2WORD16(num6);
		}
		mem[0] = num3;
		mem[1] = num4;
	}

	internal static void celt_synthesis(CeltMode mode, int[][] X, int[][] out_syn, int[] out_syn_ptrs, int[] oldBandE, int start, int effEnd, int C, int CC, int isTransient, int LM, int downsample, int silence, Span<int> synthesisWork)
	{
		int overlap = mode.overlap;
		int nbEBands = mode.nbEBands;
		int num = mode.shortMdctSize << LM;
		Span<int> array = synthesisWork.Slice(0, num);
		int num2 = 1 << LM;
		int num3;
		int num4;
		int shift;
		if (isTransient != 0)
		{
			num3 = num2;
			num4 = mode.shortMdctSize;
			shift = mode.maxLM;
		}
		else
		{
			num3 = 1;
			num4 = mode.shortMdctSize << LM;
			shift = mode.maxLM - LM;
		}
		if (CC == 2 && C == 1)
		{
			Bands.denormalise_bands(mode, X[0], array, 0, oldBandE, 0, start, effEnd, num2, downsample, silence);
			int num5 = out_syn_ptrs[1] + overlap / 2;
			array.CopyTo(out_syn[1].AsSpan(num5, num));
			for (int i = 0; i < num3; i++)
			{
				MDCT.clt_mdct_backward(mode.mdct, out_syn[1], num5 + i, out_syn[0], out_syn_ptrs[0] + num4 * i, mode.window, overlap, shift, num3);
			}
			for (int i = 0; i < num3; i++)
			{
				MDCT.clt_mdct_backward(mode.mdct, array, i, out_syn[1], out_syn_ptrs[1] + num4 * i, mode.window, overlap, shift, num3);
			}
			return;
		}
		if (CC == 1 && C == 2)
		{
			int num6 = out_syn_ptrs[0] + overlap / 2;
			Bands.denormalise_bands(mode, X[0], array, 0, oldBandE, 0, start, effEnd, num2, downsample, silence);
			Bands.denormalise_bands(mode, X[1], out_syn[0], num6, oldBandE, nbEBands, start, effEnd, num2, downsample, silence);
			for (int j = 0; j < num; j++)
			{
				array[j] = Inlines.HALF32(Inlines.ADD32(array[j], out_syn[0][num6 + j]));
			}
			for (int i = 0; i < num3; i++)
			{
				MDCT.clt_mdct_backward(mode.mdct, array, i, out_syn[0], out_syn_ptrs[0] + num4 * i, mode.window, overlap, shift, num3);
			}
			return;
		}
		int num7 = 0;
		do
		{
			Bands.denormalise_bands(mode, X[num7], array, 0, oldBandE, num7 * nbEBands, start, effEnd, num2, downsample, silence);
			for (int i = 0; i < num3; i++)
			{
				MDCT.clt_mdct_backward(mode.mdct, array, i, out_syn[num7], out_syn_ptrs[num7] + num4 * i, mode.window, overlap, shift, num3);
			}
		}
		while (++num7 < CC);
	}

	internal static void tf_decode(int start, int end, int isTransient, int[] tf_res, int LM, EntropyCoder dec, ReadOnlySpan<byte> encodedData)
	{
		uint num = dec.storage * 8;
		uint num2 = (uint)dec.tell();
		int num3 = ((isTransient != 0) ? 2 : 4);
		int num4 = ((LM > 0 && num2 + num3 + 1 <= num) ? 1 : 0);
		num -= (uint)num4;
		int num6;
		int num5 = (num6 = 0);
		for (int i = start; i < end; i++)
		{
			if (num2 + num3 <= num)
			{
				num6 ^= dec.dec_bit_logp(encodedData, (uint)num3);
				num2 = (uint)dec.tell();
				num5 |= num6;
			}
			tf_res[i] = num6;
			num3 = ((isTransient != 0) ? 4 : 5);
		}
		int num7 = 0;
		if (num4 != 0 && Tables.tf_select_table[LM][4 * isTransient + num5] != Tables.tf_select_table[LM][4 * isTransient + 2 + num5])
		{
			num7 = dec.dec_bit_logp(encodedData, 1u);
		}
		for (int i = start; i < end; i++)
		{
			tf_res[i] = Tables.tf_select_table[LM][4 * isTransient + 2 * num7 + tf_res[i]];
		}
	}

	internal static int celt_plc_pitch_search(int[][] decode_mem, int C)
	{
		int[] array = new int[1024];
		Pitch.pitch_downsample(decode_mem, array, 2048, C);
		Pitch.pitch_search(array, 360, array, 1328, 620, out var pitch);
		return 720 - pitch;
	}

	internal static int resampling_factor(int rate)
	{
		return rate switch
		{
			48000 => 1, 
			24000 => 2, 
			16000 => 3, 
			12000 => 4, 
			8000 => 6, 
			_ => 0, 
		};
	}

	internal static void comb_filter_const(Span<int> y, int y_ptr, Span<int> x, int x_ptr, int T, int N, int g10, int g11, int g12)
	{
		int num = x_ptr - T;
		int b = x[num - 2];
		int num2 = x[num - 1];
		int num3 = x[num];
		int num4 = x[num + 1];
		for (int i = 0; i < N; i++)
		{
			int num5 = x[num + i + 2];
			y[y_ptr + i] = x[x_ptr + i] + Inlines.MULT16_32_Q15(g10, num3) + Inlines.MULT16_32_Q15(g11, Inlines.ADD32(num4, num2)) + Inlines.MULT16_32_Q15(g12, Inlines.ADD32(num5, b));
			b = num2;
			num2 = num3;
			num3 = num4;
			num4 = num5;
		}
	}

	internal static void comb_filter(Span<int> y, int y_ptr, Span<int> x, int x_ptr, int T0, int T1, int N, int g0, int g1, int tapset0, int tapset1, int[] window, int overlap)
	{
		if (g0 != 0 || g1 != 0)
		{
			int b = Inlines.MULT16_16_P15(g0, gains[tapset0][0]);
			int b2 = Inlines.MULT16_16_P15(g0, gains[tapset0][1]);
			int b3 = Inlines.MULT16_16_P15(g0, gains[tapset0][2]);
			int num = Inlines.MULT16_16_P15(g1, gains[tapset1][0]);
			int num2 = Inlines.MULT16_16_P15(g1, gains[tapset1][1]);
			int num3 = Inlines.MULT16_16_P15(g1, gains[tapset1][2]);
			int num4 = x[x_ptr - T1 + 1];
			int num5 = x[x_ptr - T1];
			int num6 = x[x_ptr - T1 - 1];
			int b4 = x[x_ptr - T1 - 2];
			if (g0 == g1 && T0 == T1 && tapset0 == tapset1)
			{
				overlap = 0;
			}
			int i;
			for (i = 0; i < overlap; i++)
			{
				int num7 = x[x_ptr + i - T1 + 2];
				int num8 = Inlines.MULT16_16_Q15(window[i], window[i]);
				y[y_ptr + i] = x[x_ptr + i] + Inlines.MULT16_32_Q15(Inlines.MULT16_16_Q15((short)(32767 - num8), b), x[x_ptr + i - T0]) + Inlines.MULT16_32_Q15(Inlines.MULT16_16_Q15((short)(32767 - num8), b2), Inlines.ADD32(x[x_ptr + i - T0 + 1], x[x_ptr + i - T0 - 1])) + Inlines.MULT16_32_Q15(Inlines.MULT16_16_Q15((short)(32767 - num8), b3), Inlines.ADD32(x[x_ptr + i - T0 + 2], x[x_ptr + i - T0 - 2])) + Inlines.MULT16_32_Q15(Inlines.MULT16_16_Q15(num8, num), num5) + Inlines.MULT16_32_Q15(Inlines.MULT16_16_Q15(num8, num2), Inlines.ADD32(num4, num6)) + Inlines.MULT16_32_Q15(Inlines.MULT16_16_Q15(num8, num3), Inlines.ADD32(num7, b4));
				b4 = num6;
				num6 = num5;
				num5 = num4;
				num4 = num7;
			}
			if (g1 != 0)
			{
				comb_filter_const(y, y_ptr + i, x, x_ptr + i, T1, N - i, num, num2, num3);
			}
		}
	}

	internal static void init_caps(CeltMode m, int[] cap, int LM, int C)
	{
		for (int i = 0; i < m.nbEBands; i++)
		{
			int num = m.eBands[i + 1] - m.eBands[i] << LM;
			cap[i] = (m.cache.caps[m.nbEBands * (2 * LM + C - 1) + i] + 64) * C * num >> 2;
		}
	}
}
