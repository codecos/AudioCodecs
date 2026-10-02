using System;
using Concentus.Celt.Structs;
using Concentus.Common;

namespace Concentus.Celt;

internal static class KissFFT
{
	internal const int MAXFACTORS = 8;

	internal static int S_MUL(int a, int b)
	{
		return Inlines.MULT16_32_Q15(b, a);
	}

	internal static int S_MUL(int a, short b)
	{
		return Inlines.MULT16_32_Q15(b, a);
	}

	internal static int HALF_OF(int x)
	{
		return x >> 1;
	}

	internal static void kf_bfly2(Span<int> Fout, int fout_ptr, int m, int N)
	{
		short b = 23170;
		for (int i = 0; i < N; i++)
		{
			int num = fout_ptr + 8;
			int num2 = Fout[num];
			int num3 = Fout[num + 1];
			Fout[num] = Fout[fout_ptr] - num2;
			Fout[num + 1] = Fout[fout_ptr + 1] - num3;
			Fout[fout_ptr] += num2;
			Fout[fout_ptr + 1] += num3;
			num2 = S_MUL(Fout[num + 2] + Fout[num + 3], b);
			num3 = S_MUL(Fout[num + 3] - Fout[num + 2], b);
			Fout[num + 2] = Fout[fout_ptr + 2] - num2;
			Fout[num + 3] = Fout[fout_ptr + 3] - num3;
			Fout[fout_ptr + 2] += num2;
			Fout[fout_ptr + 3] += num3;
			num2 = Fout[num + 5];
			num3 = -Fout[num + 4];
			Fout[num + 4] = Fout[fout_ptr + 4] - num2;
			Fout[num + 5] = Fout[fout_ptr + 5] - num3;
			Fout[fout_ptr + 4] += num2;
			Fout[fout_ptr + 5] += num3;
			num2 = S_MUL(Fout[num + 7] - Fout[num + 6], b);
			num3 = S_MUL(-Fout[num + 7] - Fout[num + 6], b);
			Fout[num + 6] = Fout[fout_ptr + 6] - num2;
			Fout[num + 7] = Fout[fout_ptr + 7] - num3;
			Fout[fout_ptr + 6] += num2;
			Fout[fout_ptr + 7] += num3;
			fout_ptr += 16;
		}
	}

	internal static void kf_bfly4(Span<int> Fout, int fout_ptr, int fstride, FFTState st, int m, int N, int mm)
	{
		if (m == 1)
		{
			for (int i = 0; i < N; i++)
			{
				int num = Fout[fout_ptr] - Fout[fout_ptr + 4];
				int num2 = Fout[fout_ptr + 1] - Fout[fout_ptr + 5];
				Fout[fout_ptr] += Fout[fout_ptr + 4];
				Fout[fout_ptr + 1] += Fout[fout_ptr + 5];
				int num3 = Fout[fout_ptr + 2] + Fout[fout_ptr + 6];
				int num4 = Fout[fout_ptr + 3] + Fout[fout_ptr + 7];
				Fout[fout_ptr + 4] = Fout[fout_ptr] - num3;
				Fout[fout_ptr + 5] = Fout[fout_ptr + 1] - num4;
				Fout[fout_ptr] += num3;
				Fout[fout_ptr + 1] += num4;
				num3 = Fout[fout_ptr + 2] - Fout[fout_ptr + 6];
				num4 = Fout[fout_ptr + 3] - Fout[fout_ptr + 7];
				Fout[fout_ptr + 2] = num + num4;
				Fout[fout_ptr + 3] = num2 - num3;
				Fout[fout_ptr + 6] = num - num4;
				Fout[fout_ptr + 7] = num2 + num3;
				fout_ptr += 8;
			}
			return;
		}
		int num5 = fout_ptr;
		for (int i = 0; i < N; i++)
		{
			fout_ptr = num5 + 2 * i * mm;
			int num6 = fout_ptr + 2 * m;
			int num7 = fout_ptr + 4 * m;
			int num8 = fout_ptr + 6 * m;
			int num10;
			int num11;
			int num9 = (num10 = (num11 = 0));
			for (int j = 0; j < m; j++)
			{
				int num12 = S_MUL(Fout[num6], st.twiddles[num11]) - S_MUL(Fout[num6 + 1], st.twiddles[num11 + 1]);
				int num13 = S_MUL(Fout[num6], st.twiddles[num11 + 1]) + S_MUL(Fout[num6 + 1], st.twiddles[num11]);
				int num14 = S_MUL(Fout[num7], st.twiddles[num10]) - S_MUL(Fout[num7 + 1], st.twiddles[num10 + 1]);
				int num15 = S_MUL(Fout[num7], st.twiddles[num10 + 1]) + S_MUL(Fout[num7 + 1], st.twiddles[num10]);
				int num16 = S_MUL(Fout[num8], st.twiddles[num9]) - S_MUL(Fout[num8 + 1], st.twiddles[num9 + 1]);
				int num17 = S_MUL(Fout[num8], st.twiddles[num9 + 1]) + S_MUL(Fout[num8 + 1], st.twiddles[num9]);
				int num18 = Fout[fout_ptr] - num14;
				int num19 = Fout[fout_ptr + 1] - num15;
				Fout[fout_ptr] += num14;
				Fout[fout_ptr + 1] += num15;
				int num20 = num12 + num16;
				int num21 = num13 + num17;
				int num22 = num12 - num16;
				int num23 = num13 - num17;
				Fout[num7] = Fout[fout_ptr] - num20;
				Fout[num7 + 1] = Fout[fout_ptr + 1] - num21;
				num11 += fstride * 2;
				num10 += fstride * 4;
				num9 += fstride * 6;
				Fout[fout_ptr] += num20;
				Fout[fout_ptr + 1] += num21;
				Fout[num6] = num18 + num23;
				Fout[num6 + 1] = num19 - num22;
				Fout[num8] = num18 - num23;
				Fout[num8 + 1] = num19 + num22;
				fout_ptr += 2;
				num6 += 2;
				num7 += 2;
				num8 += 2;
			}
		}
	}

