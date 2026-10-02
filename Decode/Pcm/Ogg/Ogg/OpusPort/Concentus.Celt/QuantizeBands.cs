using System;
using Concentus.Celt.Structs;
using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Celt;

internal static class QuantizeBands
{
	private static readonly int[] pred_coef = new int[4] { 29440, 26112, 21248, 16384 };

	private static readonly int[] beta_coef = new int[4] { 30147, 22282, 12124, 6554 };

	private static readonly int beta_intra = 4915;

	private static byte[] small_energy_icdf = new byte[3] { 2, 1, 0 };

	internal static int loss_distortion(int[][] eBands, int[][] oldEBands, int start, int end, int len, int C)
	{
		int num = 0;
		int num2 = 0;
		do
		{
			for (int i = start; i < end; i++)
			{
				int num3 = Inlines.SUB16(Inlines.SHR16(eBands[num2][i], 3), Inlines.SHR16(oldEBands[num2][i], 3));
				num = Inlines.MAC16_16(num, num3, num3);
			}
		}
		while (++num2 < C);
		return Inlines.MIN32(200, Inlines.SHR32(num, 14));
	}

	internal static int quant_coarse_energy_impl(CeltMode m, int start, int end, int[][] eBands, int[][] oldEBands, int budget, int tell, byte[] prob_model, int[][] error, EntropyCoder enc, Span<byte> encodedData, int C, int LM, int intra, int max_decay, int lfe)
	{
		int num = 0;
		int[] array = new int[2];
		if (tell + 3 <= budget)
		{
			enc.enc_bit_logp(encodedData, intra, 3u);
		}
		int a;
		int a2;
		if (intra != 0)
		{
			a = 0;
			a2 = beta_intra;
		}
		else
		{
			a2 = beta_coef[LM];
			a = pred_coef[LM];
		}
		for (int i = start; i < end; i++)
		{
			int num2 = 0;
			do
			{
				int num3 = eBands[num2][i];
				int b = Inlines.MAX16(-9216, oldEBands[num2][i]);
				int num4 = Inlines.SHL32(Inlines.EXTEND32(num3), 7) - Inlines.PSHR32(Inlines.MULT16_16(a, b), 8) - array[num2];
				int value = num4 + 65536 >> 17;
				int num5 = Inlines.EXTRACT16(Inlines.MAX32(-28672, Inlines.SUB32(oldEBands[num2][i], max_decay)));
				if (value < 0 && num3 < num5)
				{
					value += Inlines.SHR16(Inlines.SUB16(num5, num3), 10);
					if (value > 0)
					{
						value = 0;
					}
				}
				int num6 = value;
				tell = enc.tell();
				int num7 = budget - tell - 3 * C * (end - i);
				if (i != start && num7 < 30)
				{
					if (num7 < 24)
					{
						value = Inlines.IMIN(1, value);
					}
					if (num7 < 16)
					{
						value = Inlines.IMAX(-1, value);
					}
				}
				if (lfe != 0 && i >= 2)
				{
					value = Inlines.IMIN(value, 0);
				}
				if (budget - tell >= 15)
				{
					int num8 = 2 * Inlines.IMIN(i, 20);
					Laplace.ec_laplace_encode(enc, encodedData, ref value, (uint)(prob_model[num8] << 7), prob_model[num8 + 1] << 6);
				}
				else if (budget - tell >= 2)
				{
					value = Inlines.IMAX(-1, Inlines.IMIN(value, 1));
					enc.enc_icdf(encodedData, (2 * value) ^ (0 - ((value < 0) ? 1 : 0)), small_energy_icdf, 2u);
				}
				else if (budget - tell >= 1)
				{
					value = Inlines.IMIN(0, value);
					enc.enc_bit_logp(encodedData, -value, 1u);
				}
				else
				{
					value = -1;
				}
				error[num2][i] = Inlines.PSHR32(num4, 7) - Inlines.SHL16(value, 10);
				num += Inlines.abs(num6 - value);
				int a3 = Inlines.SHL32(value, 10);
				int b2 = Inlines.PSHR32(Inlines.MULT16_16(a, b), 8) + array[num2] + Inlines.SHL32(a3, 7);
				b2 = Inlines.MAX32(-3670016, b2);
				oldEBands[num2][i] = Inlines.PSHR32(b2, 7);
				array[num2] = array[num2] + Inlines.SHL32(a3, 7) - Inlines.MULT16_16(a2, Inlines.PSHR32(a3, 8));
			}
			while (++num2 < C);
		}
		if (lfe == 0)
		{
			return num;
		}
		return 0;
	}

