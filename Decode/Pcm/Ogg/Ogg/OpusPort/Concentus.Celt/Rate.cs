using System;
using Concentus.Celt.Structs;
using Concentus.Common;

namespace Concentus.Celt;

internal static class Rate
{
	private static readonly byte[] LOG2_FRAC_TABLE = new byte[24]
	{
		0, 8, 13, 16, 19, 21, 23, 24, 26, 27,
		28, 29, 30, 31, 32, 32, 33, 34, 34, 35,
		36, 36, 37, 37
	};

	private const int ALLOC_STEPS = 6;

	internal static int get_pulses(int i)
	{
		if (i >= 8)
		{
			return 8 + (i & 7) << (i >> 3) - 1;
		}
		return i;
	}

	internal static int bits2pulses(CeltMode m, int band, int LM, int bits)
	{
		LM++;
		byte[] bits2 = m.cache.bits;
		int num = m.cache.index[LM * m.nbEBands + band];
		int num2 = 0;
		int num3 = bits2[num];
		bits--;
		for (int i = 0; i < 6; i++)
		{
			int num4 = num2 + num3 + 1 >> 1;
			if (bits2[num + num4] >= bits)
			{
				num3 = num4;
			}
			else
			{
				num2 = num4;
			}
		}
		if (bits - ((num2 == 0) ? (-1) : bits2[num + num2]) <= bits2[num + num3] - bits)
		{
			return num2;
		}
		return num3;
	}

	internal static int pulses2bits(CeltMode m, int band, int LM, int pulses)
	{
		LM++;
		if (pulses != 0)
		{
			return m.cache.bits[m.cache.index[LM * m.nbEBands + band] + pulses] + 1;
		}
		return 0;
	}

