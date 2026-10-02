using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Enums;

namespace Concentus.Celt.Structs;

internal class CeltEncoder
{
	internal CeltMode mode;

	internal int channels;

	internal int stream_channels;

	internal int force_intra;

	internal int clip;

	internal int disable_pf;

	internal int complexity;

	internal int upsample;

	internal int start;

	internal int end;

	internal int bitrate;

	internal int vbr;

	internal int signalling;

	internal int constrained_vbr;

	internal int loss_rate;

	internal int lsb_depth;

	internal OpusFramesize variable_duration;

	internal int lfe;

	internal uint rng;

	internal int spread_decision;

	internal int delayedIntra;

	internal int tonal_average;

	internal int lastCodedBands;

	internal int hf_average;

	internal int tapset_decision;

	internal int prefilter_period;

	internal int prefilter_gain;

	internal int prefilter_tapset;

	internal int consec_transient;

	internal AnalysisInfo analysis = new AnalysisInfo();

	internal readonly int[] preemph_memE = new int[2];

	internal readonly int[] preemph_memD = new int[2];

	internal int vbr_reservoir;

	internal int vbr_drift;

	internal int vbr_offset;

	internal int vbr_count;

	internal int overlap_max;

	internal int stereo_saving;

	internal int intensity;

	internal int[] energy_mask;

	internal int spec_avg;

	/// <summary>
	/// The original C++ defined in_mem as a single float[1] which was the "caboose"
	/// to the overall encoder struct, containing 5 separate variable-sized buffer
	/// spaces of heterogeneous datatypes. I have laid them out into separate variables here,
	/// but these were the original definitions:
	/// val32 in_mem[],        Size = channels*mode.overlap
	/// val32 prefilter_mem[], Size = channels*COMBFILTER_MAXPERIOD
	/// val16 oldBandE[],      Size = channels*mode.nbEBands
	/// val16 oldLogE[],       Size = channels*mode.nbEBands
	/// val16 oldLogE2[],      Size = channels*mode.nbEBands
	/// </summary>
	internal int[][] in_mem;

	internal int[][] prefilter_mem;

	internal int[][] oldBandE;

	internal int[][] oldLogE;

	internal int[][] oldLogE2;

	private void Reset()
	{
		mode = null;
		channels = 0;
		stream_channels = 0;
		force_intra = 0;
		clip = 0;
		disable_pf = 0;
		complexity = 0;
		upsample = 0;
		start = 0;
		end = 0;
		bitrate = 0;
		vbr = 0;
		signalling = 0;
		constrained_vbr = 0;
		loss_rate = 0;
		lsb_depth = 0;
		variable_duration = (OpusFramesize)0;
		lfe = 0;
		PartialReset();
	}

	private void PartialReset()
	{
		rng = 0u;
		spread_decision = 0;
		delayedIntra = 0;
		tonal_average = 0;
		lastCodedBands = 0;
		hf_average = 0;
		tapset_decision = 0;
		prefilter_period = 0;
		prefilter_gain = 0;
		prefilter_tapset = 0;
		consec_transient = 0;
		analysis.Reset();
		preemph_memE[0] = 0;
		preemph_memE[1] = 0;
		preemph_memD[0] = 0;
		preemph_memD[1] = 0;
		vbr_reservoir = 0;
		vbr_drift = 0;
		vbr_offset = 0;
		vbr_count = 0;
		overlap_max = 0;
		stereo_saving = 0;
		intensity = 0;
		energy_mask = null;
		spec_avg = 0;
		in_mem = null;
		prefilter_mem = null;
		oldBandE = null;
		oldLogE = null;
		oldLogE2 = null;
	}

	internal void ResetState()
	{
		PartialReset();
		in_mem = Arrays.InitTwoDimensionalArray<int>(channels, mode.overlap);
		prefilter_mem = Arrays.InitTwoDimensionalArray<int>(channels, 1024);
		oldBandE = Arrays.InitTwoDimensionalArray<int>(channels, mode.nbEBands);
		oldLogE = Arrays.InitTwoDimensionalArray<int>(channels, mode.nbEBands);
		oldLogE2 = Arrays.InitTwoDimensionalArray<int>(channels, mode.nbEBands);
		for (int i = 0; i < mode.nbEBands; i++)
		{
			oldLogE[0][i] = (oldLogE2[0][i] = -28672);
		}
		if (channels == 2)
		{
			for (int i = 0; i < mode.nbEBands; i++)
			{
				oldLogE[1][i] = (oldLogE2[1][i] = -28672);
			}
		}
		vbr_offset = 0;
		delayedIntra = 1;
		spread_decision = 2;
		tonal_average = 256;
		hf_average = 0;
		tapset_decision = 0;
	}