	internal static void quant_coarse_energy(CeltMode m, int start, int end, int effEnd, int[][] eBands, int[][] oldEBands, uint budget, int[][] error, EntropyCoder enc, Span<byte> encodedData, int C, int LM, int nbAvailableBytes, int force_intra, ref int delayedIntra, int two_pass, int loss_rate, int lfe)
	{
		EntropyCoder entropyCoder = new EntropyCoder();
		int num = 0;
		int num2 = ((force_intra != 0 || (two_pass == 0 && delayedIntra > 2 * C * (end - start) && nbAvailableBytes > (end - start) * C)) ? 1 : 0);
		int num3 = (int)(budget * delayedIntra * loss_rate / (C * 512));
		int num4 = loss_distortion(eBands, oldEBands, start, effEnd, m.nbEBands, C);
		uint num5 = (uint)enc.tell();
		if (num5 + 3 > budget)
		{
			two_pass = (num2 = 0);
		}
		int num6 = 16384;
		if (end - start > 10)
		{
			num6 = Inlines.MIN32(num6, Inlines.SHL32(nbAvailableBytes, 7));
		}
		if (lfe != 0)
		{
			num6 = 3072;
		}
		entropyCoder.Assign(enc);
		int[][] array = Arrays.InitTwoDimensionalArray<int>(C, m.nbEBands);
		int[][] array2 = Arrays.InitTwoDimensionalArray<int>(C, m.nbEBands);
		Arrays.MemCopy(oldEBands[0], 0, array[0], 0, m.nbEBands);
		if (C == 2)
		{
			Arrays.MemCopy(oldEBands[1], 0, array[1], 0, m.nbEBands);
		}
		if (two_pass != 0 || num2 != 0)
		{
			num = quant_coarse_energy_impl(m, start, end, eBands, array, (int)budget, (int)num5, Tables.e_prob_model[LM][1], array2, enc, encodedData, C, LM, 1, num6, lfe);
		}
		if (num2 == 0)
		{
			EntropyCoder entropyCoder2 = new EntropyCoder();
			byte[] array3 = null;
			int num7 = (int)enc.tell_frac();
			entropyCoder2.Assign(enc);
			uint num8 = entropyCoder.range_bytes();
			uint num9 = entropyCoder2.range_bytes();
			int start2 = (int)num8;
			uint num10 = num9 - num8;
			if (num10 != 0)
			{
				array3 = new byte[num10];
				encodedData.Slice(start2, (int)num10).CopyTo(array3);
			}
			enc.Assign(entropyCoder);
			int num11 = quant_coarse_energy_impl(m, start, end, eBands, oldEBands, (int)budget, (int)num5, Tables.e_prob_model[LM][num2], error, enc, encodedData, C, LM, 0, num6, lfe);
			if (two_pass != 0 && (num < num11 || (num == num11 && (int)enc.tell_frac() + num3 > num7)))
			{
				enc.Assign(entropyCoder2);
				array3?.AsSpan(0, (int)(num9 - num8)).CopyTo(encodedData.Slice(start2));
				Arrays.MemCopy(array[0], 0, oldEBands[0], 0, m.nbEBands);
				Arrays.MemCopy(array2[0], 0, error[0], 0, m.nbEBands);
				if (C == 2)
				{
					Arrays.MemCopy(array[1], 0, oldEBands[1], 0, m.nbEBands);
					Arrays.MemCopy(array2[1], 0, error[1], 0, m.nbEBands);
				}
				num2 = 1;
			}
		}
		else
		{
			Arrays.MemCopy(array[0], 0, oldEBands[0], 0, m.nbEBands);
			Arrays.MemCopy(array2[0], 0, error[0], 0, m.nbEBands);
			if (C == 2)
			{
				Arrays.MemCopy(array[1], 0, oldEBands[1], 0, m.nbEBands);
				Arrays.MemCopy(array2[1], 0, error[1], 0, m.nbEBands);
			}
		}
		if (num2 != 0)
		{
			delayedIntra = num4;
		}
		else
		{
			delayedIntra = Inlines.ADD32(Inlines.MULT16_32_Q15(Inlines.MULT16_16_Q15(pred_coef[LM], pred_coef[LM]), delayedIntra), num4);
		}
	}

