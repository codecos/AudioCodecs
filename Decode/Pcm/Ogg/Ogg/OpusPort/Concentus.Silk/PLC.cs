using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

/// <summary>
/// Routines for managing packet loss concealment
/// </summary>
internal static class PLC
{
	private const int NB_ATT = 2;

	private static readonly short[] HARM_ATT_Q15 = new short[2] { 32440, 31130 };

	private static readonly short[] PLC_RAND_ATTENUATE_V_Q15 = new short[2] { 31130, 26214 };

	private static readonly short[] PLC_RAND_ATTENUATE_UV_Q15 = new short[2] { 32440, 29491 };

	internal static void silk_PLC_Reset(SilkChannelDecoder psDec)
	{
		psDec.sPLC.pitchL_Q8 = Inlines.silk_LSHIFT(psDec.frame_length, 7);
		psDec.sPLC.prevGain_Q16[0] = 65536;
		psDec.sPLC.prevGain_Q16[1] = 65536;
		psDec.sPLC.subfr_length = 20;
		psDec.sPLC.nb_subfr = 2;
	}

	internal static void silk_PLC(SilkChannelDecoder psDec, SilkDecoderControl psDecCtrl, Span<short> frame, int frame_ptr, int lost)
	{
		if (psDec.fs_kHz != psDec.sPLC.fs_kHz)
		{
			silk_PLC_Reset(psDec);
			psDec.sPLC.fs_kHz = psDec.fs_kHz;
		}
		if (lost != 0)
		{
			silk_PLC_conceal(psDec, psDecCtrl, frame, frame_ptr);
			psDec.lossCnt++;
		}
		else
		{
			silk_PLC_update(psDec, psDecCtrl);
		}
	}

	internal static void silk_PLC_update(SilkChannelDecoder psDec, SilkDecoderControl psDecCtrl)
	{
		PLCStruct sPLC = psDec.sPLC;
		psDec.prevSignalType = psDec.indices.signalType;
		int num = 0;
		if (psDec.indices.signalType == 2)
		{
			for (int i = 0; i * psDec.subfr_length < psDecCtrl.pitchL[psDec.nb_subfr - 1] && i != psDec.nb_subfr; i++)
			{
				int num2 = 0;
				for (int j = 0; j < 5; j++)
				{
					num2 += psDecCtrl.LTPCoef_Q14[(psDec.nb_subfr - 1 - i) * 5 + j];
				}
				if (num2 > num)
				{
					num = num2;
					Arrays.MemCopy(psDecCtrl.LTPCoef_Q14, Inlines.silk_SMULBB(psDec.nb_subfr - 1 - i, 5), sPLC.LTPCoef_Q14, 0, 5);
					sPLC.pitchL_Q8 = Inlines.silk_LSHIFT(psDecCtrl.pitchL[psDec.nb_subfr - 1 - i], 8);
				}
			}
			Arrays.MemSetShort(sPLC.LTPCoef_Q14, 0, 5);
			sPLC.LTPCoef_Q14[2] = (short)num;
			if (num < 11469)
			{
				int b = Inlines.silk_DIV32(Inlines.silk_LSHIFT(11469, 10), Inlines.silk_max(num, 1));
				for (int j = 0; j < 5; j++)
				{
					sPLC.LTPCoef_Q14[j] = (short)Inlines.silk_RSHIFT(Inlines.silk_SMULBB(sPLC.LTPCoef_Q14[j], b), 10);
				}
			}
			else if (num > 15565)
			{
				int b2 = Inlines.silk_DIV32(Inlines.silk_LSHIFT(15565, 14), Inlines.silk_max(num, 1));
				for (int j = 0; j < 5; j++)
				{
					sPLC.LTPCoef_Q14[j] = (short)Inlines.silk_RSHIFT(Inlines.silk_SMULBB(sPLC.LTPCoef_Q14[j], b2), 14);
				}
			}
		}
		else
		{
			sPLC.pitchL_Q8 = Inlines.silk_LSHIFT(Inlines.silk_SMULBB(psDec.fs_kHz, 18), 8);
			Arrays.MemSetShort(sPLC.LTPCoef_Q14, 0, 5);
		}
		Arrays.MemCopy(psDecCtrl.PredCoef_Q12[1], 0, sPLC.prevLPC_Q12, 0, psDec.LPC_order);
		sPLC.prevLTP_scale_Q14 = (short)psDecCtrl.LTP_scale_Q14;
		Arrays.MemCopy(psDecCtrl.Gains_Q16, psDec.nb_subfr - 2, sPLC.prevGain_Q16, 0, 2);
		sPLC.subfr_length = psDec.subfr_length;
		sPLC.nb_subfr = psDec.nb_subfr;
	}

