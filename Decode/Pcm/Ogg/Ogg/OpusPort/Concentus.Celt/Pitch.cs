using System;
using Concentus.Common;

namespace Concentus.Celt;

internal static class Pitch
{
	private static readonly int[] second_check = new int[16]
	{
		0, 0, 3, 2, 3, 2, 5, 2, 3, 2,
		3, 2, 5, 2, 3, 2
	};

	internal static void find_best_pitch(Span<int> xcorr, Span<int> y, int len, int max_pitch, int[] best_pitch, int yshift, int maxcorr)
	{
		int num = 1;
		int shift = Inlines.celt_ilog2(maxcorr) - 14;
		int num2 = -1;
		int a = -1;
		int num3 = 0;
		int b = 0;
		best_pitch[0] = 0;
		best_pitch[1] = 1;
		for (int i = 0; i < len; i++)
		{
			num = Inlines.ADD32(num, Inlines.SHR32(Inlines.MULT16_16(y[i], y[i]), yshift));
		}
		for (int j = 0; j < max_pitch; j++)
		{
			if (xcorr[j] > 0)
			{
				short num4 = Inlines.EXTRACT16(Inlines.VSHR32(xcorr[j], shift));
				int num5 = Inlines.MULT16_16_Q15((int)num4, (int)num4);
				if (Inlines.MULT16_32_Q15(num5, b) > Inlines.MULT16_32_Q15(a, num))
				{
					if (Inlines.MULT16_32_Q15(num5, num3) > Inlines.MULT16_32_Q15(num2, num))
					{
						a = num2;
						b = num3;
						best_pitch[1] = best_pitch[0];
						num2 = num5;
						num3 = num;
						best_pitch[0] = j;
					}
					else
					{
						a = num5;
						b = num;
						best_pitch[1] = j;
					}
				}
			}
			num += Inlines.SHR32(Inlines.MULT16_16(y[j + len], y[j + len]), yshift) - Inlines.SHR32(Inlines.MULT16_16(y[j], y[j]), yshift);
			num = Inlines.MAX32(1, num);
		}
	}

	internal static void celt_fir5(int[] x, int[] num, int[] y, int N, int[] mem)
	{
		int a = num[0];
		int a2 = num[1];
		int a3 = num[2];
		int a4 = num[3];
		int a5 = num[4];
		int num2 = mem[0];
		int num3 = mem[1];
		int num4 = mem[2];
		int num5 = mem[3];
		int num6 = mem[4];
		for (int i = 0; i < N; i++)
		{
			int c = Inlines.SHL32(Inlines.EXTEND32(x[i]), 12);
			c = Inlines.MAC16_16(c, a, num2);
			c = Inlines.MAC16_16(c, a2, num3);
			c = Inlines.MAC16_16(c, a3, num4);
			c = Inlines.MAC16_16(c, a4, num5);
			c = Inlines.MAC16_16(c, a5, num6);
			num6 = num5;
			num5 = num4;
			num4 = num3;
			num3 = num2;
			num2 = x[i];
			y[i] = Inlines.ROUND16(c, 12);
		}
		mem[0] = num2;
		mem[1] = num3;
		mem[2] = num4;
		mem[3] = num5;
		mem[4] = num6;
	}

