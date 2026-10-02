using System;
using Concentus.Common;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

internal static class DecodeIndices
{
	internal static void silk_decode_indices(SilkChannelDecoder psDec, EntropyCoder psRangeDec, ReadOnlySpan<byte> frameData, int FrameIndex, int decode_LBRR, int condCoding)
	{
		short[] array = psDec.nlsfWork;
		byte[] array2 = psDec.nlsfIndicesWork;
		int num = ((decode_LBRR == 0 && psDec.VAD_flags[FrameIndex] == 0) ? psRangeDec.dec_icdf(frameData, Tables.silk_type_offset_no_VAD_iCDF, 8u) : (psRangeDec.dec_icdf(frameData, Tables.silk_type_offset_VAD_iCDF, 8u) + 2));
		psDec.indices.signalType = (sbyte)Inlines.silk_RSHIFT(num, 1);
		psDec.indices.quantOffsetType = (sbyte)(num & 1);
		if (condCoding == 2)
		{
			psDec.indices.GainsIndices[0] = (sbyte)psRangeDec.dec_icdf(frameData, Tables.silk_delta_gain_iCDF, 8u);
		}
		else
		{
			psDec.indices.GainsIndices[0] = (sbyte)Inlines.silk_LSHIFT(psRangeDec.dec_icdf(frameData, Tables.silk_gain_iCDF[psDec.indices.signalType], 8u), 3);
			psDec.indices.GainsIndices[0] += (sbyte)psRangeDec.dec_icdf(frameData, Tables.silk_uniform8_iCDF, 8u);
		}
		for (int i = 1; i < psDec.nb_subfr; i++)
		{
			psDec.indices.GainsIndices[i] = (sbyte)psRangeDec.dec_icdf(frameData, Tables.silk_delta_gain_iCDF, 8u);
		}
		psDec.indices.NLSFIndices[0] = (sbyte)psRangeDec.dec_icdf(frameData, psDec.psNLSF_CB.CB1_iCDF, (psDec.indices.signalType >> 1) * psDec.psNLSF_CB.nVectors, 8u);
		NLSF.silk_NLSF_unpack(array, array2, psDec.psNLSF_CB, psDec.indices.NLSFIndices[0]);
		for (int i = 0; i < psDec.psNLSF_CB.order; i++)
		{
			num = psRangeDec.dec_icdf(frameData, psDec.psNLSF_CB.ec_iCDF, array[i], 8u);
			switch (num)
			{
			case 0:
				num -= psRangeDec.dec_icdf(frameData, Tables.silk_NLSF_EXT_iCDF, 8u);
				break;
			case 8:
				num += psRangeDec.dec_icdf(frameData, Tables.silk_NLSF_EXT_iCDF, 8u);
				break;
			}
			psDec.indices.NLSFIndices[i + 1] = (sbyte)(num - 4);
		}
		if (psDec.nb_subfr == 4)
		{
			psDec.indices.NLSFInterpCoef_Q2 = (sbyte)psRangeDec.dec_icdf(frameData, Tables.silk_NLSF_interpolation_factor_iCDF, 8u);
		}
		else
		{
			psDec.indices.NLSFInterpCoef_Q2 = 4;
		}
		if (psDec.indices.signalType == 2)
		{
			int num2 = 1;
			if (condCoding == 2 && psDec.ec_prevSignalType == 2)
			{
				int num3 = (short)psRangeDec.dec_icdf(frameData, Tables.silk_pitch_delta_iCDF, 8u);
				if (num3 > 0)
				{
					num3 -= 9;
					psDec.indices.lagIndex = (short)(psDec.ec_prevLagIndex + num3);
					num2 = 0;
				}
			}
			if (num2 != 0)
			{
				psDec.indices.lagIndex = (short)(psRangeDec.dec_icdf(frameData, Tables.silk_pitch_lag_iCDF, 8u) * Inlines.silk_RSHIFT(psDec.fs_kHz, 1));
				psDec.indices.lagIndex += (short)psRangeDec.dec_icdf(frameData, psDec.pitch_lag_low_bits_iCDF, 8u);
			}
			psDec.ec_prevLagIndex = psDec.indices.lagIndex;
			psDec.indices.contourIndex = (sbyte)psRangeDec.dec_icdf(frameData, psDec.pitch_contour_iCDF, 8u);
			psDec.indices.PERIndex = (sbyte)psRangeDec.dec_icdf(frameData, Tables.silk_LTP_per_index_iCDF, 8u);
			for (int j = 0; j < psDec.nb_subfr; j++)
			{
				psDec.indices.LTPIndex[j] = (sbyte)psRangeDec.dec_icdf(frameData, Tables.silk_LTP_gain_iCDF_ptrs[psDec.indices.PERIndex], 8u);
			}
			if (condCoding == 0)
			{
				psDec.indices.LTP_scaleIndex = (sbyte)psRangeDec.dec_icdf(frameData, Tables.silk_LTPscale_iCDF, 8u);
			}
			else
			{
				psDec.indices.LTP_scaleIndex = 0;
			}
		}
		psDec.ec_prevSignalType = psDec.indices.signalType;
		psDec.indices.Seed = (sbyte)psRangeDec.dec_icdf(frameData, Tables.silk_uniform4_iCDF, 8u);
	}
}
