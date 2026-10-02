using System;
using Concentus.Common;

namespace Concentus.Silk;

internal static class SumSqrShift
{
	/// <summary>
	/// Compute number of bits to right shift the sum of squares of a vector
	/// of int16s to make it fit in an int32
	/// </summary>
	/// <param name="energy">O   Energy of x, after shifting to the right</param>
	/// <param name="shift">O   Number of bits right shift applied to energy</param>
	/// <param name="x">I   Input vector</param>
	/// <param name="len">I   Length of input vector</param>
	internal static void silk_sum_sqr_shift(out int energy, out int shift, Span<short> x, int x_ptr, int len)
	{
		int num = 0;
		int num2 = 0;
		len--;
		int i;
		for (i = 0; i < len; i += 2)
		{
			num = Inlines.silk_SMLABB_ovflw(num, x[x_ptr + i], x[x_ptr + i]);
			num = Inlines.silk_SMLABB_ovflw(num, x[x_ptr + i + 1], x[x_ptr + i + 1]);
			if (num < 0)
			{
				num = (int)Inlines.silk_RSHIFT_uint((uint)num, 2);
				num2 = 2;
				i += 2;
				break;
			}
		}
		for (; i < len; i += 2)
		{
			int a = Inlines.silk_SMULBB(x[x_ptr + i], x[x_ptr + i]);
			a = Inlines.silk_SMLABB_ovflw(a, x[x_ptr + i + 1], x[x_ptr + i + 1]);
			num = (int)Inlines.silk_ADD_RSHIFT_uint((uint)num, (uint)a, num2);
			if (num < 0)
			{
				num = (int)Inlines.silk_RSHIFT_uint((uint)num, 2);
				num2 += 2;
			}
		}
		if (i == len)
		{
			int a = Inlines.silk_SMULBB(x[x_ptr + i], x[x_ptr + i]);
			num = (int)Inlines.silk_ADD_RSHIFT_uint((uint)num, (uint)a, num2);
		}
		if ((num & 0xC0000000u) != 0L)
		{
			num = (int)Inlines.silk_RSHIFT_uint((uint)num, 2);
			num2 += 2;
		}
		shift = num2;
		energy = num;
	}

	/// <summary>
	/// Zero-index variant
	/// Compute number of bits to right shift the sum of squares of a vector
	/// of int16s to make it fit in an int32
	/// </summary>
	/// <param name="energy">O   Energy of x, after shifting to the right</param>
	/// <param name="shift">O   Number of bits right shift applied to energy</param>
	/// <param name="x">I   Input vector</param>
	/// <param name="len">I   Length of input vector</param>
	internal static void silk_sum_sqr_shift(out int energy, out int shift, short[] x, int len)
	{
		int num = 0;
		int num2 = 0;
		len--;
		int i;
		for (i = 0; i < len; i += 2)
		{
			num = Inlines.silk_SMLABB_ovflw(num, x[i], x[i]);
			num = Inlines.silk_SMLABB_ovflw(num, x[i + 1], x[i + 1]);
			if (num < 0)
			{
				num = (int)Inlines.silk_RSHIFT_uint((uint)num, 2);
				num2 = 2;
				i += 2;
				break;
			}
		}
		for (; i < len; i += 2)
		{
			int a = Inlines.silk_SMULBB(x[i], x[i]);
			a = Inlines.silk_SMLABB_ovflw(a, x[i + 1], x[i + 1]);
			num = (int)Inlines.silk_ADD_RSHIFT_uint((uint)num, (uint)a, num2);
			if (num < 0)
			{
				num = (int)Inlines.silk_RSHIFT_uint((uint)num, 2);
				num2 += 2;
			}
		}
		if (i == len)
		{
			int a = Inlines.silk_SMULBB(x[i], x[i]);
			num = (int)Inlines.silk_ADD_RSHIFT_uint((uint)num, (uint)a, num2);
		}
		if ((num & 0xC0000000u) != 0L)
		{
			num = (int)Inlines.silk_RSHIFT_uint((uint)num, 2);
			num2 += 2;
		}
		shift = num2;
		energy = num;
	}
}
