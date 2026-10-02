using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

/// <summary>
/// Normalized line spectrum frequency processor
/// </summary>
internal static class NLSF
{
	private const int MAX_LPC_ORDER = 16;

	private const int MAX_STABILIZE_LOOPS = 20;

	private const int QA = 16;

	/// <summary>
	/// Number of binary divisions, when not in low complexity mode
	/// </summary>
	private const int BIN_DIV_STEPS_A2NLSF = 3;

	private const int MAX_ITERATIONS_A2NLSF = 30;

	private static readonly byte[] ordering16 = new byte[16]
	{
		0, 15, 8, 7, 4, 11, 12, 3, 2, 13,
		10, 5, 6, 9, 14, 1
	};

	private static readonly byte[] ordering10 = new byte[10] { 0, 9, 6, 3, 4, 5, 8, 1, 2, 7 };

	/// <summary>
	/// Compute quantization errors for an LPC_order element input vector for a VQ codebook
	/// </summary>
	/// <param name="err_Q26">(O) Quantization errors [K]</param>
	/// <param name="in_Q15">(I) Input vectors to be quantized [LPC_order]</param>
	/// <param name="pCB_Q8">(I) Codebook vectors [K*LPC_order]</param>
	/// <param name="K">(I) Number of codebook vectors</param>
	/// <param name="LPC_order">(I) Number of LPCs</param>
	internal static void silk_NLSF_VQ(int[] err_Q26, short[] in_Q15, byte[] pCB_Q8, int K, int LPC_order)
	{
		int num = 0;
		for (int i = 0; i < K; i++)
		{
			int num2 = 0;
			for (int j = 0; j < LPC_order; j += 2)
			{
				int num3 = Inlines.silk_SUB_LSHIFT32(in_Q15[j], pCB_Q8[num++], 7);
				int a = Inlines.silk_SMULBB(num3, num3);
				num3 = Inlines.silk_SUB_LSHIFT32(in_Q15[j + 1], pCB_Q8[num++], 7);
				a = Inlines.silk_SMLABB(a, num3, num3);
				num2 = Inlines.silk_ADD_RSHIFT32(num2, a, 4);
			}
			err_Q26[i] = num2;
		}
	}

	/// <summary>
	/// Laroia low complexity NLSF weights
	/// </summary>
	/// <param name="pNLSFW_Q_OUT">(O) Pointer to input vector weights [D]</param>
	/// <param name="pNLSF_Q15">(I) Pointer to input vector [D]</param>
	/// <param name="D">(I) Input vector dimension (even)</param>
	internal static void silk_NLSF_VQ_weights_laroia(Span<short> pNLSFW_Q_OUT, Span<short> pNLSF_Q15, int D)
	{
		int b = Inlines.silk_max_int(pNLSF_Q15[0], 1);
		b = Inlines.silk_DIV32(131072, b);
		int b2 = Inlines.silk_max_int(pNLSF_Q15[1] - pNLSF_Q15[0], 1);
		b2 = Inlines.silk_DIV32(131072, b2);
		pNLSFW_Q_OUT[0] = (short)Inlines.silk_min_int(b + b2, 32767);
		for (int i = 1; i < D - 1; i += 2)
		{
			b = Inlines.silk_max_int(pNLSF_Q15[i + 1] - pNLSF_Q15[i], 1);
			b = Inlines.silk_DIV32(131072, b);
			pNLSFW_Q_OUT[i] = (short)Inlines.silk_min_int(b + b2, 32767);
			b2 = Inlines.silk_max_int(pNLSF_Q15[i + 2] - pNLSF_Q15[i + 1], 1);
			b2 = Inlines.silk_DIV32(131072, b2);
			pNLSFW_Q_OUT[i + 1] = (short)Inlines.silk_min_int(b + b2, 32767);
		}
		b = Inlines.silk_max_int(32768 - pNLSF_Q15[D - 1], 1);
		b = Inlines.silk_DIV32(131072, b);
		pNLSFW_Q_OUT[D - 1] = (short)Inlines.silk_min_int(b + b2, 32767);
	}

