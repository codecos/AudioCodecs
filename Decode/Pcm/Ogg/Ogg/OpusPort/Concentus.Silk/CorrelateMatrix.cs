using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk;

internal static class CorrelateMatrix
{
	internal static void silk_corrVector(short[] x, int x_ptr, short[] t, int t_ptr, int L, int order, int[] Xt, int rshifts)
	{
		int num = x_ptr + order - 1;
		if (rshifts > 0)
		{
			for (int i = 0; i < order; i++)
			{
				int num2 = 0;
				for (int j = 0; j < L; j++)
				{
					num2 += Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[num + j], t[t_ptr + j]), rshifts);
				}
				Xt[i] = num2;
				num--;
			}
		}
		else
		{
			for (int i = 0; i < order; i++)
			{
				Xt[i] = Inlines.silk_inner_prod(x, num, t, t_ptr, L);
				num--;
			}
		}
	}

	internal static void silk_corrMatrix(short[] x, int x_ptr, int L, int order, int head_room, int[] XX, int XX_ptr, BoxedValueInt rshifts)
	{
		SumSqrShift.silk_sum_sqr_shift(out var energy, out var shift, x, x_ptr, L + order - 1);
		int num = Inlines.silk_max(head_room - Inlines.silk_CLZ32(energy), 0);
		energy = Inlines.silk_RSHIFT32(energy, num);
		shift += num;
		for (int i = x_ptr; i < x_ptr + order - 1; i++)
		{
			energy -= Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[i], x[i]), shift);
		}
		if (shift < rshifts.Val)
		{
			energy = Inlines.silk_RSHIFT32(energy, rshifts.Val - shift);
			shift = rshifts.Val;
		}
		Inlines.MatrixSet(XX, XX_ptr, 0, 0, order, energy);
		int num2 = x_ptr + order - 1;
		for (int j = 1; j < order; j++)
		{
			energy = Inlines.silk_SUB32(energy, Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[num2 + L - j], x[num2 + L - j]), shift));
			energy = Inlines.silk_ADD32(energy, Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[num2 - j], x[num2 - j]), shift));
			Inlines.MatrixSet(XX, XX_ptr, j, j, order, energy);
		}
		int num3 = x_ptr + order - 2;
		if (shift > 0)
		{
			for (int k = 1; k < order; k++)
			{
				energy = 0;
				for (int i = 0; i < L; i++)
				{
					energy += Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[num2 + i], x[num3 + i]), shift);
				}
				Inlines.MatrixSet(XX, XX_ptr, k, 0, order, energy);
				Inlines.MatrixSet(XX, XX_ptr, 0, k, order, energy);
				for (int j = 1; j < order - k; j++)
				{
					energy = Inlines.silk_SUB32(energy, Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[num2 + L - j], x[num3 + L - j]), shift));
					energy = Inlines.silk_ADD32(energy, Inlines.silk_RSHIFT32(Inlines.silk_SMULBB(x[num2 - j], x[num3 - j]), shift));
					Inlines.MatrixSet(XX, XX_ptr, k + j, j, order, energy);
					Inlines.MatrixSet(XX, XX_ptr, j, k + j, order, energy);
				}
				num3--;
			}
		}
		else
		{
			for (int k = 1; k < order; k++)
			{
				energy = Inlines.silk_inner_prod(x, num2, x, num3, L);
				Inlines.MatrixSet(XX, XX_ptr, k, 0, order, energy);
				Inlines.MatrixSet(XX, XX_ptr, 0, k, order, energy);
				for (int j = 1; j < order - k; j++)
				{
					energy = Inlines.silk_SUB32(energy, Inlines.silk_SMULBB(x[num2 + L - j], x[num3 + L - j]));
					energy = Inlines.silk_SMLABB(energy, x[num2 - j], x[num3 - j]);
					Inlines.MatrixSet(XX, XX_ptr, k + j, j, order, energy);
					Inlines.MatrixSet(XX, XX_ptr, j, k + j, order, energy);
				}
				num3--;
			}
		}
		rshifts.Val = shift;
	}
}
