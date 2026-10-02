using Concentus.Common;

namespace Concentus.Silk;

internal static class ResidualEnergy
{
	internal static void silk_residual_energy(int[] nrgs, int[] nrgsQ, short[] x, short[][] a_Q12, int[] gains, int subfr_length, int nb_subfr, int LPC_order)
	{
		int num = 0;
		int num2 = LPC_order + subfr_length;
		short[] array = new short[2 * num2];
		for (int i = 0; i < nb_subfr >> 1; i++)
		{
			Filters.silk_LPC_analysis_filter(array, 0, x, num, a_Q12[i], 0, 2 * num2, LPC_order);
			int num3 = LPC_order;
			for (int j = 0; j < 2; j++)
			{
				SumSqrShift.silk_sum_sqr_shift(out var energy, out var shift, array, num3, subfr_length);
				nrgs[i * 2 + j] = energy;
				nrgsQ[i * 2 + j] = -shift;
				num3 += num2;
			}
			num += 2 * num2;
		}
		for (int i = 0; i < nb_subfr; i++)
		{
			int num4 = Inlines.silk_CLZ32(nrgs[i]) - 1;
			int num5 = Inlines.silk_CLZ32(gains[i]) - 1;
			int num6 = Inlines.silk_LSHIFT32(gains[i], num5);
			num6 = Inlines.silk_SMMUL(num6, num6);
			nrgs[i] = Inlines.silk_SMMUL(num6, Inlines.silk_LSHIFT32(nrgs[i], num4));
			nrgsQ[i] += num4 + 2 * num5 - 32 - 32;
		}
	}

	internal static int silk_residual_energy16_covar(short[] c, int c_ptr, int[] wXX, int wXX_ptr, int[] wXx, int wxx, int D, int cQ)
	{
		int[] array = new int[D];
		int num = 16 - cQ;
		int a = num;
		int num2 = 0;
		for (int i = c_ptr; i < c_ptr + D; i++)
		{
			num2 = Inlines.silk_max_32(num2, Inlines.silk_abs(c[i]));
		}
		a = Inlines.silk_min_int(a, Inlines.silk_CLZ32(num2) - 17);
		int a2 = Inlines.silk_max_32(wXX[wXX_ptr], wXX[wXX_ptr + D * D - 1]);
		a = Inlines.silk_min_int(a, Inlines.silk_CLZ32(Inlines.silk_MUL(D, Inlines.silk_RSHIFT(Inlines.silk_SMULWB(a2, num2), 4))) - 5);
		a = Inlines.silk_max_int(a, 0);
		for (int i = 0; i < D; i++)
		{
			array[i] = Inlines.silk_LSHIFT(c[c_ptr + i], a);
		}
		num -= a;
		int num3 = 0;
		for (int i = 0; i < D; i++)
		{
			num3 = Inlines.silk_SMLAWB(num3, wXx[i], array[i]);
		}
		int a3 = Inlines.silk_RSHIFT(wxx, 1 + num) - num3;
		int num4 = 0;
		for (int i = 0; i < D; i++)
		{
			num3 = 0;
			int num5 = wXX_ptr + i * D;
			for (int j = i + 1; j < D; j++)
			{
				num3 = Inlines.silk_SMLAWB(num3, wXX[num5 + j], array[j]);
			}
			num3 = Inlines.silk_SMLAWB(num3, Inlines.silk_RSHIFT(wXX[num5 + i], 1), array[i]);
			num4 = Inlines.silk_SMLAWB(num4, num3, array[i]);
		}
		a3 = Inlines.silk_ADD_LSHIFT32(a3, num4, num);
		if (a3 < 1)
		{
			return 1;
		}
		if (a3 > Inlines.silk_RSHIFT(int.MaxValue, num + 2))
		{
			return 1073741823;
		}
		return Inlines.silk_LSHIFT(a3, num + 1);
	}
}