	/// <summary>
	/// Returns RD value in Q30
	/// </summary>
	/// <param name="x_Q10">(O) Output [ order ]</param>
	/// <param name="indices">(I) Quantization indices [ order ]</param>
	/// <param name="pred_coef_Q8">(I) Backward predictor coefs [ order ]</param>
	/// <param name="quant_step_size_Q16">(I) Quantization step size</param>
	/// <param name="order">(I) Number of input values</param>
	internal static void silk_NLSF_residual_dequant(Span<short> x_Q10, Span<sbyte> indices, Span<byte> pred_coef_Q8, int quant_step_size_Q16, short order)
	{
		short num = 0;
		for (int num2 = order - 1; num2 >= 0; num2--)
		{
			int a = Inlines.silk_RSHIFT(Inlines.silk_SMULBB(num, pred_coef_Q8[num2]), 8);
			num = Inlines.silk_LSHIFT16(indices[num2], 10);
			if (num > 0)
			{
				num = Inlines.silk_SUB16(num, 102);
			}
			else if (num < 0)
			{
				num = Inlines.silk_ADD16(num, 102);
			}
			num = (short)Inlines.silk_SMLAWB(a, num, quant_step_size_Q16);
			x_Q10[num2] = num;
		}
	}

	/// <summary>
	/// Unpack predictor values and indices for entropy coding tables
	/// </summary>
	/// <param name="ec_ix">(O) Indices to entropy tables [ LPC_ORDER ]</param>
	/// <param name="pred_Q8">(O) LSF predictor [ LPC_ORDER ]</param>
	/// <param name="psNLSF_CB">(I) Codebook object</param>
	/// <param name="CB1_index">(I) Index of vector in first LSF codebook</param>
	internal static void silk_NLSF_unpack(Span<short> ec_ix, Span<byte> pred_Q8, NLSFCodebook psNLSF_CB, int CB1_index)
	{
		byte[] ec_sel = psNLSF_CB.ec_sel;
		int num = CB1_index * psNLSF_CB.order / 2;
		for (int i = 0; i < psNLSF_CB.order; i += 2)
		{
			byte b = ec_sel[num];
			num++;
			ec_ix[i] = (short)Inlines.silk_SMULBB(Inlines.silk_RSHIFT(b, 1) & 7, 9);
			pred_Q8[i] = psNLSF_CB.pred_Q8[i + (b & 1) * (psNLSF_CB.order - 1)];
			ec_ix[i + 1] = (short)Inlines.silk_SMULBB(Inlines.silk_RSHIFT(b, 5) & 7, 9);
			pred_Q8[i + 1] = psNLSF_CB.pred_Q8[i + (Inlines.silk_RSHIFT(b, 4) & 1) * (psNLSF_CB.order - 1) + 1];
		}
	}

	/// <summary>
	/// NLSF stabilizer, for a single input data vector
	/// </summary>
	/// <param name="NLSF_Q15">(I/O) Unstable/stabilized normalized LSF vector in Q15 [L]</param>
	/// <param name="NDeltaMin_Q15">(I) Min distance vector, NDeltaMin_Q15[L] must be &gt;= 1 [L+1]</param>
	/// <param name="L">(I) Number of NLSF parameters in the input vector</param>
	internal static void silk_NLSF_stabilize(short[] NLSF_Q15, short[] NDeltaMin_Q15, int L)
	{
		int num = 0;
		int i;
		for (i = 0; i < 20; i++)
		{
			int num2 = NLSF_Q15[0] - NDeltaMin_Q15[0];
			num = 0;
			int num3;
			for (int j = 1; j <= L - 1; j++)
			{
				num3 = NLSF_Q15[j] - (NLSF_Q15[j - 1] + NDeltaMin_Q15[j]);
				if (num3 < num2)
				{
					num2 = num3;
					num = j;
				}
			}
			num3 = 32768 - (NLSF_Q15[L - 1] + NDeltaMin_Q15[L]);
			if (num3 < num2)
			{
				num2 = num3;
				num = L;
			}
			if (num2 >= 0)
			{
				return;
			}
			if (num == 0)
			{
				NLSF_Q15[0] = NDeltaMin_Q15[0];
				continue;
			}
			if (num == L)
			{
				NLSF_Q15[L - 1] = (short)(32768 - NDeltaMin_Q15[L]);
				continue;
			}
			int num4 = 0;
			for (int k = 0; k < num; k++)
			{
				num4 += NDeltaMin_Q15[k];
			}
			num4 += Inlines.silk_RSHIFT(NDeltaMin_Q15[num], 1);
			int num5 = 32768;
			for (int k = L; k > num; k--)
			{
				num5 -= NDeltaMin_Q15[k];
			}
			num5 -= Inlines.silk_RSHIFT(NDeltaMin_Q15[num], 1);
			short num6 = (short)Inlines.silk_LIMIT_32(Inlines.silk_RSHIFT_ROUND(NLSF_Q15[num - 1] + NLSF_Q15[num], 1), num4, num5);
			NLSF_Q15[num - 1] = (short)(num6 - Inlines.silk_RSHIFT(NDeltaMin_Q15[num], 1));
			NLSF_Q15[num] = (short)(NLSF_Q15[num - 1] + NDeltaMin_Q15[num]);
		}
		if (i == 20)
		{
			Sort.silk_insertion_sort_increasing_all_values_int16(NLSF_Q15, L);
			NLSF_Q15[0] = (short)Inlines.silk_max_int(NLSF_Q15[0], NDeltaMin_Q15[0]);
			for (int j = 1; j < L; j++)
			{
				NLSF_Q15[j] = (short)Inlines.silk_max_int(NLSF_Q15[j], NLSF_Q15[j - 1] + NDeltaMin_Q15[j]);
			}
			NLSF_Q15[L - 1] = (short)Inlines.silk_min_int(NLSF_Q15[L - 1], 32768 - NDeltaMin_Q15[L]);
			for (int j = L - 2; j >= 0; j--)
			{
				NLSF_Q15[j] = (short)Inlines.silk_min_int(NLSF_Q15[j], NLSF_Q15[j + 1] - NDeltaMin_Q15[j + 1]);
			}
		}
	}

