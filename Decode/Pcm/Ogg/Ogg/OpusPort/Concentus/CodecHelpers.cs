using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Enums;
using Concentus.Silk;
using Concentus.Structs;

namespace Concentus;

internal static class CodecHelpers
{
	private const int MAX_DYNAMIC_FRAMESIZE = 24;

	internal static byte gen_toc(OpusMode mode, int framerate, OpusBandwidth bandwidth, int channels)
	{
		int num = 0;
		while (framerate < 400)
		{
			framerate <<= 1;
			num++;
		}
		byte b;
		switch (mode)
		{
		case OpusMode.MODE_SILK_ONLY:
			b = (byte)((int)(bandwidth - 1101) << 5);
			b |= (byte)(num - 2 << 3);
			break;
		case OpusMode.MODE_CELT_ONLY:
		{
			int num2 = (int)(bandwidth - 1102);
			if (num2 < 0)
			{
				num2 = 0;
			}
			b = 128;
			b |= (byte)(num2 << 5);
			b |= (byte)(num << 3);
			break;
		}
		default:
			b = 96;
			b |= (byte)((int)(bandwidth - 1104) << 4);
			b |= (byte)(num - 2 << 3);
			break;
		}
		return (byte)(b | (byte)(((channels == 2) ? 1u : 0u) << 2));
	}

	internal static void hp_cutoff(ReadOnlySpan<short> input, int input_ptr, int cutoff_Hz, Span<short> output, int output_ptr, int[] hp_mem, int len, int channels, int Fs)
	{
		int[] array = new int[3];
		int[] array2 = new int[2];
		int num = Inlines.silk_DIV32_16(Inlines.silk_SMULBB(2471, cutoff_Hz), Fs / 1000);
		int num2 = (array[0] = 268435456 - Inlines.silk_MUL(471, num));
		array[1] = Inlines.silk_LSHIFT(-num2, 1);
		array[2] = num2;
		int num3 = Inlines.silk_RSHIFT(num2, 6);
		array2[0] = Inlines.silk_SMULWW(num3, Inlines.silk_SMULWW(num, num) - 8388608);
		array2[1] = Inlines.silk_SMULWW(num3, num3);
		Filters.silk_biquad_alt(input, input_ptr, array, array2, hp_mem, 0, output, output_ptr, len, channels);
		if (channels == 2)
		{
			Filters.silk_biquad_alt(input, input_ptr + 1, array, array2, hp_mem, 2, output, output_ptr + 1, len, channels);
		}
	}

	internal static void dc_reject(ReadOnlySpan<short> input, int input_ptr, int cutoff_Hz, Span<short> output, int output_ptr, int[] hp_mem, int len, int channels, int Fs)
	{
		int shift = Inlines.celt_ilog2(Fs / (cutoff_Hz * 3));
		for (int i = 0; i < channels; i++)
		{
			for (int j = 0; j < len; j++)
			{
				int num = Inlines.SHL32(Inlines.EXTEND32(input[channels * j + i + input_ptr]), 15);
				int num2 = num - hp_mem[2 * i];
				hp_mem[2 * i] += Inlines.PSHR32(num - hp_mem[2 * i], shift);
				int a = num2 - hp_mem[2 * i + 1];
				hp_mem[2 * i + 1] += Inlines.PSHR32(num2 - hp_mem[2 * i + 1], shift);
				output[channels * j + i + output_ptr] = Inlines.EXTRACT16(Inlines.SATURATE(Inlines.PSHR32(a, 15), 32767));
			}
		}
	}

	internal static void stereo_fade(short[] pcm_buf, int g1, int g2, int overlap48, int frame_size, int channels, int[] window, int Fs)
	{
		int num = 48000 / Fs;
		int num2 = overlap48 / num;
		g1 = 32767 - g1;
		g2 = 32767 - g2;
		int i;
		for (i = 0; i < num2; i++)
		{
			int num3 = Inlines.MULT16_16_Q15(window[i * num], window[i * num]);
			int a = Inlines.SHR32(Inlines.MAC16_16(Inlines.MULT16_16(num3, g2), 32767 - num3, g1), 15);
			int b = Inlines.EXTRACT16(Inlines.HALF32(pcm_buf[i * channels] - pcm_buf[i * channels + 1]));
			b = Inlines.MULT16_16_Q15(a, b);
			pcm_buf[i * channels] = (short)(pcm_buf[i * channels] - b);
			pcm_buf[i * channels + 1] = (short)(pcm_buf[i * channels + 1] + b);
		}
		for (; i < frame_size; i++)
		{
			int b2 = Inlines.EXTRACT16(Inlines.HALF32(pcm_buf[i * channels] - pcm_buf[i * channels + 1]));
			b2 = Inlines.MULT16_16_Q15(g2, b2);
			pcm_buf[i * channels] = (short)(pcm_buf[i * channels] - b2);
			pcm_buf[i * channels + 1] = (short)(pcm_buf[i * channels + 1] + b2);
		}
	}