	internal static int interp_bits2pulses_encode(CeltMode m, int start, int end, int skip_start, int[] bits1, int[] bits2, int[] thresh, int[] cap, int total, out int _balance, int skip_rsv, ref int intensity, int intensity_rsv, ref int dual_stereo, int dual_stereo_rsv, int[] bits, int[] ebits, int[] fine_priority, int C, int LM, EntropyCoder ec, Span<byte> encodedData, int prev, int signalBandwidth)
	{
		int num = -1;
		int num2 = C << 3;
		int num3 = ((C > 1) ? 1 : 0);
		int num4 = LM << 3;
		int num5 = 0;
		int num6 = 64;
		int num9;
		int num8;
		int num10;
		for (int i = 0; i < 6; i++)
		{
			int num7 = num5 + num6 >> 1;
			num8 = 0;
			num9 = 0;
			num10 = end;
			while (num10-- > start)
			{
				int num11 = bits1[num10] + (num7 * bits2[num10] >> 6);
				if (num11 >= thresh[num10] || num9 != 0)
				{
					num9 = 1;
					num8 += Inlines.IMIN(num11, cap[num10]);
				}
				else if (num11 >= num2)
				{
					num8 += num2;
				}
			}
			if (num8 > total)
			{
				num6 = num7;
			}
			else
			{
				num5 = num7;
			}
		}
		num8 = 0;
		num9 = 0;
		num10 = end;
		while (num10-- > start)
		{
			int num12 = bits1[num10] + (num5 * bits2[num10] >> 6);
			if (num12 < thresh[num10] && num9 == 0)
			{
				num12 = ((num12 >= num2) ? num2 : 0);
			}
			else
			{
				num9 = 1;
			}
			num8 += (bits[num10] = Inlines.IMIN(num12, cap[num10]));
		}
		num = end;
		int num14;
		int num13;
		while (true)
		{
			num10 = num - 1;
			if (num10 <= skip_start)
			{
				total += skip_rsv;
				break;
			}
			num13 = total - num8;
			num14 = Inlines.celt_udiv(num13, m.eBands[num] - m.eBands[start]);
			num13 -= (m.eBands[num] - m.eBands[start]) * num14;
			int num15 = Inlines.IMAX(num13 - (m.eBands[num10] - m.eBands[start]), 0);
			int num16 = m.eBands[num] - m.eBands[num10];
			int num17 = bits[num10] + num14 * num16 + num15;
			if (num17 >= Inlines.IMAX(thresh[num10], num2 + 8))
			{
				if (num <= start + 2 || (num17 > ((num10 < prev) ? 7 : 9) * num16 << LM << 3 >> 4 && num10 <= signalBandwidth))
				{
					ec.enc_bit_logp(encodedData, 1, 1u);
					break;
				}
				ec.enc_bit_logp(encodedData, 0, 1u);
				num8 += 8;
				num17 -= 8;
			}
			num8 -= bits[num10] + intensity_rsv;
			if (intensity_rsv > 0)
			{
				intensity_rsv = LOG2_FRAC_TABLE[num10 - start];
			}
			num8 += intensity_rsv;
			if (num17 >= num2)
			{
				num8 += num2;
				bits[num10] = num2;
			}
			else
			{
				bits[num10] = 0;
			}
			num--;
		}
		if (intensity_rsv > 0)
		{
			intensity = Inlines.IMIN(intensity, num);
			ec.enc_uint(encodedData, (uint)(intensity - start), (uint)(num + 1 - start));
		}
		else
		{
			intensity = 0;
		}
		if (intensity <= start)
		{
			total += dual_stereo_rsv;
			dual_stereo_rsv = 0;
		}
		if (dual_stereo_rsv > 0)
		{
			ec.enc_bit_logp(encodedData, dual_stereo, 1u);
		}
		else
		{
			dual_stereo = 0;
		}
		num13 = total - num8;
		num14 = Inlines.celt_udiv(num13, m.eBands[num] - m.eBands[start]);
		num13 -= (m.eBands[num] - m.eBands[start]) * num14;
		for (num10 = start; num10 < num; num10++)
		{
			bits[num10] += num14 * (m.eBands[num10 + 1] - m.eBands[num10]);
		}
		for (num10 = start; num10 < num; num10++)
		{
			int num18 = Inlines.IMIN(num13, m.eBands[num10 + 1] - m.eBands[num10]);
			bits[num10] += num18;
			num13 -= num18;
		}
		int num19 = 0;
		for (num10 = start; num10 < num; num10++)
		{
			int num20 = m.eBands[num10 + 1] - m.eBands[num10] << LM;
			int num21 = bits[num10] + num19;
			int num22;
			if (num20 > 1)
			{
				num22 = Inlines.MAX32(num21 - cap[num10], 0);
				bits[num10] = num21 - num22;
				int num23 = C * num20 + ((C == 2 && num20 > 2 && dual_stereo == 0 && num10 < intensity) ? 1 : 0);
				int num24 = num23 * (m.logN[num10] + num4);
				int num25 = (num24 >> 1) - num23 * 21;
				if (num20 == 2)
				{
					num25 += num23 << 3 >> 2;
				}
				if (bits[num10] + num25 < num23 * 2 << 3)
				{
					num25 += num24 >> 2;
				}
				else if (bits[num10] + num25 < num23 * 3 << 3)
				{
					num25 += num24 >> 3;
				}
				ebits[num10] = Inlines.IMAX(0, bits[num10] + num25 + (num23 << 2));
				ebits[num10] = Inlines.celt_udiv(ebits[num10], num23) >> 3;
				if (C * ebits[num10] > bits[num10] >> 3)
				{
					ebits[num10] = bits[num10] >> num3 >> 3;
				}
				ebits[num10] = Inlines.IMIN(ebits[num10], 8);
				fine_priority[num10] = ((ebits[num10] * (num23 << 3) >= bits[num10] + num25) ? 1 : 0);
				bits[num10] -= C * ebits[num10] << 3;
			}
			else
			{
				num22 = Inlines.MAX32(0, num21 - (C << 3));
				bits[num10] = num21 - num22;
				ebits[num10] = 0;
				fine_priority[num10] = 1;
			}
			if (num22 > 0)
			{
				int num26 = Inlines.IMIN(num22 >> num3 + 3, 8 - ebits[num10]);
				ebits[num10] += num26;
				int num27 = num26 * C << 3;
				fine_priority[num10] = ((num27 >= num22 - num19) ? 1 : 0);
				num22 -= num27;
			}
			num19 = num22;
		}
		_balance = num19;
		for (; num10 < end; num10++)
		{
			ebits[num10] = bits[num10] >> num3 >> 3;
			bits[num10] = 0;
			fine_priority[num10] = ((ebits[num10] < 1) ? 1 : 0);
		}
		return num;
	}