	/// <summary>
	/// NLSF vector decoder
	/// </summary>
	/// <param name="pNLSF_Q15">(O) Quantized NLSF vector [ LPC_ORDER ]</param>
	/// <param name="NLSFIndices">(I) Codebook path vector [ LPC_ORDER + 1 ]</param>
	/// <param name="psNLSF_CB">(I) Codebook object</param>
	internal static void silk_NLSF_decode(short[] pNLSF_Q15, sbyte[] NLSFIndices, NLSFCodebook psNLSF_CB)
	{
		Span<byte> span = stackalloc byte[16];
		Span<short> ec_ix = stackalloc short[16];
		Span<short> x_Q = stackalloc short[16];
		Span<short> pNLSFW_Q_OUT = stackalloc short[16];
		byte[] cB1_NLSF_Q = psNLSF_CB.CB1_NLSF_Q8;
		int num = NLSFIndices[0] * psNLSF_CB.order;
		for (int i = 0; i < psNLSF_CB.order; i++)
		{
			pNLSF_Q15[i] = Inlines.silk_LSHIFT16(cB1_NLSF_Q[num + i], 7);
		}
		silk_NLSF_unpack(ec_ix, span, psNLSF_CB, NLSFIndices[0]);
		silk_NLSF_residual_dequant(x_Q, NLSFIndices.AsSpan().Slice(1), span, psNLSF_CB.quantStepSize_Q16, psNLSF_CB.order);
		silk_NLSF_VQ_weights_laroia(pNLSFW_Q_OUT, pNLSF_Q15, psNLSF_CB.order);
		for (int i = 0; i < psNLSF_CB.order; i++)
		{
			int num2 = Inlines.silk_SQRT_APPROX(Inlines.silk_LSHIFT(pNLSFW_Q_OUT[i], 16));
			int a = Inlines.silk_ADD32(pNLSF_Q15[i], Inlines.silk_DIV32_16(Inlines.silk_LSHIFT(x_Q[i], 14), (short)num2));
			pNLSF_Q15[i] = (short)Inlines.silk_LIMIT(a, 0, 32767);
		}
		silk_NLSF_stabilize(pNLSF_Q15, psNLSF_CB.deltaMin_Q15, psNLSF_CB.order);
	}

