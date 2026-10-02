using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk;

internal static class FindLTP
{
	private const int LTP_CORRS_HEAD_ROOM = 2;

	/// <summary>
	/// Finds linear prediction coeffecients and weights
	/// </summary>
	/// <param name="b_Q14"></param>
	/// <param name="WLTP"></param>
	/// <param name="LTPredCodGain_Q7"></param>
	/// <param name="r_lpc"></param>
	/// <param name="lag"></param>
	/// <param name="Wght_Q15"></param>
	/// <param name="subfr_length"></param>
	/// <param name="nb_subfr"></param>
	/// <param name="mem_offset"></param>
	/// <param name="corr_rshifts"></param>
	internal static void silk_find_LTP(short[] b_Q14, int[] WLTP, BoxedValueInt LTPredCodGain_Q7, short[] r_lpc, int[] lag, int[] Wght_Q15, int subfr_length, int nb_subfr, int mem_offset, int[] corr_rshifts)
	{
		int[] array = new int[5];
		int[] array2 = new int[5];
		int[] array3 = new int[4];
		int[] array4 = new int[4];
		int[] array5 = new int[4];
		int[] array6 = new int[5];
		int[] array7 = new int[4];
		int num = 0;
		int num2 = 0;
		int num3 = mem_offset;
		int num5;
		int a3;
		for (int i = 0; i < nb_subfr; i++)
		{
			int x_ptr = num3 - (lag[i] + 2);
			SumSqrShift.silk_sum_sqr_shift(out array7[i], out var shift, r_lpc, num3, subfr_length);
			int num4 = Inlines.silk_CLZ32(array7[i]);
			if (num4 < 2)
			{
				array7[i] = Inlines.silk_RSHIFT_ROUND(array7[i], 2 - num4);
				shift += 2 - num4;
			}
			corr_rshifts[i] = shift;
			BoxedValueInt boxedValueInt = new BoxedValueInt(corr_rshifts[i]);
			CorrelateMatrix.silk_corrMatrix(r_lpc, x_ptr, subfr_length, 5, 2, WLTP, num2, boxedValueInt);
			corr_rshifts[i] = boxedValueInt.Val;
			CorrelateMatrix.silk_corrVector(r_lpc, x_ptr, r_lpc, num3, subfr_length, 5, array6, corr_rshifts[i]);
			if (corr_rshifts[i] > shift)
			{
				array7[i] = Inlines.silk_RSHIFT(array7[i], corr_rshifts[i] - shift);
			}
			int a = 1;
			a = Inlines.silk_SMLAWB(a, array7[i], 1092);
			a = Inlines.silk_SMLAWB(a, Inlines.MatrixGet(WLTP, num2, 0, 0, 5), 1092);
			a = Inlines.silk_SMLAWB(a, Inlines.MatrixGet(WLTP, num2, 4, 4, 5), 1092);
			RegularizeCorrelations.silk_regularize_correlations(WLTP, num2, array7, i, a, 5);
			LinearAlgebra.silk_solve_LDL(WLTP, num2, 5, array6, array);
			silk_fit_LTP(array, b_Q14, num);
			array4[i] = ResidualEnergy.silk_residual_energy16_covar(b_Q14, num, WLTP, num2, array6, array7[i], 5, 14);
			num5 = Inlines.silk_min_int(corr_rshifts[i], 2);
			int a2 = Inlines.silk_LSHIFT_SAT32(Inlines.silk_SMULWB(array4[i], Wght_Q15[i]), 1 + num5) + Inlines.silk_RSHIFT(Inlines.silk_SMULWB(subfr_length, 655), corr_rshifts[i] - num5);
			a2 = Inlines.silk_max(a2, 1);
			a3 = Inlines.silk_DIV32(Inlines.silk_LSHIFT(Wght_Q15[i], 16), a2);
			a3 = Inlines.silk_RSHIFT(a3, 31 + corr_rshifts[i] - num5 - 26);
			int num6 = 0;
			for (int j = num2; j < num2 + 25; j++)
			{
				num6 = Inlines.silk_max(WLTP[j], num6);
			}
			int num7 = Inlines.silk_CLZ32(num6) - 1 - 3;
			if (8 + num7 < 31)
			{
				a3 = Inlines.silk_min_32(a3, Inlines.silk_LSHIFT(1, 8 + num7));
			}
			Inlines.silk_scale_vector32_Q26_lshift_18(WLTP, num2, a3, 25);
			array5[i] = Inlines.MatrixGet(WLTP, num2, 2, 2, 5);
			num3 += subfr_length;
			num += 5;
			num2 += 25;
		}
		int num8 = 0;
		for (int i = 0; i < nb_subfr; i++)
		{
			num8 = Inlines.silk_max_int(corr_rshifts[i], num8);
		}
		if (LTPredCodGain_Q7 != null)
		{
			int a4 = 0;
			int num9 = 0;
			for (int i = 0; i < nb_subfr; i++)
			{
				num9 = Inlines.silk_ADD32(num9, Inlines.silk_RSHIFT(Inlines.silk_ADD32(Inlines.silk_SMULWB(array7[i], Wght_Q15[i]), 1), 1 + (num8 - corr_rshifts[i])));
				a4 = Inlines.silk_ADD32(a4, Inlines.silk_RSHIFT(Inlines.silk_ADD32(Inlines.silk_SMULWB(array4[i], Wght_Q15[i]), 1), 1 + (num8 - corr_rshifts[i])));
			}
			a4 = Inlines.silk_max(a4, 1);
			int inLin = Inlines.silk_DIV32_varQ(num9, a4, 16);
			LTPredCodGain_Q7.Val = Inlines.silk_SMULBB(3, Inlines.silk_lin2log(inLin) - 2048);
		}
		num = 0;
		for (int i = 0; i < nb_subfr; i++)
		{
			array3[i] = 0;
			for (int j = num; j < num + 5; j++)
			{
				array3[i] += b_Q14[j];
			}
			num += 5;
		}
		int num10 = 0;
		int num11 = 0;
		for (int i = 0; i < nb_subfr; i++)
		{
			num10 = Inlines.silk_max_32(num10, Inlines.silk_abs(array3[i]));
			num11 = Inlines.silk_max_32(num11, 32 - Inlines.silk_CLZ32(array5[i]) + corr_rshifts[i] - num8);
		}
		num5 = num11 + 32 - Inlines.silk_CLZ32(num10) - 14;
		num5 -= 29 + num8;
		num5 = Inlines.silk_max_int(num5, 0);
		int num12 = num8 + num5;
		a3 = Inlines.silk_RSHIFT(262, num8 + num5) + 1;
		int num13 = 0;
		for (int i = 0; i < nb_subfr; i++)
		{
			a3 = Inlines.silk_ADD32(a3, Inlines.silk_RSHIFT(array5[i], num12 - corr_rshifts[i]));
			num13 = Inlines.silk_ADD32(num13, Inlines.silk_LSHIFT(Inlines.silk_SMULWW(Inlines.silk_RSHIFT(array5[i], num12 - corr_rshifts[i]), array3[i]), 2));
		}
		int a5 = Inlines.silk_DIV32_varQ(num13, a3, 12);
		num = 0;
		for (int i = 0; i < nb_subfr; i++)
		{
			a3 = ((2 - corr_rshifts[i] <= 0) ? Inlines.silk_LSHIFT_SAT32(array5[i], corr_rshifts[i] - 2) : Inlines.silk_RSHIFT(array5[i], 2 - corr_rshifts[i]));
			int a6 = Inlines.silk_MUL(Inlines.silk_DIV32(6710887, Inlines.silk_RSHIFT(6710887, 10) + a3), Inlines.silk_LSHIFT_SAT32(Inlines.silk_SUB_SAT32(a5, Inlines.silk_RSHIFT(array3[i], 2)), 4));
			a3 = 0;
			for (int j = 0; j < 5; j++)
			{
				array2[j] = Inlines.silk_max_16(b_Q14[num + j], 1638);
				a3 += array2[j];
			}
			a3 = Inlines.silk_DIV32(a6, a3);
			for (int j = 0; j < 5; j++)
			{
				b_Q14[num + j] = (short)Inlines.silk_LIMIT_32(b_Q14[num + j] + Inlines.silk_SMULWB(Inlines.silk_LSHIFT_SAT32(a3, 4), array2[j]), -16000, 28000);
			}
			num += 5;
		}
	}

	/// <summary>
	///
	/// </summary>
	/// <param name="LTP_coefs_Q16">[SilkConstants.LTP_ORDER]</param>
	/// <param name="LTP_coefs_Q14">[SilkConstants.LTP_ORDER]</param>
	/// <param name="LTP_coefs_Q14_ptr"></param>
	internal static void silk_fit_LTP(int[] LTP_coefs_Q16, Span<short> LTP_coefs_Q14, int LTP_coefs_Q14_ptr)
	{
		for (int i = 0; i < 5; i++)
		{
			LTP_coefs_Q14[LTP_coefs_Q14_ptr + i] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(LTP_coefs_Q16[i], 2));
		}
	}
}