	internal static int interp_bits2pulses_decode(CeltMode m, int start, int end, int skip_start, int[] bits1, int[] bits2, int[] thresh, int[] cap, int total, out int _balance, int skip_rsv, ref int intensity, int intensity_rsv, ref int dual_stereo, int dual_stereo_rsv, int[] bits, int[] ebits, int[] fine_priority, int C, int LM, EntropyCoder ec, ReadOnlySpan<byte> encodedData, int prev, int signalBandwidth)
	{
		int num = -1;
		int num2 = C << 3;
		int num3 = ((C > 1) ? 1 : 0);
		int num4 = LM << 3;
		int num5 = 0;
		int num6 = 64;
		int num9;
		int num8;
		int num10;
		for (int i = 0; i < 6; i++)
		{
			int num7 = num5 + num6 >> 1;
			num8 = 0;
			num9 = 0;
			num10 = end;
			while (num10-- > start)
			{
				int num11 = bits1[num10] + (num7 * bits2[num10] >> 6);
				if (num11 >= thresh[num10] || num9 != 0)
				{
					num9 = 1;
					num8 += Inlines.IMIN(num11, cap[num10]);
				}
				else if (num11 >= num2)
				{
					num8 += num2;
				}
			}
			if (num8 > total)
			{
				num6 = num7;
			}
			else
			{
				num5 = num7;
			}
		}
		num8 = 0;
		num9 = 0;
		num10 = end;
		while (num10-- > start)
		{
			int num12 = bits1[num10] + (num5 * bits2[num10] >> 6);
			if (num12 < thresh[num10] && num9 == 0)
			{
				num12 = ((num12 >= num2) ? num2 : 0);
			}
			else
			{
				num9 = 1;
			}
			num8 += (bits[num10] = Inlines.IMIN(num12, cap[num10]));
		}
		num = end;
		int num14;
		int num13;
		while (true)
		{
			num10 = num - 1;
			if (num10 <= skip_start)
			{
				total += skip_rsv;
				break;
			}
			num13 = total - num8;
			num14 = Inlines.celt_udiv(num13, m.eBands[num] - m.eBands[start]);
			num13 -= (m.eBands[num] - m.eBands[start]) * num14;
			int num15 = Inlines.IMAX(num13 - (m.eBands[num10] - m.eBands[start]), 0);
			int num16 = m.eBands[num] - m.eBands[num10];
			int num17 = bits[num10] + num14 * num16 + num15;
			if (num17 >= Inlines.IMAX(thresh[num10], num2 + 8))
			{
				if (ec.dec_bit_logp(encodedData, 1u) != 0)
				{
					break;
				}
				num8 += 8;
				num17 -= 8;
			}
			num8 -= bits[num10] + intensity_rsv;
			if (intensity_rsv > 0)
			{
				intensity_rsv = LOG2_FRAC_TABLE[num10 - start];
			}
			num8 += intensity_rsv;
			if (num17 >= num2)
			{
				num8 += num2;
				bits[num10] = num2;
			}
			else
			{
				bits[num10] = 0;
			}
			num--;
		}
		if (intensity_rsv > 0)
		{
			intensity = start + (int)ec.dec_uint(encodedData, (uint)(num + 1 - start));
		}
		else
		{
			intensity = 0;
		}
		if (intensity <= start)
		{
			total += dual_stereo_rsv;
			dual_stereo_rsv = 0;
		}
		if (dual_stereo_rsv > 0)
		{
			dual_stereo = ec.dec_bit_logp(encodedData, 1u);
		}
		else
		{
			dual_stereo = 0;
		}
		num13 = total - num8;
		num14 = Inlines.celt_udiv(num13, m.eBands[num] - m.eBands[start]);
		num13 -= (m.eBands[num] - m.eBands[start]) * num14;
		for (num10 = start; num10 < num; num10++)
		{
			bits[num10] += num14 * (m.eBands[num10 + 1] - m.eBands[num10]);
		}
		for (num10 = start; num10 < num; num10++)
		{
			int num18 = Inlines.IMIN(num13, m.eBands[num10 + 1] - m.eBands[num10]);
			bits[num10] += num18;
			num13 -= num18;
		}
		int num19 = 0;
		for (num10 = start; num10 < num; num10++)
		{
			int num20 = m.eBands[num10 + 1] - m.eBands[num10] << LM;
			int num21 = bits[num10] + num19;
			int num22;
			if (num20 > 1)
			{
				num22 = Inlines.MAX32(num21 - cap[num10], 0);
				bits[num10] = num21 - num22;
				int num23 = C * num20 + ((C == 2 && num20 > 2 && dual_stereo == 0 && num10 < intensity) ? 1 : 0);
				int num24 = num23 * (m.logN[num10] + num4);
				int num25 = (num24 >> 1) - num23 * 21;
				if (num20 == 2)
				{
					num25 += num23 << 3 >> 2;
				}
				if (bits[num10] + num25 < num23 * 2 << 3)
				{
					num25 += num24 >> 2;
				}
				else if (bits[num10] + num25 < num23 * 3 << 3)
				{
					num25 += num24 >> 3;
				}
				ebits[num10] = Inlines.IMAX(0, bits[num10] + num25 + (num23 << 2));
				ebits[num10] = Inlines.celt_udiv(ebits[num10], num23) >> 3;
				if (C * ebits[num10] > bits[num10] >> 3)
				{
					ebits[num10] = bits[num10] >> num3 >> 3;
				}
				ebits[num10] = Inlines.IMIN(ebits[num10], 8);
				fine_priority[num10] = ((ebits[num10] * (num23 << 3) >= bits[num10] + num25) ? 1 : 0);
				bits[num10] -= C * ebits[num10] << 3;
			}
			else
			{
				num22 = Inlines.MAX32(0, num21 - (C << 3));
				bits[num10] = num21 - num22;
				ebits[num10] = 0;
				fine_priority[num10] = 1;
			}
			if (num22 > 0)
			{
				int num26 = Inlines.IMIN(num22 >> num3 + 3, 8 - ebits[num10]);
				ebits[num10] += num26;
				int num27 = num26 * C << 3;
				fine_priority[num10] = ((num27 >= num22 - num19) ? 1 : 0);
				num22 -= num27;
			}
			num19 = num22;
		}
		_balance = num19;
		for (; num10 < end; num10++)
		{
			ebits[num10] = bits[num10] >> num3 >> 3;
			bits[num10] = 0;
			fine_priority[num10] = ((ebits[num10] < 1) ? 1 : 0);
		}
		return num;
	}