	/// <summary>
	/// Delayed-decision quantizer for NLSF residuals
	/// </summary>
	/// <param name="indices">(O) Quantization indices [ order ]</param>
	/// <param name="x_Q10">(O) Input [ order ]</param>
	/// <param name="w_Q5">(I) Weights [ order ] </param>
	/// <param name="pred_coef_Q8">(I) Backward predictor coefs [ order ]</param>
	/// <param name="ec_ix">(I) Indices to entropy coding tables [ order ]</param>
	/// <param name="ec_rates_Q5">(I) Rates []</param>
	/// <param name="quant_step_size_Q16">(I) Quantization step size</param>
	/// <param name="inv_quant_step_size_Q6">(I) Inverse quantization step size</param>
	/// <param name="mu_Q20">(I) R/D tradeoff</param>
	/// <param name="order">(I) Number of input values</param>
	/// <returns>RD value in Q25</returns>
	/// Fixme: Optimize this method!
	internal static int silk_NLSF_del_dec_quant(sbyte[] indices, Span<short> x_Q10, Span<short> w_Q5, Span<byte> pred_coef_Q8, Span<short> ec_ix, byte[] ec_rates_Q5, int quant_step_size_Q16, short inv_quant_step_size_Q6, int mu_Q20, short order)
	{
		Span<int> span = stackalloc int[4];
		sbyte[][] array = new sbyte[4][];
		int i;
		for (i = 0; i < 4; i++)
		{
			array[i] = new sbyte[16];
		}
		Span<short> span2 = stackalloc short[8];
		Span<int> span3 = stackalloc int[8];
		Span<int> span4 = stackalloc int[4];
		Span<int> span5 = stackalloc int[4];
		Span<int> span6 = stackalloc int[20];
		Span<int> span7 = stackalloc int[20];
		for (i = -10; i <= 9; i++)
		{
			int num = Inlines.silk_LSHIFT(i, 10);
			int num2 = Inlines.silk_ADD16((short)num, 1024);
			if (i > 0)
			{
				num = Inlines.silk_SUB16((short)num, 102);
				num2 = Inlines.silk_SUB16((short)num2, 102);
			}
			else
			{
				switch (i)
				{
				case 0:
					num2 = Inlines.silk_SUB16((short)num2, 102);
					break;
				case -1:
					num = Inlines.silk_ADD16((short)num, 102);
					break;
				default:
					num = Inlines.silk_ADD16((short)num, 102);
					num2 = Inlines.silk_ADD16((short)num2, 102);
					break;
				}
			}
			span6[i + 10] = Inlines.silk_SMULWB(num, quant_step_size_Q16);
			span7[i + 10] = Inlines.silk_SMULWB(num2, quant_step_size_Q16);
		}
		int num3 = 1;
		span3[0] = 0;
		span2[0] = 0;
		i = order - 1;
		int a2;
		while (true)
		{
			int a = Inlines.silk_LSHIFT(pred_coef_Q8[i], 8);
			int num4 = x_Q10[i];
			for (int j = 0; j < num3; j++)
			{
				int num5 = Inlines.silk_SMULWB(a, span2[j]);
				int b = Inlines.silk_SUB16((short)num4, (short)num5);
				a2 = Inlines.silk_SMULWB(inv_quant_step_size_Q6, b);
				a2 = Inlines.silk_LIMIT(a2, -10, 9);
				array[j][i] = (sbyte)a2;
				int num6 = ec_ix[i] + a2;
				int num = span6[a2 + 10];
				int num2 = span7[a2 + 10];
				num = Inlines.silk_ADD16((short)num, (short)num5);
				num2 = Inlines.silk_ADD16((short)num2, (short)num5);
				span2[j] = (short)num;
				span2[j + num3] = (short)num2;
				int num7;
				int c;
				if (a2 + 1 >= 4)
				{
					if (a2 + 1 == 4)
					{
						num7 = ec_rates_Q5[num6 + 4];
						c = 280;
					}
					else
					{
						num7 = Inlines.silk_SMLABB(108, 43, a2);
						c = Inlines.silk_ADD16((short)num7, 43);
					}
				}
				else if (a2 <= -4)
				{
					if (a2 == -4)
					{
						num7 = 280;
						c = ec_rates_Q5[num6 + 1 + 4];
					}
					else
					{
						num7 = Inlines.silk_SMLABB(108, -43, a2);
						c = Inlines.silk_SUB16((short)num7, 43);
					}
				}
				else
				{
					num7 = ec_rates_Q5[num6 + 4];
					c = ec_rates_Q5[num6 + 1 + 4];
				}
				int a3 = span3[j];
				int num8 = Inlines.silk_SUB16((short)num4, (short)num);
				span3[j] = Inlines.silk_SMLABB(Inlines.silk_MLA(a3, Inlines.silk_SMULBB(num8, num8), w_Q5[i]), mu_Q20, num7);
				num8 = Inlines.silk_SUB16((short)num4, (short)num2);
				span3[j + num3] = Inlines.silk_SMLABB(Inlines.silk_MLA(a3, Inlines.silk_SMULBB(num8, num8), w_Q5[i]), mu_Q20, c);
			}
			if (num3 <= 2)
			{
				for (int j = 0; j < num3; j++)
				{
					array[j + num3][i] = (sbyte)(array[j][i] + 1);
				}
				num3 = Inlines.silk_LSHIFT(num3, 1);
				for (int j = num3; j < 4; j++)
				{
					array[j][i] = array[j - num3][i];
				}
			}
			else
			{
				if (i <= 0)
				{
					break;
				}
				for (int j = 0; j < 4; j++)
				{
					if (span3[j] > span3[j + 4])
					{
						span5[j] = span3[j];
						span4[j] = span3[j + 4];
						span3[j] = span4[j];
						span3[j + 4] = span5[j];
						int num = span2[j];
						span2[j] = span2[j + 4];
						span2[j + 4] = (short)num;
						span[j] = j + 4;
					}
					else
					{
						span4[j] = span3[j];
						span5[j] = span3[j + 4];
						span[j] = j;
					}
				}
				while (true)
				{
					int num9 = int.MaxValue;
					int num10 = 0;
					int num11 = 0;
					int num12 = 0;
					for (int j = 0; j < 4; j++)
					{
						if (num9 > span5[j])
						{
							num9 = span5[j];
							num11 = j;
						}
						if (num10 < span4[j])
						{
							num10 = span4[j];
							num12 = j;
						}
					}
					if (num9 >= num10)
					{
						break;
					}
					span[num12] = span[num11] ^ 4;
					span3[num12] = span3[num11 + 4];
					span2[num12] = span2[num11 + 4];
					span4[num12] = 0;
					span5[num11] = int.MaxValue;
					Buffer.BlockCopy(array[num11], 0, array[num12], 0, order);
				}
				for (int j = 0; j < 4; j++)
				{
					sbyte b2 = (sbyte)Inlines.silk_RSHIFT(span[j], 2);
					array[j][i] += b2;
				}
			}
			i--;
		}
		a2 = 0;
		int num13 = int.MaxValue;
		for (int j = 0; j < 8; j++)
		{
			if (num13 > span3[j])
			{
				num13 = span3[j];
				a2 = j;
			}
		}
		for (int j = 0; j < order; j++)
		{
			indices[j] = array[a2 & 3][j];
		}
		indices[0] = (sbyte)(indices[0] + Inlines.silk_RSHIFT(a2, 2));
		return num13;
	}