	internal static void pitch_downsample(int[][] x, int[] x_lp, int len, int C)
	{
		int[] array = new int[5];
		int b = 32767;
		int[] array2 = new int[4];
		int[] mem = new int[5];
		int[] array3 = new int[5];
		int a = 26214;
		int num = Inlines.celt_maxabs32(x[0], 0, len);
		if (C == 2)
		{
			int b2 = Inlines.celt_maxabs32(x[1], 0, len);
			num = Inlines.MAX32(num, b2);
		}
		if (num < 1)
		{
			num = 1;
		}
		int num2 = Inlines.celt_ilog2(num) - 10;
		if (num2 < 0)
		{
			num2 = 0;
		}
		if (C == 2)
		{
			num2++;
		}
		int num3 = len >> 1;
		for (int i = 1; i < num3; i++)
		{
			x_lp[i] = Inlines.SHR32(Inlines.HALF32(Inlines.HALF32(x[0][2 * i - 1] + x[0][2 * i + 1]) + x[0][2 * i]), num2);
		}
		x_lp[0] = Inlines.SHR32(Inlines.HALF32(Inlines.HALF32(x[0][1]) + x[0][0]), num2);
		if (C == 2)
		{
			for (int i = 1; i < num3; i++)
			{
				x_lp[i] += Inlines.SHR32(Inlines.HALF32(Inlines.HALF32(x[1][2 * i - 1] + x[1][2 * i + 1]) + x[1][2 * i]), num2);
			}
			x_lp[0] += Inlines.SHR32(Inlines.HALF32(Inlines.HALF32(x[1][1]) + x[1][0]), num2);
		}
		Autocorrelation._celt_autocorr(x_lp, array, null, 0, 4, num3);
		array[0] += Inlines.SHR32(array[0], 13);
		for (int i = 1; i <= 4; i++)
		{
			array[i] -= Inlines.MULT16_32_Q15(2 * i * i, array[i]);
		}
		CeltLPC.celt_lpc(array2, array, 4);
		for (int i = 0; i < 4; i++)
		{
			b = Inlines.MULT16_16_Q15(29491, b);
			array2[i] = Inlines.MULT16_16_Q15(array2[i], b);
		}
		array3[0] = array2[0] + 3277;
		array3[1] = array2[1] + Inlines.MULT16_16_Q15(a, array2[0]);
		array3[2] = array2[2] + Inlines.MULT16_16_Q15(a, array2[1]);
		array3[3] = array2[3] + Inlines.MULT16_16_Q15(a, array2[2]);
		array3[4] = Inlines.MULT16_16_Q15(a, array2[3]);
		celt_fir5(x_lp, array3, x_lp, num3, mem);
	}

	internal static void pitch_search(Span<int> x_lp, int x_lp_ptr, int[] y, int len, int max_pitch, out int pitch)
	{
		int[] array = new int[2];
		int num = 0;
		int num2 = len + max_pitch;
		int[] array2 = new int[len >> 2];
		int[] array3 = new int[num2 >> 2];
		int[] array4 = new int[max_pitch >> 1];
		for (int i = 0; i < len >> 2; i++)
		{
			array2[i] = x_lp[x_lp_ptr + 2 * i];
		}
		for (int i = 0; i < num2 >> 2; i++)
		{
			array3[i] = y[2 * i];
		}
		int a = Inlines.celt_maxabs32(array2, len >> 2);
		int b = Inlines.celt_maxabs32(array3, num2 >> 2);
		num = Inlines.celt_ilog2(Inlines.MAX32(1, Inlines.MAX32(a, b))) - 11;
		if (num > 0)
		{
			for (int i = 0; i < len >> 2; i++)
			{
				array2[i] = Inlines.SHR16(array2[i], num);
			}
			for (int i = 0; i < num2 >> 2; i++)
			{
				array3[i] = Inlines.SHR16(array3[i], num);
			}
			num *= 2;
		}
		else
		{
			num = 0;
		}
		int maxcorr = CeltPitchXCorr.pitch_xcorr(array2, 0, array3, 0, array4, len >> 2, max_pitch >> 2);
		find_best_pitch(array4, array3, len >> 2, max_pitch >> 2, array, 0, maxcorr);
		maxcorr = 1;
		for (int j = 0; j < max_pitch >> 1; j++)
		{
			array4[j] = 0;
			if (Inlines.abs(j - 2 * array[0]) <= 2 || Inlines.abs(j - 2 * array[1]) <= 2)
			{
				int num3 = 0;
				for (int i = 0; i < len >> 1; i++)
				{
					num3 += Inlines.SHR32(Inlines.MULT16_16(x_lp[x_lp_ptr + i], y[j + i]), num);
				}
				array4[j] = Inlines.MAX32(-1, num3);
				maxcorr = Inlines.MAX32(maxcorr, num3);
			}
		}
		find_best_pitch(array4, y, len >> 1, max_pitch >> 1, array, num + 1, maxcorr);
		int num7;
		if (array[0] > 0 && array[0] < (max_pitch >> 1) - 1)
		{
			int num4 = array4[array[0] - 1];
			int num5 = array4[array[0]];
			int num6 = array4[array[0] + 1];
			num7 = ((num6 - num4 > Inlines.MULT16_32_Q15((short)22938, num5 - num4)) ? 1 : ((num4 - num6 > Inlines.MULT16_32_Q15((short)22938, num5 - num6)) ? (-1) : 0));
		}
		else
		{
			num7 = 0;
		}
		pitch = 2 * array[0] - num7;
	}

