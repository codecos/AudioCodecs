using System;
using Concentus.Celt;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

internal static class Filters
{
	private static readonly short A_fb1_20 = 10788;

	private static readonly short A_fb1_21 = -24290;

	private const int QA = 24;

	private static readonly int A_LIMIT = 16773022;

	internal static void silk_warped_LPC_analysis_filter(int[] state, int[] res_Q2, short[] coef_Q13, int coef_Q13_ptr, short[] input, int input_ptr, short lambda_Q16, int length, int order)
	{
		for (int i = 0; i < length; i++)
		{
			int num = Inlines.silk_SMLAWB(state[0], state[1], lambda_Q16);
			state[0] = Inlines.silk_LSHIFT(input[input_ptr + i], 14);
			int num2 = Inlines.silk_SMLAWB(state[1], state[2] - num, lambda_Q16);
			state[1] = num;
			int a = Inlines.silk_RSHIFT(order, 1);
			a = Inlines.silk_SMLAWB(a, num, coef_Q13[coef_Q13_ptr]);
			for (int j = 2; j < order; j += 2)
			{
				num = Inlines.silk_SMLAWB(state[j], state[j + 1] - num2, lambda_Q16);
				state[j] = num2;
				a = Inlines.silk_SMLAWB(a, num2, coef_Q13[coef_Q13_ptr + j - 1]);
				num2 = Inlines.silk_SMLAWB(state[j + 1], state[j + 2] - num, lambda_Q16);
				state[j + 1] = num;
				a = Inlines.silk_SMLAWB(a, num, coef_Q13[coef_Q13_ptr + j]);
			}
			state[order] = num2;
			a = Inlines.silk_SMLAWB(a, num2, coef_Q13[coef_Q13_ptr + order - 1]);
			res_Q2[i] = Inlines.silk_LSHIFT(input[input_ptr + i], 2) - Inlines.silk_RSHIFT_ROUND(a, 9);
		}
	}



