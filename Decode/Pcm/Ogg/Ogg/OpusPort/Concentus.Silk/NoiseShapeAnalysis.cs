using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

internal static class NoiseShapeAnalysis
{
	internal static int warped_gain(int[] coefs_Q24, int lambda_Q16, int order)
	{
		lambda_Q16 = -lambda_Q16;
		int b = coefs_Q24[order - 1];
		for (int num = order - 2; num >= 0; num--)
		{
			b = Inlines.silk_SMLAWB(coefs_Q24[num], b, lambda_Q16);
		}
		b = Inlines.silk_SMLAWB(16777216, b, -lambda_Q16);
		return Inlines.silk_INVERSE32_varQ(b, 40);
	}

	internal static void limit_warped_coefs(int[] coefs_syn_Q24, int[] coefs_ana_Q24, int lambda_Q16, int limit_Q24, int order)
	{
		int num = 0;
		lambda_Q16 = -lambda_Q16;
		for (int num2 = order - 1; num2 > 0; num2--)
		{
			coefs_syn_Q24[num2 - 1] = Inlines.silk_SMLAWB(coefs_syn_Q24[num2 - 1], coefs_syn_Q24[num2], lambda_Q16);
			coefs_ana_Q24[num2 - 1] = Inlines.silk_SMLAWB(coefs_ana_Q24[num2 - 1], coefs_ana_Q24[num2], lambda_Q16);
		}
		lambda_Q16 = -lambda_Q16;
		int a = Inlines.silk_SMLAWB(65536, -lambda_Q16, lambda_Q16);
		int b = Inlines.silk_SMLAWB(16777216, coefs_syn_Q24[0], lambda_Q16);
		int num3 = Inlines.silk_DIV32_varQ(a, b, 24);
		b = Inlines.silk_SMLAWB(16777216, coefs_ana_Q24[0], lambda_Q16);
		int num4 = Inlines.silk_DIV32_varQ(a, b, 24);
		for (int num2 = 0; num2 < order; num2++)
		{
			coefs_syn_Q24[num2] = Inlines.silk_SMULWW(num3, coefs_syn_Q24[num2]);
			coefs_ana_Q24[num2] = Inlines.silk_SMULWW(num4, coefs_ana_Q24[num2]);
		}
		for (int i = 0; i < 10; i++)
		{
			int num5 = -1;
			for (int num2 = 0; num2 < order; num2++)
			{
				int num6 = Inlines.silk_max(Inlines.silk_abs_int32(coefs_syn_Q24[num2]), Inlines.silk_abs_int32(coefs_ana_Q24[num2]));
				if (num6 > num5)
				{
					num5 = num6;
					num = num2;
				}
			}
			if (num5 <= limit_Q24)
			{
				break;
			}
			for (int num2 = 1; num2 < order; num2++)
			{
				coefs_syn_Q24[num2 - 1] = Inlines.silk_SMLAWB(coefs_syn_Q24[num2 - 1], coefs_syn_Q24[num2], lambda_Q16);
				coefs_ana_Q24[num2 - 1] = Inlines.silk_SMLAWB(coefs_ana_Q24[num2 - 1], coefs_ana_Q24[num2], lambda_Q16);
			}
			num3 = Inlines.silk_INVERSE32_varQ(num3, 32);
			num4 = Inlines.silk_INVERSE32_varQ(num4, 32);
			for (int num2 = 0; num2 < order; num2++)
			{
				coefs_syn_Q24[num2] = Inlines.silk_SMULWW(num3, coefs_syn_Q24[num2]);
				coefs_ana_Q24[num2] = Inlines.silk_SMULWW(num4, coefs_ana_Q24[num2]);
			}
			int chirp_Q = 64881 - Inlines.silk_DIV32_varQ(Inlines.silk_SMULWB(num5 - limit_Q24, Inlines.silk_SMLABB(819, 102, i)), Inlines.silk_MUL(num5, num + 1), 22);
			BWExpander.silk_bwexpander_32(coefs_syn_Q24, order, chirp_Q);
			BWExpander.silk_bwexpander_32(coefs_ana_Q24, order, chirp_Q);
			lambda_Q16 = -lambda_Q16;
			for (int num2 = order - 1; num2 > 0; num2--)
			{
				coefs_syn_Q24[num2 - 1] = Inlines.silk_SMLAWB(coefs_syn_Q24[num2 - 1], coefs_syn_Q24[num2], lambda_Q16);
				coefs_ana_Q24[num2 - 1] = Inlines.silk_SMLAWB(coefs_ana_Q24[num2 - 1], coefs_ana_Q24[num2], lambda_Q16);
			}
			lambda_Q16 = -lambda_Q16;
			int a2 = Inlines.silk_SMLAWB(65536, -lambda_Q16, lambda_Q16);
			b = Inlines.silk_SMLAWB(16777216, coefs_syn_Q24[0], lambda_Q16);
			num3 = Inlines.silk_DIV32_varQ(a2, b, 24);
			b = Inlines.silk_SMLAWB(16777216, coefs_ana_Q24[0], lambda_Q16);
			num4 = Inlines.silk_DIV32_varQ(a2, b, 24);
			for (int num2 = 0; num2 < order; num2++)
			{
				coefs_syn_Q24[num2] = Inlines.silk_SMULWW(num3, coefs_syn_Q24[num2]);
				coefs_ana_Q24[num2] = Inlines.silk_SMULWW(num4, coefs_ana_Q24[num2]);
			}
		}
	}


}