	internal static int compute_allocation_encode(CeltMode m, int start, int end, int[] offsets, int[] cap, int alloc_trim, ref int intensity, ref int dual_stereo, int total, out int balance, int[] pulses, int[] ebits, int[] fine_priority, int C, int LM, EntropyCoder ec, Span<byte> encodedData, int prev, int signalBandwidth)
	{
		total = Inlines.IMAX(total, 0);
		int nbEBands = m.nbEBands;
		int skip_start = start;
		int num = ((total >= 8) ? 8 : 0);
		total -= num;
		int num3;
		int num2 = (num3 = 0);
		if (C == 2)
		{
			num2 = LOG2_FRAC_TABLE[end - start];
			if (num2 > total)
			{
				num2 = 0;
			}
			else
			{
				total -= num2;
				num3 = ((total >= 8) ? 8 : 0);
				total -= num3;
			}
		}
		int[] array = new int[nbEBands];
		int[] array2 = new int[nbEBands];
		int[] array3 = new int[nbEBands];
		int[] array4 = new int[nbEBands];
		for (int i = start; i < end; i++)
		{
			array3[i] = Inlines.IMAX(C << 3, 3 * (m.eBands[i + 1] - m.eBands[i]) << LM << 3 >> 4);
			array4[i] = C * (m.eBands[i + 1] - m.eBands[i]) * (alloc_trim - 5 - LM) * (end - i - 1) * (1 << LM + 3) >> 6;
			if (m.eBands[i + 1] - m.eBands[i] << LM == 1)
			{
				array4[i] -= C << 3;
			}
		}
		int num4 = 1;
		int num5 = m.nbAllocVectors - 1;
		do
		{
			int num6 = 0;
			int num7 = 0;
			int num8 = num4 + num5 >> 1;
			int i = end;
			while (i-- > start)
			{
				int num9 = m.eBands[i + 1] - m.eBands[i];
				int num10 = C * num9 * m.allocVectors[num8 * nbEBands + i] << LM >> 2;
				if (num10 > 0)
				{
					num10 = Inlines.IMAX(0, num10 + array4[i]);
				}
				num10 += offsets[i];
				if (num10 >= array3[i] || num6 != 0)
				{
					num6 = 1;
					num7 += Inlines.IMIN(num10, cap[i]);
				}
				else if (num10 >= C << 3)
				{
					num7 += C << 3;
				}
			}
			if (num7 > total)
			{
				num5 = num8 - 1;
			}
			else
			{
				num4 = num8 + 1;
			}
		}
		while (num4 <= num5);
		num5 = num4--;
		for (int i = start; i < end; i++)
		{
			int num11 = m.eBands[i + 1] - m.eBands[i];
			int num12 = C * num11 * m.allocVectors[num4 * nbEBands + i] << LM >> 2;
			int num13 = ((num5 >= m.nbAllocVectors) ? cap[i] : (C * num11 * m.allocVectors[num5 * nbEBands + i] << LM >> 2));
			if (num12 > 0)
			{
				num12 = Inlines.IMAX(0, num12 + array4[i]);
			}
			if (num13 > 0)
			{
				num13 = Inlines.IMAX(0, num13 + array4[i]);
			}
			if (num4 > 0)
			{
				num12 += offsets[i];
			}
			num13 += offsets[i];
			if (offsets[i] > 0)
			{
				skip_start = i;
			}
			num13 = Inlines.IMAX(0, num13 - num12);
			array[i] = num12;
			array2[i] = num13;
		}
		return interp_bits2pulses_encode(m, start, end, skip_start, array, array2, array3, cap, total, out balance, num, ref intensity, num2, ref dual_stereo, num3, pulses, ebits, fine_priority, C, LM, ec, encodedData, prev, signalBandwidth);
	}