	internal static void gain_fade(short[] buffer, int buf_ptr, int g1, int g2, int overlap48, int frame_size, int channels, int[] window, int Fs)
	{
		int num = 48000 / Fs;
		int num2 = overlap48 / num;
		if (channels == 1)
		{
			for (int i = 0; i < num2; i++)
			{
				int num3 = Inlines.MULT16_16_Q15(window[i * num], window[i * num]);
				int a = Inlines.SHR32(Inlines.MAC16_16(Inlines.MULT16_16(num3, g2), 32767 - num3, g1), 15);
				buffer[buf_ptr + i] = (short)Inlines.MULT16_16_Q15(a, buffer[buf_ptr + i]);
			}
		}
		else
		{
			for (int i = 0; i < num2; i++)
			{
				int num4 = Inlines.MULT16_16_Q15(window[i * num], window[i * num]);
				int a2 = Inlines.SHR32(Inlines.MAC16_16(Inlines.MULT16_16(num4, g2), 32767 - num4, g1), 15);
				buffer[buf_ptr + i * 2] = (short)Inlines.MULT16_16_Q15(a2, buffer[buf_ptr + i * 2]);
				buffer[buf_ptr + i * 2 + 1] = (short)Inlines.MULT16_16_Q15(a2, buffer[buf_ptr + i * 2 + 1]);
			}
		}
		int num5 = 0;
		do
		{
			for (int i = num2; i < frame_size; i++)
			{
				buffer[buf_ptr + i * channels + num5] = (short)Inlines.MULT16_16_Q15(g2, buffer[buf_ptr + i * channels + num5]);
			}
		}
		while (++num5 < channels);
	}

	internal static float transient_boost(Span<float> E, int E_ptr, float[] E_1, int LM, int maxM)
	{
		float num = 0f;
		float num2 = 0f;
		int num3 = Inlines.IMIN(maxM, (1 << LM) + 1);
		for (int i = E_ptr; i < num3 + E_ptr; i++)
		{
			num += E[i];
			num2 += E_1[i];
		}
		float num4 = num * num2 / (float)(num3 * num3);
		return Inlines.MIN16(1f, (float)Math.Sqrt(Inlines.MAX16(0f, 0.05f * (num4 - 2f))));
	}

	internal static int transient_viterbi(float[] E, float[] E_1, int N, int frame_cost, int rate)
	{
		float[][] array = Arrays.InitTwoDimensionalArray<float>(24, 16);
		int[][] array2 = Arrays.InitTwoDimensionalArray<int>(24, 16);
		float num = ((rate < 80) ? 0f : ((rate <= 160) ? (((float)rate - 80f) / 80f) : 1f));
		for (int i = 0; i < 16; i++)
		{
			array2[0][i] = -1;
			array[0][i] = 1E+10f;
		}
		for (int i = 0; i < 4; i++)
		{
			array[0][1 << i] = (float)(frame_cost + rate * (1 << i)) * (1f + num * transient_boost(E, 0, E_1, i, N + 1));
			array2[0][1 << i] = i;
		}
		for (int i = 1; i < N; i++)
		{
			for (int j = 2; j < 16; j++)
			{
				array[i][j] = array[i - 1][j - 1];
				array2[i][j] = j - 1;
			}
			for (int j = 0; j < 4; j++)
			{
				array2[i][1 << j] = 1;
				float num2 = array[i - 1][1];
				for (int k = 1; k < 4; k++)
				{
					float num3 = array[i - 1][(1 << k + 1) - 1];
					if (num3 < num2)
					{
						array2[i][1 << j] = (1 << k + 1) - 1;
						num2 = num3;
					}
				}
				float num4 = (float)(frame_cost + rate * (1 << j)) * (1f + num * transient_boost(E, i, E_1, j, N - i + 1));
				array[i][1 << j] = num2;
				if (N - i < 1 << j)
				{
					array[i][1 << j] += num4 * (float)(N - i) / (float)(1 << j);
				}
				else
				{
					array[i][1 << j] += num4;
				}
			}
		}
		int num5 = 1;
		float num6 = array[N - 1][1];
		for (int i = 2; i < 16; i++)
		{
			if (array[N - 1][i] < num6)
			{
				num6 = array[N - 1][i];
				num5 = i;
			}
		}
		for (int i = N - 1; i >= 0; i--)
		{
			num5 = array2[i][num5];
		}
		return num5;
	}

