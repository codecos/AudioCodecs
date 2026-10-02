using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk;

internal static class GainQuantization
{
	private static readonly int OFFSET = 2090;

	private static readonly int SCALE_Q16 = 2251;

	private static readonly int INV_SCALE_Q16 = 1907825;

	/// <summary>
	/// Gain scalar quantization with hysteresis, uniform on log scale
	/// </summary>
	/// <param name="ind">O    gain indices [MAX_NB_SUBFR]</param>
	/// <param name="gain_Q16">I/O  gains (quantized out) [MAX_NB_SUBFR]</param>
	/// <param name="prev_ind">I/O  last index in previous frame. [Porting note] original implementation passed this as an int8*</param>
	/// <param name="conditional">I    first gain is delta coded if 1</param>
	/// <param name="nb_subfr">I    number of subframes</param>
	internal static void silk_gains_quant(sbyte[] ind, int[] gain_Q16, BoxedValueSbyte prev_ind, int conditional, int nb_subfr)
	{
		for (int i = 0; i < nb_subfr; i++)
		{
			ind[i] = (sbyte)Inlines.silk_SMULWB(SCALE_Q16, Inlines.silk_lin2log(gain_Q16[i]) - OFFSET);
			if (ind[i] < prev_ind.Val)
			{
				ind[i]++;
			}
			ind[i] = (sbyte)Inlines.silk_LIMIT_int(ind[i], 0, 63);
			if (i == 0 && conditional == 0)
			{
				ind[i] = (sbyte)Inlines.silk_LIMIT_int(ind[i], prev_ind.Val + -4, 63);
				prev_ind.Val = ind[i];
			}
			else
			{
				ind[i] -= prev_ind.Val;
				int num = 8 + prev_ind.Val;
				if (ind[i] > num)
				{
					ind[i] = (sbyte)(num + Inlines.silk_RSHIFT(ind[i] - num + 1, 1));
				}
				ind[i] = (sbyte)Inlines.silk_LIMIT_int(ind[i], -4, 36);
				if (ind[i] > num)
				{
					prev_ind.Val += (sbyte)(Inlines.silk_LSHIFT(ind[i], 1) - num);
				}
				else
				{
					prev_ind.Val += ind[i];
				}
				ind[i] -= -4;
			}
			gain_Q16[i] = Inlines.silk_log2lin(Inlines.silk_min_32(Inlines.silk_SMULWB(INV_SCALE_Q16, prev_ind.Val) + OFFSET, 3967));
		}
	}

	/// <summary>
	/// Gains scalar dequantization, uniform on log scale
	/// </summary>
	/// <param name="gain_Q16">O    quantized gains [MAX_NB_SUBFR]</param>
	/// <param name="ind">I    gain indices [MAX_NB_SUBFR]</param>
	/// <param name="prev_ind">I/O  last index in previous frame [Porting note] original implementation passed this as an int8*</param>
	/// <param name="conditional">I    first gain is delta coded if 1</param>
	/// <param name="nb_subfr">I    number of subframes</param>
	internal static void silk_gains_dequant(int[] gain_Q16, sbyte[] ind, BoxedValueSbyte prev_ind, int conditional, int nb_subfr)
	{
		for (int i = 0; i < nb_subfr; i++)
		{
			if (i == 0 && conditional == 0)
			{
				prev_ind.Val = (sbyte)Inlines.silk_max_int(ind[i], prev_ind.Val - 16);
			}
			else
			{
				int num = ind[i] + -4;
				int num2 = 8 + prev_ind.Val;
				if (num > num2)
				{
					prev_ind.Val += (sbyte)(Inlines.silk_LSHIFT(num, 1) - num2);
				}
				else
				{
					prev_ind.Val += (sbyte)num;
				}
			}
			prev_ind.Val = (sbyte)Inlines.silk_LIMIT_int(prev_ind.Val, 0, 63);
			gain_Q16[i] = Inlines.silk_log2lin(Inlines.silk_min_32(Inlines.silk_SMULWB(INV_SCALE_Q16, prev_ind.Val) + OFFSET, 3967));
		}
	}

	/// <summary>
	/// Compute unique identifier of gain indices vector
	/// </summary>
	/// <param name="ind">I    gain indices [MAX_NB_SUBFR]</param>
	/// <param name="nb_subfr">I    number of subframes</param>
	/// <returns>unique identifier of gains</returns>
	internal static int silk_gains_ID(sbyte[] ind, int nb_subfr)
	{
		int num = 0;
		for (int i = 0; i < nb_subfr; i++)
		{
			num = Inlines.silk_ADD_LSHIFT32(ind[i], num, 8);
		}
		return num;
	}
}
