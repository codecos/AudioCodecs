using System;
using System.Numerics;
using Concentus.Common;

namespace Concentus.Celt;

internal static class Kernels
{
	internal static void celt_fir(Span<short> x, Span<short> num, Span<short> y, int N, int ord, Span<short> mem)
	{
		Span<short> array = stackalloc short[ord];
		Span<short> array2 = stackalloc short[N + ord];
		int i;
		for (i = 0; i < ord; i++)
		{
			array[i] = num[ord - i - 1];
		}
		for (i = 0; i < ord; i++)
		{
			array2[i] = mem[ord - i - 1];
		}
		for (i = 0; i < N; i++)
		{
			array2[i + ord] = x[i];
		}
		for (i = 0; i < ord; i++)
		{
			mem[i] = x[N - i - 1];
		}
		for (i = 0; i < N - 3; i += 4)
		{
			int sum = 0;
			int sum2 = 0;
			int sum3 = 0;
			int sum4 = 0;
			xcorr_kernel(array, 0, array2, i, ref sum, ref sum2, ref sum3, ref sum4, ord);
			y[i] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i]), Inlines.PSHR32(sum, 12)));
			y[i + 1] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i + 1]), Inlines.PSHR32(sum2, 12)));
			y[i + 2] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i + 2]), Inlines.PSHR32(sum3, 12)));
			y[i + 3] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i + 3]), Inlines.PSHR32(sum4, 12)));
		}
		for (; i < N; i++)
		{
			int num2 = 0;
			for (int j = 0; j < ord; j++)
			{
				num2 = Inlines.MAC16_16(num2, array[j], array2[i + j]);
			}
			y[i] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i]), Inlines.PSHR32(num2, 12)));
		}
	}

	internal static void celt_fir(Span<int> x, Span<int> num, Span<int> y, int N, int ord, Span<int> mem)
	{
		Span<int> array = stackalloc int[ord];
		Span<int> array2 = stackalloc int[N + ord];
		int i;
		for (i = 0; i < ord; i++)
		{
			array[i] = num[ord - i - 1];
		}
		for (i = 0; i < ord; i++)
		{
			array2[i] = mem[ord - i - 1];
		}
		for (i = 0; i < N; i++)
		{
			array2[i + ord] = x[i];
		}
		for (i = 0; i < ord; i++)
		{
			mem[i] = x[N - i - 1];
		}
		for (i = 0; i < N - 3; i += 4)
		{
			int sum = 0;
			int sum2 = 0;
			int sum3 = 0;
			int sum4 = 0;
			xcorr_kernel(array, 0, array2, i, ref sum, ref sum2, ref sum3, ref sum4, ord);
			y[i] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i]), Inlines.PSHR32(sum, 12)));
			y[i + 1] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i + 1]), Inlines.PSHR32(sum2, 12)));
			y[i + 2] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i + 2]), Inlines.PSHR32(sum3, 12)));
			y[i + 3] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i + 3]), Inlines.PSHR32(sum4, 12)));
		}
		for (; i < N; i++)
		{
			int num2 = 0;
			for (int j = 0; j < ord; j++)
			{
				num2 = Inlines.MAC16_16(num2, array[j], array2[i + j]);
			}
			y[i] = Inlines.SATURATE16(Inlines.ADD32(Inlines.EXTEND32(x[i]), Inlines.PSHR32(num2, 12)));
		}
	}

	/// <summary>
	/// OPT: This is the kernel you really want to optimize. It gets used a lot by the prefilter and by the PLC.
	/// </summary>
	/// <param name="x"></param>
	/// <param name="y"></param>
	/// <param name="sum0"></param>
	/// <param name="len"></param>
	internal static void xcorr_kernel(Span<short> x, int x_idx, Span<short> y, int y_idx, ref int sum0, ref int sum1, ref int sum2, ref int sum3, int len)
	{
		int num = x_idx;
		int num2 = y_idx;
		short b = 0;
		short b2 = y[num2++];
		short b3 = y[num2++];
		short b4 = y[num2++];
		int i;
		for (i = 0; i < len - 3; i += 4)
		{
			short a = x[num++];
			b = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a, b2);
			sum1 = Inlines.MAC16_16(sum1, a, b3);
			sum2 = Inlines.MAC16_16(sum2, a, b4);
			sum3 = Inlines.MAC16_16(sum3, a, b);
			a = x[num++];
			b2 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a, b3);
			sum1 = Inlines.MAC16_16(sum1, a, b4);
			sum2 = Inlines.MAC16_16(sum2, a, b);
			sum3 = Inlines.MAC16_16(sum3, a, b2);
			a = x[num++];
			b3 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a, b4);
			sum1 = Inlines.MAC16_16(sum1, a, b);
			sum2 = Inlines.MAC16_16(sum2, a, b2);
			sum3 = Inlines.MAC16_16(sum3, a, b3);
			a = x[num++];
			b4 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a, b);
			sum1 = Inlines.MAC16_16(sum1, a, b2);
			sum2 = Inlines.MAC16_16(sum2, a, b3);
			sum3 = Inlines.MAC16_16(sum3, a, b4);
		}
		if (i++ < len)
		{
			short a2 = x[num++];
			b = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a2, b2);
			sum1 = Inlines.MAC16_16(sum1, a2, b3);
			sum2 = Inlines.MAC16_16(sum2, a2, b4);
			sum3 = Inlines.MAC16_16(sum3, a2, b);
		}
		if (i++ < len)
		{
			short a3 = x[num++];
			b2 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a3, b3);
			sum1 = Inlines.MAC16_16(sum1, a3, b4);
			sum2 = Inlines.MAC16_16(sum2, a3, b);
			sum3 = Inlines.MAC16_16(sum3, a3, b2);
		}
		if (i < len)
		{
			short a4 = x[num++];
			b3 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a4, b4);
			sum1 = Inlines.MAC16_16(sum1, a4, b);
			sum2 = Inlines.MAC16_16(sum2, a4, b2);
			sum3 = Inlines.MAC16_16(sum3, a4, b3);
		}
	}

	internal static void xcorr_kernel(Span<int> x, int x_idx, Span<int> y, int y_idx, ref int sum0, ref int sum1, ref int sum2, ref int sum3, int len)
	{
		int num = x_idx;
		int num2 = y_idx;
		int b = 0;
		int b2 = y[num2++];
		int b3 = y[num2++];
		int b4 = y[num2++];
		int i;
		for (i = 0; i < len - 3; i += 4)
		{
			int a = x[num++];
			b = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a, b2);
			sum1 = Inlines.MAC16_16(sum1, a, b3);
			sum2 = Inlines.MAC16_16(sum2, a, b4);
			sum3 = Inlines.MAC16_16(sum3, a, b);
			a = x[num++];
			b2 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a, b3);
			sum1 = Inlines.MAC16_16(sum1, a, b4);
			sum2 = Inlines.MAC16_16(sum2, a, b);
			sum3 = Inlines.MAC16_16(sum3, a, b2);
			a = x[num++];
			b3 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a, b4);
			sum1 = Inlines.MAC16_16(sum1, a, b);
			sum2 = Inlines.MAC16_16(sum2, a, b2);
			sum3 = Inlines.MAC16_16(sum3, a, b3);
			a = x[num++];
			b4 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a, b);
			sum1 = Inlines.MAC16_16(sum1, a, b2);
			sum2 = Inlines.MAC16_16(sum2, a, b3);
			sum3 = Inlines.MAC16_16(sum3, a, b4);
		}
		if (i++ < len)
		{
			int a2 = x[num++];
			b = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a2, b2);
			sum1 = Inlines.MAC16_16(sum1, a2, b3);
			sum2 = Inlines.MAC16_16(sum2, a2, b4);
			sum3 = Inlines.MAC16_16(sum3, a2, b);
		}
		if (i++ < len)
		{
			int a3 = x[num++];
			b2 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a3, b3);
			sum1 = Inlines.MAC16_16(sum1, a3, b4);
			sum2 = Inlines.MAC16_16(sum2, a3, b);
			sum3 = Inlines.MAC16_16(sum3, a3, b2);
		}
		if (i < len)
		{
			int a4 = x[num++];
			b3 = y[num2++];
			sum0 = Inlines.MAC16_16(sum0, a4, b4);
			sum1 = Inlines.MAC16_16(sum1, a4, b);
			sum2 = Inlines.MAC16_16(sum2, a4, b2);
			sum3 = Inlines.MAC16_16(sum3, a4, b3);
		}
	}

	internal static void xcorr_kernel_vector(int[] x, int x_idx, int[] y, int y_idx, ref int sum0, ref int sum1, ref int sum2, ref int sum3, int len)
	{
		int i = 0;
		for (int num = len - 4 - (len - 4) % Vector<int>.Count; i < num; i += Vector<int>.Count)
		{
			Vector<int> left = new Vector<int>(x, x_idx + i);
			sum0 += Vector.Dot(Vector<int>.One, Vector.Multiply(left, new Vector<int>(y, y_idx + i)));
			sum1 += Vector.Dot(Vector<int>.One, Vector.Multiply(left, new Vector<int>(y, y_idx + i + 1)));
			sum2 += Vector.Dot(Vector<int>.One, Vector.Multiply(left, new Vector<int>(y, y_idx + i + 2)));
			sum3 += Vector.Dot(Vector<int>.One, Vector.Multiply(left, new Vector<int>(y, y_idx + i + 3)));
		}
		for (; i < len; i++)
		{
			int a = x[x_idx + i];
			sum0 = Inlines.MAC16_16(sum0, a, y[y_idx + i]);
			sum1 = Inlines.MAC16_16(sum1, a, y[y_idx + i + 1]);
			sum2 = Inlines.MAC16_16(sum2, a, y[y_idx + i + 2]);
			sum3 = Inlines.MAC16_16(sum3, a, y[y_idx + i + 3]);
		}
	}

	internal static int celt_inner_prod(Span<short> x, Span<short> y, int N)
	{
		int num = 0;
		for (int i = 0; i < N; i++)
		{
			num = Inlines.MAC16_16(num, x[i], y[i]);
		}
		return num;
	}

	internal static int celt_inner_prod(Span<int> x, Span<int> y, int N)
	{
		int num = 0;
		for (int i = 0; i < N; i++)
		{
			num = Inlines.MAC16_16(num, x[i], y[i]);
		}
		return num;
	}

	internal static void dual_inner_prod(Span<int> x, Span<int> y01, Span<int> y02, int N, out int xy1, out int xy2)
	{
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < N; i++)
		{
			num = Inlines.MAC16_16(num, x[i], y01[i]);
			num2 = Inlines.MAC16_16(num2, x[i], y02[i]);
		}
		xy1 = num;
		xy2 = num2;
	}
}