	internal static int optimize_framesize<T>(ReadOnlySpan<T> x, int len, int C, int Fs, int bitrate, int tonality, float[] mem, int buffering, Downmix.downmix_func<T> downmix)
	{
		float[] array = new float[28];
		float[] array2 = new float[27];
		int num = 0;
		int num2 = Fs / 400;
		int[] array3 = new int[num2];
		array[0] = mem[0];
		array2[0] = 1f / (1f + mem[0]);
		int num3;
		int num4;
		if (buffering != 0)
		{
			num3 = 2 * num2 - buffering;
			len -= num3;
			array[1] = mem[1];
			array2[1] = 1f / (1f + mem[1]);
			array[2] = mem[2];
			array2[2] = 1f / (1f + mem[2]);
			num4 = 3;
		}
		else
		{
			num4 = 1;
			num3 = 0;
		}
		int num5 = Inlines.IMIN(len / num2, 24);
		int num6 = 0;
		int i;
		for (i = 0; i < num5; i++)
		{
			float num7 = 1f;
			downmix(x, array3, 0, num2, i * num2 + num3, 0, -2, C);
			if (i == 0)
			{
				num6 = array3[0];
			}
			for (int j = 0; j < num2; j++)
			{
				int num8 = array3[j];
				num7 += (float)(num8 - num6) * (float)(num8 - num6);
				num6 = num8;
			}
			array[i + num4] = num7;
			array2[i + num4] = 1f / num7;
		}
		array[i + num4] = array[i + num4 - 1];
		if (buffering != 0)
		{
			num5 = Inlines.IMIN(24, num5 + 2);
		}
		num = transient_viterbi(array, array2, num5, (int)((1f + 0.5f * (float)tonality) * (float)(60 * C + 40)), bitrate / 400);
		mem[0] = array[1 << num];
		if (buffering != 0)
		{
			mem[1] = array[(1 << num) + 1];
			mem[2] = array[(1 << num) + 2];
		}
		return num;
	}

	internal static int frame_size_select(int frame_size, OpusFramesize variable_duration, int Fs)
	{
		if (frame_size < Fs / 400)
		{
			return -1;
		}
		int num;
		switch (variable_duration)
		{
		case OpusFramesize.OPUS_FRAMESIZE_ARG:
			num = frame_size;
			break;
		case OpusFramesize.OPUS_FRAMESIZE_VARIABLE:
			num = Fs / 50;
			break;
		case OpusFramesize.OPUS_FRAMESIZE_2_5_MS:
		case OpusFramesize.OPUS_FRAMESIZE_5_MS:
		case OpusFramesize.OPUS_FRAMESIZE_10_MS:
		case OpusFramesize.OPUS_FRAMESIZE_20_MS:
		case OpusFramesize.OPUS_FRAMESIZE_40_MS:
		case OpusFramesize.OPUS_FRAMESIZE_60_MS:
			num = Inlines.IMIN(3 * Fs / 50, Fs / 400 << (int)(variable_duration - 5001));
			break;
		default:
			return -1;
		}
		if (num > frame_size)
		{
			return -1;
		}
		if (400 * num != Fs && 200 * num != Fs && 100 * num != Fs && 50 * num != Fs && 25 * num != Fs && 50 * num != 3 * Fs)
		{
			return -1;
		}
		return num;
	}

	internal static int compute_frame_size<T>(ReadOnlySpan<T> analysis_pcm, int frame_size, OpusFramesize variable_duration, int C, int Fs, int bitrate_bps, int delay_compensation, Downmix.downmix_func<T> downmix, float[] subframe_mem, bool analysis_enabled)
	{
		if (analysis_enabled && variable_duration == OpusFramesize.OPUS_FRAMESIZE_VARIABLE && frame_size >= Fs / 200)
		{
			int num = 3;
			num = optimize_framesize(analysis_pcm, frame_size, C, Fs, bitrate_bps, 0, subframe_mem, delay_compensation, downmix);
			while (Fs / 400 << num > frame_size)
			{
				num--;
			}
			frame_size = Fs / 400 << num;
		}
		else
		{
			frame_size = frame_size_select(frame_size, variable_duration, Fs);
		}
		if (frame_size < 0)
		{
			return -1;
		}
		return frame_size;
	}