	internal static int compute_allocation_decode(CeltMode m, int start, int end, int[] offsets, int[] cap, int alloc_trim, ref int intensity, ref int dual_stereo, int total, out int balance, int[] pulses, int[] ebits, int[] fine_priority, int C, int LM, EntropyCoder ec, ReadOnlySpan<byte> encodedData, int prev, int signalBandwidth, int[] bitsWork, int[] trimOffsetWork, int[] capWork, int[] balanceWork)
	{
		total = Inlines.IMAX(total, 0);
		int nbEBands = m.nbEBands;
		int skip_start = start;
		int num = ((total >= 8) ? 8 : 0);
		total -= num;
		int num3;
		int num2 = (num3 = 0);
		if (C == 2)
		{
			num2 = LOG2_FRAC_TABLE[end - start];
			if (num2 > total)
			{
				num2 = 0;
			}
			else
			{
				total -= num2;
				num3 = ((total >= 8) ? 8 : 0);
				total -= num3;
			}
		}
		int[] array = bitsWork;
		int[] array2 = trimOffsetWork;
		int[] array3 = capWork;
		int[] array4 = balanceWork;
		for (int i = start; i < end; i++)
		{
			array3[i] = Inlines.IMAX(C << 3, 3 * (m.eBands[i + 1] - m.eBands[i]) << LM << 3 >> 4);
			array4[i] = C * (m.eBands[i + 1] - m.eBands[i]) * (alloc_trim - 5 - LM) * (end - i - 1) * (1 << LM + 3) >> 6;
			if (m.eBands[i + 1] - m.eBands[i] << LM == 1)
			{
				array4[i] -= C << 3;
			}
		}
		int num4 = 1;
		int num5 = m.nbAllocVectors - 1;
		do
		{
			int num6 = 0;
			int num7 = 0;
			int num8 = num4 + num5 >> 1;
			int i = end;
			while (i-- > start)
			{
				int num9 = m.eBands[i + 1] - m.eBands[i];
				int num10 = C * num9 * m.allocVectors[num8 * nbEBands + i] << LM >> 2;
				if (num10 > 0)
				{
					num10 = Inlines.IMAX(0, num10 + array4[i]);
				}
				num10 += offsets[i];
				if (num10 >= array3[i] || num6 != 0)
				{
					num6 = 1;
					num7 += Inlines.IMIN(num10, cap[i]);
				}
				else if (num10 >= C << 3)
				{
					num7 += C << 3;
				}
			}
			if (num7 > total)
			{
				num5 = num8 - 1;
			}
			else
			{
				num4 = num8 + 1;
			}
		}
		while (num4 <= num5);
		num5 = num4--;
		for (int i = start; i < end; i++)
		{
			int num11 = m.eBands[i + 1] - m.eBands[i];
			int num12 = C * num11 * m.allocVectors[num4 * nbEBands + i] << LM >> 2;
			int num13 = ((num5 >= m.nbAllocVectors) ? cap[i] : (C * num11 * m.allocVectors[num5 * nbEBands + i] << LM >> 2));
			if (num12 > 0)
			{
				num12 = Inlines.IMAX(0, num12 + array4[i]);
			}
			if (num13 > 0)
			{
				num13 = Inlines.IMAX(0, num13 + array4[i]);
			}
			if (num4 > 0)
			{
				num12 += offsets[i];
			}
			num13 += offsets[i];
			if (offsets[i] > 0)
			{
				skip_start = i;
			}
			num13 = Inlines.IMAX(0, num13 - num12);
			array[i] = num12;
			array2[i] = num13;
		}
		return interp_bits2pulses_decode(m, start, end, skip_start, array, array2, array3, cap, total, out balance, num, ref intensity, num2, ref dual_stereo, num3, pulses, ebits, fine_priority, C, LM, ec, encodedData, prev, signalBandwidth);
	}
}