	/// <summary>
	/// NLSF vector encoder
	/// </summary>
	/// <param name="NLSFIndices">(I) Codebook path vector [ LPC_ORDER + 1 ]</param>
	/// <param name="pNLSF_Q15">(I/O) Quantized NLSF vector [ LPC_ORDER ]</param>
	/// <param name="psNLSF_CB">(I) Codebook object</param>
	/// <param name="pW_QW">(I) NLSF weight vector [ LPC_ORDER ]</param>
	/// <param name="NLSF_mu_Q20">(I) Rate weight for the RD optimization</param>
	/// <param name="nSurvivors">(I) Max survivors after first stage</param>
	/// <param name="signalType">(I) Signal type: 0/1/2</param>
	/// <returns>RD value in Q25</returns>
	internal static int silk_NLSF_encode(sbyte[] NLSFIndices, short[] pNLSF_Q15, NLSFCodebook psNLSF_CB, short[] pW_QW, int NLSF_mu_Q20, int nSurvivors, int signalType)
	{
		Span<short> span = stackalloc short[16];
		Span<short> x_Q = stackalloc short[16];
		Span<short> pNLSF_Q16 = stackalloc short[16];
		Span<short> pNLSFW_Q_OUT = stackalloc short[16];
		Span<short> w_Q = stackalloc short[16];
		Span<byte> span2 = stackalloc byte[16];
		Span<short> ec_ix = stackalloc short[16];
		byte[] cB1_NLSF_Q = psNLSF_CB.CB1_NLSF_Q8;
		silk_NLSF_stabilize(pNLSF_Q15, psNLSF_CB.deltaMin_Q15, psNLSF_CB.order);
		int[] array = new int[psNLSF_CB.nVectors];
		silk_NLSF_VQ(array, pNLSF_Q15, psNLSF_CB.CB1_NLSF_Q8, psNLSF_CB.nVectors, psNLSF_CB.order);
		int[] array2 = new int[nSurvivors];
		Sort.silk_insertion_sort_increasing(array, array2, psNLSF_CB.nVectors, nSurvivors);
		int[] array3 = new int[nSurvivors];
		sbyte[][] array4 = Arrays.InitTwoDimensionalArray<sbyte>(nSurvivors, 16);
		for (int i = 0; i < nSurvivors; i++)
		{
			int num = array2[i];
			int num2 = num * psNLSF_CB.order;
			for (int j = 0; j < psNLSF_CB.order; j++)
			{
				pNLSF_Q16[j] = Inlines.silk_LSHIFT16(cB1_NLSF_Q[num2 + j], 7);
				span[j] = (short)(pNLSF_Q15[j] - pNLSF_Q16[j]);
			}
			silk_NLSF_VQ_weights_laroia(pNLSFW_Q_OUT, pNLSF_Q16, psNLSF_CB.order);
			for (int j = 0; j < psNLSF_CB.order; j++)
			{
				int b = Inlines.silk_SQRT_APPROX(Inlines.silk_LSHIFT(pNLSFW_Q_OUT[j], 16));
				x_Q[j] = (short)Inlines.silk_RSHIFT(Inlines.silk_SMULBB(span[j], b), 14);
			}
			for (int j = 0; j < psNLSF_CB.order; j++)
			{
				w_Q[j] = (short)Inlines.silk_DIV32_16(Inlines.silk_LSHIFT(pW_QW[j], 5), pNLSFW_Q_OUT[j]);
			}
			silk_NLSF_unpack(ec_ix, span2, psNLSF_CB, num);
			array3[i] = silk_NLSF_del_dec_quant(array4[i], x_Q, w_Q, span2, ec_ix, psNLSF_CB.ec_Rates_Q5, psNLSF_CB.quantStepSize_Q16, psNLSF_CB.invQuantStepSize_Q6, NLSF_mu_Q20, psNLSF_CB.order);
			int num3 = (signalType >> 1) * psNLSF_CB.nVectors;
			int inLin = ((num != 0) ? (psNLSF_CB.CB1_iCDF[num3 + num - 1] - psNLSF_CB.CB1_iCDF[num3 + num]) : (256 - psNLSF_CB.CB1_iCDF[num3 + num]));
			int b2 = 1024 - Inlines.silk_lin2log(inLin);
			array3[i] = Inlines.silk_SMLABB(array3[i], b2, Inlines.silk_RSHIFT(NLSF_mu_Q20, 2));
		}
		int[] array5 = new int[1];
		Sort.silk_insertion_sort_increasing(array3, array5, nSurvivors, 1);
		NLSFIndices[0] = (sbyte)array2[array5[0]];
		Arrays.MemCopy(array4[array5[0]], 0, NLSFIndices, 1, psNLSF_CB.order);
		silk_NLSF_decode(pNLSF_Q15, NLSFIndices, psNLSF_CB);
		return array3[0];
	}

