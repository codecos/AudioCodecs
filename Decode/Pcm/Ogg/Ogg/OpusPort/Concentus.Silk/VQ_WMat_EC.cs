using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk;

internal static class VQ_WMat_EC
{
	internal static void silk_VQ_WMat_EC(BoxedValueSbyte ind, BoxedValueInt rate_dist_Q14, BoxedValueInt gain_Q7, short[] in_Q14, int in_Q14_ptr, int[] W_Q18, int W_Q18_ptr, sbyte[][] cb_Q7, byte[] cb_gain_Q7, byte[] cl_Q5, int mu_Q9, int max_gain_Q7, int L)
	{
		int num = 0;
		short[] array = new short[5];
		rate_dist_Q14.Val = int.MaxValue;
		for (int i = 0; i < L; i++)
		{
			sbyte[] array2 = cb_Q7[num++];
			int num2 = cb_gain_Q7[i];
			array[0] = (short)(in_Q14[in_Q14_ptr] - Inlines.silk_LSHIFT(array2[0], 7));
			array[1] = (short)(in_Q14[in_Q14_ptr + 1] - Inlines.silk_LSHIFT(array2[1], 7));
			array[2] = (short)(in_Q14[in_Q14_ptr + 2] - Inlines.silk_LSHIFT(array2[2], 7));
			array[3] = (short)(in_Q14[in_Q14_ptr + 3] - Inlines.silk_LSHIFT(array2[3], 7));
			array[4] = (short)(in_Q14[in_Q14_ptr + 4] - Inlines.silk_LSHIFT(array2[4], 7));
			int a = Inlines.silk_SMULBB(mu_Q9, cl_Q5[i]);
			a = Inlines.silk_ADD_LSHIFT32(a, Inlines.silk_max(Inlines.silk_SUB32(num2, max_gain_Q7), 0), 10);
			int a2 = Inlines.silk_SMULWB(W_Q18[W_Q18_ptr + 1], array[1]);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 2], array[2]);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 3], array[3]);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 4], array[4]);
			a2 = Inlines.silk_LSHIFT(a2, 1);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr], array[0]);
			a = Inlines.silk_SMLAWB(a, a2, array[0]);
			a2 = Inlines.silk_SMULWB(W_Q18[W_Q18_ptr + 7], array[2]);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 8], array[3]);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 9], array[4]);
			a2 = Inlines.silk_LSHIFT(a2, 1);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 6], array[1]);
			a = Inlines.silk_SMLAWB(a, a2, array[1]);
			a2 = Inlines.silk_SMULWB(W_Q18[W_Q18_ptr + 13], array[3]);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 14], array[4]);
			a2 = Inlines.silk_LSHIFT(a2, 1);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 12], array[2]);
			a = Inlines.silk_SMLAWB(a, a2, array[2]);
			a2 = Inlines.silk_SMULWB(W_Q18[W_Q18_ptr + 19], array[4]);
			a2 = Inlines.silk_LSHIFT(a2, 1);
			a2 = Inlines.silk_SMLAWB(a2, W_Q18[W_Q18_ptr + 18], array[3]);
			a = Inlines.silk_SMLAWB(a, a2, array[3]);
			a2 = Inlines.silk_SMULWB(W_Q18[W_Q18_ptr + 24], array[4]);
			a = Inlines.silk_SMLAWB(a, a2, array[4]);
			if (a < rate_dist_Q14.Val)
			{
				rate_dist_Q14.Val = a;
				ind.Val = (sbyte)i;
				gain_Q7.Val = num2;
			}
		}
	}
}
