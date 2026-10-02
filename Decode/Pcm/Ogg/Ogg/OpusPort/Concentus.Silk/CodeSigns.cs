using System;
using Concentus.Common;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

internal static class CodeSigns
{
	private static int silk_enc_map(int a)
	{
		return Inlines.silk_RSHIFT(a, 15) + 1;
	}

	private static int silk_dec_map(int a)
	{
		return Inlines.silk_LSHIFT(a, 1) - 1;
	}

	/// <summary>
	/// Encodes signs of excitation
	/// </summary>
	/// <param name="psRangeEnc">I/O  Compressor data structure</param>
	/// <param name="pulses">I    pulse signal</param>
	/// <param name="length">I    length of input</param>
	/// <param name="signalType">I    Signal type</param>
	/// <param name="quantOffsetType">I    Quantization offset type</param>
	/// <param name="sum_pulses">I    Sum of absolute pulses per block [MAX_NB_SHELL_BLOCKS]</param>
	internal static void silk_encode_signs(EntropyCoder psRangeEnc, Span<byte> encodedData, Span<sbyte> pulses, int length, int signalType, int quantOffsetType, int[] sum_pulses)
	{
		byte[] array = new byte[2];
		byte[] silk_sign_iCDF = Tables.silk_sign_iCDF;
		array[1] = 0;
		int num = 0;
		int num2 = Inlines.silk_SMULBB(7, Inlines.silk_ADD_LSHIFT(quantOffsetType, signalType, 1));
		int num3 = num2;
		length = Inlines.silk_RSHIFT(length + 8, 4);
		for (num2 = 0; num2 < length; num2++)
		{
			int num4 = sum_pulses[num2];
			if (num4 > 0)
			{
				array[0] = silk_sign_iCDF[num3 + Inlines.silk_min(num4 & 0x1F, 6)];
				for (int i = num; i < num + 16; i++)
				{
					if (pulses[i] != 0)
					{
						psRangeEnc.enc_icdf(encodedData, silk_enc_map(pulses[i]), array, 8u);
					}
				}
			}
			num += 16;
		}
	}

	/// <summary>
	/// Decodes signs of excitation
	/// </summary>
	/// <param name="psRangeDec">I/O  Compressor data structure</param>
	/// <param name="pulses">I/O  pulse signal</param>
	/// <param name="length">I    length of input</param>
	/// <param name="signalType">I    Signal type</param>
	/// <param name="quantOffsetType">I    Quantization offset type</param>
	/// <param name="sum_pulses">I    Sum of absolute pulses per block [MAX_NB_SHELL_BLOCKS]</param>
	internal static void silk_decode_signs(SilkChannelDecoder psDec, EntropyCoder psRangeDec, ReadOnlySpan<byte> encodedData, short[] pulses, int length, int signalType, int quantOffsetType, int[] sum_pulses)
	{
		byte[] array = psDec.signIcdfWork;
		byte[] silk_sign_iCDF = Tables.silk_sign_iCDF;
		array[1] = 0;
		int num = 0;
		int num2 = Inlines.silk_SMULBB(7, Inlines.silk_ADD_LSHIFT(quantOffsetType, signalType, 1));
		int num3 = num2;
		length = Inlines.silk_RSHIFT(length + 8, 4);
		for (num2 = 0; num2 < length; num2++)
		{
			int num4 = sum_pulses[num2];
			if (num4 > 0)
			{
				array[0] = silk_sign_iCDF[num3 + Inlines.silk_min(num4 & 0x1F, 6)];
				for (int i = 0; i < 16; i++)
				{
					if (pulses[num + i] > 0)
					{
						pulses[num + i] *= (short)silk_dec_map(psRangeDec.dec_icdf(encodedData, array, 8u));
					}
				}
			}
			num += 16;
		}
	}
}