	/// <summary>
	/// helper function for NLSF2A(..)
	/// </summary>
	/// <param name="o">(O) intermediate polynomial, QA [dd+1]</param>
	/// <param name="cLSF">(I) vector of interleaved 2*cos(LSFs), QA [d]</param>
	/// <param name="dd">(I) polynomial order (= 1/2 * filter order)</param>
	internal static void silk_NLSF2A_find_poly(int[] o, Span<int> cLSF, int dd)
	{
		o[0] = Inlines.silk_LSHIFT(1, 16);
		o[1] = -cLSF[0];
		for (int i = 1; i < dd; i++)
		{
			int num = cLSF[2 * i];
			o[i + 1] = Inlines.silk_LSHIFT(o[i - 1], 1) - (int)Inlines.silk_RSHIFT_ROUND64(Inlines.silk_SMULL(num, o[i]), 16);
			for (int num2 = i; num2 > 1; num2--)
			{
				o[num2] += o[num2 - 2] - (int)Inlines.silk_RSHIFT_ROUND64(Inlines.silk_SMULL(num, o[num2 - 1]), 16);
			}
			o[1] -= num;
		}
	}

	/// <summary>
	/// compute whitening filter coefficients from normalized line spectral frequencies
	/// </summary>
	/// <param name="a_Q12">(O) monic whitening filter coefficients in Q12,  [ d ]</param>
	/// <param name="NLSF">(I) normalized line spectral frequencies in Q15, [ d ]</param>
	/// <param name="d">(I) filter order (should be even)</param>
	internal static void silk_NLSF2A(short[] a_Q12, short[] NLSF, int d, int[] array = null, int[] array2 = null, int[] array3 = null, int[] array4 = null)
	{
		array ??= new int[d];
		array2 ??= new int[d / 2 + 1];
		array3 ??= new int[d / 2 + 1];
		array4 ??= new int[d];
		int num = 0;
		byte[] array5 = ((d == 16) ? ordering16 : ordering10);
		for (int i = 0; i < d; i++)
		{
			int num2 = Inlines.silk_RSHIFT(NLSF[i], 8);
			int b = NLSF[i] - Inlines.silk_LSHIFT(num2, 8);
			int num3 = Tables.silk_LSFCosTab_Q12[num2];
			int a = Tables.silk_LSFCosTab_Q12[num2 + 1] - num3;
			array[array5[i]] = Inlines.silk_RSHIFT_ROUND(Inlines.silk_LSHIFT(num3, 8) + Inlines.silk_MUL(a, b), 4);
		}
		int num4 = Inlines.silk_RSHIFT(d, 1);
		silk_NLSF2A_find_poly(array2, array.AsSpan(), num4);
		silk_NLSF2A_find_poly(array3, array.AsSpan().Slice(1), num4);
		for (int i = 0; i < num4; i++)
		{
			int num5 = array2[i + 1] + array2[i];
			int num6 = array3[i + 1] - array3[i];
			array4[i] = -num6 - num5;
			array4[d - i - 1] = num6 - num5;
		}
		int j;
		for (j = 0; j < 10; j++)
		{
			int num7 = 0;
			for (int i = 0; i < d; i++)
			{
				int num8 = Inlines.silk_abs(array4[i]);
				if (num8 > num7)
				{
					num7 = num8;
					num = i;
				}
			}
			num7 = Inlines.silk_RSHIFT_ROUND(num7, 5);
			if (num7 <= 32767)
			{
				break;
			}
			num7 = Inlines.silk_min(num7, 163838);
			int chirp_Q = 65470 - Inlines.silk_DIV32(Inlines.silk_LSHIFT(num7 - 32767, 14), Inlines.silk_RSHIFT32(Inlines.silk_MUL(num7, num + 1), 2));
			Filters.silk_bwexpander_32(array4, d, chirp_Q);
		}
		if (j == 10)
		{
			for (int i = 0; i < d; i++)
			{
				a_Q12[i] = (short)Inlines.silk_SAT16(Inlines.silk_RSHIFT_ROUND(array4[i], 5));
				array4[i] = Inlines.silk_LSHIFT(a_Q12[i], 5);
			}
		}
		else
		{
			for (int i = 0; i < d; i++)
			{
				a_Q12[i] = (short)Inlines.silk_RSHIFT_ROUND(array4[i], 5);
			}
		}
		for (j = 0; j < 16; j++)
		{
			if (Filters.silk_LPC_inverse_pred_gain(a_Q12, d) >= 107374)
			{
				break;
			}
			Filters.silk_bwexpander_32(array4, d, 65536 - Inlines.silk_LSHIFT(2, j));
			for (int i = 0; i < d; i++)
			{
				a_Q12[i] = (short)Inlines.silk_RSHIFT_ROUND(array4[i], 5);
			}
		}
	}

