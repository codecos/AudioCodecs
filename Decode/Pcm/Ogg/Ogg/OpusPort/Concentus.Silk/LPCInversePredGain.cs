using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk;

internal static class LPCInversePredGain
{
	private const float RC_THRESHOLD = 0.9999f;

	private const int QA = 24;

	private static readonly int A_LIMIT = 16773022;

	internal static int LPC_inverse_pred_gain_QA(int[][] A_QA, int order)
	{
		int[] array = A_QA[order & 1];
		int a = 1073741824;
		int num2;
		int num3;
		for (int num = order - 1; num > 0; num--)
		{
			if (array[num] > A_LIMIT || array[num] < -A_LIMIT)
			{
				return 0;
			}
			num2 = -Inlines.silk_LSHIFT(array[num], 7);
			num3 = 1073741824 - Inlines.silk_SMMUL(num2, num2);
			int num4 = 32 - Inlines.silk_CLZ32(Inlines.silk_abs(num3));
			int b = Inlines.silk_INVERSE32_varQ(num3, num4 + 30);
			a = Inlines.silk_LSHIFT(Inlines.silk_SMMUL(a, num3), 2);
			int[] array2 = array;
			array = A_QA[num & 1];
			for (int i = 0; i < num; i++)
			{
				int a2 = array2[i] - Inlines.MUL32_FRAC_Q(array2[num - i - 1], num2, 31);
				array[i] = Inlines.MUL32_FRAC_Q(a2, b, num4);
			}
		}
		if (array[0] > A_LIMIT || array[0] < -A_LIMIT)
		{
			return 0;
		}
		num2 = -Inlines.silk_LSHIFT(array[0], 7);
		num3 = 1073741824 - Inlines.silk_SMMUL(num2, num2);
		return Inlines.silk_LSHIFT(Inlines.silk_SMMUL(a, num3), 2);
	}

	internal static int silk_LPC_inverse_pred_gain(short[] A_Q12, int order)
	{
		int[][] array = Arrays.InitTwoDimensionalArray<int>(2, 16);
		int num = 0;
		int[] array2 = array[order & 1];
		for (int i = 0; i < order; i++)
		{
			num += A_Q12[i];
			array2[i] = Inlines.silk_LSHIFT32(A_Q12[i], 12);
		}
		if (num >= 4096)
		{
			return 0;
		}
		return LPC_inverse_pred_gain_QA(array, order);
	}

	internal static int silk_LPC_inverse_pred_gain_Q24(int[] A_Q24, int order)
	{
		int[][] array = Arrays.InitTwoDimensionalArray<int>(2, 16);
		int[] array2 = array[order & 1];
		for (int i = 0; i < order; i++)
		{
			array2[i] = Inlines.silk_RSHIFT32(A_Q24[i], 0);
		}
		return LPC_inverse_pred_gain_QA(array, order);
	}
}