	internal int opus_custom_encoder_init_arch(CeltMode mode, int channels)
	{
		if (channels < 0 || channels > 2)
		{
			return -1;
		}
		if (this == null || mode == null)
		{
			return -7;
		}
		Reset();
		this.mode = mode;
		stream_channels = (this.channels = channels);
		upsample = 1;
		start = 0;
		end = this.mode.effEBands;
		signalling = 1;
		constrained_vbr = 1;
		clip = 1;
		bitrate = -1;
		vbr = 0;
		force_intra = 0;
		complexity = 5;
		lsb_depth = 24;
		ResetState();
		return 0;
	}

	internal int celt_encoder_init(int sampling_rate, int channels)
	{
		int num = opus_custom_encoder_init_arch(CeltMode.mode48000_960_120, channels);
		if (num != 0)
		{
			return num;
		}
		upsample = CeltCommon.resampling_factor(sampling_rate);
		return 0;
	}

	internal int run_prefilter(int[][] input, int[][] prefilter_mem, int CC, int N, int prefilter_tapset, out int pitch, out int gain, out int qgain, int enabled, int nbAvailableBytes)
	{
		int[][] array = new int[CC][];
		CeltMode celtMode = mode;
		int overlap = celtMode.overlap;
		for (int i = 0; i < CC; i++)
		{
			array[i] = new int[N + 1024];
		}
		int num = 0;
		do
		{
			Arrays.MemCopy(prefilter_mem[num], 0, array[num], 0, 1024);
			Arrays.MemCopy(input[num], overlap, array[num], 1024, N);
		}
		while (++num < CC);
		int pitch2;
		int b;
		if (enabled != 0)
		{
			int[] array2 = new int[1024 + N >> 1];
			Pitch.pitch_downsample(array, array2, 1024 + N, CC);
			Pitch.pitch_search(array2, 512, array2, N, 979, out pitch2);
			pitch2 = 1024 - pitch2;
			b = Pitch.remove_doubling(array2, 1024, 15, N, ref pitch2, prefilter_period, prefilter_gain);
			if (pitch2 > 1022)
			{
				pitch2 = 1022;
			}
			b = Inlines.MULT16_16_Q15(22938, b);
			if (loss_rate > 2)
			{
				b = Inlines.HALF32(b);
			}
			if (loss_rate > 4)
			{
				b = Inlines.HALF32(b);
			}
			if (loss_rate > 8)
			{
				b = 0;
			}
		}
		else
		{
			b = 0;
			pitch2 = 15;
		}
		int num2 = 6554;
		if (Inlines.abs(pitch2 - prefilter_period) * 10 > pitch2)
		{
			num2 += 6554;
		}
		if (nbAvailableBytes < 25)
		{
			num2 += 3277;
		}
		if (nbAvailableBytes < 35)
		{
			num2 += 3277;
		}
		if (prefilter_gain > 13107)
		{
			num2 -= 3277;
		}
		if (prefilter_gain > 18022)
		{
			num2 -= 3277;
		}
		num2 = Inlines.MAX16(num2, 6554);
		int result;
		int num3;
		if (b < num2)
		{
			b = 0;
			result = 0;
			num3 = 0;
		}
		else
		{
			if (Inlines.ABS32(b - prefilter_gain) < 3277)
			{
				b = prefilter_gain;
			}
			num3 = (b + 1536 >> 10) / 3 - 1;
			num3 = Inlines.IMAX(0, Inlines.IMIN(7, num3));
			b = 3072 * (num3 + 1);
			result = 1;
		}
		num = 0;
		do
		{
			int num4 = celtMode.shortMdctSize - overlap;
			prefilter_period = Inlines.IMAX(prefilter_period, 15);
			Arrays.MemCopy(in_mem[num], 0, input[num], 0, overlap);
			if (num4 != 0)
			{
				CeltCommon.comb_filter(input[num], overlap, array[num], 1024, prefilter_period, prefilter_period, num4, -prefilter_gain, -prefilter_gain, this.prefilter_tapset, this.prefilter_tapset, null, 0);
			}
			CeltCommon.comb_filter(input[num], overlap + num4, array[num], 1024 + num4, prefilter_period, pitch2, N - num4, -prefilter_gain, -b, this.prefilter_tapset, prefilter_tapset, celtMode.window, overlap);
			Arrays.MemCopy(input[num], N, in_mem[num], 0, overlap);
			if (N > 1024)
			{
				Arrays.MemCopy(array[num], N, prefilter_mem[num], 0, 1024);
				continue;
			}
			Arrays.MemMoveInt(prefilter_mem[num], N, 0, 1024 - N);
			Arrays.MemCopy(array[num], 1024, prefilter_mem[num], 1024 - N, N);
		}
		while (++num < CC);
		gain = b;
		pitch = pitch2;
		qgain = num3;
		return result;
	}

