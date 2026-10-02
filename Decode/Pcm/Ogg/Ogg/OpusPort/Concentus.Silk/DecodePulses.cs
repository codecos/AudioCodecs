using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

internal static class DecodePulses
{
	internal static void silk_decode_pulses(SilkChannelDecoder psDec, EntropyCoder psRangeDec, ReadOnlySpan<byte> frameData, short[] pulses, int signalType, int quantOffsetType, int frame_length)
	{
		int[] array = psDec.pulseRateWork;
		int[] array2 = psDec.pulseScaleWork;
		int num = psRangeDec.dec_icdf(frameData, Tables.silk_rate_levels_iCDF[signalType >> 1], 8u);
		int num2 = Inlines.silk_RSHIFT(frame_length, 4);
		if (num2 * 16 < frame_length)
		{
			num2++;
		}
		for (int i = 0; i < num2; i++)
		{
			array2[i] = 0;
			array[i] = psRangeDec.dec_icdf(frameData, Tables.silk_pulses_per_block_iCDF[num], 8u);
			while (array[i] == 17)
			{
				array2[i]++;
				array[i] = psRangeDec.dec_icdf(frameData, Tables.silk_pulses_per_block_iCDF[9], (array2[i] == 10) ? 1 : 0, 8u);
			}
		}
		for (int i = 0; i < num2; i++)
		{
			if (array[i] > 0)
			{
				ShellCoder.silk_shell_decoder(psDec, pulses, Inlines.silk_SMULBB(i, 16), psRangeDec, frameData, array[i]);
			}
			else
			{
				Arrays.MemSetWithOffset(pulses, (short)0, Inlines.silk_SMULBB(i, 16), 16);
			}
		}
		for (int i = 0; i < num2; i++)
		{
			if (array2[i] <= 0)
			{
				continue;
			}
			int num3 = array2[i];
			int num4 = Inlines.silk_SMULBB(i, 16);
			for (int j = 0; j < 16; j++)
			{
				int num5 = pulses[num4 + j];
				for (int k = 0; k < num3; k++)
				{
					num5 = Inlines.silk_LSHIFT(num5, 1);
					num5 += psRangeDec.dec_icdf(frameData, Tables.silk_lsb_iCDF, 8u);
				}
				pulses[num4 + j] = (short)num5;
			}
			array[i] |= num3 << 5;
		}
		CodeSigns.silk_decode_signs(psDec, psRangeDec, frameData, pulses, frame_length, signalType, quantOffsetType, array);
	}
}