	/// <summary>
	///
	/// </summary>
	/// <param name="energy1">O</param>
	/// <param name="shift1">O</param>
	/// <param name="energy2">O</param>
	/// <param name="shift2">O</param>
	/// <param name="exc_Q14">I</param>
	/// <param name="prevGain_Q10">I</param>
	/// <param name="subfr_length">I</param>
	/// <param name="nb_subfr">I</param>
	internal static void silk_PLC_energy(out int energy1, out int shift1, out int energy2, out int shift2, int[] exc_Q14, int[] prevGain_Q10, int subfr_length, int nb_subfr)
	{
		int num = 0;
		short[] array = new short[2 * subfr_length];
		for (int i = 0; i < 2; i++)
		{
			for (int j = 0; j < subfr_length; j++)
			{
				array[num + j] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT(Inlines.silk_SMULWW(exc_Q14[j + (i + nb_subfr - 2) * subfr_length], prevGain_Q10[i]), 8));
			}
			num += subfr_length;
		}
		SumSqrShift.silk_sum_sqr_shift(out energy1, out shift1, array, subfr_length);
		SumSqrShift.silk_sum_sqr_shift(out energy2, out shift2, array, subfr_length, subfr_length);
	}

	internal static void silk_PLC_conceal(SilkChannelDecoder psDec, SilkDecoderControl psDecCtrl, Span<short> frame, int frame_ptr)
	{
		short[] array = new short[psDec.ltp_mem_length];
		int[] array2 = new int[psDec.ltp_mem_length + psDec.frame_length];
		PLCStruct sPLC = psDec.sPLC;
		int[] array3 = new int[2]
		{
			Inlines.silk_RSHIFT(sPLC.prevGain_Q16[0], 6),
			Inlines.silk_RSHIFT(sPLC.prevGain_Q16[1], 6)
		};
		if (psDec.first_frame_after_reset != 0)
		{
			Arrays.MemSetShort(sPLC.prevLPC_Q12, 0, 16);
		}
		silk_PLC_energy(out var energy, out var shift, out var energy2, out var shift2, psDec.exc_Q14, array3, psDec.subfr_length, psDec.nb_subfr);
		int num = ((Inlines.silk_RSHIFT(energy, shift2) >= Inlines.silk_RSHIFT(energy2, shift)) ? Inlines.silk_max_int(0, sPLC.nb_subfr * sPLC.subfr_length - 128) : Inlines.silk_max_int(0, (sPLC.nb_subfr - 1) * sPLC.subfr_length - 128));
		short[] lTPCoef_Q = sPLC.LTPCoef_Q14;
		short num2 = sPLC.randScale_Q14;
		int a = HARM_ATT_Q15[Inlines.silk_min_int(1, psDec.lossCnt)];
		int b = ((psDec.prevSignalType != 2) ? PLC_RAND_ATTENUATE_UV_Q15[Inlines.silk_min_int(1, psDec.lossCnt)] : PLC_RAND_ATTENUATE_V_Q15[Inlines.silk_min_int(1, psDec.lossCnt)]);
		BWExpander.silk_bwexpander(sPLC.prevLPC_Q12, psDec.LPC_order, 64881);
		if (psDec.lossCnt == 0)
		{
			num2 = 16384;
			if (psDec.prevSignalType == 2)
			{
				for (int i = 0; i < 5; i++)
				{
					num2 -= lTPCoef_Q[i];
				}
				num2 = Inlines.silk_max_16(3277, num2);
				num2 = (short)Inlines.silk_RSHIFT(Inlines.silk_SMULBB(num2, sPLC.prevLTP_scale_Q14), 14);
			}
			else
			{
				int b2 = LPCInversePredGain.silk_LPC_inverse_pred_gain(sPLC.prevLPC_Q12, psDec.LPC_order);
				int b3 = Inlines.silk_min_32(Inlines.silk_RSHIFT(1073741824, 3), b2);
				b3 = Inlines.silk_max_32(Inlines.silk_RSHIFT(1073741824, 8), b3);
				b3 = Inlines.silk_LSHIFT(b3, 3);
				b = Inlines.silk_RSHIFT(Inlines.silk_SMULWB(b3, b), 14);
			}
		}
		int num3 = sPLC.rand_seed;
		int num4 = Inlines.silk_RSHIFT_ROUND(sPLC.pitchL_Q8, 8);
		int num5 = psDec.ltp_mem_length;
		int num6 = psDec.ltp_mem_length - num4 - psDec.LPC_order - 2;
		Filters.silk_LPC_analysis_filter(array, num6, psDec.outBuf, num6, sPLC.prevLPC_Q12, 0, psDec.ltp_mem_length - num6, psDec.LPC_order);
		int a2 = Inlines.silk_INVERSE32_varQ(sPLC.prevGain_Q16[1], 46);
		a2 = Inlines.silk_min(a2, 1073741823);
		for (int i = num6 + psDec.LPC_order; i < psDec.ltp_mem_length; i++)
		{
			array2[i] = Inlines.silk_SMULWB(a2, array[i]);
		}
		for (int j = 0; j < psDec.nb_subfr; j++)
		{
			int num7 = num5 - num4 + 2;
			for (int i = 0; i < psDec.subfr_length; i++)
			{
				int a3 = 2;
				a3 = Inlines.silk_SMLAWB(a3, array2[num7], lTPCoef_Q[0]);
				a3 = Inlines.silk_SMLAWB(a3, array2[num7 - 1], lTPCoef_Q[1]);
				a3 = Inlines.silk_SMLAWB(a3, array2[num7 - 2], lTPCoef_Q[2]);
				a3 = Inlines.silk_SMLAWB(a3, array2[num7 - 3], lTPCoef_Q[3]);
				a3 = Inlines.silk_SMLAWB(a3, array2[num7 - 4], lTPCoef_Q[4]);
				num7++;
				num3 = Inlines.silk_RAND(num3);
				num6 = Inlines.silk_RSHIFT(num3, 25) & 0x7F;
				array2[num5] = Inlines.silk_LSHIFT32(Inlines.silk_SMLAWB(a3, psDec.exc_Q14[num + num6], num2), 2);
				num5++;
			}
			for (int k = 0; k < 5; k++)
			{
				lTPCoef_Q[k] = (short)Inlines.silk_RSHIFT(Inlines.silk_SMULBB(a, lTPCoef_Q[k]), 15);
			}
			num2 = (short)Inlines.silk_RSHIFT(Inlines.silk_SMULBB(num2, b), 15);
			sPLC.pitchL_Q8 = Inlines.silk_SMLAWB(sPLC.pitchL_Q8, sPLC.pitchL_Q8, 655);
			sPLC.pitchL_Q8 = Inlines.silk_min_32(sPLC.pitchL_Q8, Inlines.silk_LSHIFT(Inlines.silk_SMULBB(18, psDec.fs_kHz), 8));
			num4 = Inlines.silk_RSHIFT_ROUND(sPLC.pitchL_Q8, 8);
		}
		int num8 = psDec.ltp_mem_length - 16;
		Arrays.MemCopy(psDec.sLPC_Q14_buf, 0, array2, num8, 16);
		for (int i = 0; i < psDec.frame_length; i++)
		{
			int num9 = num8 + 16 + i;
			int a4 = Inlines.silk_RSHIFT(psDec.LPC_order, 1);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 1], sPLC.prevLPC_Q12[0]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 2], sPLC.prevLPC_Q12[1]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 3], sPLC.prevLPC_Q12[2]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 4], sPLC.prevLPC_Q12[3]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 5], sPLC.prevLPC_Q12[4]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 6], sPLC.prevLPC_Q12[5]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 7], sPLC.prevLPC_Q12[6]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 8], sPLC.prevLPC_Q12[7]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 9], sPLC.prevLPC_Q12[8]);
			a4 = Inlines.silk_SMLAWB(a4, array2[num9 - 10], sPLC.prevLPC_Q12[9]);
			for (int k = 10; k < psDec.LPC_order; k++)
			{
				a4 = Inlines.silk_SMLAWB(a4, array2[num9 - k - 1], sPLC.prevLPC_Q12[k]);
			}
			array2[num9] = Inlines.silk_ADD_LSHIFT32(array2[num9], a4, 4);
			frame[frame_ptr + i] = (short)Inlines.silk_SAT16(Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWW(array2[num9], array3[1]), 8)));
		}
		Arrays.MemCopy(array2, num8 + psDec.frame_length, psDec.sLPC_Q14_buf, 0, 16);
		sPLC.rand_seed = num3;
		sPLC.randScale_Q14 = num2;
		for (int i = 0; i < 4; i++)
		{
			psDecCtrl.pitchL[i] = num4;
		}
	}

	internal static void silk_PLC_glue_frames(SilkChannelDecoder psDec, Span<short> frame, int frame_ptr, int length)
	{
		PLCStruct sPLC = psDec.sPLC;
		if (psDec.lossCnt != 0)
		{
			SumSqrShift.silk_sum_sqr_shift(out sPLC.conc_energy, out sPLC.conc_energy_shift, frame, frame_ptr, length);
			sPLC.last_frame_lost = 1;
			return;
		}
		if (psDec.sPLC.last_frame_lost != 0)
		{
			SumSqrShift.silk_sum_sqr_shift(out var energy, out var shift, frame, frame_ptr, length);
			if (shift > sPLC.conc_energy_shift)
			{
				sPLC.conc_energy = Inlines.silk_RSHIFT(sPLC.conc_energy, shift - sPLC.conc_energy_shift);
			}
			else if (shift < sPLC.conc_energy_shift)
			{
				energy = Inlines.silk_RSHIFT(energy, sPLC.conc_energy_shift - shift);
			}
			if (energy > sPLC.conc_energy)
			{
				int num = Inlines.silk_CLZ32(sPLC.conc_energy);
				num--;
				sPLC.conc_energy = Inlines.silk_LSHIFT(sPLC.conc_energy, num);
				energy = Inlines.silk_RSHIFT(energy, Inlines.silk_max_32(24 - num, 0));
				int num2 = Inlines.silk_LSHIFT(Inlines.silk_SQRT_APPROX(Inlines.silk_DIV32(sPLC.conc_energy, Inlines.silk_max(energy, 1))), 4);
				int a = Inlines.silk_DIV32_16(65536 - num2, length);
				a = Inlines.silk_LSHIFT(a, 2);
				for (int i = frame_ptr; i < frame_ptr + length; i++)
				{
					frame[i] = (short)Inlines.silk_SMULWB(num2, frame[i]);
					num2 += a;
					if (num2 > 65536)
					{
						break;
					}
				}
			}
		}
		sPLC.last_frame_lost = 0;
	}
}
