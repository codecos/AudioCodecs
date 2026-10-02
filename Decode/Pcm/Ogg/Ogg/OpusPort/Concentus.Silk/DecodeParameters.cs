using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

internal class DecodeParameters
{
	internal static void silk_decode_parameters(SilkChannelDecoder psDec, SilkDecoderControl psDecCtrl, int condCoding)
	{
		short[] array = psDec.nlsfWork;
		short[] array2 = psDec.nlsfInterpWork;
		BoxedValueSbyte boxedValueSbyte = psDec.gainIndexWork;
		boxedValueSbyte.Val = psDec.LastGainIndex;
		GainQuantization.silk_gains_dequant(psDecCtrl.Gains_Q16, psDec.indices.GainsIndices, boxedValueSbyte, (condCoding == 2) ? 1 : 0, psDec.nb_subfr);
		psDec.LastGainIndex = boxedValueSbyte.Val;
		NLSF.silk_NLSF_decode(array, psDec.indices.NLSFIndices, psDec.psNLSF_CB);
		NLSF.silk_NLSF2A(psDecCtrl.PredCoef_Q12[1], array, psDec.LPC_order, psDec.nlsfPolyWork, psDec.nlsfPolyAWork, psDec.nlsfPolyBWork, psDec.nlsfCoefWork);
		if (psDec.first_frame_after_reset == 1)
		{
			psDec.indices.NLSFInterpCoef_Q2 = 4;
		}
		if (psDec.indices.NLSFInterpCoef_Q2 < 4)
		{
			for (int i = 0; i < psDec.LPC_order; i++)
			{
				array2[i] = (short)(psDec.prevNLSF_Q15[i] + Inlines.silk_RSHIFT(Inlines.silk_MUL(psDec.indices.NLSFInterpCoef_Q2, array[i] - psDec.prevNLSF_Q15[i]), 2));
			}
			NLSF.silk_NLSF2A(psDecCtrl.PredCoef_Q12[0], array2, psDec.LPC_order, psDec.nlsfPolyWork, psDec.nlsfPolyAWork, psDec.nlsfPolyBWork, psDec.nlsfCoefWork);
		}
		else
		{
			Arrays.MemCopy(psDecCtrl.PredCoef_Q12[1], 0, psDecCtrl.PredCoef_Q12[0], 0, psDec.LPC_order);
		}
		Arrays.MemCopy(array, 0, psDec.prevNLSF_Q15, 0, psDec.LPC_order);
		if (psDec.lossCnt != 0)
		{
			BWExpander.silk_bwexpander(psDecCtrl.PredCoef_Q12[0], psDec.LPC_order, 63570);
			BWExpander.silk_bwexpander(psDecCtrl.PredCoef_Q12[1], psDec.LPC_order, 63570);
		}
		if (psDec.indices.signalType == 2)
		{
			DecodePitch.silk_decode_pitch(psDec.indices.lagIndex, psDec.indices.contourIndex, psDecCtrl.pitchL, psDec.fs_kHz, psDec.nb_subfr);
			sbyte[][] array3 = Tables.silk_LTP_vq_ptrs_Q7[psDec.indices.PERIndex];
			int num;
			for (int j = 0; j < psDec.nb_subfr; j++)
			{
				num = psDec.indices.LTPIndex[j];
				for (int i = 0; i < 5; i++)
				{
					psDecCtrl.LTPCoef_Q14[j * 5 + i] = (short)Inlines.silk_LSHIFT(array3[num][i], 7);
				}
			}
			num = psDec.indices.LTP_scaleIndex;
			psDecCtrl.LTP_scale_Q14 = Tables.silk_LTPScales_table_Q14[num];
		}
		else
		{
			Arrays.MemSetInt(psDecCtrl.pitchL, 0, psDec.nb_subfr);
			Arrays.MemSetShort(psDecCtrl.LTPCoef_Q14, 0, 5 * psDec.nb_subfr);
			psDec.indices.PERIndex = 0;
			psDecCtrl.LTP_scale_Q14 = 0;
		}
	}
}
