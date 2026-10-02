using Concentus.Celt;
using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk;

internal static class PitchAnalysisCore
{
	private class silk_pe_stage3_vals
	{
		internal readonly int[] Values = new int[5];
	}

	private const int SCRATCH_SIZE = 22;

	private const int SF_LENGTH_4KHZ = 20;

	private const int SF_LENGTH_8KHZ = 40;

	private const int MIN_LAG_4KHZ = 8;

	private const int MIN_LAG_8KHZ = 16;

	private const int MAX_LAG_4KHZ = 72;

	private const int MAX_LAG_8KHZ = 143;

	private const int CSTRIDE_4KHZ = 65;

	private const int CSTRIDE_8KHZ = 132;

	private const int D_COMP_MIN = 13;

	private const int D_COMP_MAX = 147;

	private const int D_COMP_STRIDE = 134;

	internal static int silk_pitch_analysis_core(short[] frame, int[] pitch_out, BoxedValueShort lagIndex, BoxedValueSbyte contourIndex, BoxedValueInt LTPCorr_Q15, int prevLag, int search_thres1_Q16, int search_thres2_Q13, int Fs_kHz, int complexity, int nb_subfr)
	{
		int[] array = new int[6];
		int[] array2 = new int[24];
		int[] array3 = new int[11];
		int num = (20 + nb_subfr * 5) * Fs_kHz;
		int num2 = (20 + nb_subfr * 5) * 4;
		int num3 = (20 + nb_subfr * 5) * 8;
		int num4 = 5 * Fs_kHz;
		int num5 = 2 * Fs_kHz;
		int num6 = 18 * Fs_kHz - 1;
		short[] array4 = new short[num3];
		switch (Fs_kHz)
		{
		case 16:
			Arrays.MemSetInt(array, 0, 2);
			Resampler.silk_resampler_down2(array, array4, frame, num);
			break;
		case 12:
			Arrays.MemSetInt(array, 0, 6);
			Resampler.silk_resampler_down2_3(array, array4, frame, num);
			break;
		default:
			Arrays.MemCopy(frame, 0, array4, 0, num3);
			break;
		}
		Arrays.MemSetInt(array, 0, 2);
		short[] array5 = new short[num2];
		Resampler.silk_resampler_down2(array, array5, array4, num3);
		for (int num7 = num2 - 1; num7 > 0; num7--)
		{
			array5[num7] = Inlines.silk_ADD_SAT16(array5[num7], array5[num7 - 1]);
		}
		SumSqrShift.silk_sum_sqr_shift(out var energy, out var shift, array5, num2);
		if (shift > 0)
		{
			shift = Inlines.silk_RSHIFT(shift, 1);
			for (int num7 = 0; num7 < num2; num7++)
			{
				array5[num7] = Inlines.silk_RSHIFT16(array5[num7], shift);
			}
		}
		short[] array6 = new short[nb_subfr * 132];
		int[] array7 = new int[65];
		Arrays.MemSetShort(array6, 0, (nb_subfr >> 1) * 65);
		short[] array8 = array5;
		int num8 = Inlines.silk_LSHIFT(20, 2);
		for (int i = 0; i < nb_subfr >> 1; i++)
		{
			short[] array9 = array8;
			int num9 = num8 - 8;
			CeltPitchXCorr.pitch_xcorr(array8, num8, array8, num8 - 72, array7, 40, 65);
			int a = array7[64];
			int a2 = Inlines.silk_inner_prod_self(array8, num8, 40);
			a2 = Inlines.silk_ADD32(a2, Inlines.silk_inner_prod_self(array9, num9, 40));
			a2 = Inlines.silk_ADD32(a2, Inlines.silk_SMULBB(40, 4000));
			Inlines.MatrixSet(array6, i, 0, 65, (short)Inlines.silk_DIV32_varQ(a, a2, 14));
			for (int j = 9; j <= 72; j++)
			{
				num9--;
				a = array7[72 - j];
				a2 = Inlines.silk_ADD32(a2, Inlines.silk_SMULBB(array9[num9], array9[num9]) - Inlines.silk_SMULBB(array9[num9 + 40], array9[num9 + 40]));
				Inlines.MatrixSet(array6, i, j - 8, 65, (short)Inlines.silk_DIV32_varQ(a, a2, 14));
			}
			num8 += 40;
		}
		if (nb_subfr == 4)
		{
			for (int num7 = 72; num7 >= 8; num7--)
			{
				int num10 = Inlines.MatrixGet(array6, 0, num7 - 8, 65) + Inlines.MatrixGet(array6, 1, num7 - 8, 65);
				num10 = Inlines.silk_SMLAWB(num10, num10, Inlines.silk_LSHIFT(-num7, 4));
				array6[num7 - 8] = (short)num10;
			}
		}
		else
		{
			for (int num7 = 72; num7 >= 8; num7--)
			{
				int num10 = Inlines.silk_LSHIFT(array6[num7 - 8], 1);
				num10 = Inlines.silk_SMLAWB(num10, num10, Inlines.silk_LSHIFT(-num7, 4));
				array6[num7 - 8] = (short)num10;
			}
		}
		int num11 = Inlines.silk_ADD_LSHIFT32(4, complexity, 1);
		Sort.silk_insertion_sort_decreasing_int16(array6, array2, 65, num11);
		int num12 = array6[0];
		if (num12 < 3277)
		{
			Arrays.MemSetInt(pitch_out, 0, nb_subfr);
			LTPCorr_Q15.Val = 0;
			lagIndex.Val = 0;
			contourIndex.Val = 0;
			return 1;
		}
		int num13 = Inlines.silk_SMULWB(search_thres1_Q16, num12);
		for (int num7 = 0; num7 < num11; num7++)
		{
			if (array6[num7] > num13)
			{
				array2[num7] = Inlines.silk_LSHIFT(array2[num7] + 8, 1);
				continue;
			}
			num11 = num7;
			break;
		}
		short[] array10 = new short[134];
		for (int num7 = 13; num7 < 147; num7++)
		{
			array10[num7 - 13] = 0;
		}
		for (int num7 = 0; num7 < num11; num7++)
		{
			array10[array2[num7] - 13] = 1;
		}
		for (int num7 = 146; num7 >= 16; num7--)
		{
			array10[num7 - 13] += (short)(array10[num7 - 1 - 13] + array10[num7 - 2 - 13]);
		}
		num11 = 0;
		for (int num7 = 16; num7 < 144; num7++)
		{
			if (array10[num7 + 1 - 13] > 0)
			{
				array2[num11] = num7;
				num11++;
			}
		}
		for (int num7 = 146; num7 >= 16; num7--)
		{
			array10[num7 - 13] += (short)(array10[num7 - 1 - 13] + array10[num7 - 2 - 13] + array10[num7 - 3 - 13]);
		}
		int num14 = 0;
		for (int num7 = 16; num7 < 147; num7++)
		{
			if (array10[num7 - 13] > 0)
			{
				array10[num14] = (short)(num7 - 2);
				num14++;
			}
		}
		SumSqrShift.silk_sum_sqr_shift(out energy, out shift, array4, num3);
		if (shift > 0)
		{
			shift = Inlines.silk_RSHIFT(shift, 1);
			for (int num7 = 0; num7 < num3; num7++)
			{
				array4[num7] = Inlines.silk_RSHIFT16(array4[num7], shift);
			}
		}
		Arrays.MemSetShort(array6, 0, nb_subfr * 132);
		array8 = array4;
		num8 = 160;
		for (int i = 0; i < nb_subfr; i++)
		{
			int a3 = Inlines.silk_ADD32(Inlines.silk_inner_prod(array8, num8, array8, num8, 40), 1);
			for (int k = 0; k < num14; k++)
			{
				int j = array10[k];
				short[] array9 = array8;
				int num9 = num8 - j;
				int a = Inlines.silk_inner_prod(array8, num8, array9, num9, 40);
				if (a > 0)
				{
					int b = Inlines.silk_inner_prod_self(array9, num9, 40);
					Inlines.MatrixSet(array6, i, j - 14, 132, (short)Inlines.silk_DIV32_varQ(a, Inlines.silk_ADD32(a3, b), 14));
				}
				else
				{
					Inlines.MatrixSet(array6, i, j - 14, 132, (short)0);
				}
			}
			num8 += 40;
		}
		int a4 = int.MinValue;
		int num15 = int.MinValue;
		int num16 = 0;
		int num17 = -1;
		int num18;
		if (prevLag > 0)
		{
			switch (Fs_kHz)
			{
			case 12:
				prevLag = Inlines.silk_DIV32_16(Inlines.silk_LSHIFT(prevLag, 1), 3);
				break;
			case 16:
				prevLag = Inlines.silk_RSHIFT(prevLag, 1);
				break;
			}
			num18 = Inlines.silk_lin2log(prevLag);
		}
		else
		{
			num18 = 0;
		}
		int num19;
		sbyte[][] array11;
		if (nb_subfr == 4)
		{
			array11 = Tables.silk_CB_lags_stage2;
			num19 = ((Fs_kHz != 8 || complexity <= 0) ? 3 : 11);
		}
		else
		{
			array11 = Tables.silk_CB_lags_stage2_10_ms;
			num19 = 3;
		}
		for (int i = 0; i < num11; i++)
		{
			int j = array2[i];
			for (int k = 0; k < num19; k++)
			{
				array3[k] = 0;
				for (int num7 = 0; num7 < nb_subfr; num7++)
				{
					int num20 = j + array11[num7][k];
					array3[k] += Inlines.MatrixGet(array6, num7, num20 - 14, 132);
				}
			}
			int num21 = int.MinValue;
			int num22 = 0;
			for (int num7 = 0; num7 < num19; num7++)
			{
				if (array3[num7] > num21)
				{
					num21 = array3[num7];
					num22 = num7;
				}
			}
			int num23 = Inlines.silk_lin2log(j);
			int num24 = num21 - Inlines.silk_RSHIFT(Inlines.silk_SMULBB(nb_subfr * 1638, num23), 7);
			if (prevLag > 0)
			{
				int num25 = num23 - num18;
				num25 = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(num25, num25), 7);
				int a5 = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(nb_subfr * 1638, LTPCorr_Q15.Val), 15);
				a5 = Inlines.silk_DIV32(Inlines.silk_MUL(a5, num25), num25 + 64);
				num24 -= a5;
			}
			if (num24 > num15 && num21 > Inlines.silk_SMULBB(nb_subfr, search_thres2_Q13) && Tables.silk_CB_lags_stage2[0][num22] <= 16)
			{
				num15 = num24;
				a4 = num21;
				num17 = j;
				num16 = num22;
			}
		}
		if (num17 == -1)
		{
			Arrays.MemSetInt(pitch_out, 0, nb_subfr);
			LTPCorr_Q15.Val = 0;
			lagIndex.Val = 0;
			contourIndex.Val = 0;
			return 1;
		}
		LTPCorr_Q15.Val = Inlines.silk_LSHIFT(Inlines.silk_DIV32_16(a4, nb_subfr), 2);
		if (Fs_kHz > 8)
		{
			SumSqrShift.silk_sum_sqr_shift(out energy, out shift, frame, num);
			short[] array13;
			if (shift > 0)
			{
				short[] array12 = new short[num];
				shift = Inlines.silk_RSHIFT(shift, 1);
				for (int num7 = 0; num7 < num; num7++)
				{
					array12[num7] = Inlines.silk_RSHIFT16(frame[num7], shift);
				}
				array13 = array12;
			}
			else
			{
				array13 = frame;
			}
			int num26 = num16;
			num17 = Inlines.silk_LIMIT_int(Fs_kHz switch
			{
				12 => Inlines.silk_RSHIFT(Inlines.silk_SMULBB(num17, 3), 1), 
				16 => Inlines.silk_LSHIFT(num17, 1), 
				_ => Inlines.silk_SMULBB(num17, 3), 
			}, num5, num6);
			int num27 = Inlines.silk_max_int(num17 - 2, num5);
			int num28 = Inlines.silk_min_int(num17 + 2, num6);
			int num29 = num17;
			num16 = 0;
			a4 = int.MinValue;
			for (int i = 0; i < nb_subfr; i++)
			{
				pitch_out[i] = num17 + 2 * Tables.silk_CB_lags_stage2[i][num26];
			}
			if (nb_subfr == 4)
			{
				num19 = Tables.silk_nb_cbk_searchs_stage3[complexity];
				array11 = Tables.silk_CB_lags_stage3;
			}
			else
			{
				num19 = 12;
				array11 = Tables.silk_CB_lags_stage3_10_ms;
			}
			silk_pe_stage3_vals[] array14 = new silk_pe_stage3_vals[nb_subfr * num19];
			silk_pe_stage3_vals[] array15 = new silk_pe_stage3_vals[nb_subfr * num19];
			for (int l = 0; l < nb_subfr * num19; l++)
			{
				array14[l] = new silk_pe_stage3_vals();
				array15[l] = new silk_pe_stage3_vals();
			}
			silk_P_Ana_calc_corr_st3(array15, array13, num27, num4, nb_subfr, complexity);
			silk_P_Ana_calc_energy_st3(array14, array13, num27, num4, nb_subfr, complexity);
			int num30 = 0;
			int a6 = Inlines.silk_DIV32_16(1638, num17);
			array8 = array13;
			num8 = 20 * Fs_kHz;
			int a3 = Inlines.silk_ADD32(Inlines.silk_inner_prod_self(array8, num8, nb_subfr * num4), 1);
			for (int j = num27; j <= num28; j++)
			{
				for (int k = 0; k < num19; k++)
				{
					int a = 0;
					energy = a3;
					for (int i = 0; i < nb_subfr; i++)
					{
						a = Inlines.silk_ADD32(a, Inlines.MatrixGet(array15, i, k, num19).Values[num30]);
						energy = Inlines.silk_ADD32(energy, Inlines.MatrixGet(array14, i, k, num19).Values[num30]);
					}
					int num21;
					if (a > 0)
					{
						num21 = Inlines.silk_DIV32_varQ(a, energy, 14);
						int b2 = 32767 - Inlines.silk_MUL(a6, k);
						num21 = Inlines.silk_SMULWB(num21, b2);
					}
					else
					{
						num21 = 0;
					}
					if (num21 > a4 && j + Tables.silk_CB_lags_stage3[0][k] <= num6)
					{
						a4 = num21;
						num29 = j;
						num16 = k;
					}
				}
				num30++;
			}
			for (int i = 0; i < nb_subfr; i++)
			{
				pitch_out[i] = num29 + array11[i][num16];
				pitch_out[i] = Inlines.silk_LIMIT(pitch_out[i], num5, 18 * Fs_kHz);
			}
			lagIndex.Val = (short)(num29 - num5);
			contourIndex.Val = (sbyte)num16;
		}
		else
		{
			for (int i = 0; i < nb_subfr; i++)
			{
				pitch_out[i] = num17 + array11[i][num16];
				pitch_out[i] = Inlines.silk_LIMIT(pitch_out[i], 16, 144);
			}
			lagIndex.Val = (short)(num17 - 16);
			contourIndex.Val = (sbyte)num16;
		}
		return 0;
	}

	private static void silk_P_Ana_calc_corr_st3(silk_pe_stage3_vals[] cross_corr_st3, short[] frame, int start_lag, int sf_length, int nb_subfr, int complexity)
	{
		sbyte[][] array;
		sbyte[][] array2;
		int num;
		if (nb_subfr == 4)
		{
			array = Tables.silk_Lag_range_stage3[complexity];
			array2 = Tables.silk_CB_lags_stage3;
			num = Tables.silk_nb_cbk_searchs_stage3[complexity];
		}
		else
		{
			array = Tables.silk_Lag_range_stage3_10_ms;
			array2 = Tables.silk_CB_lags_stage3_10_ms;
			num = 12;
		}
		int[] array3 = new int[22];
		int[] array4 = new int[22];
		int num2 = Inlines.silk_LSHIFT(sf_length, 2);
		for (int i = 0; i < nb_subfr; i++)
		{
			int num3 = 0;
			int num4 = array[i][0];
			int num5 = array[i][1];
			CeltPitchXCorr.pitch_xcorr(frame, num2, frame, num2 - start_lag - num5, array4, sf_length, num5 - num4 + 1);
			for (int j = num4; j <= num5; j++)
			{
				array3[num3] = array4[num5 - j];
				num3++;
			}
			int num6 = array[i][0];
			for (int k = 0; k < num; k++)
			{
				int num7 = array2[i][k] - num6;
				for (int j = 0; j < 5; j++)
				{
					Inlines.MatrixGet(cross_corr_st3, i, k, num).Values[j] = array3[num7 + j];
				}
			}
			num2 += sf_length;
		}
	}

	private static void silk_P_Ana_calc_energy_st3(silk_pe_stage3_vals[] energies_st3, short[] frame, int start_lag, int sf_length, int nb_subfr, int complexity)
	{
		sbyte[][] array;
		sbyte[][] array2;
		int num;
		if (nb_subfr == 4)
		{
			array = Tables.silk_Lag_range_stage3[complexity];
			array2 = Tables.silk_CB_lags_stage3;
			num = Tables.silk_nb_cbk_searchs_stage3[complexity];
		}
		else
		{
			array = Tables.silk_Lag_range_stage3_10_ms;
			array2 = Tables.silk_CB_lags_stage3_10_ms;
			num = 12;
		}
		int[] array3 = new int[22];
		int num2 = Inlines.silk_LSHIFT(sf_length, 2);
		for (int i = 0; i < nb_subfr; i++)
		{
			int num3 = 0;
			int num4 = num2 - (start_lag + array[i][0]);
			int num5 = (array3[num3] = Inlines.silk_inner_prod_self(frame, num4, sf_length));
			num3++;
			int num6 = array[i][1] - array[i][0] + 1;
			for (int j = 1; j < num6; j++)
			{
				num5 -= Inlines.silk_SMULBB(frame[num4 + sf_length - j], frame[num4 + sf_length - j]);
				num5 = (array3[num3] = Inlines.silk_ADD_SAT32(num5, Inlines.silk_SMULBB(frame[num4 - j], frame[num4 - j])));
				num3++;
			}
			int num7 = array[i][0];
			for (int j = 0; j < num; j++)
			{
				int num8 = array2[i][j] - num7;
				for (int k = 0; k < 5; k++)
				{
					Inlines.MatrixGet(energies_st3, i, j, num).Values[k] = array3[num8 + k];
				}
			}
			num2 += sf_length;
		}
	}
}
