using System;
using Concentus.Common;

namespace Concentus.Celt;

internal static class CeltLPC
{
	private const int LPC_ORDER = 24;

	internal static void celt_lpc(int[] _lpc, int[] ac, int p)
	{
		int num = ac[0];
		Span<int> span = stackalloc int[24];
		if (ac[0] != 0)
		{
			for (int i = 0; i < p; i++)
			{
				int num2 = 0;
				for (int j = 0; j < i; j++)
				{
					num2 += Inlines.MULT32_32_Q31(span[j], ac[i - j]);
				}
				num2 += Inlines.SHR32(ac[i + 1], 3);
				int num3 = -Inlines.frac_div32(Inlines.SHL32(num2, 3), num);
				span[i] = Inlines.SHR32(num3, 3);
				for (int j = 0; j < i + 1 >> 1; j++)
				{
					int num4 = span[j];
					int num5 = span[i - 1 - j];
					span[j] = num4 + Inlines.MULT32_32_Q31(num3, num5);
					span[i - 1 - j] = num5 + Inlines.MULT32_32_Q31(num3, num4);
				}
				num -= Inlines.MULT32_32_Q31(Inlines.MULT32_32_Q31(num3, num3), num);
				if (num < Inlines.SHR32(ac[0], 10))
				{
					break;
				}
			}
		}
		for (int i = 0; i < p; i++)
		{
			_lpc[i] = Inlines.ROUND16(span[i], 16);
		}
	}

	internal static void celt_iir(Span<int> _x, int[] den, Span<int> _y, int N, int ord, Span<int> mem)
	{
		int[] array = new int[ord];
		int[] array2 = new int[N + ord];
		int i;
		for (i = 0; i < ord; i++)
		{
			array[i] = den[ord - i - 1];
		}
		for (i = 0; i < ord; i++)
		{
			array2[i] = -mem[ord - i - 1];
		}
		for (; i < N + ord; i++)
		{
			array2[i] = 0;
		}
		for (i = 0; i < N - 3; i += 4)
		{
			int sum = _x[i];
			int sum2 = _x[i + 1];
			int sum3 = _x[i + 2];
			int sum4 = _x[i + 3];
			Kernels.xcorr_kernel(array, 0, array2, i, ref sum, ref sum2, ref sum3, ref sum4, ord);
			array2[i + ord] = -Inlines.ROUND16(sum, 12);
			_y[i] = sum;
			sum2 = Inlines.MAC16_16(sum2, array2[i + ord], den[0]);
			array2[i + ord + 1] = -Inlines.ROUND16(sum2, 12);
			_y[i + 1] = sum2;
			sum3 = Inlines.MAC16_16(sum3, array2[i + ord + 1], den[0]);
			sum3 = Inlines.MAC16_16(sum3, array2[i + ord], den[1]);
			array2[i + ord + 2] = -Inlines.ROUND16(sum3, 12);
			_y[i + 2] = sum3;
			sum4 = Inlines.MAC16_16(sum4, array2[i + ord + 2], den[0]);
			sum4 = Inlines.MAC16_16(sum4, array2[i + ord + 1], den[1]);
			sum4 = Inlines.MAC16_16(sum4, array2[i + ord], den[2]);
			array2[i + ord + 3] = -Inlines.ROUND16(sum4, 12);
			_y[i + 3] = sum4;
		}
		for (; i < N; i++)
		{
			int num = _x[i];
			for (int j = 0; j < ord; j++)
			{
				num -= Inlines.MULT16_16(array[j], array2[i + j]);
			}
			array2[i + ord] = Inlines.ROUND16(num, 12);
			_y[i] = num;
		}
		for (i = 0; i < ord; i++)
		{
			mem[i] = _y[N - i - 1];
		}
	}
}