	internal static int remove_doubling(int[] x, int maxperiod, int minperiod, int N, ref int T0_, int prev_period, int prev_gain)
	{
		Span<int> span = new int[3];
		int num = minperiod;
		maxperiod /= 2;
		minperiod /= 2;
		T0_ /= 2;
		prev_period /= 2;
		N /= 2;
		int num2 = maxperiod;
		if (T0_ >= maxperiod)
		{
			T0_ = maxperiod - 1;
		}
		int num4;
		int num3 = (num4 = T0_);
		Span<int> span2 = new int[maxperiod + 1];
		Span<int> span3 = x.AsSpan();
		Span<int> x2 = span3.Slice(num2);
		span3 = x.AsSpan();
		Span<int> y = span3.Slice(num2);
		span3 = x.AsSpan();
		Kernels.dual_inner_prod(x2, y, span3.Slice(num2 - num4), N, out var xy, out var xy2);
		span2[0] = xy;
		int num5 = xy;
		for (int i = 1; i <= maxperiod; i++)
		{
			int num6 = num2 - i;
			num5 = num5 + Inlines.MULT16_16(x[num6], x[num6]) - Inlines.MULT16_16(x[num6 + N], x[num6 + N]);
			span2[i] = Inlines.MAX32(0, num5);
		}
		num5 = span2[num4];
		int b = xy2;
		int num7 = num5;
		int num8 = 1 + Inlines.HALF32(Inlines.MULT32_32_Q31(xy, num5));
		int num9 = Inlines.celt_ilog2(num8) >> 1;
		int num10 = Inlines.VSHR32(Inlines.MULT16_32_Q15(Inlines.celt_rsqrt_norm(Inlines.VSHR32(num8, 2 * (num9 - 7))), xy2), num9 + 1);
		int b2 = num10;
		for (int j = 2; j <= 15; j++)
		{
			int num11 = 0;
			int num12 = Inlines.celt_udiv(2 * num4 + j, 2 * j);
			if (num12 < minperiod)
			{
				break;
			}
			int num13 = ((j != 2) ? Inlines.celt_udiv(2 * second_check[j] * num4 + j, 2 * j) : ((num12 + num4 <= maxperiod) ? (num4 + num12) : num4));
			span3 = x.AsSpan();
			Span<int> x3 = span3.Slice(num2);
			span3 = x.AsSpan();
			Span<int> y2 = span3.Slice(num2 - num12);
			span3 = x.AsSpan();
			Kernels.dual_inner_prod(x3, y2, span3.Slice(num2 - num13), N, out xy2, out var xy3);
			xy2 += xy3;
			num5 = span2[num12] + span2[num13];
			int num14 = 1 + Inlines.MULT32_32_Q31(xy, num5);
			int num15 = Inlines.celt_ilog2(num14) >> 1;
			int num16 = Inlines.VSHR32(Inlines.MULT16_32_Q15(Inlines.celt_rsqrt_norm(Inlines.VSHR32(num14, 2 * (num15 - 7))), xy2), num15 + 1);
			num11 = ((Inlines.abs(num12 - prev_period) <= 1) ? prev_gain : ((Inlines.abs(num12 - prev_period) <= 2 && 5 * j * j < num4) ? Inlines.HALF16(prev_gain) : 0));
			int num17 = Inlines.MAX16(9830, Inlines.MULT16_16_Q15(22938, b2) - num11);
			if (num12 < 3 * minperiod)
			{
				num17 = Inlines.MAX16(13107, Inlines.MULT16_16_Q15(27853, b2) - num11);
			}
			else if (num12 < 2 * minperiod)
			{
				num17 = Inlines.MAX16(16384, Inlines.MULT16_16_Q15(29491, b2) - num11);
			}
			if (num16 > num17)
			{
				b = xy2;
				num7 = num5;
				num3 = num12;
				num10 = num16;
			}
		}
		b = Inlines.MAX32(0, b);
		int num18 = ((num7 > b) ? Inlines.SHR32(Inlines.frac_div32(b, num7 + 1), 16) : 32767);
		for (int j = 0; j < 3; j++)
		{
			ref int reference = ref span[j];
			span3 = x.AsSpan();
			Span<int> x4 = span3.Slice(num2);
			span3 = x.AsSpan();
			reference = Kernels.celt_inner_prod(x4, span3.Slice(num2 - (num3 + j - 1)), N);
		}
		int num19 = ((span[2] - span[0] > Inlines.MULT16_32_Q15((short)22938, span[1] - span[0])) ? 1 : ((span[0] - span[2] > Inlines.MULT16_32_Q15((short)22938, span[1] - span[2])) ? (-1) : 0));
		if (num18 > num10)
		{
			num18 = num10;
		}
		T0_ = 2 * num3 + num19;
		if (T0_ < num)
		{
			T0_ = num;
		}
		return num18;
	}
}
