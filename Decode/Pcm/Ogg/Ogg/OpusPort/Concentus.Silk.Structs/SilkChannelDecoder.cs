using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

/// <summary>
/// Decoder state
/// </summary>
internal class SilkChannelDecoder
{
	internal int prev_gain_Q16;

	internal readonly int[] exc_Q14 = new int[320];

	internal readonly int[] sLPC_Q14_buf = new int[16];

	internal readonly short[] outBuf = new short[480];

	internal int lagPrev;

	internal sbyte LastGainIndex;

	internal int fs_kHz;

	internal int fs_API_hz;

	internal int nb_subfr;

	internal int frame_length;

	internal int subfr_length;

	internal int ltp_mem_length;

	internal int LPC_order;

	internal readonly short[] prevNLSF_Q15 = new short[16];

	internal int first_frame_after_reset;

	internal byte[] pitch_lag_low_bits_iCDF;

	internal byte[] pitch_contour_iCDF;

	internal int nFramesDecoded;

	internal int nFramesPerPacket;

	internal int ec_prevSignalType;

	internal short ec_prevLagIndex;

	internal readonly int[] VAD_flags = new int[3];

	internal int LBRR_flag;

	internal readonly int[] LBRR_flags = new int[3];

	internal readonly SilkResamplerState resampler_state = new SilkResamplerState();

	internal NLSFCodebook psNLSF_CB;

	internal readonly SideInfoIndices indices = new SideInfoIndices();

	internal readonly CNGState sCNG = new CNGState();

	internal int lossCnt;

	internal int prevSignalType;

	internal readonly PLCStruct sPLC = new PLCStruct();

	internal readonly SilkDecoderControl decodeControlWork = new SilkDecoderControl();
	internal readonly short[] pulsesWork = new short[1024];
	internal readonly short[] ltpCoefWork = new short[2048];
	internal readonly int[] ltpStateWork = new int[2048];
	internal readonly int[] subframeWork = new int[256];
	internal readonly int[] lpcWork = new int[272];
	internal readonly short[] nlsfWork = new short[16];
	internal readonly short[] nlsfInterpWork = new short[16];
	internal readonly byte[] nlsfIndicesWork = new byte[16];
	internal readonly int[] pulseRateWork = new int[20];
	internal readonly int[] pulseScaleWork = new int[20];
	internal readonly int[] cngWork = new int[976];
	internal readonly BoxedValueSbyte gainIndexWork = new BoxedValueSbyte();
	internal readonly int[] nlsfPolyWork = new int[16];
	internal readonly int[] nlsfPolyAWork = new int[9];
	internal readonly int[] nlsfPolyBWork = new int[9];
	internal readonly int[] nlsfCoefWork = new int[16];
	internal readonly short[] shellRootWork = new short[2];
	internal readonly short[] shellPairWork = new short[4];
	internal readonly short[] shellLeafWork = new short[8];
	internal readonly byte[] signIcdfWork = new byte[2];

	internal void Reset()
	{
		prev_gain_Q16 = 0;
		Arrays.MemSetInt(exc_Q14, 0, 320);
		Arrays.MemSetInt(sLPC_Q14_buf, 0, 16);
		Arrays.MemSetShort(outBuf, 0, 480);
		lagPrev = 0;
		LastGainIndex = 0;
		fs_kHz = 0;
		fs_API_hz = 0;
		nb_subfr = 0;
		frame_length = 0;
		subfr_length = 0;
		ltp_mem_length = 0;
		LPC_order = 0;
		Arrays.MemSetShort(prevNLSF_Q15, 0, 16);
		first_frame_after_reset = 0;
		pitch_lag_low_bits_iCDF = null;
		pitch_contour_iCDF = null;
		nFramesDecoded = 0;
		nFramesPerPacket = 0;
		ec_prevSignalType = 0;
		ec_prevLagIndex = 0;
		Arrays.MemSetInt(VAD_flags, 0, 3);
		LBRR_flag = 0;
		Arrays.MemSetInt(LBRR_flags, 0, 3);
		resampler_state.Reset();
		psNLSF_CB = null;
		indices.Reset();
		sCNG.Reset();
		lossCnt = 0;
		prevSignalType = 0;
		sPLC.Reset();
	}

	/// <summary>
	/// Init Decoder State
	/// </summary>
	/// <returns></returns>
	internal int silk_init_decoder()
	{
		Reset();
		first_frame_after_reset = 1;
		prev_gain_Q16 = 65536;
		silk_CNG_Reset();
		silk_PLC_Reset();
		return 0;
	}

	/// <summary>
	/// Resets CNG state
	/// </summary>
	private void silk_CNG_Reset()
	{
		int num = Inlines.silk_DIV32_16(32767, LPC_order + 1);
		int num2 = 0;
		for (int i = 0; i < LPC_order; i++)
		{
			num2 += num;
			sCNG.CNG_smth_NLSF_Q15[i] = (short)num2;
		}
		sCNG.CNG_smth_Gain_Q16 = 0;
		sCNG.rand_seed = 3176576;
	}