	internal int celt_encode_with_ec(Span<short> pcm, int pcm_ptr, int frame_size, Span<byte> compressed, int compressed_ptr, int nbCompressedBytes, EntropyCoder enc)
	{
		int num = 0;
		int num2 = 0;
		int num3 = channels;
		int num4 = stream_channels;
		int pitch = 15;
		int gain = 0;
		int dual_stereo = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int tf_chan = 0;
		int pitch_change = 0;
		int num8 = 0;
		int surround_masking = 0;
		int num9 = 0;
		int surround_trim = 0;
		int num10 = 510000;
		CeltMode celtMode = mode;
		int nbEBands = celtMode.nbEBands;
		int overlap = celtMode.overlap;
		short[] eBands = celtMode.eBands;
		int num11 = start;
		int num12 = end;
		int tf_estimate = 0;
		if (nbCompressedBytes < 2 || pcm.IsEmpty)
		{
			return -1;
		}
		frame_size *= upsample;
		int i;
		for (i = 0; i <= celtMode.maxLM && celtMode.shortMdctSize << i != frame_size; i++)
		{
		}
		if (i > celtMode.maxLM)
		{
			return -1;
		}
		int num13 = 1 << i;
		int num14 = num13 * celtMode.shortMdctSize;
		int num16;
		int num15;
		if (enc == null)
		{
			num15 = 1;
			num16 = 0;
		}
		else
		{
			num15 = enc.tell();
			num16 = num15 + 4 >> 3;
		}
		nbCompressedBytes = Inlines.IMIN(nbCompressedBytes, 1275);
		int num17 = nbCompressedBytes - num16;
		int num19;
		int num20;
		if (vbr != 0 && bitrate != -1)
		{
			int num18 = celtMode.Fs >> 3;
			num19 = (bitrate * frame_size + (num18 >> 1)) / num18;
			num20 = num19 >> 6;
		}
		else
		{
			num19 = 0;
			int num21 = bitrate * frame_size;
			if (num15 > 1)
			{
				num21 += num15;
			}
			if (bitrate != -1)
			{
				nbCompressedBytes = Inlines.IMAX(2, Inlines.IMIN(nbCompressedBytes, (num21 + 4 * celtMode.Fs) / (8 * celtMode.Fs) - ((signalling != 0) ? 1 : 0)));
			}
			num20 = nbCompressedBytes;
		}
		if (bitrate != -1)
		{
			num10 = bitrate - (40 * num4 + 20) * ((400 >> i) - 50);
		}
		if (enc == null)
		{
			enc = new EntropyCoder();
			enc.enc_init((uint)nbCompressedBytes);
		}
		if (num19 > 0 && constrained_vbr != 0)
		{
			int num22 = num19;
			int num23 = Inlines.IMIN(Inlines.IMAX((num15 == 1) ? 2 : 0, num19 + num22 - vbr_reservoir >> 6), num17);
			if (num23 < num17)
			{
				nbCompressedBytes = num16 + num23;
				num17 = num23;
				enc.enc_shrink(compressed.Slice(compressed_ptr), (uint)nbCompressedBytes);
			}
		}
		int num24 = nbCompressedBytes * 8;
		int num25 = num12;
		if (num25 > celtMode.effEBands)
		{
			num25 = celtMode.effEBands;
		}
		int[][] array = Arrays.InitTwoDimensionalArray<int>(num3, num14 + overlap);
		int a = Inlines.MAX32(overlap_max, Inlines.celt_maxabs32(pcm, pcm_ptr, num4 * (num14 - overlap) / upsample));
		overlap_max = Inlines.celt_maxabs32(pcm, pcm_ptr + num4 * (num14 - overlap) / upsample, num4 * overlap / upsample);
		num7 = ((Inlines.MAX32(a, overlap_max) == 0) ? 1 : 0);
		if (num15 == 1)
		{
			enc.enc_bit_logp(compressed.Slice(compressed_ptr), num7, 15u);
		}
		else
		{
			num7 = 0;
		}
		if (num7 != 0)
		{
			if (num19 > 0)
			{
				num20 = (nbCompressedBytes = Inlines.IMIN(nbCompressedBytes, num16 + 2));
				num24 = nbCompressedBytes * 8;
				num17 = 2;
				enc.enc_shrink(compressed.Slice(compressed_ptr), (uint)nbCompressedBytes);
			}
			num15 = nbCompressedBytes * 8;
			enc.nbits_total += num15 - enc.tell();
		}
		int num26 = 0;
		do
		{
			int num27 = 0;
			CeltCommon.celt_preemphasis(pcm, pcm_ptr + num26, array[num26], overlap, num14, num3, upsample, celtMode.preemph, ref preemph_memE[num26], num27);
		}
		while (++num26 < num3);
		int enabled = ((((lfe != 0 && num17 > 3) || num17 > 12 * num4) && num11 == 0 && num7 == 0 && disable_pf == 0 && complexity >= 5 && (consec_transient == 0 || i == 3 || variable_duration != OpusFramesize.OPUS_FRAMESIZE_VARIABLE)) ? 1 : 0);
		num5 = tapset_decision;
		int qgain;
		int num28 = run_prefilter(array, prefilter_mem, num3, num14, num5, out pitch, out gain, out qgain, enabled, num17);
		if ((gain > 13107 || prefilter_gain > 13107) && (analysis.valid == 0 || (double)analysis.tonality > 0.3) && ((double)pitch > 1.26 * (double)prefilter_period || (double)pitch < 0.79 * (double)prefilter_period))
		{
			pitch_change = 1;
		}
		if (num28 == 0)
		{
			if (num11 == 0 && num15 + 16 <= num24)
			{
				enc.enc_bit_logp(compressed.Slice(compressed_ptr), 0, 1u);
			}
		}
		else
		{
			enc.enc_bit_logp(compressed.Slice(compressed_ptr), 1, 1u);
			pitch++;
			int num29 = Inlines.EC_ILOG((uint)pitch) - 5;
			enc.enc_uint(compressed.Slice(compressed_ptr), (uint)num29, 6u);
			enc.enc_bits(compressed.Slice(compressed_ptr), (uint)(pitch - (16 << num29)), (uint)(4 + num29));
			pitch--;
			enc.enc_bits(compressed.Slice(compressed_ptr), (uint)qgain, 3u);
			enc.enc_icdf(compressed.Slice(compressed_ptr), num5, Tables.tapset_icdf, 2u);
		}
		num2 = 0;
		num = 0;
		if (complexity >= 1 && lfe == 0)
		{
			num2 = CeltCommon.transient_analysis(array, num14 + overlap, num3, out tf_estimate, out tf_chan);
		}
		if (i > 0 && enc.tell() + 3 <= num24)
		{
			if (num2 != 0)
			{
				num = num13;
			}
		}
		else
		{
			num2 = 0;
			num8 = 1;
		}
		int[][] array2 = Arrays.InitTwoDimensionalArray<int>(num3, num14);
		int[][] array3 = Arrays.InitTwoDimensionalArray<int>(num3, nbEBands);
		int[][] array4 = Arrays.InitTwoDimensionalArray<int>(num3, nbEBands);
		int num30 = ((num != 0 && complexity >= 8) ? 1 : 0);
		int[][] array5 = Arrays.InitTwoDimensionalArray<int>(num3, nbEBands);
		if (num30 != 0)
		{
			CeltCommon.compute_mdcts(celtMode, 0, array, array2, num4, num3, i, upsample);
			Bands.compute_band_energies(celtMode, array2, array3, num25, num4, i);
			QuantizeBands.amp2Log2(celtMode, num25, num12, array3, array5, num4);
			for (int j = 0; j < nbEBands; j++)
			{
				array5[0][j] += Inlines.HALF16(Inlines.SHL16(i, 10));
			}
			if (num4 == 2)
			{
				for (int j = 0; j < nbEBands; j++)
				{
					array5[1][j] += Inlines.HALF16(Inlines.SHL16(i, 10));
				}
			}
		}
		CeltCommon.compute_mdcts(celtMode, num, array, array2, num4, num3, i, upsample);
		if (num3 == 2 && num4 == 1)
		{
			tf_chan = 0;
		}
		Bands.compute_band_energies(celtMode, array2, array3, num25, num4, i);
		if (lfe != 0)
		{
			for (int j = 2; j < num12; j++)
			{
				array3[0][j] = Inlines.IMIN(array3[0][j], Inlines.MULT16_32_Q15((short)3, array3[0][0]));
				array3[0][j] = Inlines.MAX32(array3[0][j], 1);
			}
		}
		QuantizeBands.amp2Log2(celtMode, num25, num12, array3, array4, num4);
		int[] array6 = new int[num4 * nbEBands];
		if (num11 == 0 && energy_mask != null && lfe == 0)
		{
			int num31 = 0;
			int num32 = 0;
			int num33 = 0;
			int num34 = Inlines.IMAX(2, lastCodedBands);
			for (num26 = 0; num26 < num4; num26++)
			{
				for (int j = 0; j < num34; j++)
				{
					int num35 = Inlines.MAX16(Inlines.MIN16(energy_mask[nbEBands * num26 + j], 256), -2048);
					if (num35 > 0)
					{
						num35 = Inlines.HALF16(num35);
					}
					num31 += Inlines.MULT16_16(num35, eBands[j + 1] - eBands[j]);
					num33 += eBands[j + 1] - eBands[j];
					num32 += Inlines.MULT16_16(num35, 1 + 2 * j - num34);
				}
			}
			num31 = Inlines.DIV32_16(num31, num33);
			num31 += 205;
			num32 = num32 * 6 / (num4 * (num34 - 1) * (num34 + 1) * num34);
			num32 = Inlines.HALF32(num32);
			num32 = Inlines.MAX32(Inlines.MIN32(num32, 32), -32);
			int k;
			for (k = 0; eBands[k + 1] < eBands[num34] / 2; k++)
			{
			}
			int num36 = 0;
			for (int j = 0; j < num34; j++)
			{
				int num37 = num31 + num32 * (j - k);
				int a2 = ((num4 != 2) ? energy_mask[j] : Inlines.MAX16(energy_mask[j], energy_mask[nbEBands + j]));
				a2 = Inlines.MIN16(a2, 0);
				a2 -= num37;
				if (a2 > 256)
				{
					array6[j] = a2 - 256;
					num36++;
				}
			}
			if (num36 >= 3)
			{
				num31 += 256;
				if (num31 > 0)
				{
					num31 = 0;
					num32 = 0;
					Arrays.MemSetInt(array6, 0, num34);
				}
				else
				{
					for (int j = 0; j < num34; j++)
					{
						array6[j] = Inlines.MAX16(0, array6[j] - 256);
					}
				}
			}
			num31 += 205;
			surround_trim = 64 * num32;
			surround_masking = num31;
		}
		if (lfe == 0)
		{
			int num38 = -10240;
			int num39 = 0;
			int num40 = ((num != 0) ? Inlines.HALF16(Inlines.SHL16(i, 10)) : 0);
			for (int j = num11; j < num12; j++)
			{
				num38 = Inlines.MAX16(num38 - 1024, array4[0][j] - num40);
				if (num4 == 2)
				{
					num38 = Inlines.MAX16(num38, array4[1][j] - num40);
				}
				num39 += num38;
			}
			num39 /= num12 - num11;
			num9 = Inlines.SUB16(num39, spec_avg);
			num9 = Inlines.MIN16(3072, Inlines.MAX16(-1536, num9));
			spec_avg += (short)Inlines.MULT16_16_Q15(655, num9);
		}
		if (num30 == 0)
		{
			Arrays.MemCopy(array4[0], 0, array5[0], 0, nbEBands);
			if (num4 == 2)
			{
				Arrays.MemCopy(array4[1], 0, array5[1], 0, nbEBands);
			}
		}
		if (i > 0 && enc.tell() + 3 <= num24 && num2 == 0 && complexity >= 5 && lfe == 0 && CeltCommon.patch_transient_decision(array4, oldBandE, nbEBands, num11, num12, num4) != 0)
		{
			num2 = 1;
			num = num13;
			CeltCommon.compute_mdcts(celtMode, num, array, array2, num4, num3, i, upsample);
			Bands.compute_band_energies(celtMode, array2, array3, num25, num4, i);
			QuantizeBands.amp2Log2(celtMode, num25, num12, array3, array4, num4);
			for (int j = 0; j < nbEBands; j++)
			{
				array5[0][j] += Inlines.HALF16(Inlines.SHL16(i, 10));
			}
			if (num4 == 2)
			{
				for (int j = 0; j < nbEBands; j++)
				{
					array5[1][j] += Inlines.HALF16(Inlines.SHL16(i, 10));
				}
			}
			tf_estimate = 3277;
		}
		if (i > 0 && enc.tell() + 3 <= num24)
		{
			enc.enc_bit_logp(compressed.Slice(compressed_ptr), num2, 3u);
		}
		int[][] array7 = Arrays.InitTwoDimensionalArray<int>(num4, num14);
		Bands.normalise_bands(celtMode, array2, array7, array3, num25, num4, num13);
		int[] array8 = new int[nbEBands];
		int tf_select;
		if (num20 >= 15 * num4 && num11 == 0 && complexity >= 2 && lfe == 0)
		{
			int num41 = ((num20 < 40) ? 12 : ((num20 < 60) ? 6 : ((num20 >= 100) ? 3 : 4)));
			num41 *= 2;
			tf_select = CeltCommon.tf_analysis(celtMode, num25, num2, array8, num41, array7, num14, i, out var _, tf_estimate, tf_chan);
			for (int j = num25; j < num12; j++)
			{
				array8[j] = array8[num25 - 1];
			}
		}
		else
		{
			int tf_sum = 0;
			for (int j = 0; j < num12; j++)
			{
				array8[j] = num2;
			}
			tf_select = 0;
		}
		int[][] error = Arrays.InitTwoDimensionalArray<int>(num4, nbEBands);
		QuantizeBands.quant_coarse_energy(celtMode, num11, num12, num25, array4, oldBandE, (uint)num24, error, enc, compressed.Slice(compressed_ptr), num4, i, num17, force_intra, ref delayedIntra, (complexity >= 4) ? 1 : 0, loss_rate, lfe);
		CeltCommon.tf_encode(num11, num12, num2, array8, i, tf_select, enc, compressed.Slice(compressed_ptr));
		if (enc.tell() + 4 <= num24)
		{
			if (lfe != 0)
			{
				tapset_decision = 0;
				spread_decision = 2;
			}
			else if (num != 0 || complexity < 3 || num17 < 10 * num4 || num11 != 0)
			{
				if (complexity == 0)
				{
					spread_decision = 0;
				}
				else
				{
					spread_decision = 2;
				}
			}
			else
			{
				spread_decision = Bands.spreading_decision(celtMode, array7, ref tonal_average, spread_decision, ref hf_average, ref tapset_decision, (num28 != 0 && num == 0) ? 1 : 0, num25, num4, num13);
			}
			enc.enc_icdf(compressed.Slice(compressed_ptr), spread_decision, Tables.spread_icdf, 5u);
		}
		int[] array9 = new int[nbEBands];
		int tot_boost_;
		int maxDepth = CeltCommon.dynalloc_analysis(array4, array5, nbEBands, num11, num12, num4, array9, lsb_depth, celtMode.logN, num2, vbr, constrained_vbr, eBands, i, num20, out tot_boost_, lfe, array6);
		if (lfe != 0)
		{
			array9[0] = Inlines.IMIN(8, num20 / 3);
		}
		int[] array10 = new int[nbEBands];
		CeltCommon.init_caps(celtMode, array10, i, num4);
		int num42 = 6;
		num24 <<= 3;
		int num43 = 0;
		num15 = (int)enc.tell_frac();
		for (int j = num11; j < num12; j++)
		{
			int num44 = num4 * (eBands[j + 1] - eBands[j]) << i;
			int num45 = Inlines.IMIN(num44 << 3, Inlines.IMAX(48, num44));
			int num46 = num42;
			int num47 = 0;
			int num48 = 0;
			while (num15 + (num46 << 3) < num24 - num43 && num47 < array10[j])
			{
				int num49 = ((num48 < array9[j]) ? 1 : 0);
				enc.enc_bit_logp(compressed.Slice(compressed_ptr), num49, (uint)num46);
				num15 = (int)enc.tell_frac();
				if (num49 == 0)
				{
					break;
				}
				num47 += num45;
				num43 += num45;
				num46 = 1;
				num48++;
			}
			if (num48 != 0)
			{
				num42 = Inlines.IMAX(2, num42 - 1);
			}
			array9[j] = num47;
		}
		if (num4 == 2)
		{
			if (i != 0)
			{
				dual_stereo = CeltCommon.stereo_analysis(celtMode, array7, i);
			}
			intensity = Bands.hysteresis_decision(num10 / 1000, Tables.intensity_thresholds, Tables.intensity_histeresis, 21, intensity);
			intensity = Inlines.IMIN(num12, Inlines.IMAX(num11, intensity));
		}
		int num50 = 5;
		if (num15 + 48 <= num24 - num43)
		{
			num50 = ((lfe == 0) ? CeltCommon.alloc_trim_analysis(celtMode, array7, array4, num12, i, num4, analysis, ref stereo_saving, tf_estimate, intensity, surround_trim) : 5);
			enc.enc_icdf(compressed.Slice(compressed_ptr), num50, Tables.trim_icdf, 7u);
			num15 = (int)enc.tell_frac();
		}
		if (num19 > 0)
		{
			int num51 = celtMode.maxLM - i;
			nbCompressedBytes = Inlines.IMIN(nbCompressedBytes, 1275 >> 3 - i);
			int num52 = num19 - (40 * num4 + 20 << 3);
			if (constrained_vbr != 0)
			{
				num52 += vbr_offset >> num51;
			}
			int num53 = CeltCommon.compute_vbr(celtMode, analysis, num52, i, num10, lastCodedBands, num4, intensity, constrained_vbr, stereo_saving, tot_boost_, tf_estimate, pitch_change, maxDepth, variable_duration, lfe, (energy_mask != null) ? 1 : 0, surround_masking, num9);
			num53 += num15;
			int a3 = (num15 + num43 + 64 - 1 >> 6) + 2 - num16;
			num17 = num53 + 32 >> 6;
			num17 = Inlines.IMAX(a3, num17);
			num17 = Inlines.IMIN(nbCompressedBytes, num17 + num16) - num16;
			int num54 = num53 - num19;
			num53 = num17 << 6;
			if (num7 != 0)
			{
				num17 = 2;
				num53 = 128;
				num54 = 0;
			}
			int a4;
			if (vbr_count < 970)
			{
				vbr_count++;
				a4 = Inlines.celt_rcp(Inlines.SHL32(vbr_count + 20, 16));
			}
			else
			{
				a4 = 33;
			}
			if (constrained_vbr != 0)
			{
				vbr_reservoir += num53 - num19;
			}
			if (constrained_vbr != 0)
			{
				vbr_drift += Inlines.MULT16_32_Q15(a4, num54 * (1 << num51) - vbr_offset - vbr_drift);
				vbr_offset = -vbr_drift;
			}
			if (constrained_vbr != 0 && vbr_reservoir < 0)
			{
				int num55 = -vbr_reservoir / 64;
				num17 += ((num7 == 0) ? num55 : 0);
				vbr_reservoir = 0;
			}
			nbCompressedBytes = Inlines.IMIN(nbCompressedBytes, num17 + num16);
			enc.enc_shrink(compressed.Slice(compressed_ptr), (uint)nbCompressedBytes);
		}
		int[] array11 = new int[nbEBands];
		int[] pulses = new int[nbEBands];
		int[] fine_priority = new int[nbEBands];
		int num56 = (nbCompressedBytes * 8 << 3) - (int)enc.tell_frac() - 1;
		int num57 = ((num2 != 0 && i >= 2 && num56 >= i + 2 << 3) ? 8 : 0);
		num56 -= num57;
		int signalBandwidth = num12 - 1;
		if (analysis.valid != 0)
		{
			int b = ((num10 < 32000 * num4) ? 13 : ((num10 < 48000 * num4) ? 16 : ((num10 < 60000 * num4) ? 18 : ((num10 >= 80000 * num4) ? 20 : 19))));
			signalBandwidth = Inlines.IMAX(analysis.bandwidth, b);
		}
		if (lfe != 0)
		{
			signalBandwidth = 1;
		}
		int balance;
		int num58 = Rate.compute_allocation_encode(celtMode, num11, num12, array9, array10, num50, ref intensity, ref dual_stereo, num56, out balance, pulses, array11, fine_priority, num4, i, enc, compressed.Slice(compressed_ptr), lastCodedBands, signalBandwidth);
		if (lastCodedBands != 0)
		{
			lastCodedBands = Inlines.IMIN(lastCodedBands + 1, Inlines.IMAX(lastCodedBands - 1, num58));
		}
		else
		{
			lastCodedBands = num58;
		}
		QuantizeBands.quant_fine_energy(celtMode, num11, num12, oldBandE, error, array11, enc, compressed.Slice(compressed_ptr), num4);
		byte[] collapse_masks = new byte[num4 * nbEBands];
		Bands.quant_all_bands_encode(1, celtMode, num11, num12, array7[0], (num4 == 2) ? array7[1] : null, collapse_masks, array3, pulses, num, spread_decision, dual_stereo, intensity, array8, nbCompressedBytes * 64 - num57, balance, enc, compressed.Slice(compressed_ptr), i, num58, ref rng);
		if (num57 > 0)
		{
			num6 = ((consec_transient < 2) ? 1 : 0);
			enc.enc_bits(compressed.Slice(compressed_ptr), (uint)num6, 1u);
		}
		QuantizeBands.quant_energy_finalise(celtMode, num11, num12, oldBandE, error, array11, fine_priority, nbCompressedBytes * 8 - enc.tell(), enc, compressed.Slice(compressed_ptr), num4);
		if (num7 != 0)
		{
			for (int j = 0; j < nbEBands; j++)
			{
				oldBandE[0][j] = -28672;
			}
			if (num4 == 2)
			{
				for (int j = 0; j < nbEBands; j++)
				{
					oldBandE[1][j] = -28672;
				}
			}
		}
		prefilter_period = pitch;
		prefilter_gain = gain;
		prefilter_tapset = num5;
		if (num3 == 2 && num4 == 1)
		{
			Arrays.MemCopy(oldBandE[0], 0, oldBandE[1], 0, nbEBands);
		}
		if (num2 == 0)
		{
			Arrays.MemCopy(oldLogE[0], 0, oldLogE2[0], 0, nbEBands);
			Arrays.MemCopy(oldBandE[0], 0, oldLogE[0], 0, nbEBands);
			if (num3 == 2)
			{
				Arrays.MemCopy(oldLogE[1], 0, oldLogE2[1], 0, nbEBands);
				Arrays.MemCopy(oldBandE[1], 0, oldLogE[1], 0, nbEBands);
			}
		}
		else
		{
			for (int j = 0; j < nbEBands; j++)
			{
				oldLogE[0][j] = Inlines.MIN16(oldLogE[0][j], oldBandE[0][j]);
			}
			if (num3 == 2)
			{
				for (int j = 0; j < nbEBands; j++)
				{
					oldLogE[1][j] = Inlines.MIN16(oldLogE[1][j], oldBandE[1][j]);
				}
			}
		}
		num26 = 0;
		do
		{
			for (int j = 0; j < num11; j++)
			{
				oldBandE[num26][j] = 0;
				oldLogE[num26][j] = (oldLogE2[num26][j] = -28672);
			}
			for (int j = num12; j < nbEBands; j++)
			{
				oldBandE[num26][j] = 0;
				oldLogE[num26][j] = (oldLogE2[num26][j] = -28672);
			}
		}
		while (++num26 < num3);
		if (num2 != 0 || num8 != 0)
		{
			consec_transient++;
		}
		else
		{
			consec_transient = 0;
		}
		rng = enc.rng;
		enc.enc_done(compressed.Slice(compressed_ptr));
		if (enc.get_error() != 0)
		{
			return -3;
		}
		return nbCompressedBytes;
	}