	internal static void kf_bfly3(Span<int> Fout, int fout_ptr, int fstride, FFTState st, int m, int N, int mm)
	{
		int num = 2 * m;
		int num2 = 4 * m;
		int num3 = fout_ptr;
		for (int i = 0; i < N; i++)
		{
			fout_ptr = num3 + 2 * i * mm;
			int num5;
			int num4 = (num5 = 0);
			int num6 = m;
			do
			{
				int num7 = S_MUL(Fout[fout_ptr + num], st.twiddles[num4]) - S_MUL(Fout[fout_ptr + num + 1], st.twiddles[num4 + 1]);
				int num8 = S_MUL(Fout[fout_ptr + num], st.twiddles[num4 + 1]) + S_MUL(Fout[fout_ptr + num + 1], st.twiddles[num4]);
				int num9 = S_MUL(Fout[fout_ptr + num2], st.twiddles[num5]) - S_MUL(Fout[fout_ptr + num2 + 1], st.twiddles[num5 + 1]);
				int num10 = S_MUL(Fout[fout_ptr + num2], st.twiddles[num5 + 1]) + S_MUL(Fout[fout_ptr + num2 + 1], st.twiddles[num5]);
				int num11 = num7 + num9;
				int num12 = num8 + num10;
				int a = num7 - num9;
				int a2 = num8 - num10;
				num4 += fstride * 2;
				num5 += fstride * 4;
				Fout[fout_ptr + num] = Fout[fout_ptr] - HALF_OF(num11);
				Fout[fout_ptr + num + 1] = Fout[fout_ptr + 1] - HALF_OF(num12);
				a = S_MUL(a, -28378);
				a2 = S_MUL(a2, -28378);
				Fout[fout_ptr] += num11;
				Fout[fout_ptr + 1] += num12;
				Fout[fout_ptr + num2] = Fout[fout_ptr + num] + a2;
				Fout[fout_ptr + num2 + 1] = Fout[fout_ptr + num + 1] - a;
				Fout[fout_ptr + num] -= a2;
				Fout[fout_ptr + num + 1] += a;
				fout_ptr += 2;
			}
			while (--num6 != 0);
		}
	}