	/// <summary>
	/// Helper function for A2NLSF(..) Transforms polynomials from cos(n*f) to cos(f)^n
	/// </summary>
	/// <param name="p">(I/O) Polynomial</param>
	/// <param name="dd">(I) Polynomial order (= filter order / 2 )</param>
	internal static void silk_A2NLSF_trans_poly(int[] p, int dd)
	{
		for (int i = 2; i <= dd; i++)
		{
			for (int num = dd; num > i; num--)
			{
				p[num - 2] -= p[num];
			}
			p[i - 2] -= Inlines.silk_LSHIFT(p[i], 1);
		}
	}

	/// <summary>
	/// Helper function for A2NLSF(..) Polynomial evaluation
	/// </summary>
	/// <param name="p">(I) Polynomial, Q16</param>
	/// <param name="x">(I) Evaluation point, Q12</param>
	/// <param name="dd">(I) Order</param>
	/// <returns>the polynomial evaluation, in Q16</returns>
	internal static int silk_A2NLSF_eval_poly(int[] p, int x, int dd)
	{
		int num = p[dd];
		int c = Inlines.silk_LSHIFT(x, 4);
		if (8 == dd)
		{
			num = Inlines.silk_SMLAWW(p[7], num, c);
			num = Inlines.silk_SMLAWW(p[6], num, c);
			num = Inlines.silk_SMLAWW(p[5], num, c);
			num = Inlines.silk_SMLAWW(p[4], num, c);
			num = Inlines.silk_SMLAWW(p[3], num, c);
			num = Inlines.silk_SMLAWW(p[2], num, c);
			num = Inlines.silk_SMLAWW(p[1], num, c);
			num = Inlines.silk_SMLAWW(p[0], num, c);
		}
		else
		{
			for (int num2 = dd - 1; num2 >= 0; num2--)
			{
				num = Inlines.silk_SMLAWW(p[num2], num, c);
			}
		}
		return num;
	}

	internal static void silk_A2NLSF_init(int[] a_Q16, int[] P, int[] Q, int dd)
	{
		P[dd] = Inlines.silk_LSHIFT(1, 16);
		Q[dd] = Inlines.silk_LSHIFT(1, 16);
		for (int i = 0; i < dd; i++)
		{
			P[i] = -a_Q16[dd - i - 1] - a_Q16[dd + i];
			Q[i] = -a_Q16[dd - i - 1] + a_Q16[dd + i];
		}
		for (int i = dd; i > 0; i--)
		{
			P[i - 1] -= P[i];
			Q[i - 1] += Q[i];
		}
		silk_A2NLSF_trans_poly(P, dd);
		silk_A2NLSF_trans_poly(Q, dd);
	}

