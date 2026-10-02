using System;
using System.Numerics;
using Concentus.Common;

namespace Concentus.Celt;

internal static class CeltPitchXCorr
{
	internal static int pitch_xcorr(int[] _x, int x_idx, int[] _y, int y_idx, Span<int> xcorr, int len, int max_pitch)
	{
		int num = 1;
		int i;
		if (Vector.IsHardwareAccelerated)
		{
			for (i = 0; i < max_pitch - 3; i += 4)
			{
				int sum = 0;
				int sum2 = 0;
				int sum3 = 0;
				int sum4 = 0;
				Kernels.xcorr_kernel_vector(_x, x_idx, _y, y_idx + i, ref sum, ref sum2, ref sum3, ref sum4, len);
				xcorr[i] = sum;
				xcorr[i + 1] = sum2;
				xcorr[i + 2] = sum3;
				xcorr[i + 3] = sum4;
				sum = Inlines.MAX32(sum, sum2);
				sum3 = Inlines.MAX32(sum3, sum4);
				sum = Inlines.MAX32(sum, sum3);
				num = Inlines.MAX32(num, sum);
			}
		}
		else
		{
			for (i = 0; i < max_pitch - 3; i += 4)
			{
				int sum5 = 0;
				int sum6 = 0;
				int sum7 = 0;
				int sum8 = 0;
				Kernels.xcorr_kernel(_x, x_idx, _y, y_idx + i, ref sum5, ref sum6, ref sum7, ref sum8, len);
				xcorr[i] = sum5;
				xcorr[i + 1] = sum6;
				xcorr[i + 2] = sum7;
				xcorr[i + 3] = sum8;
				sum5 = Inlines.MAX32(sum5, sum6);
				sum7 = Inlines.MAX32(sum7, sum8);
				sum5 = Inlines.MAX32(sum5, sum7);
				num = Inlines.MAX32(num, sum5);
			}
		}
		for (; i < max_pitch; i++)
		{
			int num2 = Kernels.celt_inner_prod(_x.AsSpan(), _y.AsSpan(i), len);
			xcorr[i] = num2;
			num = Inlines.MAX32(num, num2);
		}
		return num;
	}

	internal static int pitch_xcorr(short[] _x, int x_idx, short[] _y, int y_idx, int[] xcorr, int len, int max_pitch)
	{
		int num = 1;
		int i;
		for (i = 0; i < max_pitch - 3; i += 4)
		{
			int sum = 0;
			int sum2 = 0;
			int sum3 = 0;
			int sum4 = 0;
			Kernels.xcorr_kernel(_x, x_idx, _y, y_idx + i, ref sum, ref sum2, ref sum3, ref sum4, len);
			xcorr[i] = sum;
			xcorr[i + 1] = sum2;
			xcorr[i + 2] = sum3;
			xcorr[i + 3] = sum4;
			sum = Inlines.MAX32(sum, sum2);
			sum3 = Inlines.MAX32(sum3, sum4);
			sum = Inlines.MAX32(sum, sum3);
			num = Inlines.MAX32(num, sum);
		}
		for (; i < max_pitch; i++)
		{
			num = Inlines.MAX32(num, xcorr[i] = Kernels.celt_inner_prod(_x.AsSpan(x_idx), _y.AsSpan(y_idx + i), len));
		}
		return num;
	}
}
