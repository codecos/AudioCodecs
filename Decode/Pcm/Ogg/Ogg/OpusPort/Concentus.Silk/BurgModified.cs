using Concentus.Celt;
using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk;

internal static class BurgModified
{
	private const int MAX_FRAME_SIZE = 384;

	private const int QA = 25;

	private const int N_BITS_HEAD_ROOM = 2;

	private const int MIN_RSHIFTS = -16;

	private const int MAX_RSHIFTS = 7;

	internal static void silk_burg_modified(BoxedValueInt res_nrg, BoxedValueInt res_nrg_Q, int[] A_Q16, short[] x, int x_ptr, int minInvGain_Q30, int subfr_length, int nb_subfr, int D)
	{
		int[] array = new int[16];
		int[] array2 = new int[16];
		int[] array3 = new int[16];
		int[] array4 = new int[17];
		int[] array5 = new int[17];
		int[] array6 = new int[16];
		long num = Inlines.silk_inner_prod16_aligned_64(x, x_ptr, x, x_ptr, subfr_length * nb_subfr);
		int num2 = Inlines.silk_CLZ64(num);
		int num3 = 35 - num2;
		if (num3 > 7)
		{
			num3 = 7;
		}
		if (num3 < -16)
		{
			num3 = -16;
		}
		int num4 = (int)((num3 <= 0) ? Inlines.silk_LSHIFT32((int)num, -num3) : Inlines.silk_RSHIFT64(num, num3));
		array5[0] = (array4[0] = num4 + Inlines.silk_SMMUL(42950, num4) + 1);
		Arrays.MemSetInt(array, 0, 16);
		if (num3 > 0)
		{
			for (int i = 0; i < nb_subfr; i++)
			{
				int num5 = x_ptr + i * subfr_length;
				for (int j = 1; j < D + 1; j++)
				{
					array[j - 1] += (int)Inlines.silk_RSHIFT64(Inlines.silk_inner_prod16_aligned_64(x, num5, x, num5 + j, subfr_length - j), num3);
				}
			}
		}
		else
		{
			for (int i = 0; i < nb_subfr; i++)
			{
				int num5 = x_ptr + i * subfr_length;
				CeltPitchXCorr.pitch_xcorr(x, num5, x, num5 + 1, array6, subfr_length - D, D);
				for (int j = 1; j < D + 1; j++)
				{
					int k = j + subfr_length - D;
					int num6 = 0;
					for (; k < subfr_length; k++)
					{
						num6 = Inlines.MAC16_16(num6, x[num5 + k], x[num5 + k - j]);
					}
					array6[j - 1] += num6;
				}
				for (int j = 1; j < D + 1; j++)
				{
					array[j - 1] += Inlines.silk_LSHIFT32(array6[j - 1], -num3);
				}
			}
		}
		Arrays.MemCopy(array, 0, array2, 0, 16);
		array5[0] = (array4[0] = num4 + Inlines.silk_SMMUL(42950, num4) + 1);
		int num7 = 1073741824;
		int num8 = 0;
		for (int j = 0; j < D; j++)
		{
			int num9;
			int num10;
			if (num3 > -2)
			{
				for (int i = 0; i < nb_subfr; i++)
				{
					int num5 = x_ptr + i * subfr_length;
					int b = -Inlines.silk_LSHIFT32(x[num5 + j], 16 - num3);
					int b2 = -Inlines.silk_LSHIFT32(x[num5 + subfr_length - j - 1], 16 - num3);
					num9 = Inlines.silk_LSHIFT32(x[num5 + j], 9);
					num10 = Inlines.silk_LSHIFT32(x[num5 + subfr_length - j - 1], 9);
					for (int l = 0; l < j; l++)
					{
						array[l] = Inlines.silk_SMLAWB(array[l], b, x[num5 + j - l - 1]);
						array2[l] = Inlines.silk_SMLAWB(array2[l], b2, x[num5 + subfr_length - j + l]);
						int b3 = array3[l];
						num9 = Inlines.silk_SMLAWB(num9, b3, x[num5 + j - l - 1]);
						num10 = Inlines.silk_SMLAWB(num10, b3, x[num5 + subfr_length - j + l]);
					}
					num9 = Inlines.silk_LSHIFT32(-num9, 7 - num3);
					num10 = Inlines.silk_LSHIFT32(-num10, 7 - num3);
					for (int l = 0; l <= j; l++)
					{
						array4[l] = Inlines.silk_SMLAWB(array4[l], num9, x[num5 + j - l]);
						array5[l] = Inlines.silk_SMLAWB(array5[l], num10, x[num5 + subfr_length - j + l - 1]);
					}
				}
			}
			else
			{
				for (int i = 0; i < nb_subfr; i++)
				{
					int num5 = x_ptr + i * subfr_length;
					int b = -Inlines.silk_LSHIFT32(x[num5 + j], -num3);
					int b2 = -Inlines.silk_LSHIFT32(x[num5 + subfr_length - j - 1], -num3);
					num9 = Inlines.silk_LSHIFT32(x[num5 + j], 17);
					num10 = Inlines.silk_LSHIFT32(x[num5 + subfr_length - j - 1], 17);
					for (int l = 0; l < j; l++)
					{
						array[l] = Inlines.silk_MLA(array[l], b, x[num5 + j - l - 1]);
						array2[l] = Inlines.silk_MLA(array2[l], b2, x[num5 + subfr_length - j + l]);
						int c = Inlines.silk_RSHIFT_ROUND(array3[l], 8);
						num9 = Inlines.silk_MLA(num9, x[num5 + j - l - 1], c);
						num10 = Inlines.silk_MLA(num10, x[num5 + subfr_length - j + l], c);
					}
					num9 = -num9;
					num10 = -num10;
					for (int l = 0; l <= j; l++)
					{
						array4[l] = Inlines.silk_SMLAWW(array4[l], num9, Inlines.silk_LSHIFT32(x[num5 + j - l], -num3 - 1));
						array5[l] = Inlines.silk_SMLAWW(array5[l], num10, Inlines.silk_LSHIFT32(x[num5 + subfr_length - j + l - 1], -num3 - 1));
					}
				}
			}
			num9 = array[j];
			num10 = array2[j];
			int a = 0;
			int num11 = Inlines.silk_ADD32(array5[0], array4[0]);
			for (int l = 0; l < j; l++)
			{
				int b3 = array3[l];
				num2 = Inlines.silk_CLZ32(Inlines.silk_abs(b3)) - 1;
				num2 = Inlines.silk_min(7, num2);
				int c = Inlines.silk_LSHIFT32(b3, num2);
				num9 = Inlines.silk_ADD_LSHIFT32(num9, Inlines.silk_SMMUL(array2[j - l - 1], c), 7 - num2);
				num10 = Inlines.silk_ADD_LSHIFT32(num10, Inlines.silk_SMMUL(array[j - l - 1], c), 7 - num2);
				a = Inlines.silk_ADD_LSHIFT32(a, Inlines.silk_SMMUL(array5[j - l], c), 7 - num2);
				num11 = Inlines.silk_ADD_LSHIFT32(num11, Inlines.silk_SMMUL(Inlines.silk_ADD32(array5[l + 1], array4[l + 1]), c), 7 - num2);
			}
			array4[j + 1] = num9;
			array5[j + 1] = num10;
			a = Inlines.silk_ADD32(a, num10);
			a = Inlines.silk_LSHIFT32(-a, 1);
			int num12 = ((Inlines.silk_abs(a) >= num11) ? ((a > 0) ? int.MaxValue : int.MinValue) : Inlines.silk_DIV32_varQ(a, num11, 31));
			num9 = 1073741824 - Inlines.silk_SMMUL(num12, num12);
			num9 = Inlines.silk_LSHIFT(Inlines.silk_SMMUL(num7, num9), 2);
			if (num9 <= minInvGain_Q30)
			{
				num10 = 1073741824 - Inlines.silk_DIV32_varQ(minInvGain_Q30, num7, 30);
				num12 = Inlines.silk_SQRT_APPROX(num10);
				num12 = Inlines.silk_RSHIFT32(num12 + Inlines.silk_DIV32(num10, num12), 1);
				num12 = Inlines.silk_LSHIFT32(num12, 16);
				if (a < 0)
				{
					num12 = -num12;
				}
				num7 = minInvGain_Q30;
				num8 = 1;
			}
			else
			{
				num7 = num9;
			}
			for (int l = 0; l < j + 1 >> 1; l++)
			{
				num9 = array3[l];
				num10 = array3[j - l - 1];
				array3[l] = Inlines.silk_ADD_LSHIFT32(num9, Inlines.silk_SMMUL(num10, num12), 1);
				array3[j - l - 1] = Inlines.silk_ADD_LSHIFT32(num10, Inlines.silk_SMMUL(num9, num12), 1);
			}
			array3[j] = Inlines.silk_RSHIFT32(num12, 6);
			if (num8 != 0)
			{
				for (int l = j + 1; l < D; l++)
				{
					array3[l] = 0;
				}
				break;
			}
			for (int l = 0; l <= j + 1; l++)
			{
				num9 = array4[l];
				num10 = array5[j - l + 1];
				array4[l] = Inlines.silk_ADD_LSHIFT32(num9, Inlines.silk_SMMUL(num10, num12), 1);
				array5[j - l + 1] = Inlines.silk_ADD_LSHIFT32(num10, Inlines.silk_SMMUL(num9, num12), 1);
			}
		}
		if (num8 != 0)
		{
			for (int l = 0; l < D; l++)
			{
				A_Q16[l] = -Inlines.silk_RSHIFT_ROUND(array3[l], 9);
			}
			if (num3 > 0)
			{
				for (int i = 0; i < nb_subfr; i++)
				{
					int num5 = x_ptr + i * subfr_length;
					num4 -= (int)Inlines.silk_RSHIFT64(Inlines.silk_inner_prod16_aligned_64(x, num5, x, num5, D), num3);
				}
			}
			else
			{
				for (int i = 0; i < nb_subfr; i++)
				{
					int num5 = x_ptr + i * subfr_length;
					num4 -= Inlines.silk_LSHIFT32(Inlines.silk_inner_prod_self(x, num5, D), -num3);
				}
			}
			res_nrg.Val = Inlines.silk_LSHIFT(Inlines.silk_SMMUL(num7, num4), 2);
			res_nrg_Q.Val = -num3;
		}
		else
		{
			int num11 = array4[0];
			int num9 = 65536;
			for (int l = 0; l < D; l++)
			{
				int c = Inlines.silk_RSHIFT_ROUND(array3[l], 9);
				num11 = Inlines.silk_SMLAWW(num11, array4[l + 1], c);
				num9 = Inlines.silk_SMLAWW(num9, c, c);
				A_Q16[l] = -c;
			}
			res_nrg.Val = Inlines.silk_SMLAWW(num11, Inlines.silk_SMMUL(42950, num4), -num9);
			res_nrg_Q.Val = -num3;
		}
	}
}