	internal static void quant_fine_energy(CeltMode m, int start, int end, int[][] oldEBands, int[][] error, int[] fine_quant, EntropyCoder enc, Span<byte> encodedData, int C)
	{
		for (int i = start; i < end; i++)
		{
			int num = 1 << fine_quant[i];
			if (fine_quant[i] <= 0)
			{
				continue;
			}
			int num2 = 0;
			do
			{
				int num3 = error[num2][i] + 512 >> 10 - fine_quant[i];
				if (num3 > num - 1)
				{
					num3 = num - 1;
				}
				if (num3 < 0)
				{
					num3 = 0;
				}
				enc.enc_bits(encodedData, (uint)num3, (uint)fine_quant[i]);
				int num4 = Inlines.SUB16(Inlines.SHR32(Inlines.SHL32(num3, 10) + 512, fine_quant[i]), 512);
				oldEBands[num2][i] += num4;
				error[num2][i] -= num4;
			}
			while (++num2 < C);
		}
	}

	internal static void quant_energy_finalise(CeltMode m, int start, int end, int[][] oldEBands, int[][] error, int[] fine_quant, int[] fine_priority, int bits_left, EntropyCoder enc, Span<byte> encodedData, int C)
	{
		for (int i = 0; i < 2; i++)
		{
			for (int j = start; j < end; j++)
			{
				if (bits_left < C)
				{
					break;
				}
				if (fine_quant[j] < 8 && fine_priority[j] == i)
				{
					int num = 0;
					do
					{
						int num2 = ((error[num][j] >= 0) ? 1 : 0);
						enc.enc_bits(encodedData, (uint)num2, 1u);
						int num3 = Inlines.SHR16(Inlines.SHL16(num2, 10) - 512, fine_quant[j] + 1);
						oldEBands[num][j] += num3;
						bits_left--;
					}
					while (++num < C);
				}
			}
		}
	}

	internal static void unquant_coarse_energy(CeltMode m, int start, int end, int[] oldEBands, int intra, EntropyCoder dec, ReadOnlySpan<byte> encodedData, int C, int LM, int[] history)
	{
		byte[] array = Tables.e_prob_model[LM][intra];
		int a;
		int a2;
		if (intra != 0)
		{
			a = 0;
			a2 = beta_intra;
		}
		else
		{
			a2 = beta_coef[LM];
			a = pred_coef[LM];
		}
		int num = (int)(dec.storage * 8);
		for (int i = start; i < end; i++)
		{
			int num2 = 0;
			do
			{
				int num3 = dec.tell();
				int a3;
				if (num - num3 >= 15)
				{
					int num4 = 2 * Inlines.IMIN(i, 20);
					a3 = Laplace.ec_laplace_decode(dec, encodedData, (uint)(array[num4] << 7), array[num4 + 1] << 6);
				}
				else if (num - num3 < 2)
				{
					a3 = ((num - num3 < 1) ? (-1) : (-dec.dec_bit_logp(encodedData, 1u)));
				}
				else
				{
					a3 = dec.dec_icdf(encodedData, small_energy_icdf, 2u);
					a3 = (a3 >> 1) ^ -(a3 & 1);
				}
				int a4 = Inlines.SHL32(a3, 10);
				oldEBands[i + num2 * m.nbEBands] = Inlines.MAX16(-9216, oldEBands[i + num2 * m.nbEBands]);
				int b = Inlines.PSHR32(Inlines.MULT16_16(a, oldEBands[i + num2 * m.nbEBands]), 8) + history[num2] + Inlines.SHL32(a4, 7);
				b = Inlines.MAX32(-3670016, b);
				oldEBands[i + num2 * m.nbEBands] = Inlines.PSHR32(b, 7);
				history[num2] = history[num2] + Inlines.SHL32(a4, 7) - Inlines.MULT16_16(a2, Inlines.PSHR32(a4, 8));
			}
			while (++num2 < C);
		}
	}

