using Concentus.Common;

namespace Concentus.Silk;

/// <summary>
/// Approximate sigmoid function
/// </summary>
internal static class Sigmoid
{
	private static readonly int[] sigm_LUT_slope_Q10 = new int[6] { 237, 153, 73, 30, 12, 7 };

	private static readonly int[] sigm_LUT_pos_Q15 = new int[6] { 16384, 23955, 28861, 31213, 32178, 32548 };

	private static readonly int[] sigm_LUT_neg_Q15 = new int[6] { 16384, 8812, 3906, 1554, 589, 219 };

	internal static int silk_sigm_Q15(int in_Q5)
	{
		int num;
		if (in_Q5 < 0)
		{
			in_Q5 = -in_Q5;
			if (in_Q5 >= 192)
			{
				return 0;
			}
			num = Inlines.silk_RSHIFT(in_Q5, 5);
			return sigm_LUT_neg_Q15[num] - Inlines.silk_SMULBB(sigm_LUT_slope_Q10[num], in_Q5 & 0x1F);
		}
		if (in_Q5 >= 192)
		{
			return 32767;
		}
		num = Inlines.silk_RSHIFT(in_Q5, 5);
		return sigm_LUT_pos_Q15[num] + Inlines.silk_SMULBB(sigm_LUT_slope_Q10[num], in_Q5 & 0x1F);
	}
}