	internal void SetComplexity(int value)
	{
		if (value < 0 || value > 10)
		{
			throw new ArgumentException("Complexity must be between 0 and 10 inclusive");
		}
		complexity = value;
	}

	internal void SetStartBand(int value)
	{
		if (value < 0 || value >= mode.nbEBands)
		{
			throw new ArgumentException("Start band above max number of ebands (or negative)");
		}
		start = value;
	}

	internal void SetEndBand(int value)
	{
		if (value < 1 || value > mode.nbEBands)
		{
			throw new ArgumentException("End band above max number of ebands (or less than 1)");
		}
		end = value;
	}

	internal void SetPacketLossPercent(int value)
	{
		if (value < 0 || value > 100)
		{
			throw new ArgumentException("Packet loss must be between 0 and 100");
		}
		loss_rate = value;
	}

	internal void SetPrediction(int value)
	{
		if (value < 0 || value > 2)
		{
			throw new ArgumentException("CELT prediction mode must be 0, 1, or 2");
		}
		disable_pf = ((value <= 1) ? 1 : 0);
		force_intra = ((value == 0) ? 1 : 0);
	}

	internal void SetVBRConstraint(bool value)
	{
		constrained_vbr = (value ? 1 : 0);
	}

	internal void SetVBR(bool value)
	{
		vbr = (value ? 1 : 0);
	}