	/// <summary>
	/// Resets PLC state
	/// </summary>
	private void silk_PLC_Reset()
	{
		sPLC.pitchL_Q8 = Inlines.silk_LSHIFT(frame_length, 7);
		sPLC.prevGain_Q16[0] = 65536;
		sPLC.prevGain_Q16[1] = 65536;
		sPLC.subfr_length = 20;
		sPLC.nb_subfr = 2;
	}

	internal int silk_decoder_set_fs(int fs_kHz, int fs_API_Hz)
	{
		int num = 0;
		subfr_length = Inlines.silk_SMULBB(5, fs_kHz);
		int num2 = Inlines.silk_SMULBB(nb_subfr, subfr_length);
		if (this.fs_kHz != fs_kHz || fs_API_hz != fs_API_Hz)
		{
			num += Resampler.silk_resampler_init(resampler_state, Inlines.silk_SMULBB(fs_kHz, 1000), fs_API_Hz, 0);
			fs_API_hz = fs_API_Hz;
		}
		if (this.fs_kHz != fs_kHz || num2 != frame_length)
		{
			if (fs_kHz == 8)
			{
				if (nb_subfr == 4)
				{
					pitch_contour_iCDF = Tables.silk_pitch_contour_NB_iCDF;
				}
				else
				{
					pitch_contour_iCDF = Tables.silk_pitch_contour_10_ms_NB_iCDF;
				}
			}
			else if (nb_subfr == 4)
			{
				pitch_contour_iCDF = Tables.silk_pitch_contour_iCDF;
			}
			else
			{
				pitch_contour_iCDF = Tables.silk_pitch_contour_10_ms_iCDF;
			}
			if (this.fs_kHz != fs_kHz)
			{
				ltp_mem_length = Inlines.silk_SMULBB(20, fs_kHz);
				if (fs_kHz == 8 || fs_kHz == 12)
				{
					LPC_order = 10;
					psNLSF_CB = Tables.silk_NLSF_CB_NB_MB;
				}
				else
				{
					LPC_order = 16;
					psNLSF_CB = Tables.silk_NLSF_CB_WB;
				}
				switch (fs_kHz)
				{
				case 16:
					pitch_lag_low_bits_iCDF = Tables.silk_uniform8_iCDF;
					break;
				case 12:
					pitch_lag_low_bits_iCDF = Tables.silk_uniform6_iCDF;
					break;
				case 8:
					pitch_lag_low_bits_iCDF = Tables.silk_uniform4_iCDF;
					break;
				}
				first_frame_after_reset = 1;
				lagPrev = 100;
				LastGainIndex = 10;
				prevSignalType = 0;
				Arrays.MemSetShort(outBuf, 0, 480);
				Arrays.MemSetInt(sLPC_Q14_buf, 0, 16);
			}
			this.fs_kHz = fs_kHz;
			frame_length = num2;
		}
		return num;
	}

	internal int silk_decode_frame(EntropyCoder psRangeDec, ReadOnlySpan<byte> frameData, Span<short> pOut, int pOut_ptr, BoxedValueInt pN, int lostFlag, int condCoding)
	{
		SilkDecoderControl silkDecoderControl = decodeControlWork;
		int num = frame_length;
		silkDecoderControl.LTP_scale_Q14 = 0;
		if (lostFlag == 0 || (lostFlag == 2 && LBRR_flags[nFramesDecoded] == 1))
		{
			short[] pulses = pulsesWork;
			DecodeIndices.silk_decode_indices(this, psRangeDec, frameData, nFramesDecoded, lostFlag, condCoding);
			DecodePulses.silk_decode_pulses(this, psRangeDec, frameData, pulses, indices.signalType, indices.quantOffsetType, frame_length);
			DecodeParameters.silk_decode_parameters(this, silkDecoderControl, condCoding);
			DecodeCore.silk_decode_core(this, silkDecoderControl, pOut, pOut_ptr, pulses);
			PLC.silk_PLC(this, silkDecoderControl, pOut, pOut_ptr, 0);
			lossCnt = 0;
			prevSignalType = indices.signalType;
			first_frame_after_reset = 0;
		}
		else
		{
			PLC.silk_PLC(this, silkDecoderControl, pOut, pOut_ptr, 1);
		}
		int num2 = ltp_mem_length - frame_length;
		Arrays.MemMoveShort(outBuf, frame_length, 0, num2);
		pOut.Slice(pOut_ptr, frame_length).CopyTo(outBuf.AsSpan(num2));
		CNG.silk_CNG(this, silkDecoderControl, pOut, pOut_ptr, num);
		PLC.silk_PLC_glue_frames(this, pOut, pOut_ptr, num);
		lagPrev = silkDecoderControl.pitchL[nb_subfr - 1];
		pN.Val = num;
		return 0;
	}
}
