using System;
using System.Buffers;
using Concentus.Celt.Structs;
using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Celt;

internal static class Bands
{
	private static readonly short[] ComputeQnTable = { 16384, 17866, 19483, 21247, 23170, 25267, 27554, 30048 };

	internal class band_ctx
	{
		internal int encode;

		internal CeltMode m;

		internal int i;

		internal int intensity;

		internal int spread;

		internal int tf_change;

		internal EntropyCoder ec;

		internal int remaining_bits;

		internal int[][] bandE;

		internal uint seed;

		internal int[] vqScratch;
	}

	internal struct split_ctx
	{
		internal int inv;

		internal int imid;

		internal int iside;

		internal int delta;

		internal int itheta;

		internal int qalloc;
	}

	private static readonly byte[] bit_interleave_table = new byte[16]
	{
		0, 1, 1, 1, 2, 3, 3, 3, 2, 3,
		3, 3, 2, 3, 3, 3
	};

	private static readonly byte[] bit_deinterleave_table = new byte[16]
	{
		0, 3, 12, 15, 48, 51, 60, 63, 192, 195,
		204, 207, 240, 243, 252, 255
	};

	internal static int hysteresis_decision(int val, int[] thresholds, int[] hysteresis, int N, int prev)
	{
		int i;
		for (i = 0; i < N && val >= thresholds[i]; i++)
		{
		}
		if (i > prev && val < thresholds[prev] + hysteresis[prev])
		{
			i = prev;
		}
		if (i < prev && val > thresholds[prev - 1] - hysteresis[prev - 1])
		{
			i = prev;
		}
		return i;
	}

	internal static uint celt_lcg_rand(uint seed)
	{
		return 1664525 * seed + 1013904223;
	}

	internal static int bitexact_cos(int x)
	{
		int num = 4096 + x * x >> 13;
		num = 32767 - num + Inlines.FRAC_MUL16(num, -7651 + Inlines.FRAC_MUL16(num, 8277 + Inlines.FRAC_MUL16(-626, num)));
		return 1 + num;
	}

	internal static int bitexact_log2tan(int isin, int icos)
	{
		int num = Inlines.EC_ILOG((uint)icos);
		int num2 = Inlines.EC_ILOG((uint)isin);
		icos <<= 15 - num;
		isin <<= 15 - num2;
		return (num2 - num) * 2048 + Inlines.FRAC_MUL16(isin, Inlines.FRAC_MUL16(isin, -2597) + 7932) - Inlines.FRAC_MUL16(icos, Inlines.FRAC_MUL16(icos, -2597) + 7932);
	}

	internal static void compute_band_energies(CeltMode m, int[][] X, int[][] bandE, int end, int C, int LM)
	{
		short[] eBands = m.eBands;
		_ = m.shortMdctSize;
		int num = 0;
		do
		{
			for (int i = 0; i < end; i++)
			{
				int num2 = 0;
				int num3 = 0;
				num2 = Inlines.celt_maxabs32(X[num], eBands[i] << LM, eBands[i + 1] - eBands[i] << LM);
				if (num2 > 0)
				{
					int num4 = Inlines.celt_ilog2(num2) - 14 + ((m.logN[i] >> 3) + LM + 1 >> 1);
					int num5 = eBands[i] << LM;
					if (num4 > 0)
					{
						do
						{
							num3 = Inlines.MAC16_16(num3, Inlines.EXTRACT16(Inlines.SHR32(X[num][num5], num4)), Inlines.EXTRACT16(Inlines.SHR32(X[num][num5], num4)));
						}
						while (++num5 < eBands[i + 1] << LM);
					}
					else
					{
						do
						{
							num3 = Inlines.MAC16_16(num3, Inlines.EXTRACT16(Inlines.SHL32(X[num][num5], -num4)), Inlines.EXTRACT16(Inlines.SHL32(X[num][num5], -num4)));
						}
						while (++num5 < eBands[i + 1] << LM);
					}
					bandE[num][i] = 1 + Inlines.VSHR32(Inlines.celt_sqrt(num3), -num4);
				}
				else
				{
					bandE[num][i] = 1;
				}
			}
		}
		while (++num < C);
	}

	internal static void normalise_bands(CeltMode m, int[][] freq, int[][] X, int[][] bandE, int end, int C, int M)
	{
		short[] eBands = m.eBands;
		int num = 0;
		do
		{
			int num2 = 0;
			do
			{
				int num3 = Inlines.celt_zlog2(bandE[num][num2]) - 13;
				int b = Inlines.EXTRACT16(Inlines.celt_rcp(Inlines.SHL32(Inlines.VSHR32(bandE[num][num2], num3), 3)));
				int num4 = M * eBands[num2];
				do
				{
					X[num][num4] = Inlines.MULT16_16_Q15(Inlines.VSHR32(freq[num][num4], num3 - 1), b);
				}
				while (++num4 < M * eBands[num2 + 1]);
			}
			while (++num2 < end);
		}
		while (++num < C);
	}

	internal static void denormalise_bands(CeltMode m, int[] X, Span<int> freq, int freq_ptr, Span<int> bandLogE, int bandLogE_ptr, int start, int end, int M, int downsample, int silence)
	{
		short[] eBands = m.eBands;
		int num = M * m.shortMdctSize;
		int num2 = M * eBands[end];
		if (downsample != 1)
		{
			num2 = Inlines.IMIN(num2, num / downsample);
		}
		if (silence != 0)
		{
			num2 = 0;
			start = (end = 0);
		}
		int index = freq_ptr;
		int num3 = M * eBands[start];
		for (int i = 0; i < M * eBands[start]; i++)
		{
			freq[index++] = 0;
		}
		for (int i = start; i < end; i++)
		{
			int num4 = M * eBands[i];
			int num5 = M * eBands[i + 1];
			int num6 = Inlines.ADD16(bandLogE[bandLogE_ptr + i], Inlines.SHL16(Tables.eMeans[i], 6));
			int num7 = 16 - (num6 >> 10);
			int b;
			if (num7 > 31)
			{
				num7 = 0;
				b = 0;
			}
			else
			{
				b = Inlines.celt_exp2_frac(num6 & 0x3FF);
			}
			if (num7 < 0)
			{
				if (num7 < -2)
				{
					b = 32767;
					num7 = -2;
				}
				do
				{
					freq[index] = Inlines.SHR32(Inlines.MULT16_16(X[num3], b), -num7);
				}
				while (++num4 < num5);
			}
			else
			{
				do
				{
					freq[index++] = Inlines.SHR32(Inlines.MULT16_16(X[num3++], b), num7);
				}
				while (++num4 < num5);
			}
		}
		Arrays.MemSetWithOffset(freq, 0, freq_ptr + num2, num - num2);
	}

	internal static void anti_collapse(CeltMode m, int[][] X_, byte[] collapse_masks, int LM, int C, int size, int start, int end, int[] logE, int[] prev1logE, int[] prev2logE, int[] pulses, uint seed)
	{
		for (int i = start; i < end; i++)
		{
			int num = m.eBands[i + 1] - m.eBands[i];
			int a = Inlines.celt_udiv(1 + pulses[i], m.eBands[i + 1] - m.eBands[i]) >> LM;
			int b = Inlines.SHR32(Inlines.celt_exp2(-Inlines.SHL16(a, 7)), 1);
			int a2 = Inlines.MULT16_32_Q15((short)16384, Inlines.MIN32(32767, b));
			int num2 = num << LM;
			int num3 = Inlines.celt_ilog2(num2) >> 1;
			int a3 = Inlines.celt_rsqrt_norm(Inlines.SHL32(num2, 7 - num3 << 1));
			int num4 = 0;
			do
			{
				int num5 = 0;
				int a4 = prev1logE[num4 * m.nbEBands + i];
				int num6 = prev2logE[num4 * m.nbEBands + i];
				if (C == 1)
				{
					a4 = Inlines.MAX16(a4, prev1logE[m.nbEBands + i]);
					num6 = Inlines.MAX16(num6, prev2logE[m.nbEBands + i]);
				}
				int b2 = Inlines.EXTEND32(logE[num4 * m.nbEBands + i]) - Inlines.EXTEND32(Inlines.MIN16(a4, num6));
				b2 = Inlines.MAX32(0, b2);
				int b4;
				if (b2 < 16384)
				{
					int b3 = Inlines.SHR32(Inlines.celt_exp2((short)(-Inlines.EXTRACT16(b2))), 1);
					b4 = 2 * Inlines.MIN16(16383, b3);
				}
				else
				{
					b4 = 0;
				}
				if (LM == 3)
				{
					b4 = Inlines.MULT16_16_Q14(23170, Inlines.MIN32(23169, b4));
				}
				b4 = Inlines.SHR16(Inlines.MIN16(a2, b4), 1);
				b4 = Inlines.SHR32(Inlines.MULT16_16_Q15(a3, b4), num3);
				int num7 = m.eBands[i] << LM;
				for (int j = 0; j < 1 << LM; j++)
				{
					if ((collapse_masks[i * C + num4] & (1 << j)) == 0)
					{
						int num8 = num7 + j;
						for (int k = 0; k < num; k++)
						{
							seed = celt_lcg_rand(seed);
							X_[num4][num8 + (k << LM)] = (((seed & 0x8000) != 0) ? b4 : (-b4));
						}
						num5 = 1;
					}
				}
				if (num5 != 0)
				{
					VQ.renormalise_vector(X_[num4].AsSpan().Slice(num7), num << LM, 32767);
				}
			}
			while (++num4 < C);
		}
	}