	internal static void kf_bfly5(Span<int> Fout, int fout_ptr, int fstride, FFTState st, int m, int N, int mm)
	{
		int num = fout_ptr;
		short b = 10126;
		short b2 = -31164;
		short b3 = -26510;
		short b4 = -19261;
		for (int i = 0; i < N; i++)
		{
			int num3;
			int num4;
			int num5;
			int num2 = (num3 = (num4 = (num5 = 0)));
			fout_ptr = num + 2 * i * mm;
			int num6 = fout_ptr;
			int num7 = fout_ptr + 2 * m;
			int num8 = fout_ptr + 4 * m;
			int num9 = fout_ptr + 6 * m;
			int num10 = fout_ptr + 8 * m;
			for (int j = 0; j < m; j++)
			{
				int num11 = Fout[num6];
				int num12 = Fout[num6 + 1];
				int num13 = S_MUL(Fout[num7], st.twiddles[num2]) - S_MUL(Fout[num7 + 1], st.twiddles[num2 + 1]);
				int num14 = S_MUL(Fout[num7], st.twiddles[num2 + 1]) + S_MUL(Fout[num7 + 1], st.twiddles[num2]);
				int num15 = S_MUL(Fout[num8], st.twiddles[num3]) - S_MUL(Fout[num8 + 1], st.twiddles[num3 + 1]);
				int num16 = S_MUL(Fout[num8], st.twiddles[num3 + 1]) + S_MUL(Fout[num8 + 1], st.twiddles[num3]);
				int num17 = S_MUL(Fout[num9], st.twiddles[num4]) - S_MUL(Fout[num9 + 1], st.twiddles[num4 + 1]);
				int num18 = S_MUL(Fout[num9], st.twiddles[num4 + 1]) + S_MUL(Fout[num9 + 1], st.twiddles[num4]);
				int num19 = S_MUL(Fout[num10], st.twiddles[num5]) - S_MUL(Fout[num10 + 1], st.twiddles[num5 + 1]);
				int num20 = S_MUL(Fout[num10], st.twiddles[num5 + 1]) + S_MUL(Fout[num10 + 1], st.twiddles[num5]);
				num2 += 2 * fstride;
				num3 += 4 * fstride;
				num4 += 6 * fstride;
				num5 += 8 * fstride;
				int num21 = num13 + num19;
				int num22 = num14 + num20;
				int a = num13 - num19;
				int a2 = num14 - num20;
				int num23 = num15 + num17;
				int num24 = num16 + num18;
				int a3 = num15 - num17;
				int a4 = num16 - num18;
				Fout[num6] += num21 + num23;
				Fout[num6 + 1] += num22 + num24;
				int num25 = num11 + S_MUL(num21, b) + S_MUL(num23, b3);
				int num26 = num12 + S_MUL(num22, b) + S_MUL(num24, b3);
				int num27 = S_MUL(a2, b2) + S_MUL(a4, b4);
				int num28 = -S_MUL(a, b2) - S_MUL(a3, b4);
				Fout[num7] = num25 - num27;
				Fout[num7 + 1] = num26 - num28;
				Fout[num10] = num25 + num27;
				Fout[num10 + 1] = num26 + num28;
				int num29 = num11 + S_MUL(num21, b3) + S_MUL(num23, b);
				int num30 = num12 + S_MUL(num22, b3) + S_MUL(num24, b);
				int num31 = -S_MUL(a2, b4) + S_MUL(a4, b2);
				int num32 = S_MUL(a, b4) - S_MUL(a3, b2);
				Fout[num8] = num29 + num31;
				Fout[num8 + 1] = num30 + num32;
				Fout[num9] = num29 - num31;
				Fout[num9 + 1] = num30 - num32;
				num6 += 2;
				num7 += 2;
				num8 += 2;
				num9 += 2;
				num10 += 2;
			}
		}
	}

	internal static void opus_fft_impl(FFTState st, Span<int> fout, int fout_ptr)
	{
		int[] array = new int[8];
		int num = ((st.shift > 0) ? st.shift : 0);
		array[0] = 1;
		int num2 = 0;
		int num4;
		do
		{
			int num3 = st.factors[2 * num2];
			num4 = st.factors[2 * num2 + 1];
			array[num2 + 1] = array[num2] * num3;
			num2++;
		}
		while (num4 != 1);
		num4 = st.factors[2 * num2 - 1];
		for (int num5 = num2 - 1; num5 >= 0; num5--)
		{
			int num6 = ((num5 == 0) ? 1 : st.factors[2 * num5 - 1]);
			switch (st.factors[2 * num5])
			{
			case 2:
				kf_bfly2(fout, fout_ptr, num4, array[num5]);
				break;
			case 4:
				kf_bfly4(fout, fout_ptr, array[num5] << num, st, num4, array[num5], num6);
				break;
			case 3:
				kf_bfly3(fout, fout_ptr, array[num5] << num, st, num4, array[num5], num6);
				break;
			case 5:
				kf_bfly5(fout, fout_ptr, array[num5] << num, st, num4, array[num5], num6);
				break;
			}
			num4 = num6;
		}
	}

	internal static void opus_fft(FFTState st, int[] fin, int[] fout)
	{
		int shift = st.scale_shift - 1;
		short scale = st.scale;
		for (int i = 0; i < st.nfft; i++)
		{
			fout[2 * st.bitrev[i]] = Inlines.SHR32(Inlines.MULT16_32_Q16(scale, fin[2 * i]), shift);
			fout[2 * st.bitrev[i] + 1] = Inlines.SHR32(Inlines.MULT16_32_Q16(scale, fin[2 * i + 1]), shift);
		}
		opus_fft_impl(st, fout, 0);
	}
}
