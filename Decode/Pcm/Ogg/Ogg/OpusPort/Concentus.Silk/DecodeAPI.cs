using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Silk.Enums;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

internal static class DecodeAPI
{
	private static readonly int[] prevPitchLagScale = new int[3] { 6, 4, 3 };
	/// <summary>
	/// Reset decoder state
	/// </summary>
	/// <param name="decState">I/O  Stat</param>
	/// <returns>Returns error code</returns>
	internal static int silk_InitDecoder(SilkDecoder decState)
	{
		decState.Reset();
		int result = SilkError.SILK_NO_ERROR;
		SilkChannelDecoder[] channel_state = decState.channel_state;
		for (int i = 0; i < 2; i++)
		{
			result = channel_state[i].silk_init_decoder();
		}
		decState.sStereo.Reset();
		decState.prev_decode_only_middle = 0;
		return result;
	}

	internal static int silk_Decode(SilkDecoder psDec, DecControlState decControl, ReadOnlySpan<byte> frameData, int lostFlag, int newPacketFlag, EntropyCoder psRangeDec, Span<short> samplesOut, int samplesOut_ptr, out int nSamplesOut)
	{
		int num = 0;
		int num2 = SilkError.SILK_NO_ERROR;
		BoxedValueInt boxedValueInt = psDec.samplesOutWork;
		int[] array = psDec.sampleOffsetsWork;
		int[] array2 = psDec.stereoWork;
		SilkChannelDecoder[] channel_state = psDec.channel_state;
		nSamplesOut = 0;
		if (newPacketFlag != 0)
		{
			for (int i = 0; i < decControl.nChannelsInternal; i++)
			{
				channel_state[i].nFramesDecoded = 0;
			}
		}
		if (decControl.nChannelsInternal > psDec.nChannelsInternal)
		{
			num2 += channel_state[1].silk_init_decoder();
		}
		int num3 = ((decControl.nChannelsInternal == 1 && psDec.nChannelsInternal == 2 && decControl.internalSampleRate == 1000 * channel_state[0].fs_kHz) ? 1 : 0);
		if (channel_state[0].nFramesDecoded == 0)
		{
			for (int i = 0; i < decControl.nChannelsInternal; i++)
			{
				if (decControl.payloadSize_ms == 0)
				{
					channel_state[i].nFramesPerPacket = 1;
					channel_state[i].nb_subfr = 2;
				}
				else if (decControl.payloadSize_ms == 10)
				{
					channel_state[i].nFramesPerPacket = 1;
					channel_state[i].nb_subfr = 2;
				}
				else if (decControl.payloadSize_ms == 20)
				{
					channel_state[i].nFramesPerPacket = 1;
					channel_state[i].nb_subfr = 4;
				}
				else if (decControl.payloadSize_ms == 40)
				{
					channel_state[i].nFramesPerPacket = 2;
					channel_state[i].nb_subfr = 4;
				}
				else
				{
					if (decControl.payloadSize_ms != 60)
					{
						return SilkError.SILK_DEC_INVALID_FRAME_SIZE;
					}
					channel_state[i].nFramesPerPacket = 3;
					channel_state[i].nb_subfr = 4;
				}
				int num4 = (decControl.internalSampleRate >> 10) + 1;
				if (num4 != 8 && num4 != 12 && num4 != 16)
				{
					return SilkError.SILK_DEC_INVALID_SAMPLING_FREQUENCY;
				}
				num2 += channel_state[i].silk_decoder_set_fs(num4, decControl.API_sampleRate);
			}
		}
		if (decControl.nChannelsAPI == 2 && decControl.nChannelsInternal == 2 && (psDec.nChannelsAPI == 1 || psDec.nChannelsInternal == 1))
		{
			Arrays.MemSetShort(psDec.sStereo.pred_prev_Q13, 0, 2);
			Arrays.MemSetShort(psDec.sStereo.sSide, 0, 2);
			channel_state[1].resampler_state.Assign(channel_state[0].resampler_state);
		}
		psDec.nChannelsAPI = decControl.nChannelsAPI;
		psDec.nChannelsInternal = decControl.nChannelsInternal;
		if (decControl.API_sampleRate > 48000 || decControl.API_sampleRate < 8000)
		{
			return SilkError.SILK_DEC_INVALID_SAMPLING_FREQUENCY;
		}
		if (lostFlag != 1 && channel_state[0].nFramesDecoded == 0)
		{
			for (int i = 0; i < decControl.nChannelsInternal; i++)
			{
				for (int j = 0; j < channel_state[i].nFramesPerPacket; j++)
				{
					channel_state[i].VAD_flags[j] = psRangeDec.dec_bit_logp(frameData, 1u);
				}
				channel_state[i].LBRR_flag = psRangeDec.dec_bit_logp(frameData, 1u);
			}
			for (int i = 0; i < decControl.nChannelsInternal; i++)
			{
				Arrays.MemSetInt(channel_state[i].LBRR_flags, 0, 3);
				if (channel_state[i].LBRR_flag == 0)
				{
					continue;
				}
				if (channel_state[i].nFramesPerPacket == 1)
				{
					channel_state[i].LBRR_flags[0] = 1;
					continue;
				}
				int a = psRangeDec.dec_icdf(frameData, Tables.silk_LBRR_flags_iCDF_ptr[channel_state[i].nFramesPerPacket - 2], 8u) + 1;
				for (int j = 0; j < channel_state[i].nFramesPerPacket; j++)
				{
					channel_state[i].LBRR_flags[j] = Inlines.silk_RSHIFT(a, j) & 1;
				}
			}
			if (lostFlag == 0)
			{
				for (int j = 0; j < channel_state[0].nFramesPerPacket; j++)
				{
					for (int i = 0; i < decControl.nChannelsInternal; i++)
					{
						if (channel_state[i].LBRR_flags[j] == 0)
						{
							continue;
						}
						short[] pulses = psDec.lbrrPulsesWork;
						if (decControl.nChannelsInternal == 2 && i == 0)
						{
							Stereo.silk_stereo_decode_pred(psRangeDec, frameData, array2, psDec.stereoPredWork);
							if (channel_state[1].LBRR_flags[j] == 0)
							{
								BoxedValueInt boxedValueInt2 = psDec.decodeOnlyMidWork;
								boxedValueInt2.Val = num;
								Stereo.silk_stereo_decode_mid_only(psRangeDec, frameData, boxedValueInt2);
								num = boxedValueInt2.Val;
							}
						}
						DecodeIndices.silk_decode_indices(condCoding: (j > 0 && channel_state[i].LBRR_flags[j - 1] != 0) ? 2 : 0, psDec: channel_state[i], psRangeDec: psRangeDec, frameData: frameData, FrameIndex: j, decode_LBRR: 1);
						DecodePulses.silk_decode_pulses(channel_state[i], psRangeDec, frameData, pulses, channel_state[i].indices.signalType, channel_state[i].indices.quantOffsetType, channel_state[i].frame_length);
					}
				}
			}
		}
		if (decControl.nChannelsInternal == 2)
		{
			if (lostFlag == 0 || (lostFlag == 2 && channel_state[0].LBRR_flags[channel_state[0].nFramesDecoded] == 1))
			{
				Stereo.silk_stereo_decode_pred(psRangeDec, frameData, array2, psDec.stereoPredWork);
				if ((lostFlag == 0 && channel_state[1].VAD_flags[channel_state[0].nFramesDecoded] == 0) || (lostFlag == 2 && channel_state[1].LBRR_flags[channel_state[0].nFramesDecoded] == 0))
				{
					BoxedValueInt boxedValueInt3 = psDec.decodeOnlyMidWork;
					boxedValueInt3.Val = num;
					Stereo.silk_stereo_decode_mid_only(psRangeDec, frameData, boxedValueInt3);
					num = boxedValueInt3.Val;
				}
				else
				{
					num = 0;
				}
			}
			else
			{
				for (int i = 0; i < 2; i++)
				{
					array2[i] = psDec.sStereo.pred_prev_Q13[i];
				}
			}
		}
		if (decControl.nChannelsInternal == 2 && num == 0 && psDec.prev_decode_only_middle == 1)
		{
			Arrays.MemSetShort(psDec.channel_state[1].outBuf, 0, 480);
			Arrays.MemSetInt(psDec.channel_state[1].sLPC_Q14_buf, 0, 16);
			psDec.channel_state[1].lagPrev = 100;
			psDec.channel_state[1].LastGainIndex = 10;
			psDec.channel_state[1].prevSignalType = 0;
			psDec.channel_state[1].first_frame_after_reset = 1;
		}
		int num5 = ((decControl.internalSampleRate * decControl.nChannelsInternal < decControl.API_sampleRate * decControl.nChannelsAPI) ? 1 : 0);
		Span<short> span;
		if (num5 != 0)
		{
			span = samplesOut;
			array[0] = samplesOut_ptr;
			array[1] = samplesOut_ptr + channel_state[0].frame_length + 2;
		}
		else
		{
				span = psDec.internalSamplesWork;
			array[0] = 0;
			array[1] = channel_state[0].frame_length + 2;
		}
		int num6 = ((lostFlag != 0) ? ((psDec.prev_decode_only_middle == 0 || (decControl.nChannelsInternal == 2 && lostFlag == 2 && channel_state[1].LBRR_flags[channel_state[1].nFramesDecoded] == 1)) ? 1 : 0) : ((num == 0) ? 1 : 0));
		for (int i = 0; i < decControl.nChannelsInternal; i++)
		{
			if (i == 0 || num6 != 0)
			{
				int num7 = channel_state[0].nFramesDecoded - i;
				int condCoding = ((num7 > 0) ? ((lostFlag == 2) ? ((channel_state[i].LBRR_flags[num7 - 1] != 0) ? 2 : 0) : ((i > 0 && psDec.prev_decode_only_middle != 0) ? 1 : 2)) : 0);
				num2 += channel_state[i].silk_decode_frame(psRangeDec, frameData, span, array[i] + 2, boxedValueInt, lostFlag, condCoding);
			}
			else
			{
				Arrays.MemSetWithOffset<short>(span, 0, array[i] + 2, boxedValueInt.Val);
			}
			channel_state[i].nFramesDecoded++;
		}
		if (decControl.nChannelsAPI == 2 && decControl.nChannelsInternal == 2)
		{
			Stereo.silk_stereo_MS_to_LR(psDec.sStereo, span, array[0], span, array[1], array2, channel_state[0].fs_kHz, boxedValueInt.Val);
		}
		else
		{
			psDec.sStereo.sMid.AsSpan(0, 2).CopyTo(span.Slice(array[0]));
			span.Slice(array[0] + boxedValueInt.Val, 2).CopyTo(psDec.sStereo.sMid);
		}
		nSamplesOut = Inlines.silk_DIV32(boxedValueInt.Val * decControl.API_sampleRate, Inlines.silk_SMULBB(channel_state[0].fs_kHz, 1000));
		Span<short> output;
		int num8;
		if (decControl.nChannelsAPI == 2)
		{
			output = psDec.resampleWork;
			num8 = 0;
		}
		else
		{
			output = samplesOut;
			num8 = samplesOut_ptr;
		}
		if (num5 != 0)
		{
			short[] array3 = psDec.internalSamplesWork;
			samplesOut.Slice(samplesOut_ptr, decControl.nChannelsInternal * (channel_state[0].frame_length + 2)).CopyTo(array3);
			span = array3;
			array[0] = 0;
			array[1] = channel_state[0].frame_length + 2;
		}
		for (int i = 0; i < Inlines.silk_min(decControl.nChannelsAPI, decControl.nChannelsInternal); i++)
		{
			num2 += Resampler.silk_resampler(channel_state[i].resampler_state, output, num8, span, array[i] + 1, boxedValueInt.Val);
			if (decControl.nChannelsAPI == 2)
			{
				int num9 = samplesOut_ptr + i;
				for (int j = 0; j < nSamplesOut; j++)
				{
					samplesOut[num9 + 2 * j] = output[num8 + j];
				}
			}
		}
		if (decControl.nChannelsAPI == 2 && decControl.nChannelsInternal == 1)
		{
			if (num3 != 0)
			{
				num2 += Resampler.silk_resampler(channel_state[1].resampler_state, output, num8, span, array[0] + 1, boxedValueInt.Val);
				for (int j = 0; j < nSamplesOut; j++)
				{
					samplesOut[samplesOut_ptr + 1 + 2 * j] = output[num8 + j];
				}
			}
			else
			{
				for (int j = 0; j < nSamplesOut; j++)
				{
					samplesOut[samplesOut_ptr + 1 + 2 * j] = samplesOut[samplesOut_ptr + 2 * j];
				}
			}
		}
		if (channel_state[0].prevSignalType == 2)
		{
			decControl.prevPitchLag = channel_state[0].lagPrev * prevPitchLagScale[channel_state[0].fs_kHz - 8 >> 2];
		}
		else
		{
			decControl.prevPitchLag = 0;
		}
		if (lostFlag == 1)
		{
			for (int j = 0; j < psDec.nChannelsInternal; j++)
			{
				psDec.channel_state[j].LastGainIndex = 10;
			}
		}
		else
		{
			psDec.prev_decode_only_middle = num;
		}
		return num2;
	}
}
