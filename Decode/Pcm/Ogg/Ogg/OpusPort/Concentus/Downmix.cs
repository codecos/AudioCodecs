using System;
using Concentus.Common;

namespace Concentus;

internal static class Downmix
{
	/// <summary>
	///
	/// </summary>
	/// <typeparam name="T">The type of signal being handled (either short or float)</typeparam>
	/// <param name="_x"></param>
	/// <param name="sub"></param>
	/// <param name="subframe"></param>
	/// <param name="offset"></param>
	/// <param name="c1"></param>
	/// <param name="c2"></param>
	/// <param name="C"></param>
	internal delegate void downmix_func<T>(ReadOnlySpan<T> _x, Span<int> sub, int sub_ptr, int subframe, int offset, int c1, int c2, int C);

	internal static void downmix_float(ReadOnlySpan<float> x, Span<int> sub, int sub_ptr, int subframe, int offset, int c1, int c2, int C)
	{
		for (int i = 0; i < subframe; i++)
		{
			sub[sub_ptr + i] = Inlines.FLOAT2INT16(x[(i + offset) * C + c1]);
		}
		if (c2 > -1)
		{
			for (int i = 0; i < subframe; i++)
			{
				sub[sub_ptr + i] += Inlines.FLOAT2INT16(x[(i + offset) * C + c2]);
			}
		}
		else if (c2 == -2)
		{
			for (int j = 1; j < C; j++)
			{
				int num = j;
				for (int i = 0; i < subframe; i++)
				{
					sub[sub_ptr + i] += Inlines.FLOAT2INT16(x[(i + offset) * C + num]);
				}
			}
		}
		int num2 = 4096;
		num2 = ((C != -2) ? (num2 / 2) : (num2 / C));
		for (int i = 0; i < subframe; i++)
		{
			sub[sub_ptr + i] *= num2;
		}
	}

	internal static void downmix_int(ReadOnlySpan<short> x, Span<int> sub, int sub_ptr, int subframe, int offset, int c1, int c2, int C)
	{
		for (int i = 0; i < subframe; i++)
		{
			sub[i + sub_ptr] = x[(i + offset) * C + c1];
		}
		if (c2 > -1)
		{
			for (int i = 0; i < subframe; i++)
			{
				sub[i + sub_ptr] += x[(i + offset) * C + c2];
			}
		}
		else if (c2 == -2)
		{
			for (int j = 1; j < C; j++)
			{
				for (int i = 0; i < subframe; i++)
				{
					sub[i + sub_ptr] += x[(i + offset) * C + j];
				}
			}
		}
		int num = 4096;
		num = ((C != -2) ? (num / 2) : (num / C));
		for (int i = 0; i < subframe; i++)
		{
			sub[i + sub_ptr] *= num;
		}
	}
}
