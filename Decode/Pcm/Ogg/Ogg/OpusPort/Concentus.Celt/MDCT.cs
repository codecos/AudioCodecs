using System;
using Concentus.Celt.Structs;
using Concentus.Common;

namespace Concentus.Celt;

internal static class MDCT
{
	internal static void clt_mdct_forward(MDCTLookup l, Span<int> input, int input_ptr, Span<int> output, int output_ptr, int[] window, int overlap, int shift, int stride)
	{
		FFTState fFTState = l.kfft[shift];
		int num = 0;
		int shift2 = fFTState.scale_shift - 1;
		int scale = fFTState.scale;
		int num2 = l.n;
		short[] trig = l.trig;
		int i;
		for (i = 0; i < shift; i++)
		{
			num2 >>= 1;
			num += num2;
		}
		int num3 = num2 >> 1;
		int num4 = num2 >> 2;
		int[] array = new int[num3];
		int[] array2 = new int[num4 * 2];
		int num5 = input_ptr + (overlap >> 1);
		int num6 = input_ptr + num3 - 1 + (overlap >> 1);
		int num7 = 0;
		int num8 = overlap >> 1;
		int num9 = (overlap >> 1) - 1;
		for (i = 0; i < overlap + 3 >> 2; i++)
		{
			array[num7++] = Inlines.MULT16_32_Q15(window[num9], input[num5 + num3]) + Inlines.MULT16_32_Q15(window[num8], input[num6]);
			array[num7++] = Inlines.MULT16_32_Q15(window[num8], input[num5]) - Inlines.MULT16_32_Q15(window[num9], input[num6 - num3]);
			num5 += 2;
			num6 -= 2;
			num8 += 2;
			num9 -= 2;
		}
		num8 = 0;
		num9 = overlap - 1;
		for (; i < num4 - (overlap + 3 >> 2); i++)
		{
			array[num7++] = input[num6];
			array[num7++] = input[num5];
			num5 += 2;
			num6 -= 2;
		}
		for (; i < num4; i++)
		{
			array[num7++] = Inlines.MULT16_32_Q15(window[num9], input[num6]) - Inlines.MULT16_32_Q15(window[num8], input[num5 - num3]);
			array[num7++] = Inlines.MULT16_32_Q15(window[num9], input[num5]) + Inlines.MULT16_32_Q15(window[num8], input[num6 + num3]);
			num5 += 2;
			num6 -= 2;
			num8 += 2;
			num9 -= 2;
		}
		int num10 = 0;
		int num11 = num;
		for (i = 0; i < num4; i++)
		{
			short b = trig[num11 + i];
			short b2 = trig[num11 + num4 + i];
			int a = array[num10++];
			int a2 = array[num10++];
			int b3 = KissFFT.S_MUL(a, b) - KissFFT.S_MUL(a2, b2);
			int b4 = KissFFT.S_MUL(a2, b) + KissFFT.S_MUL(a, b2);
			array2[2 * fFTState.bitrev[i]] = Inlines.PSHR32(Inlines.MULT16_32_Q16(scale, b3), shift2);
			array2[2 * fFTState.bitrev[i] + 1] = Inlines.PSHR32(Inlines.MULT16_32_Q16(scale, b4), shift2);
		}
		KissFFT.opus_fft_impl(fFTState, array2, 0);
		int num12 = 0;
		int num13 = output_ptr;
		int num14 = output_ptr + stride * (num3 - 1);
		int num15 = num;
		for (i = 0; i < num4; i++)
		{
			int num16 = KissFFT.S_MUL(array2[num12 + 1], trig[num15 + num4 + i]) - KissFFT.S_MUL(array2[num12], trig[num15 + i]);
			int num17 = KissFFT.S_MUL(array2[num12], trig[num15 + num4 + i]) + KissFFT.S_MUL(array2[num12 + 1], trig[num15 + i]);
			output[num13] = num16;
			output[num14] = num17;
			num12 += 2;
			num13 += 2 * stride;
			num14 -= 2 * stride;
		}
	}

	internal static void clt_mdct_backward(MDCTLookup l, Span<int> input, int input_ptr, Span<int> output, int output_ptr, int[] window, int overlap, int shift, int stride)
	{
		int num = 0;
		int num2 = l.n;
		for (int i = 0; i < shift; i++)
		{
			num2 >>= 1;
			num += num2;
		}
		int num3 = num2 >> 1;
		int num4 = num2 >> 2;
		int num5 = input_ptr + stride * (num3 - 1);
		int num6 = output_ptr + (overlap >> 1);
		short[] bitrev = l.kfft[shift].bitrev;
		int num7 = 0;
		for (int i = 0; i < num4; i++)
		{
			int num8 = bitrev[num7++];
			int num9 = num6 + 2 * num8;
			output[num9 + 1] = KissFFT.S_MUL(input[num5], l.trig[num + i]) + KissFFT.S_MUL(input[input_ptr], l.trig[num + num4 + i]);
			output[num9] = KissFFT.S_MUL(input[input_ptr], l.trig[num + i]) - KissFFT.S_MUL(input[num5], l.trig[num + num4 + i]);
			input_ptr += 2 * stride;
			num5 -= 2 * stride;
		}
		KissFFT.opus_fft_impl(l.kfft[shift], output, output_ptr + (overlap >> 1));
		int num10 = output_ptr + (overlap >> 1);
		int num11 = output_ptr + (overlap >> 1) + num3 - 2;
		int num12 = num;
		int num13 = num12 + num4 - 1;
		int num14 = num12 + num3 - 1;
		for (int i = 0; i < num4 + 1 >> 1; i++)
		{
			int a = output[num10 + 1];
			int a2 = output[num10];
			short b = l.trig[num12 + i];
			short b2 = l.trig[num12 + num4 + i];
			int num15 = KissFFT.S_MUL(a, b) + KissFFT.S_MUL(a2, b2);
			int num16 = KissFFT.S_MUL(a, b2) - KissFFT.S_MUL(a2, b);
			int a3 = output[num11 + 1];
			a2 = output[num11];
			output[num10] = num15;
			output[num11 + 1] = num16;
			b = l.trig[num13 - i];
			b2 = l.trig[num14 - i];
			num15 = KissFFT.S_MUL(a3, b) + KissFFT.S_MUL(a2, b2);
			num16 = KissFFT.S_MUL(a3, b2) - KissFFT.S_MUL(a2, b);
			output[num11] = num15;
			output[num10 + 1] = num16;
			num10 += 2;
			num11 -= 2;
		}
		int index = output_ptr + overlap - 1;
		num11 = output_ptr;
		int num17 = 0;
		int num18 = overlap - 1;
		for (int i = 0; i < overlap / 2; i++)
		{
			int b3 = output[index];
			int b4 = output[num11];
			output[num11++] = Inlines.MULT16_32_Q15(window[num18], b4) - Inlines.MULT16_32_Q15(window[num17], b3);
			output[index--] = Inlines.MULT16_32_Q15(window[num17], b4) + Inlines.MULT16_32_Q15(window[num18], b3);
			num17++;
			num18--;
		}
	}
}