	internal static int compute_stereo_width(ReadOnlySpan<short> pcm, int pcm_ptr, int frame_size, int Fs, StereoWidthState mem)
	{
		int num = Fs / frame_size;
		int a = 32767 - 819175 / Inlines.IMAX(50, num);
		int num3;
		int num4;
		int num2 = (num3 = (num4 = 0));
		for (int i = 0; i < frame_size - 3; i += 4)
		{
			int num5 = 0;
			int num6 = 0;
			int num7 = 0;
			int num8 = pcm_ptr + 2 * i;
			int num9 = pcm[num8];
			int num10 = pcm[num8 + 1];
			num5 = Inlines.SHR32(Inlines.MULT16_16(num9, num9), 2);
			num6 = Inlines.SHR32(Inlines.MULT16_16(num9, num10), 2);
			num7 = Inlines.SHR32(Inlines.MULT16_16(num10, num10), 2);
			num9 = pcm[num8 + 2];
			num10 = pcm[num8 + 3];
			num5 += Inlines.SHR32(Inlines.MULT16_16(num9, num9), 2);
			num6 += Inlines.SHR32(Inlines.MULT16_16(num9, num10), 2);
			num7 += Inlines.SHR32(Inlines.MULT16_16(num10, num10), 2);
			num9 = pcm[num8 + 4];
			num10 = pcm[num8 + 5];
			num5 += Inlines.SHR32(Inlines.MULT16_16(num9, num9), 2);
			num6 += Inlines.SHR32(Inlines.MULT16_16(num9, num10), 2);
			num7 += Inlines.SHR32(Inlines.MULT16_16(num10, num10), 2);
			num9 = pcm[num8 + 6];
			num10 = pcm[num8 + 7];
			num5 += Inlines.SHR32(Inlines.MULT16_16(num9, num9), 2);
			num6 += Inlines.SHR32(Inlines.MULT16_16(num9, num10), 2);
			num7 += Inlines.SHR32(Inlines.MULT16_16(num10, num10), 2);
			num2 += Inlines.SHR32(num5, 10);
			num3 += Inlines.SHR32(num6, 10);
			num4 += Inlines.SHR32(num7, 10);
		}
		mem.XX += Inlines.MULT16_32_Q15(a, num2 - mem.XX);
		mem.XY += Inlines.MULT16_32_Q15(a, num3 - mem.XY);
		mem.YY += Inlines.MULT16_32_Q15(a, num4 - mem.YY);
		mem.XX = Inlines.MAX32(0, mem.XX);
		mem.XY = Inlines.MAX32(0, mem.XY);
		mem.YY = Inlines.MAX32(0, mem.YY);
		if (Inlines.MAX32(mem.XX, mem.YY) > 210)
		{
			int num11 = Inlines.celt_sqrt(mem.XX);
			int num12 = Inlines.celt_sqrt(mem.YY);
			int num13 = Inlines.celt_sqrt(num11);
			int num14 = Inlines.celt_sqrt(num12);
			mem.XY = Inlines.MIN32(mem.XY, num11 * num12);
			int num15 = Inlines.SHR32(Inlines.frac_div32(mem.XY, 1 + Inlines.MULT16_16(num11, num12)), 16);
			int b = 32767 * Inlines.ABS16(num13 - num14) / (1 + num13 + num14);
			int num16 = Inlines.MULT16_16_Q15(Inlines.celt_sqrt(1073741824 - Inlines.MULT16_16(num15, num15)), b);
			mem.smoothed_width += (num16 - mem.smoothed_width) / num;
			mem.max_follower = Inlines.MAX16(mem.max_follower - 655 / num, mem.smoothed_width);
		}
		else
		{
			int num16 = 0;
			int num15 = 32767;
			int b = 0;
		}
		return Inlines.EXTRACT16(Inlines.MIN32(32767, 20 * mem.max_follower));
	}

	internal static void smooth_fade(Span<short> in1, int in1_ptr, Span<short> in2, int in2_ptr, Span<short> output, int output_ptr, int overlap, int channels, int[] window, int Fs)
	{
		int num = 48000 / Fs;
		for (int i = 0; i < channels; i++)
		{
			for (int j = 0; j < overlap; j++)
			{
				int num2 = Inlines.MULT16_16_Q15(window[j * num], window[j * num]);
				output[output_ptr + j * channels + i] = (short)Inlines.SHR32(Inlines.MAC16_16(Inlines.MULT16_16(num2, in2[in2_ptr + j * channels + i]), 32767 - num2, in1[in1_ptr + j * channels + i]), 15);
			}
		}
	}

	internal static string opus_strerror(int error)
	{
		string[] array = new string[8] { "success", "invalid argument", "buffer too small", "internal error", "corrupted stream", "request not implemented", "invalid state", "memory allocation failed" };
		if (error > 0 || error < -7)
		{
			return "unknown error";
		}
		return array[-error];
	}

	/// <summary>
	/// Returns the version number of this library
	/// </summary>
	/// <returns></returns>
	internal static string GetVersionString()
	{
		return "Concentus 2.1.2";
	}
}