	internal static void intensity_stereo(CeltMode m, Span<int> X, int X_ptr, Span<int> Y, int Y_ptr, int[][] bandE, int bandID, int N)
	{
		int shift = Inlines.celt_zlog2(Inlines.MAX32(bandE[0][bandID], bandE[1][bandID])) - 13;
		int num = Inlines.VSHR32(bandE[0][bandID], shift);
		int num2 = Inlines.VSHR32(bandE[1][bandID], shift);
		int b = 1 + Inlines.celt_sqrt(1 + Inlines.MULT16_16(num, num) + Inlines.MULT16_16(num2, num2));
		int a = Inlines.DIV32_16(Inlines.SHL32(num, 14), b);
		int a2 = Inlines.DIV32_16(Inlines.SHL32(num2, 14), b);
		for (int i = 0; i < N; i++)
		{
			int b2 = X[X_ptr + i];
			int b3 = Y[Y_ptr + i];
			X[X_ptr + i] = Inlines.EXTRACT16(Inlines.SHR32(Inlines.MAC16_16(Inlines.MULT16_16(a, b2), a2, b3), 14));
		}
	}

	private static void stereo_split(Span<int> X, int X_ptr, Span<int> Y, int Y_ptr, int N)
	{
		for (int i = 0; i < N; i++)
		{
			int num = Inlines.MULT16_16(23170, X[X_ptr + i]);
			int num2 = Inlines.MULT16_16(23170, Y[Y_ptr + i]);
			X[X_ptr + i] = Inlines.EXTRACT16(Inlines.SHR32(Inlines.ADD32(num, num2), 15));
			Y[Y_ptr + i] = Inlines.EXTRACT16(Inlines.SHR32(Inlines.SUB32(num2, num), 15));
		}
	}

