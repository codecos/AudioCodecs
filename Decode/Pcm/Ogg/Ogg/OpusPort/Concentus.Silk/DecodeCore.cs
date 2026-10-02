using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

internal static class DecodeCore
{
	internal static void silk_decode_core(SilkChannelDecoder psDec, SilkDecoderControl psDecCtrl, Span<short> xq, int xq_ptr, short[] pulses)
	{
		int num = 0;
		short[] lTPCoef_Q = psDecCtrl.LTPCoef_Q14;
		short[] array = psDec.ltpCoefWork;
		int[] array2 = psDec.ltpStateWork;
		int[] array3 = psDec.subframeWork;
		int[] array4 = psDec.lpcWork;
		int num2 = Tables.silk_Quantization_Offsets_Q10[psDec.indices.signalType >> 1][psDec.indices.quantOffsetType];
		int num3 = ((psDec.indices.NLSFInterpCoef_Q2 < 4) ? 1 : 0);
		int seed = psDec.indices.Seed;
		for (int i = 0; i < psDec.frame_length; i++)
		{
			seed = Inlines.silk_RAND(seed);
			psDec.exc_Q14[i] = Inlines.silk_LSHIFT(pulses[i], 14);
			if (psDec.exc_Q14[i] > 0)
			{
				psDec.exc_Q14[i] -= 1280;
			}
			else if (psDec.exc_Q14[i] < 0)
			{
				psDec.exc_Q14[i] += 1280;
			}
			psDec.exc_Q14[i] += num2 << 4;
			if (seed < 0)
			{
				psDec.exc_Q14[i] = -psDec.exc_Q14[i];
			}
			seed = Inlines.silk_ADD32_ovflw(seed, pulses[i]);
		}
		Arrays.MemCopy(psDec.sLPC_Q14_buf, 0, array4, 0, 16);
		int num4 = 0;
		int num5 = xq_ptr;
		int num6 = psDec.ltp_mem_length;
		for (int j = 0; j < psDec.nb_subfr; j++)
		{
			int[] array5 = array3;
			int num7 = 0;
			short[] array6 = psDecCtrl.PredCoef_Q12[j >> 1];
			int num8 = j * 5;
			int num9 = psDec.indices.signalType;
			int b = Inlines.silk_RSHIFT(psDecCtrl.Gains_Q16[j], 6);
			int a = Inlines.silk_INVERSE32_varQ(psDecCtrl.Gains_Q16[j], 47);
			int num10;
			if (psDecCtrl.Gains_Q16[j] != psDec.prev_gain_Q16)
			{
				num10 = Inlines.silk_DIV32_varQ(psDec.prev_gain_Q16, psDecCtrl.Gains_Q16[j], 16);
				for (int i = 0; i < 16; i++)
				{
					array4[i] = Inlines.silk_SMULWW(num10, array4[i]);
				}
			}
			else
			{
				num10 = 65536;
			}
			psDec.prev_gain_Q16 = psDecCtrl.Gains_Q16[j];
			if (psDec.lossCnt != 0 && psDec.prevSignalType == 2 && psDec.indices.signalType != 2 && j < 2)
			{
				Arrays.MemSetWithOffset(lTPCoef_Q, (short)0, num8, 5);
				lTPCoef_Q[num8 + 2] = 4096;
				num9 = 2;
				psDecCtrl.pitchL[j] = psDec.lagPrev;
			}
			if (num9 == 2)
			{
				num = psDecCtrl.pitchL[j];
				if (j == 0 || (j == 2 && num3 != 0))
				{
					int num11 = psDec.ltp_mem_length - num - psDec.LPC_order - 2;
					if (j == 2)
					{
						xq.Slice(xq_ptr, 2 * psDec.subfr_length).CopyTo(psDec.outBuf.AsSpan(psDec.ltp_mem_length));
					}
					Filters.silk_LPC_analysis_filter(array, num11, psDec.outBuf, num11 + j * psDec.subfr_length, array6, 0, psDec.ltp_mem_length - num11, psDec.LPC_order);
					if (j == 0)
					{
						a = Inlines.silk_LSHIFT(Inlines.silk_SMULWB(a, psDecCtrl.LTP_scale_Q14), 2);
					}
					for (int i = 0; i < num + 2; i++)
					{
						array2[num6 - i - 1] = Inlines.silk_SMULWB(a, array[psDec.ltp_mem_length - i - 1]);
					}
				}
				else if (num10 != 65536)
				{
					for (int i = 0; i < num + 2; i++)
					{
						array2[num6 - i - 1] = Inlines.silk_SMULWW(num10, array2[num6 - i - 1]);
					}
				}
			}
			if (num9 == 2)
			{
				int num12 = num6 - num + 2;
				for (int i = 0; i < psDec.subfr_length; i++)
				{
					int a2 = 2;
					a2 = Inlines.silk_SMLAWB(a2, array2[num12], lTPCoef_Q[num8]);
					a2 = Inlines.silk_SMLAWB(a2, array2[num12 - 1], lTPCoef_Q[num8 + 1]);
					a2 = Inlines.silk_SMLAWB(a2, array2[num12 - 2], lTPCoef_Q[num8 + 2]);
					a2 = Inlines.silk_SMLAWB(a2, array2[num12 - 3], lTPCoef_Q[num8 + 3]);
					a2 = Inlines.silk_SMLAWB(a2, array2[num12 - 4], lTPCoef_Q[num8 + 4]);
					num12++;
					array5[num7 + i] = Inlines.silk_ADD_LSHIFT32(psDec.exc_Q14[num4 + i], a2, 1);
					array2[num6] = Inlines.silk_LSHIFT(array5[num7 + i], 1);
					num6++;
				}
			}
			else
			{
				array5 = psDec.exc_Q14;
				num7 = num4;
			}
			for (int i = 0; i < psDec.subfr_length; i++)
			{
				int a3 = Inlines.silk_RSHIFT(psDec.LPC_order, 1);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 1], array6[0]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 2], array6[1]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 3], array6[2]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 4], array6[3]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 5], array6[4]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 6], array6[5]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 7], array6[6]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 8], array6[7]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 9], array6[8]);
				a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 10], array6[9]);
				if (psDec.LPC_order == 16)
				{
					a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 11], array6[10]);
					a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 12], array6[11]);
					a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 13], array6[12]);
					a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 14], array6[13]);
					a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 15], array6[14]);
					a3 = Inlines.silk_SMLAWB(a3, array4[16 + i - 16], array6[15]);
				}
				array4[16 + i] = Inlines.silk_ADD_LSHIFT32(array5[num7 + i], a3, 4);
				xq[num5 + i] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWW(array4[16 + i], b), 8));
			}
			Arrays.MemCopy(array4, psDec.subfr_length, array4, 0, 16);
			num4 += psDec.subfr_length;
			num5 += psDec.subfr_length;
		}
		Arrays.MemCopy(array4, 0, psDec.sLPC_Q14_buf, 0, 16);
	}
}