	/// <summary>
	/// Compute Normalized Line Spectral Frequencies (NLSFs) from whitening filter coefficients
	/// If not all roots are found, the a_Q16 coefficients are bandwidth expanded until convergence.
	/// </summary>
	/// <param name="NLSF">(O) Normalized Line Spectral Frequencies in Q15 (0..2^15-1) [d]</param>
	/// <param name="a_Q16">(I/O) Monic whitening filter coefficients in Q16 [d]</param>
	/// <param name="d">(I) Filter order (must be even)</param>
	internal static void silk_A2NLSF(short[] NLSF, int[] a_Q16, int d)
	{
		int[] array = new int[9];
		int[] array2 = new int[9];
		int[][] array3 = new int[2][] { array, array2 };
		int dd = Inlines.silk_RSHIFT(d, 1);
		silk_A2NLSF_init(a_Q16, array, array2, dd);
		int[] p = array;
		int num = Tables.silk_LSFCosTab_Q12[0];
		int num2 = silk_A2NLSF_eval_poly(p, num, dd);
		int num3;
		if (num2 < 0)
		{
			NLSF[0] = 0;
			p = array2;
			num2 = silk_A2NLSF_eval_poly(p, num, dd);
			num3 = 1;
		}
		else
		{
			num3 = 0;
		}
		int num4 = 1;
		int num5 = 0;
		int num6 = 0;
		while (true)
		{
			int num7 = Tables.silk_LSFCosTab_Q12[num4];
			int num8 = silk_A2NLSF_eval_poly(p, num7, dd);
			if ((num2 <= 0 && num8 >= num6) || (num2 >= 0 && num8 <= -num6))
			{
				num6 = ((num8 == 0) ? 1 : 0);
				int num9 = -256;
				for (int i = 0; i < 3; i++)
				{
					int num10 = Inlines.silk_RSHIFT_ROUND(num + num7, 1);
					int num11 = silk_A2NLSF_eval_poly(p, num10, dd);
					if ((num2 <= 0 && num11 >= 0) || (num2 >= 0 && num11 <= 0))
					{
						num7 = num10;
						num8 = num11;
					}
					else
					{
						num = num10;
						num2 = num11;
						num9 = Inlines.silk_ADD_RSHIFT(num9, 128, i);
					}
				}
				if (Inlines.silk_abs(num2) < 65536)
				{
					int num12 = num2 - num8;
					int a = Inlines.silk_LSHIFT(num2, 5) + Inlines.silk_RSHIFT(num12, 1);
					if (num12 != 0)
					{
						num9 += Inlines.silk_DIV32(a, num12);
					}
				}
				else
				{
					num9 += Inlines.silk_DIV32(num2, Inlines.silk_RSHIFT(num2 - num8, 5));
				}
				NLSF[num3] = (short)Inlines.silk_min_32(Inlines.silk_LSHIFT(num4, 8) + num9, 32767);
				num3++;
				if (num3 < d)
				{
					p = array3[num3 & 1];
					num = Tables.silk_LSFCosTab_Q12[num4 - 1];
					num2 = Inlines.silk_LSHIFT(1 - (num3 & 2), 12);
					continue;
				}
				break;
			}
			num4++;
			num = num7;
			num2 = num8;
			num6 = 0;
			if (num4 <= 128)
			{
				continue;
			}
			num5++;
			if (num5 > 30)
			{
				NLSF[0] = (short)Inlines.silk_DIV32_16(32768, (short)(d + 1));
				for (num4 = 1; num4 < d; num4++)
				{
					NLSF[num4] = (short)Inlines.silk_SMULBB(num4 + 1, NLSF[0]);
				}
				break;
			}
			Filters.silk_bwexpander_32(a_Q16, d, 65536 - Inlines.silk_SMULBB(10 + num5, num5));
			silk_A2NLSF_init(a_Q16, array, array2, dd);
			p = array;
			num = Tables.silk_LSFCosTab_Q12[0];
			num2 = silk_A2NLSF_eval_poly(p, num, dd);
			if (num2 < 0)
			{
				NLSF[0] = 0;
				p = array2;
				num2 = silk_A2NLSF_eval_poly(p, num, dd);
				num3 = 1;
			}
			else
			{
				num3 = 0;
			}
			num4 = 1;
		}
	}

}