	private static void stereo_merge(Span<int> X, Span<int> Y, int mid, int N)
	{
		Kernels.dual_inner_prod(Y, X, Y, N, out var xy, out var xy2);
		xy = Inlines.MULT16_32_Q15(mid, xy);
		int num = Inlines.SHR16(mid, 1);
		int num2 = Inlines.MULT16_16(num, num) + xy2 - 2 * xy;
		int num3 = Inlines.MULT16_16(num, num) + xy2 + 2 * xy;
		if (num3 < 161061 || num2 < 161061)
		{
			X.Slice(0, N).CopyTo(Y);
			return;
		}
		int num4 = Inlines.celt_ilog2(num2) >> 1;
		int num5 = Inlines.celt_ilog2(num3) >> 1;
		int a = Inlines.celt_rsqrt_norm(Inlines.VSHR32(num2, num4 - 7 << 1));
		int a2 = Inlines.celt_rsqrt_norm(Inlines.VSHR32(num3, num5 - 7 << 1));
		if (num4 < 7)
		{
			num4 = 7;
		}
		if (num5 < 7)
		{
			num5 = 7;
		}
		for (int i = 0; i < N; i++)
		{
			int a3 = Inlines.MULT16_16_P15(mid, X[i]);
			int b = Y[i];
			X[i] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MULT16_16(a, Inlines.SUB16(a3, b)), num4 + 1));
			Y[i] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.MULT16_16(a2, Inlines.ADD16(a3, b)), num5 + 1));
		}
	}

	internal static int spreading_decision(CeltMode m, int[][] X, ref int average, int last_decision, ref int hf_average, ref int tapset_decision, int update_hf, int end, int C, int M)
	{
		int num = 0;
		int num2 = 0;
		short[] eBands = m.eBands;
		int num3 = 0;
		if (M * (eBands[end] - eBands[end - 1]) <= 8)
		{
			return 0;
		}
		int num4 = 0;
		do
		{
			for (int i = 0; i < end; i++)
			{
				int num5 = 0;
				int[] array = new int[3];
				int[] array2 = X[num4];
				int num6 = M * eBands[i];
				int num7 = M * (eBands[i + 1] - eBands[i]);
				if (num7 <= 8)
				{
					continue;
				}
				for (int j = num6; j < num7 + num6; j++)
				{
					int num8 = Inlines.MULT16_16(Inlines.MULT16_16_Q15(array2[j], array2[j]), num7);
					if (num8 < 2048)
					{
						array[0]++;
					}
					if (num8 < 512)
					{
						array[1]++;
					}
					if (num8 < 128)
					{
						array[2]++;
					}
				}
				if (i > m.nbEBands - 4)
				{
					num3 += Inlines.celt_udiv(32 * (array[1] + array[0]), num7);
				}
				num5 = ((2 * array[2] >= num7) ? 1 : 0) + ((2 * array[1] >= num7) ? 1 : 0) + ((2 * array[0] >= num7) ? 1 : 0);
				num += num5 * 256;
				num2++;
			}
		}
		while (++num4 < C);
		if (update_hf != 0)
		{
			if (num3 != 0)
			{
				num3 = Inlines.celt_udiv(num3, C * (4 - m.nbEBands + end));
			}
			hf_average = hf_average + num3 >> 1;
			num3 = hf_average;
			if (tapset_decision == 2)
			{
				num3 += 4;
			}
			else if (tapset_decision == 0)
			{
				num3 -= 4;
			}
			if (num3 > 22)
			{
				tapset_decision = 2;
			}
			else if (num3 > 18)
			{
				tapset_decision = 1;
			}
			else
			{
				tapset_decision = 0;
			}
		}
		num = Inlines.celt_udiv(num, num2);
		num = 3 * (average = num + average >> 1) + ((3 - last_decision << 7) + 64) + 2 >> 2;
		if (num < 80)
		{
			return 3;
		}
		if (num < 256)
		{
			return 2;
		}
		if (num < 384)
		{
			return 1;
		}
		return 0;
	}

	internal static void deinterleave_hadamard(Span<int> X, int X_ptr, int N0, int stride, int hadamard)
	{
		int num = N0 * stride;
		int[] array = ArrayPool<int>.Shared.Rent(num);
		try
		{
			if (hadamard != 0)
			{
				int num2 = stride - 2;
				for (int i = 0; i < stride; i++)
				{
					for (int j = 0; j < N0; j++)
					{
						array[Tables.ordery_table[num2 + i] * N0 + j] = X[j * stride + i + X_ptr];
					}
				}
			}
			else
			{
				for (int i = 0; i < stride; i++)
				{
					for (int j = 0; j < N0; j++)
					{
						array[i * N0 + j] = X[j * stride + i + X_ptr];
					}
				}
			}
			array.AsSpan(0, num).CopyTo(X.Slice(X_ptr));
		}
		finally
		{
			ArrayPool<int>.Shared.Return(array);
		}
	}

	internal static void interleave_hadamard(Span<int> X, int X_ptr, int N0, int stride, int hadamard)
	{
		int num = N0 * stride;
		int[] array = ArrayPool<int>.Shared.Rent(num);
		try
		{
			if (hadamard != 0)
			{
				int num2 = stride - 2;
				for (int i = 0; i < stride; i++)
				{
					for (int j = 0; j < N0; j++)
					{
						array[j * stride + i] = X[Tables.ordery_table[num2 + i] * N0 + j + X_ptr];
					}
				}
			}
			else
			{
				for (int j = 0; j < N0; j++)
				{
					for (int i = 0; i < stride; i++)
					{
						array[j * stride + i] = X[i * N0 + j + X_ptr];
					}
				}
			}
			array.AsSpan(0, num).CopyTo(X.Slice(X_ptr));
		}
		finally
		{
			ArrayPool<int>.Shared.Return(array);
		}
	}

	internal static void haar1(Span<int> X, int X_ptr, int N0, int stride)
	{
		N0 >>= 1;
		for (int i = 0; i < stride; i++)
		{
			for (int j = 0; j < N0; j++)
			{
				int num = X_ptr + i + stride * 2 * j;
				int a = Inlines.MULT16_16(23170, X[num]);
				int b = Inlines.MULT16_16(23170, X[num + stride]);
				X[num] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.ADD32(a, b), 15));
				X[num + stride] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.SUB32(a, b), 15));
			}
		}
	}

	internal static void haar1ZeroOffset(int[] X, int N0, int stride)
	{
		N0 >>= 1;
		for (int i = 0; i < stride; i++)
		{
			for (int j = 0; j < N0; j++)
			{
				int num = i + stride * 2 * j;
				int a = Inlines.MULT16_16(23170, X[num]);
				int b = Inlines.MULT16_16(23170, X[num + stride]);
				X[num] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.ADD32(a, b), 15));
				X[num + stride] = Inlines.EXTRACT16(Inlines.PSHR32(Inlines.SUB32(a, b), 15));
			}
		}
	}

	internal static int compute_qn(int N, int b, int offset, int pulse_cap, int stereo)
	{
		int num = 2 * N - 1;
		if (stereo != 0 && N == 2)
		{
			num--;
		}
		int b2 = Inlines.celt_sudiv(b + num * offset, num);
		b2 = Inlines.IMIN(b - pulse_cap - 32, b2);
		b2 = Inlines.IMIN(64, b2);
		if (b2 < 4)
		{
			return 1;
		}
		int num2 = ComputeQnTable[b2 & 7] >> 14 - (b2 >> 3);
		return num2 + 1 >> 1 << 1;
	}

	internal static void compute_theta_decode(band_ctx ctx, ref split_ctx sctx, ReadOnlySpan<byte> encodedData, Span<int> X, int X_ptr, Span<int> Y, int Y_ptr, int N, ref int b, int B, int B0, int LM, int stereo, ref int fill)
	{
		int num = 0;
		int inv = 0;
		CeltMode m = ctx.m;
		int i = ctx.i;
		int intensity = ctx.intensity;
		EntropyCoder ec = ctx.ec;
		_ = ctx.bandE;
		int num2 = m.logN[i] + LM * 8;
		int offset = (num2 >> 1) - ((stereo != 0 && N == 2) ? 16 : 4);
		int num3 = compute_qn(N, b, offset, num2, stereo);
		if (stereo != 0 && i >= intensity)
		{
			num3 = 1;
		}
		int num4 = (int)ec.tell_frac();
		if (num3 != 1)
		{
			if (stereo != 0 && N > 2)
			{
				int num5 = 3;
				int num6 = num;
				int num7 = num3 / 2;
				uint ft = (uint)(num5 * (num7 + 1) + num7);
				int num8 = (int)ec.decode(ft);
				num6 = ((num8 >= (num7 + 1) * num5) ? (num7 + 1 + (num8 - (num7 + 1) * num5)) : (num8 / num5));
				ec.dec_update(encodedData, (uint)((num6 <= num7) ? (num5 * num6) : (num6 - 1 - num7 + (num7 + 1) * num5)), (uint)((num6 <= num7) ? (num5 * (num6 + 1)) : (num6 - num7 + (num7 + 1) * num5)), ft);
				num = num6;
			}
			else if (B0 > 1 || stereo != 0)
			{
				num = (int)ec.dec_uint(encodedData, (uint)(num3 + 1));
			}
			else
			{
				int num9 = 1;
				int num10 = ((num3 >> 1) + 1) * ((num3 >> 1) + 1);
				int num11 = 0;
				int num12 = (int)ec.decode((uint)num10);
				if (num12 < (num3 >> 1) * ((num3 >> 1) + 1) >> 1)
				{
					num = (int)(Inlines.isqrt32((uint)(8 * num12 + 1)) - 1) >> 1;
					num9 = num + 1;
					num11 = num * (num + 1) >> 1;
				}
				else
				{
					num = (int)(2 * (num3 + 1) - Inlines.isqrt32((uint)(8 * (num10 - num12 - 1) + 1))) >> 1;
					num9 = num3 + 1 - num;
					num11 = num10 - ((num3 + 1 - num) * (num3 + 2 - num) >> 1);
				}
				ec.dec_update(encodedData, (uint)num11, (uint)(num11 + num9), (uint)num10);
			}
			num = Inlines.celt_udiv(num * 16384, num3);
		}
		else if (stereo != 0)
		{
			inv = ((b > 16 && ctx.remaining_bits > 16) ? ec.dec_bit_logp(encodedData, 2u) : 0);
			num = 0;
		}
		int num13 = (int)ec.tell_frac() - num4;
		b -= num13;
		int num14;
		int num15;
		int delta;
		switch (num)
		{
		case 0:
			num14 = 32767;
			num15 = 0;
			fill &= (1 << B) - 1;
			delta = -16384;
			break;
		case 16384:
			num14 = 0;
			num15 = 32767;
			fill &= (1 << B) - 1 << B;
			delta = 16384;
			break;
		default:
			num14 = bitexact_cos((short)num);
			num15 = bitexact_cos((short)(16384 - num));
			delta = Inlines.FRAC_MUL16(N - 1 << 7, bitexact_log2tan(num15, num14));
			break;
		}
		sctx.inv = inv;
		sctx.imid = num14;
		sctx.iside = num15;
		sctx.delta = delta;
		sctx.itheta = num;
		sctx.qalloc = num13;
	}

	internal static void compute_theta_encode(band_ctx ctx, ref split_ctx sctx, Span<byte> encodedData, Span<int> X, int X_ptr, Span<int> Y, int Y_ptr, int N, ref int b, int B, int B0, int LM, int stereo, ref int fill)
	{
		int num = 0;
		int num2 = 0;
		CeltMode m = ctx.m;
		int i = ctx.i;
		int intensity = ctx.intensity;
		EntropyCoder ec = ctx.ec;
		int[][] bandE = ctx.bandE;
		int num3 = m.logN[i] + LM * 8;
		int offset = (num3 >> 1) - ((stereo != 0 && N == 2) ? 16 : 4);
		int num4 = compute_qn(N, b, offset, num3, stereo);
		if (stereo != 0 && i >= intensity)
		{
			num4 = 1;
		}
		num = VQ.stereo_itheta(X.Slice(X_ptr), Y.Slice(Y_ptr), stereo, N);
		int num5 = (int)ec.tell_frac();
		if (num4 != 1)
		{
			num = num * num4 + 8192 >> 14;
			if (stereo != 0 && N > 2)
			{
				int num6 = 3;
				int num7 = num;
				int num8 = num4 / 2;
				uint ft = (uint)(num6 * (num8 + 1) + num8);
				ec.encode(encodedData, (uint)((num7 <= num8) ? (num6 * num7) : (num7 - 1 - num8 + (num8 + 1) * num6)), (uint)((num7 <= num8) ? (num6 * (num7 + 1)) : (num7 - num8 + (num8 + 1) * num6)), ft);
			}
			else if (B0 > 1 || stereo != 0)
			{
				ec.enc_uint(encodedData, (uint)num, (uint)(num4 + 1));
			}
			else
			{
				int num9 = 1;
				int num10 = ((num4 >> 1) + 1) * ((num4 >> 1) + 1);
				num9 = ((num <= num4 >> 1) ? (num + 1) : (num4 + 1 - num));
				int num11 = ((num <= num4 >> 1) ? (num * (num + 1) >> 1) : (num10 - ((num4 + 1 - num) * (num4 + 2 - num) >> 1)));
				ec.encode(encodedData, (uint)num11, (uint)(num11 + num9), (uint)num10);
			}
			num = Inlines.celt_udiv(num * 16384, num4);
			if (stereo != 0)
			{
				if (num == 0)
				{
					intensity_stereo(m, X, X_ptr, Y, Y_ptr, bandE, i, N);
				}
				else
				{
					stereo_split(X, X_ptr, Y, Y_ptr, N);
				}
			}
		}
		else if (stereo != 0)
		{
			num2 = ((num > 8192) ? 1 : 0);
			if (num2 != 0)
			{
				for (int j = 0; j < N; j++)
				{
					Y[Y_ptr + j] = -Y[Y_ptr + j];
				}
			}
			intensity_stereo(m, X, X_ptr, Y, Y_ptr, bandE, i, N);
			if (b > 16 && ctx.remaining_bits > 16)
			{
				ec.enc_bit_logp(encodedData, num2, 2u);
			}
			else
			{
				num2 = 0;
			}
			num = 0;
		}
		int num12 = (int)ec.tell_frac() - num5;
		b -= num12;
		int num13;
		int num14;
		int delta;
		switch (num)
		{
		case 0:
			num13 = 32767;
			num14 = 0;
			fill &= (1 << B) - 1;
			delta = -16384;
			break;
		case 16384:
			num13 = 0;
			num14 = 32767;
			fill &= (1 << B) - 1 << B;
			delta = 16384;
			break;
		default:
			num13 = bitexact_cos((short)num);
			num14 = bitexact_cos((short)(16384 - num));
			delta = Inlines.FRAC_MUL16(N - 1 << 7, bitexact_log2tan(num14, num13));
			break;
		}
		sctx.inv = num2;
		sctx.imid = num13;
		sctx.iside = num14;
		sctx.delta = delta;
		sctx.itheta = num;
		sctx.qalloc = num12;
	}

	internal static uint quant_band_n1_encode(band_ctx ctx, Span<byte> encodedData, Span<int> X, int X_ptr, Span<int> Y, int Y_ptr, int b, Span<int> lowband_out, int lowband_out_ptr)
	{
		int num = 0;
		Span<int> span = X;
		int index = X_ptr;
		EntropyCoder ec = ctx.ec;
		int num2 = ((!Y.IsEmpty) ? 1 : 0);
		int num3 = 0;
		do
		{
			int num4 = 0;
			if (ctx.remaining_bits >= 8)
			{
				num4 = ((span[index] < 0) ? 1 : 0);
				ec.enc_bits(encodedData, (uint)num4, 1u);
				ctx.remaining_bits -= 8;
				b -= 8;
			}
			if (num != 0)
			{
				span[index] = ((num4 != 0) ? (-16384) : 16384);
			}
			span = Y;
			index = Y_ptr;
		}
		while (++num3 < 1 + num2);
		if (!lowband_out.IsEmpty)
		{
			lowband_out[lowband_out_ptr] = Inlines.SHR16(X[X_ptr], 4);
		}
		return 1u;
	}

	internal static uint quant_band_n1_decode(band_ctx ctx, ReadOnlySpan<byte> encodedData, Span<int> X, int X_ptr, Span<int> Y, int Y_ptr, int b, Span<int> lowband_out, int lowband_out_ptr)
	{
		int num = 1;
		Span<int> span = X;
		int index = X_ptr;
		EntropyCoder ec = ctx.ec;
		int num2 = ((!Y.IsEmpty) ? 1 : 0);
		int num3 = 0;
		do
		{
			int num4 = 0;
			if (ctx.remaining_bits >= 8)
			{
				num4 = (int)ec.dec_bits(encodedData, 1u);
				ctx.remaining_bits -= 8;
				b -= 8;
			}
			if (num != 0)
			{
				span[index] = ((num4 != 0) ? (-16384) : 16384);
			}
			span = Y;
			index = Y_ptr;
		}
		while (++num3 < 1 + num2);
		if (!lowband_out.IsEmpty)
		{
			lowband_out[lowband_out_ptr] = Inlines.SHR16(X[X_ptr], 4);
		}
		return 1u;
	}

	internal static uint quant_partition_encode(band_ctx ctx, Span<byte> encodedData, Span<int> X, int X_ptr, int N, int b, int B, Span<int> lowband, int lowband_ptr, int LM, int gain, int fill)
	{
		int num = 0;
		int num2 = B;
		int num3 = 0;
		int num4 = 0;
		uint result = 0u;
		int num5 = 0;
		int num6 = 0;
		CeltMode m = ctx.m;
		int i = ctx.i;
		int spread = ctx.spread;
		EntropyCoder ec = ctx.ec;
		byte[] bits = m.cache.bits;
		int num7 = m.cache.index[(LM + 1) * m.nbEBands + i];
		if (LM != -1 && b > bits[num7 + bits[num7]] + 12 && N > 2)
		{
			split_ctx split_ctx = default;
			int lowband_ptr2 = 0;
			N >>= 1;
			num6 = X_ptr + N;
			LM--;
			if (B == 1)
			{
				fill = (fill & 1) | (fill << 1);
			}
			B = B + 1 >> 1;
			compute_theta_encode(ctx, ref split_ctx, encodedData, X, X_ptr, X, num6, N, ref b, B, num2, LM, 0, ref fill);
			num = split_ctx.imid;
			int iside = split_ctx.iside;
			int num8 = split_ctx.delta;
			int itheta = split_ctx.itheta;
			int qalloc = split_ctx.qalloc;
			num3 = num;
			num4 = iside;
			if (num2 > 1 && (itheta & 0x3FFF) != 0)
			{
				num8 = ((itheta <= 8192) ? Inlines.IMIN(0, num8 + (N << 3 >> 5 - LM)) : (num8 - (num8 >> 4 - LM)));
			}
			int num9 = Inlines.IMAX(0, Inlines.IMIN(b, (b - num8) / 2));
			int num10 = b - num9;
			ctx.remaining_bits -= qalloc;
			if (!lowband.IsEmpty)
			{
				lowband_ptr2 = lowband_ptr + N;
			}
			int remaining_bits = ctx.remaining_bits;
			if (num9 >= num10)
			{
				result = quant_partition_encode(ctx, encodedData, X, X_ptr, N, num9, B, lowband, lowband_ptr, LM, Inlines.MULT16_16_P15(gain, num3), fill);
				remaining_bits = num9 - (remaining_bits - ctx.remaining_bits);
				if (remaining_bits > 24 && itheta != 0)
				{
					num10 += remaining_bits - 24;
				}
				result |= quant_partition_encode(ctx, encodedData, X, num6, N, num10, B, lowband, lowband_ptr2, LM, Inlines.MULT16_16_P15(gain, num4), fill >> B) << (num2 >> 1);
			}
			else
			{
				result = quant_partition_encode(ctx, encodedData, X, num6, N, num10, B, lowband, lowband_ptr2, LM, Inlines.MULT16_16_P15(gain, num4), fill >> B) << (num2 >> 1);
				remaining_bits = num10 - (remaining_bits - ctx.remaining_bits);
				if (remaining_bits > 24 && itheta != 16384)
				{
					num9 += remaining_bits - 24;
				}
				result |= quant_partition_encode(ctx, encodedData, X, X_ptr, N, num9, B, lowband, lowband_ptr, LM, Inlines.MULT16_16_P15(gain, num3), fill);
			}
		}
		else
		{
			int num11 = Rate.bits2pulses(m, i, LM, b);
			int num12 = Rate.pulses2bits(m, i, LM, num11);
			ctx.remaining_bits -= num12;
			while (ctx.remaining_bits < 0 && num11 > 0)
			{
				ctx.remaining_bits += num12;
				num11--;
				num12 = Rate.pulses2bits(m, i, LM, num11);
				ctx.remaining_bits -= num12;
			}
			if (num11 != 0)
			{
				int k = Rate.get_pulses(num11);
				result = VQ.alg_quant(X, X_ptr, N, k, spread, B, ec, encodedData);
			}
			else if (num5 != 0)
			{
				uint num13 = (uint)((int)(1L << B) - 1);
				fill &= (int)num13;
				if (fill == 0)
				{
					Arrays.MemSetWithOffset(X, 0, X_ptr, N);
				}
				else
				{
					if (lowband.IsEmpty)
					{
						for (int j = 0; j < N; j++)
						{
							ctx.seed = celt_lcg_rand(ctx.seed);
							X[X_ptr + j] = (int)ctx.seed >> 20;
						}
						result = num13;
					}
					else
					{
						for (int j = 0; j < N; j++)
						{
							ctx.seed = celt_lcg_rand(ctx.seed);
							int num14 = 4;
							num14 = (((ctx.seed & 0x8000) != 0) ? num14 : (-num14));
							X[X_ptr + j] = lowband[lowband_ptr + j] + num14;
						}
						result = (uint)fill;
					}
					VQ.renormalise_vector(X.Slice(X_ptr), N, gain);
				}
			}
		}
		return result;
	}

	internal static uint quant_partition_decode(band_ctx ctx, ReadOnlySpan<byte> encodedData, Span<int> X, int X_ptr, int N, int b, int B, Span<int> lowband, int lowband_ptr, int LM, int gain, int fill)
	{
		int num = 0;
		int num2 = B;
		int num3 = 0;
		int num4 = 0;
		uint result = 0u;
		int num5 = 1;
		int num6 = 0;
		CeltMode m = ctx.m;
		int i = ctx.i;
		int spread = ctx.spread;
		EntropyCoder ec = ctx.ec;
		byte[] bits = m.cache.bits;
		int num7 = m.cache.index[(LM + 1) * m.nbEBands + i];
		if (LM != -1 && b > bits[num7 + bits[num7]] + 12 && N > 2)
		{
			split_ctx split_ctx = default;
			int lowband_ptr2 = 0;
			N >>= 1;
			num6 = X_ptr + N;
			LM--;
			if (B == 1)
			{
				fill = (fill & 1) | (fill << 1);
			}
			B = B + 1 >> 1;
			compute_theta_decode(ctx, ref split_ctx, encodedData, X, X_ptr, X, num6, N, ref b, B, num2, LM, 0, ref fill);
			num = split_ctx.imid;
			int iside = split_ctx.iside;
			int num8 = split_ctx.delta;
			int itheta = split_ctx.itheta;
			int qalloc = split_ctx.qalloc;
			num3 = num;
			num4 = iside;
			if (num2 > 1 && (itheta & 0x3FFF) != 0)
			{
				num8 = ((itheta <= 8192) ? Inlines.IMIN(0, num8 + (N << 3 >> 5 - LM)) : (num8 - (num8 >> 4 - LM)));
			}
			int num9 = Inlines.IMAX(0, Inlines.IMIN(b, (b - num8) / 2));
			int num10 = b - num9;
			ctx.remaining_bits -= qalloc;
			if (!lowband.IsEmpty)
			{
				lowband_ptr2 = lowband_ptr + N;
			}
			int remaining_bits = ctx.remaining_bits;
			if (num9 >= num10)
			{
				result = quant_partition_decode(ctx, encodedData, X, X_ptr, N, num9, B, lowband, lowband_ptr, LM, Inlines.MULT16_16_P15(gain, num3), fill);
				remaining_bits = num9 - (remaining_bits - ctx.remaining_bits);
				if (remaining_bits > 24 && itheta != 0)
				{
					num10 += remaining_bits - 24;
				}
				result |= quant_partition_decode(ctx, encodedData, X, num6, N, num10, B, lowband, lowband_ptr2, LM, Inlines.MULT16_16_P15(gain, num4), fill >> B) << (num2 >> 1);
			}
			else
			{
				result = quant_partition_decode(ctx, encodedData, X, num6, N, num10, B, lowband, lowband_ptr2, LM, Inlines.MULT16_16_P15(gain, num4), fill >> B) << (num2 >> 1);
				remaining_bits = num10 - (remaining_bits - ctx.remaining_bits);
				if (remaining_bits > 24 && itheta != 16384)
				{
					num9 += remaining_bits - 24;
				}
				result |= quant_partition_decode(ctx, encodedData, X, X_ptr, N, num9, B, lowband, lowband_ptr, LM, Inlines.MULT16_16_P15(gain, num3), fill);
			}
		}
		else
		{
			int num11 = Rate.bits2pulses(m, i, LM, b);
			int num12 = Rate.pulses2bits(m, i, LM, num11);
			ctx.remaining_bits -= num12;
			while (ctx.remaining_bits < 0 && num11 > 0)
			{
				ctx.remaining_bits += num12;
				num11--;
				num12 = Rate.pulses2bits(m, i, LM, num11);
				ctx.remaining_bits -= num12;
			}
			if (num11 != 0)
			{
				int k = Rate.get_pulses(num11);
				result = VQ.alg_unquant(X, X_ptr, N, k, spread, B, ec, encodedData, gain, ctx.vqScratch);
			}
			else if (num5 != 0)
			{
				uint num13 = (uint)((int)(1L << B) - 1);
				fill &= (int)num13;
				if (fill == 0)
				{
					Arrays.MemSetWithOffset(X, 0, X_ptr, N);
				}
				else
				{
					if (lowband.IsEmpty)
					{
						for (int j = 0; j < N; j++)
						{
							ctx.seed = celt_lcg_rand(ctx.seed);
							X[X_ptr + j] = (int)ctx.seed >> 20;
						}
						result = num13;
					}
					else
					{
						for (int j = 0; j < N; j++)
						{
							ctx.seed = celt_lcg_rand(ctx.seed);
							int num14 = 4;
							num14 = (((ctx.seed & 0x8000) != 0) ? num14 : (-num14));
							X[X_ptr + j] = lowband[lowband_ptr + j] + num14;
						}
						result = (uint)fill;
					}
					VQ.renormalise_vector(X.Slice(X_ptr), N, gain);
				}
			}
		}
		return result;
	}

	internal static uint quant_band_encode(band_ctx ctx, Span<byte> encodedData, Span<int> X, int X_ptr, int N, int b, int B, Span<int> lowband, int lowband_ptr, int LM, Span<int> lowband_out, int lowband_out_ptr, int gain, Span<int> lowband_scratch, int lowband_scratch_ptr, int fill)
	{
		int n = N;
		int num = B;
		int num2 = 0;
		int num3 = 0;
		uint num4 = 0u;
		int num5 = 1;
		int num6 = ctx.tf_change;
		int hadamard = ((num == 1) ? 1 : 0);
		n = Inlines.celt_udiv(n, B);
		if (N == 1)
		{
			return quant_band_n1_encode(ctx, encodedData, X, X_ptr, null, 0, b, lowband_out, lowband_out_ptr);
		}
		if (num6 > 0)
		{
			num3 = num6;
		}
		if (!lowband_scratch.IsEmpty && !lowband.IsEmpty && (num3 != 0 || ((n & 1) == 0 && num6 < 0) || num > 1))
		{
			lowband.Slice(lowband_ptr, N).CopyTo(lowband_scratch.Slice(lowband_scratch_ptr, N));
			lowband = lowband_scratch;
			lowband_ptr = lowband_scratch_ptr;
		}
		for (int i = 0; i < num3; i++)
		{
			haar1(X, X_ptr, N >> i, 1 << i);
			if (!lowband.IsEmpty)
			{
				haar1(lowband, lowband_ptr, N >> i, 1 << i);
			}
			fill = bit_interleave_table[fill & 0xF] | (bit_interleave_table[fill >> 4] << 2);
		}
		B >>= num3;
		n <<= num3;
		while ((n & 1) == 0 && num6 < 0)
		{
			haar1(X, X_ptr, n, B);
			if (!lowband.IsEmpty)
			{
				haar1(lowband, lowband_ptr, n, B);
			}
			fill |= fill << B;
			B <<= 1;
			n >>= 1;
			num2++;
			num6++;
		}
		num = B;
		int num7 = n;
		if (num > 1)
		{
			deinterleave_hadamard(X, X_ptr, n >> num3, num << num3, hadamard);
			if (!lowband.IsEmpty)
			{
				deinterleave_hadamard(lowband, lowband_ptr, n >> num3, num << num3, hadamard);
			}
		}
		num4 = quant_partition_encode(ctx, encodedData, X, X_ptr, N, b, B, lowband, lowband_ptr, LM, gain, fill);
		if (num5 != 0)
		{
			if (num > 1)
			{
				interleave_hadamard(X, X_ptr, n >> num3, num << num3, hadamard);
			}
			n = num7;
			B = num;
			for (int i = 0; i < num2; i++)
			{
				B >>= 1;
				n <<= 1;
				num4 |= num4 >> B;
				haar1(X, X_ptr, n, B);
			}
			for (int i = 0; i < num3; i++)
			{
				num4 = bit_deinterleave_table[num4];
				haar1(X, X_ptr, N >> i, 1 << i);
			}
			B <<= num3;
			if (!lowband_out.IsEmpty)
			{
				int a = Inlines.celt_sqrt(Inlines.SHL32(N, 22));
				for (int j = 0; j < N; j++)
				{
					lowband_out[lowband_out_ptr + j] = Inlines.MULT16_16_Q15(a, X[X_ptr + j]);
				}
			}
			num4 &= (uint)((1 << B) - 1);
		}
		return num4;
	}

	internal static uint quant_band_decode(band_ctx ctx, ReadOnlySpan<byte> encodedData, Span<int> X, int X_ptr, int N, int b, int B, Span<int> lowband, int lowband_ptr, int LM, Span<int> lowband_out, int lowband_out_ptr, int gain, Span<int> lowband_scratch, int lowband_scratch_ptr, int fill)
	{
		int n = N;
		int num = B;
		int num2 = 0;
		int num3 = 0;
		uint num4 = 0u;
		int num5 = 1;
		int num6 = ctx.tf_change;
		int hadamard = ((num == 1) ? 1 : 0);
		n = Inlines.celt_udiv(n, B);
		if (N == 1)
		{
			return quant_band_n1_decode(ctx, encodedData, X, X_ptr, null, 0, b, lowband_out, lowband_out_ptr);
		}
		if (num6 > 0)
		{
			num3 = num6;
		}
		if (!lowband_scratch.IsEmpty && !lowband.IsEmpty && (num3 != 0 || ((n & 1) == 0 && num6 < 0) || num > 1))
		{
			lowband.Slice(lowband_ptr, N).CopyTo(lowband_scratch.Slice(lowband_scratch_ptr, N));
			lowband = lowband_scratch;
			lowband_ptr = lowband_scratch_ptr;
		}
		for (int i = 0; i < num3; i++)
		{
			if (!lowband.IsEmpty)
			{
				haar1(lowband, lowband_ptr, N >> i, 1 << i);
			}
			fill = bit_interleave_table[fill & 0xF] | (bit_interleave_table[fill >> 4] << 2);
		}
		B >>= num3;
		n <<= num3;
		while ((n & 1) == 0 && num6 < 0)
		{
			if (!lowband.IsEmpty)
			{
				haar1(lowband, lowband_ptr, n, B);
			}
			fill |= fill << B;
			B <<= 1;
			n >>= 1;
			num2++;
			num6++;
		}
		num = B;
		int num7 = n;
		if (num > 1 && !lowband.IsEmpty)
		{
			deinterleave_hadamard(lowband, lowband_ptr, n >> num3, num << num3, hadamard);
		}
		num4 = quant_partition_decode(ctx, encodedData, X, X_ptr, N, b, B, lowband, lowband_ptr, LM, gain, fill);
		if (num5 != 0)
		{
			if (num > 1)
			{
				interleave_hadamard(X, X_ptr, n >> num3, num << num3, hadamard);
			}
			n = num7;
			B = num;
			for (int i = 0; i < num2; i++)
			{
				B >>= 1;
				n <<= 1;
				num4 |= num4 >> B;
				haar1(X, X_ptr, n, B);
			}
			for (int i = 0; i < num3; i++)
			{
				num4 = bit_deinterleave_table[num4];
				haar1(X, X_ptr, N >> i, 1 << i);
			}
			B <<= num3;
			if (!lowband_out.IsEmpty)
			{
				int a = Inlines.celt_sqrt(Inlines.SHL32(N, 22));
				for (int j = 0; j < N; j++)
				{
					lowband_out[lowband_out_ptr + j] = Inlines.MULT16_16_Q15(a, X[X_ptr + j]);
				}
			}
			num4 &= (uint)((1 << B) - 1);
		}
		return num4;
	}

	internal static uint quant_band_stereo_encode(band_ctx ctx, Span<byte> encodedData, Span<int> X, int X_ptr, Span<int> Y, int Y_ptr, int N, int b, int B, Span<int> lowband, int lowband_ptr, int LM, Span<int> lowband_out, int lowband_out_ptr, Span<int> lowband_scratch, int lowband_scratch_ptr, int fill)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		uint num5 = 0u;
		int num6 = 0;
		split_ctx split_ctx = default;
		EntropyCoder ec = ctx.ec;
		if (N == 1)
		{
			return quant_band_n1_encode(ctx, encodedData, X, X_ptr, Y, Y_ptr, b, lowband_out, lowband_out_ptr);
		}
		int fill2 = fill;
		compute_theta_encode(ctx, ref split_ctx, encodedData, X, X_ptr, Y, Y_ptr, N, ref b, B, B, LM, 1, ref fill);
		num2 = split_ctx.inv;
		num = split_ctx.imid;
		int iside = split_ctx.iside;
		int delta = split_ctx.delta;
		int itheta = split_ctx.itheta;
		int qalloc = split_ctx.qalloc;
		num3 = num;
		num4 = iside;
		if (N == 2)
		{
			int num7 = 0;
			int num8 = b;
			int num9 = 0;
			if (itheta != 0 && itheta != 16384)
			{
				num9 = 8;
			}
			num8 -= num9;
			bool num10 = itheta > 8192;
			ctx.remaining_bits -= qalloc + num9;
			Span<int> x;
			int num11;
			Span<int> span;
			if (num10)
			{
				x = Y;
				num11 = Y_ptr;
				span = X;
			}
			else
			{
				x = X;
				num11 = X_ptr;
				span = Y;
			}
			if (num9 != 0)
			{
				num7 = ((x[num11] * span[Y_ptr + 1] - x[num11 + 1] * span[Y_ptr] < 0) ? 1 : 0);
				ec.enc_bits(encodedData, (uint)num7, 1u);
			}
			num7 = 1 - 2 * num7;
			num5 = quant_band_encode(ctx, encodedData, x, num11, N, num8, B, lowband, lowband_ptr, LM, lowband_out, lowband_out_ptr, 32767, lowband_scratch, lowband_scratch_ptr, fill2);
			span[Y_ptr] = -num7 * x[num11 + 1];
			span[Y_ptr + 1] = num7 * x[num11];
			if (num6 != 0)
			{
				X[X_ptr] = Inlines.MULT16_16_Q15(num3, X[X_ptr]);
				X[X_ptr + 1] = Inlines.MULT16_16_Q15(num3, X[X_ptr + 1]);
				Y[Y_ptr] = Inlines.MULT16_16_Q15(num4, Y[Y_ptr]);
				Y[Y_ptr + 1] = Inlines.MULT16_16_Q15(num4, Y[Y_ptr + 1]);
				int a = X[X_ptr];
				X[X_ptr] = Inlines.SUB16(a, Y[Y_ptr]);
				Y[Y_ptr] = Inlines.ADD16(a, Y[Y_ptr]);
				a = X[X_ptr + 1];
				X[X_ptr + 1] = Inlines.SUB16(a, Y[Y_ptr + 1]);
				Y[Y_ptr + 1] = Inlines.ADD16(a, Y[Y_ptr + 1]);
			}
		}
		else
		{
			int num8 = Inlines.IMAX(0, Inlines.IMIN(b, (b - delta) / 2));
			int num9 = b - num8;
			ctx.remaining_bits -= qalloc;
			int remaining_bits = ctx.remaining_bits;
			if (num8 >= num9)
			{
				num5 = quant_band_encode(ctx, encodedData, X, X_ptr, N, num8, B, lowband, lowband_ptr, LM, lowband_out, lowband_out_ptr, 32767, lowband_scratch, lowband_scratch_ptr, fill);
				remaining_bits = num8 - (remaining_bits - ctx.remaining_bits);
				if (remaining_bits > 24 && itheta != 0)
				{
					num9 += remaining_bits - 24;
				}
				num5 |= quant_band_encode(ctx, encodedData, Y, Y_ptr, N, num9, B, null, 0, LM, null, 0, num4, null, 0, fill >> B);
			}
			else
			{
				num5 = quant_band_encode(ctx, encodedData, Y, Y_ptr, N, num9, B, null, 0, LM, null, 0, num4, null, 0, fill >> B);
				remaining_bits = num9 - (remaining_bits - ctx.remaining_bits);
				if (remaining_bits > 24 && itheta != 16384)
				{
					num8 += remaining_bits - 24;
				}
				num5 |= quant_band_encode(ctx, encodedData, X, X_ptr, N, num8, B, lowband, lowband_ptr, LM, lowband_out, lowband_out_ptr, 32767, lowband_scratch, lowband_scratch_ptr, fill);
			}
		}
		if (num6 != 0)
		{
			if (N != 2)
			{
				stereo_merge(X.Slice(X_ptr), Y.Slice(Y_ptr), num3, N);
			}
			if (num2 != 0)
			{
				for (int i = Y_ptr; i < N + Y_ptr; i++)
				{
					Y[i] = (short)(-Y[i]);
				}
			}
		}
		return num5;
	}

	internal static uint quant_band_stereo_decode(band_ctx ctx, ReadOnlySpan<byte> encodedData, Span<int> X, int X_ptr, Span<int> Y, int Y_ptr, int N, int b, int B, Span<int> lowband, int lowband_ptr, int LM, Span<int> lowband_out, int lowband_out_ptr, Span<int> lowband_scratch, int lowband_scratch_ptr, int fill)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		uint num5 = 0u;
		int num6 = 1;
		split_ctx split_ctx = default;
		EntropyCoder ec = ctx.ec;
		if (N == 1)
		{
			return quant_band_n1_decode(ctx, encodedData, X, X_ptr, Y, Y_ptr, b, lowband_out, lowband_out_ptr);
		}
		int fill2 = fill;
		compute_theta_decode(ctx, ref split_ctx, encodedData, X, X_ptr, Y, Y_ptr, N, ref b, B, B, LM, 1, ref fill);
		num2 = split_ctx.inv;
		num = split_ctx.imid;
		int iside = split_ctx.iside;
		int delta = split_ctx.delta;
		int itheta = split_ctx.itheta;
		int qalloc = split_ctx.qalloc;
		num3 = num;
		num4 = iside;
		if (N == 2)
		{
			int num7 = 0;
			int num8 = b;
			int num9 = 0;
			if (itheta != 0 && itheta != 16384)
			{
				num9 = 8;
			}
			num8 -= num9;
			bool num10 = itheta > 8192;
			ctx.remaining_bits -= qalloc + num9;
			Span<int> x;
			int num11;
			Span<int> span;
			if (num10)
			{
				x = Y;
				num11 = Y_ptr;
				span = X;
			}
			else
			{
				x = X;
				num11 = X_ptr;
				span = Y;
			}
			if (num9 != 0)
			{
				num7 = (int)ec.dec_bits(encodedData, 1u);
			}
			num7 = 1 - 2 * num7;
			num5 = quant_band_decode(ctx, encodedData, x, num11, N, num8, B, lowband, lowband_ptr, LM, lowband_out, lowband_out_ptr, 32767, lowband_scratch, lowband_scratch_ptr, fill2);
			span[Y_ptr] = -num7 * x[num11 + 1];
			span[Y_ptr + 1] = num7 * x[num11];
			if (num6 != 0)
			{
				X[X_ptr] = Inlines.MULT16_16_Q15(num3, X[X_ptr]);
				X[X_ptr + 1] = Inlines.MULT16_16_Q15(num3, X[X_ptr + 1]);
				Y[Y_ptr] = Inlines.MULT16_16_Q15(num4, Y[Y_ptr]);
				Y[Y_ptr + 1] = Inlines.MULT16_16_Q15(num4, Y[Y_ptr + 1]);
				int a = X[X_ptr];
				X[X_ptr] = Inlines.SUB16(a, Y[Y_ptr]);
				Y[Y_ptr] = Inlines.ADD16(a, Y[Y_ptr]);
				a = X[X_ptr + 1];
				X[X_ptr + 1] = Inlines.SUB16(a, Y[Y_ptr + 1]);
				Y[Y_ptr + 1] = Inlines.ADD16(a, Y[Y_ptr + 1]);
			}
		}
		else
		{
			int num8 = Inlines.IMAX(0, Inlines.IMIN(b, (b - delta) / 2));
			int num9 = b - num8;
			ctx.remaining_bits -= qalloc;
			int remaining_bits = ctx.remaining_bits;
			if (num8 >= num9)
			{
				num5 = quant_band_decode(ctx, encodedData, X, X_ptr, N, num8, B, lowband, lowband_ptr, LM, lowband_out, lowband_out_ptr, 32767, lowband_scratch, lowband_scratch_ptr, fill);
				remaining_bits = num8 - (remaining_bits - ctx.remaining_bits);
				if (remaining_bits > 24 && itheta != 0)
				{
					num9 += remaining_bits - 24;
				}
				num5 |= quant_band_decode(ctx, encodedData, Y, Y_ptr, N, num9, B, null, 0, LM, null, 0, num4, null, 0, fill >> B);
			}
			else
			{
				num5 = quant_band_decode(ctx, encodedData, Y, Y_ptr, N, num9, B, null, 0, LM, null, 0, num4, null, 0, fill >> B);
				remaining_bits = num9 - (remaining_bits - ctx.remaining_bits);
				if (remaining_bits > 24 && itheta != 16384)
				{
					num8 += remaining_bits - 24;
				}
				num5 |= quant_band_decode(ctx, encodedData, X, X_ptr, N, num8, B, lowband, lowband_ptr, LM, lowband_out, lowband_out_ptr, 32767, lowband_scratch, lowband_scratch_ptr, fill);
			}
		}
		if (num6 != 0)
		{
			if (N != 2)
			{
				stereo_merge(X.Slice(X_ptr), Y.Slice(Y_ptr), num3, N);
			}
			if (num2 != 0)
			{
				for (int i = Y_ptr; i < N + Y_ptr; i++)
				{
					Y[i] = (short)(-Y[i]);
				}
			}
		}
		return num5;
	}

	internal static void quant_all_bands_encode(int encode, CeltMode m, int start, int end, int[] X_, int[] Y_, byte[] collapse_masks, int[][] bandE, int[] pulses, int shortBlocks, int spread, int dual_stereo, int intensity, int[] tf_res, int total_bits, int balance, EntropyCoder ec, Span<byte> encodedData, int LM, int codedBands, ref uint seed)
	{
		short[] eBands = m.eBands;
		int num = 1;
		int num2 = ((Y_ == null) ? 1 : 2);
		int num3 = ((encode == 0) ? 1 : 0);
		band_ctx band_ctx = new band_ctx();
		int num4 = 1 << LM;
		int num5 = ((shortBlocks == 0) ? 1 : num4);
		int num6 = num4 * eBands[start];
		int[] array = new int[num2 * (num4 * eBands[m.nbEBands - 1] - num6)];
		int num7 = num4 * eBands[m.nbEBands - 1] - num6;
		int[] array2 = X_;
		int lowband_scratch_ptr = num4 * eBands[m.nbEBands - 1];
		int num8 = 0;
		band_ctx.bandE = bandE;
		band_ctx.vqScratch = null;
		band_ctx.ec = ec;
		band_ctx.encode = encode;
		band_ctx.intensity = intensity;
		band_ctx.m = m;
		band_ctx.seed = seed;
		band_ctx.spread = spread;
		for (int i = start; i < end; i++)
		{
			int num9 = -1;
			int num10 = 0;
			int num11 = 0;
			band_ctx.i = i;
			int num12 = ((i == end - 1) ? 1 : 0);
			int[] array3 = X_;
			int x_ptr = num4 * eBands[i];
			int[] array4;
			if (Y_ != null)
			{
				array4 = Y_;
				num10 = num4 * eBands[i];
			}
			else
			{
				array4 = null;
			}
			int num13 = num4 * eBands[i + 1] - num4 * eBands[i];
			int num14 = (int)ec.tell_frac();
			if (i != start)
			{
				balance -= num14;
			}
			int num15 = (band_ctx.remaining_bits = total_bits - num14 - 1);
			int num17;
			if (i <= codedBands - 1)
			{
				int num16 = Inlines.celt_sudiv(balance, Inlines.IMIN(3, codedBands - i));
				num17 = Inlines.IMAX(0, Inlines.IMIN(16383, Inlines.IMIN(num15 + 1, pulses[i] + num16)));
			}
			else
			{
				num17 = 0;
			}
			if (num3 != 0 && num4 * eBands[i] - num13 >= num4 * eBands[start] && (num != 0 || num8 == 0))
			{
				num8 = i;
			}
			num11 = (band_ctx.tf_change = tf_res[i]);
			if (i >= m.effEBands)
			{
				array3 = array;
				x_ptr = 0;
				if (Y_ != null)
				{
					array4 = array;
					num10 = 0;
				}
				array2 = null;
			}
			if (i == end - 1)
			{
				array2 = null;
			}
			uint num20;
			uint num21;
			if (num8 != 0 && (spread != 3 || num5 > 1 || num11 < 0))
			{
				num9 = Inlines.IMAX(0, num4 * eBands[num8] - num6 - num13);
				int num18 = num8;
				while (num4 * eBands[--num18] > num9 + num6)
				{
				}
				int num19 = num8 - 1;
				while (num4 * eBands[++num19] < num9 + num6 + num13)
				{
				}
				num20 = (num21 = 0u);
				int num22 = num18;
				do
				{
					num20 |= collapse_masks[num22 * num2];
					num21 |= collapse_masks[num22 * num2 + num2 - 1];
				}
				while (++num22 < num19);
			}
			else
			{
				num20 = (num21 = (uint)((1 << num5) - 1));
			}
			if (dual_stereo != 0 && i == intensity)
			{
				dual_stereo = 0;
				if (num3 != 0)
				{
					for (int j = 0; j < num4 * eBands[i] - num6; j++)
					{
						array[j] = Inlines.HALF32(array[j] + array[num7 + j]);
					}
				}
			}
			if (dual_stereo != 0)
			{
				num20 = quant_band_encode(band_ctx, encodedData, array3, x_ptr, num13, num17 / 2, num5, (num9 != -1) ? array : null, num9, LM, (num12 != 0) ? null : array, num4 * eBands[i] - num6, 32767, array2, lowband_scratch_ptr, (int)num20);
				num21 = quant_band_encode(band_ctx, encodedData, array4, num10, num13, num17 / 2, num5, (num9 != -1) ? array : null, num7 + num9, LM, (num12 != 0) ? null : array, num7 + (num4 * eBands[i] - num6), 32767, array2, lowband_scratch_ptr, (int)num21);
			}
			else
			{
				num20 = ((array4 == null) ? quant_band_encode(band_ctx, encodedData, array3, x_ptr, num13, num17, num5, (num9 != -1) ? array : null, num9, LM, (num12 != 0) ? null : array, num4 * eBands[i] - num6, 32767, array2, lowband_scratch_ptr, (int)(num20 | num21)) : quant_band_stereo_encode(band_ctx, encodedData, array3, x_ptr, array4, num10, num13, num17, num5, (num9 != -1) ? array : null, num9, LM, (num12 != 0) ? null : array, num4 * eBands[i] - num6, array2, lowband_scratch_ptr, (int)(num20 | num21)));
				num21 = num20;
			}
			collapse_masks[i * num2] = (byte)(num20 & 0xFF);
			collapse_masks[i * num2 + num2 - 1] = (byte)(num21 & 0xFF);
			balance += pulses[i] + num14;
			num = ((num17 > num13 << 3) ? 1 : 0);
		}
		seed = band_ctx.seed;
	}

	internal static void quant_all_bands_decode(int encode, CeltMode m, int start, int end, int[] X_, int[] Y_, byte[] collapse_masks, int[][] bandE, int[] pulses, int shortBlocks, int spread, int dual_stereo, int intensity, int[] tf_res, int total_bits, int balance, EntropyCoder ec, ReadOnlySpan<byte> encodedData, int LM, int codedBands, ref uint seed, band_ctx band_ctx, int[] scratch)
	{
		short[] eBands = m.eBands;
		int num = 1;
		int num2 = ((Y_ == null) ? 1 : 2);
		int num3 = ((encode == 0) ? 1 : 0);
		int num4 = 1 << LM;
		int num5 = ((shortBlocks == 0) ? 1 : num4);
		int num6 = num4 * eBands[start];
		int[] array = scratch;
		int num7 = num4 * eBands[m.nbEBands - 1] - num6;
		int[] array2 = X_;
		int lowband_scratch_ptr = num4 * eBands[m.nbEBands - 1];
		int num8 = 0;
		band_ctx.bandE = bandE;
		band_ctx.ec = ec;
		band_ctx.encode = encode;
		band_ctx.intensity = intensity;
		band_ctx.m = m;
		band_ctx.seed = seed;
		band_ctx.spread = spread;
		for (int i = start; i < end; i++)
		{
			int num9 = -1;
			int num10 = 0;
			int num11 = 0;
			band_ctx.i = i;
			int num12 = ((i == end - 1) ? 1 : 0);
			int[] array3 = X_;
			int x_ptr = num4 * eBands[i];
			int[] array4;
			if (Y_ != null)
			{
				array4 = Y_;
				num10 = num4 * eBands[i];
			}
			else
			{
				array4 = null;
			}
			int num13 = num4 * eBands[i + 1] - num4 * eBands[i];
			int num14 = (int)ec.tell_frac();
			if (i != start)
			{
				balance -= num14;
			}
			int num15 = (band_ctx.remaining_bits = total_bits - num14 - 1);
			int num17;
			if (i <= codedBands - 1)
			{
				int num16 = Inlines.celt_sudiv(balance, Inlines.IMIN(3, codedBands - i));
				num17 = Inlines.IMAX(0, Inlines.IMIN(16383, Inlines.IMIN(num15 + 1, pulses[i] + num16)));
			}
			else
			{
				num17 = 0;
			}
			if (num3 != 0 && num4 * eBands[i] - num13 >= num4 * eBands[start] && (num != 0 || num8 == 0))
			{
				num8 = i;
			}
			num11 = (band_ctx.tf_change = tf_res[i]);
			if (i >= m.effEBands)
			{
				array3 = array;
				x_ptr = 0;
				if (Y_ != null)
				{
					array4 = array;
					num10 = 0;
				}
				array2 = null;
			}
			if (i == end - 1)
			{
				array2 = null;
			}
			uint num20;
			uint num21;
			if (num8 != 0 && (spread != 3 || num5 > 1 || num11 < 0))
			{
				num9 = Inlines.IMAX(0, num4 * eBands[num8] - num6 - num13);
				int num18 = num8;
				while (num4 * eBands[--num18] > num9 + num6)
				{
				}
				int num19 = num8 - 1;
				while (num4 * eBands[++num19] < num9 + num6 + num13)
				{
				}
				num20 = (num21 = 0u);
				int num22 = num18;
				do
				{
					num20 |= collapse_masks[num22 * num2];
					num21 |= collapse_masks[num22 * num2 + num2 - 1];
				}
				while (++num22 < num19);
			}
			else
			{
				num20 = (num21 = (uint)((1 << num5) - 1));
			}
			if (dual_stereo != 0 && i == intensity)
			{
				dual_stereo = 0;
				if (num3 != 0)
				{
					for (int j = 0; j < num4 * eBands[i] - num6; j++)
					{
						array[j] = Inlines.HALF32(array[j] + array[num7 + j]);
					}
				}
			}
			if (dual_stereo != 0)
			{
				num20 = quant_band_decode(band_ctx, encodedData, array3, x_ptr, num13, num17 / 2, num5, (num9 != -1) ? array : null, num9, LM, (num12 != 0) ? null : array, num4 * eBands[i] - num6, 32767, array2, lowband_scratch_ptr, (int)num20);
				num21 = quant_band_decode(band_ctx, encodedData, array4, num10, num13, num17 / 2, num5, (num9 != -1) ? array : null, num7 + num9, LM, (num12 != 0) ? null : array, num7 + (num4 * eBands[i] - num6), 32767, array2, lowband_scratch_ptr, (int)num21);
			}
			else
			{
				num20 = ((array4 == null) ? quant_band_decode(band_ctx, encodedData, array3, x_ptr, num13, num17, num5, (num9 != -1) ? array : null, num9, LM, (num12 != 0) ? null : array, num4 * eBands[i] - num6, 32767, array2, lowband_scratch_ptr, (int)(num20 | num21)) : quant_band_stereo_decode(band_ctx, encodedData, array3, x_ptr, array4, num10, num13, num17, num5, (num9 != -1) ? array : null, num9, LM, (num12 != 0) ? null : array, num4 * eBands[i] - num6, array2, lowband_scratch_ptr, (int)(num20 | num21)));
				num21 = num20;
			}
			collapse_masks[i * num2] = (byte)(num20 & 0xFF);
			collapse_masks[i * num2 + num2 - 1] = (byte)(num21 & 0xFF);
			balance += pulses[i] + num14;
			num = ((num17 > num13 << 3) ? 1 : 0);
		}
		seed = band_ctx.seed;
	}
}
