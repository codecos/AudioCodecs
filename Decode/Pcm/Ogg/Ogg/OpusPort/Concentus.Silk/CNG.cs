using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

/// <summary>
/// Comfort noise generation and estimation
/// </summary>
internal static class CNG
{
	/// <summary>
	/// Generates excitation for CNG LPC synthesis
	/// </summary>
	/// <param name="exc_Q10">O    CNG excitation signal Q10</param>
	/// <param name="exc_buf_Q14">I    Random samples buffer Q10</param>
	/// <param name="Gain_Q16">I    Gain to apply</param>
	/// <param name="length">I    Length</param>
	/// <param name="rand_seed">I/O  Seed to random index generator</param>
	internal static void silk_CNG_exc(Span<int> exc_Q10, int exc_Q10_ptr, int[] exc_buf_Q14, int Gain_Q16, int length, ref int rand_seed)
	{
		int num;
		for (num = 255; num > length; num = Inlines.silk_RSHIFT(num, 1))
		{
		}
		int num2 = rand_seed;
		for (int i = exc_Q10_ptr; i < exc_Q10_ptr + length; i++)
		{
			num2 = Inlines.silk_RAND(num2);
			int num3 = Inlines.silk_RSHIFT(num2, 24) & num;
			exc_Q10[i] = (short)Inlines.silk_SAT16(Inlines.silk_SMULWW(exc_buf_Q14[num3], Gain_Q16 >> 4));
		}
		rand_seed = num2;
	}

	/// <summary>
	/// Resets CNG state
	/// </summary>
	/// <param name="psDec">I/O  Decoder state</param>
	internal static void silk_CNG_Reset(SilkChannelDecoder psDec)
	{
		int num = Inlines.silk_DIV32_16(32767, (short)(psDec.LPC_order + 1));
		int num2 = 0;
		for (int i = 0; i < psDec.LPC_order; i++)
		{
			num2 += num;
			psDec.sCNG.CNG_smth_NLSF_Q15[i] = (short)num2;
		}
		psDec.sCNG.CNG_smth_Gain_Q16 = 0;
		psDec.sCNG.rand_seed = 3176576;
	}

	/// <summary>
	/// Updates CNG estimate, and applies the CNG when packet was lost
	/// </summary>
	/// <param name="psDec">I/O  Decoder state</param>
	/// <param name="psDecCtrl">I/O  Decoder control</param>
	/// <param name="frame">I/O  Signal</param>
	/// <param name="length">I    Length of residual</param>
	internal static void silk_CNG(SilkChannelDecoder psDec, SilkDecoderControl psDecCtrl, Span<short> frame, int frame_ptr, int length)
	{
		short[] array = psDec.nlsfWork;
		CNGState sCNG = psDec.sCNG;
		if (psDec.fs_kHz != sCNG.fs_kHz)
		{
			silk_CNG_Reset(psDec);
			sCNG.fs_kHz = psDec.fs_kHz;
		}
		if (psDec.lossCnt == 0 && psDec.prevSignalType == 0)
		{
			for (int i = 0; i < psDec.LPC_order; i++)
			{
				sCNG.CNG_smth_NLSF_Q15[i] += (short)Inlines.silk_SMULWB(psDec.prevNLSF_Q15[i] - sCNG.CNG_smth_NLSF_Q15[i], 16348);
			}
			int num = 0;
			for (int i = 0; i < psDec.nb_subfr; i++)
			{
				if (psDecCtrl.Gains_Q16[i] > num)
				{
					num = psDecCtrl.Gains_Q16[i];
				}
			}
			Arrays.MemMoveInt(sCNG.CNG_exc_buf_Q14, 0, psDec.subfr_length, (psDec.nb_subfr - 1) * psDec.subfr_length);
			for (int i = 0; i < psDec.nb_subfr; i++)
			{
				sCNG.CNG_smth_Gain_Q16 += Inlines.silk_SMULWB(psDecCtrl.Gains_Q16[i] - sCNG.CNG_smth_Gain_Q16, 4634);
			}
		}
		if (psDec.lossCnt != 0)
		{
			int[] array2 = psDec.cngWork;
			int num2 = Inlines.silk_SMULWW(psDec.sPLC.randScale_Q14, psDec.sPLC.prevGain_Q16[1]);
			if (num2 >= 2097152 || sCNG.CNG_smth_Gain_Q16 > 8388608)
			{
				num2 = Inlines.silk_SMULTT(num2, num2);
				num2 = Inlines.silk_SUB_LSHIFT32(Inlines.silk_SMULTT(sCNG.CNG_smth_Gain_Q16, sCNG.CNG_smth_Gain_Q16), num2, 5);
				num2 = Inlines.silk_LSHIFT32(Inlines.silk_SQRT_APPROX(num2), 16);
			}
			else
			{
				num2 = Inlines.silk_SMULWW(num2, num2);
				num2 = Inlines.silk_SUB_LSHIFT32(Inlines.silk_SMULWW(sCNG.CNG_smth_Gain_Q16, sCNG.CNG_smth_Gain_Q16), num2, 5);
				num2 = Inlines.silk_LSHIFT32(Inlines.silk_SQRT_APPROX(num2), 8);
			}
			silk_CNG_exc(array2, 16, sCNG.CNG_exc_buf_Q14, num2, length, ref sCNG.rand_seed);
			NLSF.silk_NLSF2A(array, sCNG.CNG_smth_NLSF_Q15, psDec.LPC_order, psDec.nlsfPolyWork, psDec.nlsfPolyAWork, psDec.nlsfPolyBWork, psDec.nlsfCoefWork);
			Arrays.MemCopy(sCNG.CNG_synth_state, 0, array2, 0, 16);
			for (int i = 0; i < length; i++)
			{
				int num3 = 16 + i;
				int a = Inlines.silk_RSHIFT(psDec.LPC_order, 1);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 1], array[0]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 2], array[1]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 3], array[2]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 4], array[3]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 5], array[4]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 6], array[5]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 7], array[6]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 8], array[7]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 9], array[8]);
				a = Inlines.silk_SMLAWB(a, array2[num3 - 10], array[9]);
				if (psDec.LPC_order == 16)
				{
					a = Inlines.silk_SMLAWB(a, array2[num3 - 11], array[10]);
					a = Inlines.silk_SMLAWB(a, array2[num3 - 12], array[11]);
					a = Inlines.silk_SMLAWB(a, array2[num3 - 13], array[12]);
					a = Inlines.silk_SMLAWB(a, array2[num3 - 14], array[13]);
					a = Inlines.silk_SMLAWB(a, array2[num3 - 15], array[14]);
					a = Inlines.silk_SMLAWB(a, array2[num3 - 16], array[15]);
				}
				array2[num3] = Inlines.silk_ADD_LSHIFT(array2[num3], a, 4);
				frame[frame_ptr + i] = Inlines.silk_ADD_SAT16(frame[frame_ptr + i], (short)Inlines.silk_RSHIFT_ROUND(array2[num3], 10));
			}
			Arrays.MemCopy(array2, length, sCNG.CNG_synth_state, 0, 16);
		}
		else
		{
			Arrays.MemSetInt(sCNG.CNG_synth_state, 0, psDec.LPC_order);
		}
	}
}
