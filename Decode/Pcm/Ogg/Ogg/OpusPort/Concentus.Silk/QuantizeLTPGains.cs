using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk;

internal static class QuantizeLTPGains
{
	internal static void silk_quant_LTP_gains(short[] B_Q14, sbyte[] cbk_index, BoxedValueSbyte periodicity_index, BoxedValueInt sum_log_gain_Q7, int[] W_Q18, int mu_Q9, int lowComplexity, int nb_subfr)
	{
		sbyte[] array = new sbyte[4];
		int num = int.MaxValue;
		int val = 0;
		sbyte[][] cb_Q;
		for (int i = 0; i < 3; i++)
		{
			int num2 = 51;
			byte[] cl_Q = Tables.silk_LTP_gain_BITS_Q5_ptrs[i];
			cb_Q = Tables.silk_LTP_vq_ptrs_Q7[i];
			byte[] cb_gain_Q = Tables.silk_LTP_vq_gain_ptrs_Q7[i];
			int l = Tables.silk_LTP_vq_sizes[i];
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = sum_log_gain_Q7.Val;
			for (int j = 0; j < nb_subfr; j++)
			{
				int max_gain_Q = Inlines.silk_log2lin(5333 - num6 + 896) - num2;
				BoxedValueSbyte boxedValueSbyte = new BoxedValueSbyte(array[j]);
				BoxedValueInt boxedValueInt = new BoxedValueInt();
				BoxedValueInt boxedValueInt2 = new BoxedValueInt();
				VQ_WMat_EC.silk_VQ_WMat_EC(boxedValueSbyte, boxedValueInt, boxedValueInt2, B_Q14, num4, W_Q18, num3, cb_Q, cb_gain_Q, cl_Q, mu_Q9, max_gain_Q, l);
				int val2 = boxedValueInt.Val;
				int val3 = boxedValueInt2.Val;
				array[j] = boxedValueSbyte.Val;
				num5 = Inlines.silk_ADD_POS_SAT32(num5, val2);
				num6 = Inlines.silk_max(0, num6 + Inlines.silk_lin2log(num2 + val3) - 896);
				num4 += 5;
				num3 += 25;
			}
			num5 = Inlines.silk_min(2147483646, num5);
			if (num5 < num)
			{
				num = num5;
				periodicity_index.Val = (sbyte)i;
				Arrays.MemCopy(array, 0, cbk_index, 0, nb_subfr);
				val = num6;
			}
			if (lowComplexity != 0 && num5 < Tables.silk_LTP_gain_middle_avg_RD_Q14)
			{
				break;
			}
		}
		cb_Q = Tables.silk_LTP_vq_ptrs_Q7[periodicity_index.Val];
		for (int j = 0; j < nb_subfr; j++)
		{
			for (int i = 0; i < 5; i++)
			{
				B_Q14[j * 5 + i] = (short)Inlines.silk_LSHIFT(cb_Q[cbk_index[j]][i], 7);
			}
		}
		sum_log_gain_Q7.Val = val;
	}
}