	internal void SetBitrate(int value)
	{
		if (value <= 500 && value != -1)
		{
			throw new ArgumentException("Bitrate out of range");
		}
		value = Inlines.IMIN(value, 260000 * channels);
		bitrate = value;
	}

	internal void SetChannels(int value)
	{
		if (value < 1 || value > 2)
		{
			throw new ArgumentException("Channel count must be 1 or 2");
		}
		stream_channels = value;
	}

	internal void SetLSBDepth(int value)
	{
		if (value < 8 || value > 24)
		{
			throw new ArgumentException("Bit depth must be between 8 and 24");
		}
		lsb_depth = value;
	}

	internal int GetLSBDepth()
	{
		return lsb_depth;
	}

	internal void SetExpertFrameDuration(OpusFramesize value)
	{
		variable_duration = value;
	}

	internal void SetSignalling(int value)
	{
		signalling = value;
	}

	internal void SetAnalysis(AnalysisInfo value)
	{
		if (value == null)
		{
			throw new ArgumentNullException("AnalysisInfo");
		}
		analysis.Assign(value);
	}

	internal CeltMode GetMode()
	{
		return mode;
	}

	internal uint GetFinalRange()
	{
		return rng;
	}

	internal void SetLFE(int value)
	{
		lfe = value;
	}

	internal void SetEnergyMask(int[] value)
	{
		energy_mask = value;
	}
}