	internal static void unquant_fine_energy(CeltMode m, int start, int end, int[] oldEBands, int[] fine_quant, EntropyCoder dec, ReadOnlySpan<byte> encodedData, int C)
	{
		for (int i = start; i < end; i++)
		{
			if (fine_quant[i] > 0)
			{
				int num = 0;
				do
				{
					int num2 = Inlines.SUB16(Inlines.SHR32(Inlines.SHL32((int)dec.dec_bits(encodedData, (uint)fine_quant[i]), 10) + 512, fine_quant[i]), 512);
					oldEBands[i + num * m.nbEBands] += num2;
				}
				while (++num < C);
			}
		}
	}

	internal static void unquant_energy_finalise(CeltMode m, int start, int end, int[] oldEBands, int[] fine_quant, int[] fine_priority, int bits_left, EntropyCoder dec, ReadOnlySpan<byte> encodedData, int C)
	{
		for (int i = 0; i < 2; i++)
		{
			for (int j = start; j < end; j++)
			{
				if (bits_left < C)
				{
					break;
				}
				if (fine_quant[j] < 8 && fine_priority[j] == i)
				{
					int num = 0;
					do
					{
						int num2 = Inlines.SHR16(Inlines.SHL16((int)dec.dec_bits(encodedData, 1u), 10) - 512, fine_quant[j] + 1);
						oldEBands[j + num * m.nbEBands] += num2;
						bits_left--;
					}
					while (++num < C);
				}
			}
		}
	}

	/// <summary>
	/// non-pointer case
	/// </summary>
	/// <param name="m"></param>
	/// <param name="effEnd"></param>
	/// <param name="end"></param>
	/// <param name="bandE"></param>
	/// <param name="bandLogE"></param>
	/// <param name="C"></param>
	internal static void amp2Log2(CeltMode m, int effEnd, int end, int[][] bandE, int[][] bandLogE, int C)
	{
		int num = 0;
		do
		{
			for (int i = 0; i < effEnd; i++)
			{
				bandLogE[num][i] = Inlines.celt_log2(Inlines.SHL32(bandE[num][i], 2)) - Inlines.SHL16((int)Tables.eMeans[i], 6);
			}
			for (int i = effEnd; i < end; i++)
			{
				bandLogE[num][i] = -14336;
			}
		}
		while (++num < C);
	}

	/// <summary>
	/// only needed in one place
	/// </summary>
	/// <param name="m"></param>
	/// <param name="effEnd"></param>
	/// <param name="end"></param>
	/// <param name="bandE"></param>
	/// <param name="bandLogE"></param>
	/// <param name="C"></param>
	internal static void amp2Log2(CeltMode m, int effEnd, int end, int[] bandE, Span<int> bandLogE, int bandLogE_ptr, int C)
	{
		int num = 0;
		do
		{
			for (int i = 0; i < effEnd; i++)
			{
				bandLogE[bandLogE_ptr + num * m.nbEBands + i] = Inlines.celt_log2(Inlines.SHL32(bandE[i + num * m.nbEBands], 2)) - Inlines.SHL16((int)Tables.eMeans[i], 6);
			}
			for (int i = effEnd; i < end; i++)
			{
				bandLogE[bandLogE_ptr + num * m.nbEBands + i] = -14336;
			}
		}
		while (++num < C);
	}
}