	private static void silk_prefilt(SilkPrefilterState P, int[] st_res_Q12, int[] xw_Q3, int xw_Q3_ptr, int HarmShapeFIRPacked_Q12, int Tilt_Q14, int LF_shp_Q14, int lag, int length)
	{
		short[] sLTP_shp = P.sLTP_shp;
		int num = P.sLTP_shp_buf_idx;
		int num2 = P.sLF_AR_shp_Q12;
		int num3 = P.sLF_MA_shp_Q12;
		for (int i = 0; i < length; i++)
		{
			int a;
			if (lag > 0)
			{
				int num4 = lag + num;
				a = Inlines.silk_SMULBB(sLTP_shp[(num4 - 1 - 1) & 0x1FF], HarmShapeFIRPacked_Q12);
				a = Inlines.silk_SMLABT(a, sLTP_shp[(num4 - 1) & 0x1FF], HarmShapeFIRPacked_Q12);
				a = Inlines.silk_SMLABB(a, sLTP_shp[(num4 - 1 + 1) & 0x1FF], HarmShapeFIRPacked_Q12);
			}
			else
			{
				a = 0;
			}
			int a2 = Inlines.silk_SMULWB(num2, Tilt_Q14);
			int a3 = Inlines.silk_SMLAWB(Inlines.silk_SMULWT(num2, LF_shp_Q14), num3, LF_shp_Q14);
			num2 = Inlines.silk_SUB32(st_res_Q12[i], Inlines.silk_LSHIFT(a2, 2));
			num3 = Inlines.silk_SUB32(num2, Inlines.silk_LSHIFT(a3, 2));
			num = (num - 1) & 0x1FF;
			sLTP_shp[num] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(num3, 12));
			xw_Q3[xw_Q3_ptr + i] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SUB32(num3, a), 9);
		}
		P.sLF_AR_shp_Q12 = num2;
		P.sLF_MA_shp_Q12 = num3;
		P.sLTP_shp_buf_idx = num;
	}

	/// <summary>
	/// Second order ARMA filter, alternative implementation
	/// </summary>
	/// <param name="input">I     input signal</param>
	/// <param name="B_Q28">I     MA coefficients [3]</param>
	/// <param name="A_Q28">I     AR coefficients [2]</param>
	/// <param name="S">I/O   State vector [2]</param>
	/// <param name="output">O     output signal</param>
	/// <param name="len">I     signal length (must be even)</param>
	/// <param name="stride">I     Operate on interleaved signal if &gt; 1</param>
	internal static void silk_biquad_alt(ReadOnlySpan<short> input, int input_ptr, int[] B_Q28, int[] A_Q28, int[] S, Span<short> output, int output_ptr, int len, int stride)
	{
		int b = -A_Q28[0] & 0x3FFF;
		int c = Inlines.silk_RSHIFT(-A_Q28[0], 14);
		int b2 = -A_Q28[1] & 0x3FFF;
		int c2 = Inlines.silk_RSHIFT(-A_Q28[1], 14);
		for (int i = 0; i < len; i++)
		{
			int c3 = input[input_ptr + i * stride];
			int num = Inlines.silk_LSHIFT(Inlines.silk_SMLAWB(S[0], B_Q28[0], c3), 2);
			S[0] = S[1] + Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(num, b), 14);
			S[0] = Inlines.silk_SMLAWB(S[0], num, c);
			S[0] = Inlines.silk_SMLAWB(S[0], B_Q28[1], c3);
			S[1] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(num, b2), 14);
			S[1] = Inlines.silk_SMLAWB(S[1], num, c2);
			S[1] = Inlines.silk_SMLAWB(S[1], B_Q28[2], c3);
			output[output_ptr + i * stride] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT(num + 16384 - 1, 14));
		}
	}

	internal static void silk_biquad_alt(ReadOnlySpan<short> input, int input_ptr, int[] B_Q28, int[] A_Q28, Span<int> S, int S_ptr, Span<short> output, int output_ptr, int len, int stride)
	{
		int b = -A_Q28[0] & 0x3FFF;
		int c = Inlines.silk_RSHIFT(-A_Q28[0], 14);
		int b2 = -A_Q28[1] & 0x3FFF;
		int c2 = Inlines.silk_RSHIFT(-A_Q28[1], 14);
		for (int i = 0; i < len; i++)
		{
			int index = S_ptr + 1;
			int c3 = input[input_ptr + i * stride];
			int num = Inlines.silk_LSHIFT(Inlines.silk_SMLAWB(S[S_ptr], B_Q28[0], c3), 2);
			S[S_ptr] = S[index] + Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(num, b), 14);
			S[S_ptr] = Inlines.silk_SMLAWB(S[S_ptr], num, c);
			S[S_ptr] = Inlines.silk_SMLAWB(S[S_ptr], B_Q28[1], c3);
			S[index] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_SMULWB(num, b2), 14);
			S[index] = Inlines.silk_SMLAWB(S[index], num, c2);
			S[index] = Inlines.silk_SMLAWB(S[index], B_Q28[2], c3);
			output[output_ptr + i * stride] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT(num + 16384 - 1, 14));
		}
	}

	/// <summary>
	/// Split signal into two decimated bands using first-order allpass filters
	/// </summary>
	/// <param name="input">I    Input signal [N]</param>
	/// <param name="S">I/O  State vector [2]</param>
	/// <param name="outL">O    Low band [N/2]</param>
	/// <param name="outH">O    High band [N/2]</param>
	/// <param name="N">I    Number of input samples</param>
	internal static void silk_ana_filt_bank_1(Span<short> input, int input_ptr, int[] S, short[] outL, Span<short> outH, int outH_ptr, int N)
	{
		int num = Inlines.silk_RSHIFT(N, 1);
		for (int i = 0; i < num; i++)
		{
			int a = Inlines.silk_LSHIFT(input[input_ptr + 2 * i], 10);
			int num2 = Inlines.silk_SUB32(a, S[0]);
			int b = Inlines.silk_SMLAWB(num2, num2, A_fb1_21);
			int b2 = Inlines.silk_ADD32(S[0], b);
			S[0] = Inlines.silk_ADD32(a, b);
			a = Inlines.silk_LSHIFT(input[input_ptr + 2 * i + 1], 10);
			b = Inlines.silk_SMULWB(Inlines.silk_SUB32(a, S[1]), A_fb1_20);
			int a2 = Inlines.silk_ADD32(S[1], b);
			S[1] = Inlines.silk_ADD32(a, b);
			outL[i] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(Inlines.silk_ADD32(a2, b2), 11));
			outH[outH_ptr + i] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(Inlines.silk_SUB32(a2, b2), 11));
		}
	}

	/// <summary>
	/// Chirp (bandwidth expand) LP AR filter
	/// </summary>
	/// <param name="ar">I/O  AR filter to be expanded (without leading 1)</param>
	/// <param name="d">I    Length of ar</param>
	/// <param name="chirp_Q16">I    Chirp factor in Q16</param>
	internal static void silk_bwexpander_32(int[] ar, int d, int chirp_Q16)
	{
		int b = chirp_Q16 - 65536;
		for (int i = 0; i < d - 1; i++)
		{
			ar[i] = Inlines.silk_SMULWW(chirp_Q16, ar[i]);
			chirp_Q16 += Inlines.silk_RSHIFT_ROUND(Inlines.silk_MUL(chirp_Q16, b), 16);
		}
		ar[d - 1] = Inlines.silk_SMULWW(chirp_Q16, ar[d - 1]);
	}

	/// <summary>
	/// Elliptic/Cauer filters designed with 0.1 dB passband ripple,
	/// 80 dB minimum stopband attenuation, and
	/// [0.95 : 0.15 : 0.35] normalized cut off frequencies.
	/// Helper function, interpolates the filter taps
	/// </summary>
	/// <param name="B_Q28">order [TRANSITION_NB]</param>
	/// <param name="A_Q28">order [TRANSITION_NA]</param>
	/// <param name="ind"></param>
	/// <param name="fac_Q16"></param>
	internal static void silk_LP_interpolate_filter_taps(int[] B_Q28, int[] A_Q28, int ind, int fac_Q16)
	{
		if (ind < 4)
		{
			if (fac_Q16 > 0)
			{
				if (fac_Q16 < 32768)
				{
					for (int i = 0; i < 3; i++)
					{
						B_Q28[i] = Inlines.silk_SMLAWB(Tables.silk_Transition_LP_B_Q28[ind][i], Tables.silk_Transition_LP_B_Q28[ind + 1][i] - Tables.silk_Transition_LP_B_Q28[ind][i], fac_Q16);
					}
					for (int j = 0; j < 2; j++)
					{
						A_Q28[j] = Inlines.silk_SMLAWB(Tables.silk_Transition_LP_A_Q28[ind][j], Tables.silk_Transition_LP_A_Q28[ind + 1][j] - Tables.silk_Transition_LP_A_Q28[ind][j], fac_Q16);
					}
				}
				else
				{
					for (int i = 0; i < 3; i++)
					{
						B_Q28[i] = Inlines.silk_SMLAWB(Tables.silk_Transition_LP_B_Q28[ind + 1][i], Tables.silk_Transition_LP_B_Q28[ind + 1][i] - Tables.silk_Transition_LP_B_Q28[ind][i], fac_Q16 - 65536);
					}
					for (int j = 0; j < 2; j++)
					{
						A_Q28[j] = Inlines.silk_SMLAWB(Tables.silk_Transition_LP_A_Q28[ind + 1][j], Tables.silk_Transition_LP_A_Q28[ind + 1][j] - Tables.silk_Transition_LP_A_Q28[ind][j], fac_Q16 - 65536);
					}
				}
			}
			else
			{
				Arrays.MemCopy(Tables.silk_Transition_LP_B_Q28[ind], 0, B_Q28, 0, 3);
				Arrays.MemCopy(Tables.silk_Transition_LP_A_Q28[ind], 0, A_Q28, 0, 2);
			}
		}
		else
		{
			Arrays.MemCopy(Tables.silk_Transition_LP_B_Q28[4], 0, B_Q28, 0, 3);
			Arrays.MemCopy(Tables.silk_Transition_LP_A_Q28[4], 0, A_Q28, 0, 2);
		}
	}

	/// <summary>
	/// LPC analysis filter
	/// NB! State is kept internally and the
	/// filter always starts with zero state
	/// first d output samples are set to zero
	/// </summary>
	/// <param name="output">O    Output signal</param>
	/// <param name="input">I    Input signal</param>
	/// <param name="B">I    MA prediction coefficients, Q12 [order]</param>
	/// <param name="len">I    Signal length</param>
	/// <param name="d">I    Filter order</param>
	internal static void silk_LPC_analysis_filter(Span<short> output, int output_ptr, Span<short> input, int input_ptr, Span<short> B, int B_ptr, int len, int d)
	{
		Span<short> mem = stackalloc short[16];
		Span<short> num = stackalloc short[16];
		for (int i = 0; i < d; i++)
		{
			num[i] = (short)(-B[B_ptr + i]);
		}
		for (int i = 0; i < d; i++)
		{
			mem[i] = input[input_ptr + d - i - 1];
		}
		Kernels.celt_fir(input.Slice(input_ptr + d), num, output.Slice(output_ptr + d), len - d, d, mem);
		for (int i = output_ptr; i < output_ptr + d; i++)
		{
			output[i] = 0;
		}
	}

	/// <summary>
	/// Compute inverse of LPC prediction gain, and
	/// test if LPC coefficients are stable (all poles within unit circle)
	/// </summary>
	/// <param name="A_QA">Prediction coefficients, order [2][SILK_MAX_ORDER_LPC]</param>
	/// <param name="order">Prediction order</param>
	/// <returns>inverse prediction gain in energy domain, Q30</returns>
	internal static int LPC_inverse_pred_gain_QA(int[][] A_QA, int order)
	{
		int[] array = A_QA[order & 1];
		int a = 1073741824;
		int num2;
		int num3;
		for (int num = order - 1; num > 0; num--)
		{
			if (array[num] > A_LIMIT || array[num] < -A_LIMIT)
			{
				return 0;
			}
			num2 = -Inlines.silk_LSHIFT(array[num], 7);
			num3 = 1073741824 - Inlines.silk_SMMUL(num2, num2);
			int num4 = 32 - Inlines.silk_CLZ32(Inlines.silk_abs(num3));
			int b = Inlines.silk_INVERSE32_varQ(num3, num4 + 30);
			a = Inlines.silk_LSHIFT(Inlines.silk_SMMUL(a, num3), 2);
			int[] array2 = array;
			array = A_QA[num & 1];
			for (int i = 0; i < num; i++)
			{
				int a2 = array2[i] - Inlines.MUL32_FRAC_Q(array2[num - i - 1], num2, 31);
				array[i] = Inlines.MUL32_FRAC_Q(a2, b, num4);
			}
		}
		if (array[0] > A_LIMIT || array[0] < -A_LIMIT)
		{
			return 0;
		}
		num2 = -Inlines.silk_LSHIFT(array[0], 7);
		num3 = 1073741824 - Inlines.silk_SMMUL(num2, num2);
		return Inlines.silk_LSHIFT(Inlines.silk_SMMUL(a, num3), 2);
	}

	/// <summary>
	/// For input in Q12 domain
	/// </summary>
	/// <param name="A_Q12">Prediction coefficients, Q12 [order]</param>
	/// <param name="order">I   Prediction order</param>
	/// <returns>inverse prediction gain in energy domain, Q30</returns>
	internal static int silk_LPC_inverse_pred_gain(short[] A_Q12, int order)
	{
		int[][] array = new int[2][]
		{
			new int[order],
			new int[order]
		};
		int num = 0;
		int[] array2 = array[order & 1];
		for (int i = 0; i < order; i++)
		{
			num += A_Q12[i];
			array2[i] = Inlines.silk_LSHIFT32(A_Q12[i], 12);
		}
		if (num >= 4096)
		{
			return 0;
		}
		return LPC_inverse_pred_gain_QA(array, order);
	}
}
