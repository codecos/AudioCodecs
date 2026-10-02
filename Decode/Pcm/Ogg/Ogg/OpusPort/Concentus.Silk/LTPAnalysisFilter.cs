using Concentus.Common;

namespace Concentus.Silk;

internal static class LTPAnalysisFilter
{
	internal static void silk_LTP_analysis_filter(short[] LTP_res, short[] x, int x_ptr, short[] LTPCoef_Q14, int[] pitchL, int[] invGains_Q16, int subfr_length, int nb_subfr, int pre_length)
	{
		short[] array = new short[5];
		int num = x_ptr;
		int num2 = 0;
		for (int i = 0; i < nb_subfr; i++)
		{
			int num3 = num - pitchL[i];
			array[0] = LTPCoef_Q14[i * 5];
			array[1] = LTPCoef_Q14[i * 5 + 1];
			array[2] = LTPCoef_Q14[i * 5 + 2];
			array[3] = LTPCoef_Q14[i * 5 + 3];
			array[4] = LTPCoef_Q14[i * 5 + 4];
			for (int j = 0; j < subfr_length + pre_length; j++)
			{
				int num4 = num2 + j;
				LTP_res[num4] = x[num + j];
				int a = Inlines.silk_SMULBB(x[num3 + 2], array[0]);
				a = Inlines.silk_SMLABB_ovflw(a, x[num3 + 1], array[1]);
				a = Inlines.silk_SMLABB_ovflw(a, x[num3], array[2]);
				a = Inlines.silk_SMLABB_ovflw(a, x[num3 - 1], array[3]);
				a = Inlines.silk_SMLABB_ovflw(a, x[num3 - 2], array[4]);
				a = Inlines.silk_RSHIFT_ROUND(a, 14);
				LTP_res[num4] = (short)Inlines.silk_SAT16(x[num + j] - a);
				LTP_res[num4] = (short)Inlines.silk_SMULWB(invGains_Q16[i], LTP_res[num4]);
				num3++;
			}
			num2 += subfr_length + pre_length;
			num += subfr_length;
		}
	}
}
