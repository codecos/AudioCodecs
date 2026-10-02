using System;
using Concentus.Celt.Structs;
using Concentus.Common;
using Concentus.Common.CPlusPlus;
using Concentus.Enums;
using Concentus.Silk;
using Concentus.Silk.Structs;

namespace Concentus.Structs;

/// <summary>
/// The Opus decoder structure.
///
///  Opus is a stateful codec with overlapping blocks and as a result Opus
///  packets are not coded independently of each other. Packets must be
///  passed into the decoder serially and in the correct order for a correct
///  decode. Lost packets can be replaced with loss concealment by calling
///  the decoder with a null reference and zero length for the missing packet.
///
///  A single codec state may only be accessed from a single thread at
///  a time and any required locking must be performed by the caller. Separate
///  streams must be decoded with separate decoder states and can be decoded
///  in parallel.
/// </summary>
public class OpusDecoder : IOpusDecoder, IDisposable
{
	internal int channels;

	internal int Fs;

	/// Sampling rate (at the API level) 
	internal readonly DecControlState DecControl = new DecControlState();

	internal int decode_gain;

	internal int stream_channels;

	internal OpusBandwidth bandwidth;

	internal OpusMode mode;

	internal OpusMode prev_mode;

	internal int frame_size;

	internal int prev_redundancy;

	internal int last_packet_duration;

	internal uint rangeFinal;

	internal SilkDecoder SilkDecoder = new SilkDecoder();

	internal CeltDecoder Celt_Decoder = new CeltDecoder();

	private static readonly byte[] SILENCE = new byte[2] { 255, 255 };
	private readonly short[] decodeScratch = new short[5760 * 2];
	private readonly short[] redundancyScratch = new short[960 * 2];
	private readonly short[] silkScratch = new short[5760 * 2];
	private readonly short[] celtScratch = new short[5760 * 2];
	private readonly short[] packetSizesScratch = new short[48];
	private readonly EntropyCoder entropyWork = new EntropyCoder();

	/// <summary>
	/// Gets the encoded bandwidth of the last packet decoded. This may be lower than the actual decoding sample rate,
	/// and is only an indicator of the encoded audio's quality
	/// </summary>
	public OpusBandwidth Bandwidth => bandwidth;

	/// <inheritdoc />
	public uint FinalRange => rangeFinal;

	/// <summary>
	/// Gets the sample rate that this decoder decodes to. Always constant for the lifetime of the decoder
	/// </summary>
	public int SampleRate => Fs;

	/// <summary>
	/// Gets the number of channels that this decoder decodes to. Always constant for the lifetime of the decoder.
	/// </summary>
	public int NumChannels => channels;

	/// <summary>
	/// Gets the last estimated pitch value of the decoded audio
	/// </summary>
	public int Pitch
	{
		get
		{
			if (prev_mode == OpusMode.MODE_CELT_ONLY)
			{
				return Celt_Decoder.GetPitch();
			}
			return DecControl.prevPitchLag;
		}
	}

	/// <summary>
	/// Gets or sets the gain (Q8) to use in decoding
	/// </summary>
	public int Gain
	{
		get
		{
			return decode_gain;
		}
		set
		{
			if (value < -32768 || value > 32767)
			{
				throw new ArgumentException("Gain must be within the range of a signed int16");
			}
			decode_gain = value;
		}
	}

	/// <summary>
	/// Gets the duration of the last packet, in PCM samples per channel
	/// </summary>
	public int LastPacketDuration => last_packet_duration;

	internal OpusDecoder()
	{
	}

	internal void Reset()
	{
		channels = 0;
		Fs = 0;
		DecControl.Reset();
		decode_gain = 0;
		PartialReset();
	}

	/// <summary>
	/// OPUS_DECODER_RESET_START
	/// </summary>
	internal void PartialReset()
	{
		stream_channels = 0;
		bandwidth = (OpusBandwidth)0;
		mode = (OpusMode)0;
		prev_mode = (OpusMode)0;
		frame_size = 0;
		prev_redundancy = 0;
		last_packet_duration = 0;
		rangeFinal = 0u;
	}

	/// <summary>
	/// Allocates and initializes a decoder state.
	/// Internally Opus stores data at 48000 Hz, so that should be the default
	/// value for Fs. However, the decoder can efficiently decode to buffers
	/// at 8, 12, 16, and 24 kHz so if for some reason the caller cannot use
	/// data at the full sample rate, or knows the compressed data doesn't
	/// use the full frequency range, it can request decoding at a reduced
	/// rate. Likewise, the decoder is capable of filling in either mono or
	/// interleaved stereo pcm buffers, at the caller's request.
	/// </summary>
	/// <param name="Fs">Sample rate to decode at (Hz). This must be one of 8000, 12000, 16000, 24000, or 48000.</param>
	/// <param name="channels">Number of channels (1 or 2) to decode</param>
	/// <returns>The created encoder</returns>
	[Obsolete("Use OpusCodecFactory methods which can give you native code if supported by your platform")]
	public OpusDecoder(int Fs, int channels)
	{
		if (Fs != 48000 && Fs != 24000 && Fs != 16000 && Fs != 12000 && Fs != 8000)
		{
			throw new ArgumentException("Sample rate is invalid (must be 8/12/16/24/48 Khz)");
		}
		if (channels != 1 && channels != 2)
		{
			throw new ArgumentException("Number of channels must be 1 or 2");
		}
		int num = opus_decoder_init(Fs, channels);
		switch (num)
		{
		case -1:
			throw new ArgumentException("OPUS_BAD_ARG when creating decoder");
		default:
			throw new OpusException("Error while initializing decoder: " + CodecHelpers.opus_strerror(num), num);
		case 0:
			break;
		}
	}

	/// Initializes a previously allocated decoder state.
	/// The state must be at least the size returned by opus_decoder_get_size().
	/// This is intended for applications which use their own allocator instead of malloc. @see opus_decoder_create,opus_decoder_get_size
	/// To reset a previously initialized state, use the #OPUS_RESET_STATE CTL.
	/// @param [in] st <tt>OpusDecoder*</tt>: Decoder state.
	/// @param [in] Fs <tt>opus_int32</tt>: Sampling rate to decode to (Hz).
	///                                     This must be one of 8000, 12000, 16000,
	///                                     24000, or 48000.
	/// @param [in] channels <tt>int</tt>: Number of channels (1 or 2) to decode
	/// @retval #OPUS_OK Success or @ref opus_errorcodes
	internal int opus_decoder_init(int Fs, int channels)
	{
		if ((Fs != 48000 && Fs != 24000 && Fs != 16000 && Fs != 12000 && Fs != 8000) || (channels != 1 && channels != 2))
		{
			return -1;
		}
		Reset();
		SilkDecoder silkDecoder = SilkDecoder;
		CeltDecoder celt_Decoder = Celt_Decoder;
		stream_channels = (this.channels = channels);
		this.Fs = Fs;
		DecControl.API_sampleRate = this.Fs;
		DecControl.nChannelsAPI = this.channels;
		if (DecodeAPI.silk_InitDecoder(silkDecoder) != 0)
		{
			return -3;
		}
		if (celt_Decoder.celt_decoder_init(Fs, channels) != 0)
		{
			return -3;
		}
		celt_Decoder.SetSignalling(0);
		prev_mode = (OpusMode)0;
		frame_size = Fs / 400;
		return 0;
	}

	internal int opus_decode_frame(ReadOnlySpan<byte> data, int data_ptr, int len, Span<short> pcm, int pcm_ptr, int frame_size, int decode_fec)
	{
		int num = 0;
		EntropyCoder entropyCoder = entropyWork;
		short[] array = null;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		uint num6 = 0u;
		SilkDecoder silkDecoder = SilkDecoder;
		CeltDecoder celt_Decoder = Celt_Decoder;
		int num7 = Fs / 50;
		int num8 = num7 >> 1;
		int num9 = num8 >> 1;
		int num10 = num9 >> 1;
		if (frame_size < num10)
		{
			return -2;
		}
		frame_size = Inlines.IMIN(frame_size, Fs / 25 * 3);
		if (len <= 1)
		{
			data = null;
			frame_size = Inlines.IMIN(frame_size, this.frame_size);
		}
		int num11;
		OpusMode opusMode;
		if (!data.IsEmpty)
		{
			num11 = this.frame_size;
			opusMode = mode;
			entropyCoder.dec_init(data.Slice(data_ptr), (uint)len);
		}
		else
		{
			num11 = frame_size;
			opusMode = prev_mode;
			if (opusMode == (OpusMode)0)
			{
				for (int i = pcm_ptr; i < pcm_ptr + num11 * channels; i++)
				{
					pcm[i] = 0;
				}
				return num11;
			}
			if (num11 > num7)
			{
				do
				{
					int num12 = opus_decode_frame(null, 0, 0, pcm, pcm_ptr, Inlines.IMIN(num11, num7), 0);
					if (num12 < 0)
					{
						return num12;
					}
					pcm_ptr += num12 * channels;
					num11 -= num12;
				}
				while (num11 > 0);
				return frame_size;
			}
			if (num11 < num7)
			{
				if (num11 > num8)
				{
					num11 = num8;
				}
				else if (opusMode != OpusMode.MODE_SILK_ONLY && num11 > num9 && num11 < num8)
				{
					num11 = num9;
				}
			}
		}
		int num13 = ((opusMode != OpusMode.MODE_CELT_ONLY && frame_size >= num8) ? 1 : 0);
		int num14 = 0;
		int num15 = 0;
		if (!data.IsEmpty && prev_mode > (OpusMode)0 && ((opusMode == OpusMode.MODE_CELT_ONLY && prev_mode != OpusMode.MODE_CELT_ONLY && prev_redundancy == 0) || (opusMode != OpusMode.MODE_CELT_ONLY && prev_mode == OpusMode.MODE_CELT_ONLY)))
		{
			num2 = 1;
			if (opusMode == OpusMode.MODE_CELT_ONLY)
			{
				num15 = num9 * channels;
			}
			else
			{
				num14 = num9 * channels;
			}
		}
		short[] array2 = num15 == 0 ? Array.Empty<short>() : redundancyScratch;
		if (num2 != 0 && opusMode == OpusMode.MODE_CELT_ONLY)
		{
			array = array2;
			opus_decode_frame(null, 0, 0, array, 0, Inlines.IMIN(num9, num11), 0);
		}
		if (num11 > frame_size)
		{
			return -1;
		}
		frame_size = num11;
		short[] array3 = (opusMode != OpusMode.MODE_CELT_ONLY && num13 == 0) ? silkScratch : Array.Empty<short>();
		if (opusMode != OpusMode.MODE_CELT_ONLY)
		{
			int num16 = 0;
			Span<short> span;
			if (num13 != 0)
			{
				span = pcm;
				num16 = pcm_ptr;
			}
			else
			{
				span = array3;
				num16 = 0;
			}
			if (prev_mode == OpusMode.MODE_CELT_ONLY)
			{
				DecodeAPI.silk_InitDecoder(silkDecoder);
			}
			DecControl.payloadSize_ms = Inlines.IMAX(10, 1000 * num11 / Fs);
			if (!data.IsEmpty)
			{
				DecControl.nChannelsInternal = stream_channels;
				if (opusMode == OpusMode.MODE_SILK_ONLY)
				{
					if (bandwidth == OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND)
					{
						DecControl.internalSampleRate = 8000;
					}
					else if (bandwidth == OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND)
					{
						DecControl.internalSampleRate = 12000;
					}
					else if (bandwidth == OpusBandwidth.OPUS_BANDWIDTH_WIDEBAND)
					{
						DecControl.internalSampleRate = 16000;
					}
					else
					{
						DecControl.internalSampleRate = 16000;
					}
				}
				else
				{
					DecControl.internalSampleRate = 16000;
				}
			}
			int num17 = (data.IsEmpty ? 1 : (2 * decode_fec));
			int num18 = 0;
			do
			{
				int newPacketFlag = ((num18 == 0) ? 1 : 0);
				if (DecodeAPI.silk_Decode(silkDecoder, DecControl, data.Slice(data_ptr), num17, newPacketFlag, entropyCoder, span, num16, out var nSamplesOut) != 0)
				{
					if (num17 == 0)
					{
						return -3;
					}
					nSamplesOut = frame_size;
					Arrays.MemSetWithOffset<short>(span, 0, num16, frame_size * channels);
				}
				num16 += nSamplesOut * channels;
				num18 += nSamplesOut;
			}
			while (num18 < frame_size);
		}
		int startBand = 0;
		if (decode_fec == 0 && opusMode != OpusMode.MODE_CELT_ONLY && !data.IsEmpty && entropyCoder.tell() + 17 + 20 * ((mode == OpusMode.MODE_HYBRID) ? 1 : 0) <= 8 * len)
		{
			num3 = ((opusMode != OpusMode.MODE_HYBRID) ? 1 : entropyCoder.dec_bit_logp(data.Slice(data_ptr), 12u));
			if (num3 != 0)
			{
				num5 = entropyCoder.dec_bit_logp(data.Slice(data_ptr), 1u);
				num4 = ((opusMode == OpusMode.MODE_HYBRID) ? ((int)(entropyCoder.dec_uint(data.Slice(data_ptr), 256u) + 2)) : (len - (entropyCoder.tell() + 7 >> 3)));
				len -= num4;
				if (len * 8 < entropyCoder.tell())
				{
					len = 0;
					num4 = 0;
					num3 = 0;
				}
				entropyCoder.storage = (uint)(entropyCoder.storage - num4);
			}
		}
		if (opusMode != OpusMode.MODE_CELT_ONLY)
		{
			startBand = 17;
		}
		int endBand = 21;
		switch (bandwidth)
		{
		case OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND:
			endBand = 13;
			break;
		case OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND:
		case OpusBandwidth.OPUS_BANDWIDTH_WIDEBAND:
			endBand = 17;
			break;
		case OpusBandwidth.OPUS_BANDWIDTH_SUPERWIDEBAND:
			endBand = 19;
			break;
		case OpusBandwidth.OPUS_BANDWIDTH_FULLBAND:
			endBand = 21;
			break;
		}
		celt_Decoder.SetEndBand(endBand);
		celt_Decoder.SetChannels(stream_channels);
		if (num3 != 0)
		{
			num2 = 0;
			num14 = 0;
		}
		short[] array4 = num14 == 0 ? Array.Empty<short>() : redundancyScratch;
		if (num2 != 0 && opusMode != OpusMode.MODE_CELT_ONLY)
		{
			array = array4;
			opus_decode_frame(null, 0, 0, array, 0, Inlines.IMIN(num9, num11), 0);
		}
		short[] array5 = num3 != 0 ? celtScratch : Array.Empty<short>();
		if (num3 != 0 && num5 != 0)
		{
			celt_Decoder.SetStartBand(0);
			celt_Decoder.celt_decode_with_ec(data, data_ptr + len, num4, array5, 0, num9, null, 0);
			num6 = celt_Decoder.GetFinalRange();
		}
		celt_Decoder.SetStartBand(startBand);
			if (opusMode != OpusMode.MODE_SILK_ONLY)
			{
				int num19 = Inlines.IMIN(num7, frame_size);
				if (num19 <= 0)
				{
				}
			if (opusMode != prev_mode && prev_mode > (OpusMode)0 && prev_redundancy == 0)
			{
				celt_Decoder.ResetState();
			}
			num = celt_Decoder.celt_decode_with_ec((decode_fec != 0) ? ((ReadOnlySpan<byte>)null) : data, data_ptr, len, pcm, pcm_ptr, num19, entropyCoder, num13);
		}
		else
		{
			if (num13 == 0)
			{
				for (int i = pcm_ptr; i < frame_size * channels + pcm_ptr; i++)
				{
					pcm[i] = 0;
				}
			}
			if (prev_mode == OpusMode.MODE_HYBRID && (num3 == 0 || num5 == 0 || prev_redundancy == 0))
			{
				celt_Decoder.SetStartBand(0);
				celt_Decoder.celt_decode_with_ec(SILENCE, 0, 2, pcm, pcm_ptr, num10, null, num13);
			}
		}
		if (opusMode != OpusMode.MODE_CELT_ONLY && num13 == 0)
		{
			for (int i = 0; i < frame_size * channels; i++)
			{
				pcm[pcm_ptr + i] = Inlines.SAT16(Inlines.ADD32(pcm[pcm_ptr + i], array3[i]));
			}
		}
		int[] window = celt_Decoder.GetMode().window;
		if (num3 != 0 && num5 == 0)
		{
			celt_Decoder.ResetState();
			celt_Decoder.SetStartBand(0);
			celt_Decoder.celt_decode_with_ec(data, data_ptr + len, num4, array5, 0, num9, null, 0);
			num6 = celt_Decoder.GetFinalRange();
			CodecHelpers.smooth_fade(pcm, pcm_ptr + channels * (frame_size - num10), array5, channels * num10, pcm, pcm_ptr + channels * (frame_size - num10), num10, channels, window, Fs);
		}
		if (num3 != 0 && num5 != 0)
		{
			for (int j = 0; j < channels; j++)
			{
				for (int i = 0; i < num10; i++)
				{
					pcm[channels * i + j + pcm_ptr] = array5[channels * i + j];
				}
			}
			CodecHelpers.smooth_fade(array5, channels * num10, pcm, pcm_ptr + channels * num10, pcm, pcm_ptr + channels * num10, num10, channels, window, Fs);
		}
		if (num2 != 0)
		{
			if (num11 >= num9)
			{
				for (int i = 0; i < channels * num10; i++)
				{
					pcm[i] = array[i];
				}
				CodecHelpers.smooth_fade(array, channels * num10, pcm, pcm_ptr + channels * num10, pcm, pcm_ptr + channels * num10, num10, channels, window, Fs);
			}
			else
			{
				CodecHelpers.smooth_fade(array, 0, pcm, pcm_ptr, pcm, pcm_ptr, num10, channels, window, Fs);
			}
		}
		if (decode_gain != 0)
		{
			int b = Inlines.celt_exp2(Inlines.MULT16_16_P15(21771, decode_gain));
			for (int i = pcm_ptr; i < pcm_ptr + frame_size * channels; i++)
			{
				int x = Inlines.MULT16_32_P16(pcm[i], b);
				pcm[i] = (short)Inlines.SATURATE(x, 32767);
			}
		}
		if (len <= 1)
		{
			rangeFinal = 0u;
		}
		else
		{
			rangeFinal = entropyCoder.rng ^ num6;
		}
		prev_mode = opusMode;
		prev_redundancy = ((num3 != 0 && num5 == 0) ? 1 : 0);
		if (num >= 0)
		{
			return num11;
		}
		return num;
	}

	internal int opus_decode_native(ReadOnlySpan<byte> data, int data_ptr, int len, Span<short> pcm_out, int pcm_out_ptr, int frame_size, int decode_fec, int self_delimited, out int packet_offset, int soft_clip)
	{
		packet_offset = 0;
		short[] array = packetSizesScratch;
		if (decode_fec < 0 || decode_fec > 1)
		{
			return -1;
		}
		if ((decode_fec != 0 || len == 0 || data.IsEmpty) && frame_size % (Fs / 400) != 0)
		{
			return -1;
		}
		if (len == 0 || data.IsEmpty)
		{
			int num = 0;
			do
			{
				int num2 = opus_decode_frame(null, 0, 0, pcm_out, pcm_out_ptr + num * channels, frame_size - num, 0);
				if (num2 < 0)
				{
					return num2;
				}
				num += num2;
			}
			while (num < frame_size);
			last_packet_duration = num;
			return num;
		}
		if (len < 0)
		{
			return -1;
		}
		OpusMode encoderMode = OpusPacketInfo.GetEncoderMode(data.Slice(data_ptr));
		OpusBandwidth opusBandwidth = OpusPacketInfo.GetBandwidth(data.Slice(data_ptr));
		int numSamplesPerFrame = OpusPacketInfo.GetNumSamplesPerFrame(data.Slice(data_ptr), Fs);
		int numEncodedChannels = OpusPacketInfo.GetNumEncodedChannels(data.Slice(data_ptr));
		byte out_toc;
		int payload_offset;
		int num3 = OpusPacketInfo.opus_packet_parse_impl(data, data_ptr, len, self_delimited, out out_toc, null, null, 0, array, 0, out payload_offset, out packet_offset);
		if (num3 < 0)
		{
			return num3;
		}
		data_ptr += payload_offset;
		if (decode_fec != 0)
		{
			int packet_offset2;
			if (frame_size < numSamplesPerFrame || encoderMode == OpusMode.MODE_CELT_ONLY || mode == OpusMode.MODE_CELT_ONLY)
			{
				return opus_decode_native(null, 0, 0, pcm_out, pcm_out_ptr, frame_size, 0, 0, out packet_offset2, soft_clip);
			}
			int num4 = last_packet_duration;
			int num5;
			if (frame_size - numSamplesPerFrame != 0)
			{
				num5 = opus_decode_native(null, 0, 0, pcm_out, pcm_out_ptr, frame_size - numSamplesPerFrame, 0, 0, out packet_offset2, soft_clip);
				if (num5 < 0)
				{
					last_packet_duration = num4;
					return num5;
				}
			}
			mode = encoderMode;
			bandwidth = opusBandwidth;
			this.frame_size = numSamplesPerFrame;
			stream_channels = numEncodedChannels;
			num5 = opus_decode_frame(data, data_ptr, array[0], pcm_out, pcm_out_ptr + channels * (frame_size - numSamplesPerFrame), numSamplesPerFrame, 1);
			if (num5 < 0)
			{
				return num5;
			}
			last_packet_duration = frame_size;
			return frame_size;
		}
		if (num3 * numSamplesPerFrame > frame_size)
		{
			return -2;
		}
		mode = encoderMode;
		bandwidth = opusBandwidth;
		this.frame_size = numSamplesPerFrame;
		stream_channels = numEncodedChannels;
		int num6 = 0;
		for (int i = 0; i < num3; i++)
		{
			int num7 = opus_decode_frame(data, data_ptr, array[i], pcm_out, pcm_out_ptr + num6 * channels, frame_size - num6, 0);
			if (num7 < 0)
			{
				return num7;
			}
			data_ptr += array[i];
			num6 += num7;
		}
		last_packet_duration = num6;
		return num6;
	}

	/// <summary>
	/// Decodes an Opus packet.
	/// </summary>
	/// <param name="in_data">The input payload. This may be NULL if the previous packet was lost in transit (when PLC is enabled)</param>
	/// <param name="in_data_offset">The offset to use when reading the input payload. Usually 0</param>
	/// <param name="len">The number of bytes in the payload</param>
	/// <param name="out_pcm">A buffer to put the output PCM. The output size is (# of samples) * (# of channels).
	/// You can use the OpusPacketInfo helpers to get a hint of the frame size before you decode the packet if you need
	/// exact sizing. Otherwise, the minimum safe buffer size is 5760 samples</param>
	/// <param name="out_pcm_offset">The offset to use when writing to the output buffer</param>
	/// <param name="frame_size">The number of samples (per channel) of available space in the output PCM buf.
	/// If this is less than the maximum packet duration (120ms; 5760 for 48khz), this function will
	/// not be capable of decoding some packets. In the case of PLC (data == NULL) or FEC (decode_fec == true),
	/// then frame_size needs to be exactly the duration of the audio that is missing, otherwise the decoder will
	/// not be in an optimal state to decode the next incoming packet. For the PLC and FEC cases, frame_size *must*
	/// be a multiple of 10 ms.</param>
	/// <param name="decode_fec">Indicates that we want to recreate the PREVIOUS (lost) packet using FEC data from THIS packet. Using this packet
	/// recovery scheme, you will actually decode this packet twice, first with decode_fec TRUE and then again with FALSE. If FEC data is not
	/// available in this packet, the decoder will simply generate a best-effort recreation of the lost packet.</param>
	/// <returns>The number of decoded samples</returns>
	[Obsolete("Use Span<> overrides if possible")]
	public int Decode(byte[] in_data, int in_data_offset, int len, short[] out_pcm, int out_pcm_offset, int frame_size, bool decode_fec = false)
	{
		if (in_data == null)
		{
			return Decode(ReadOnlySpan<byte>.Empty, out_pcm.AsSpan(out_pcm_offset), frame_size, decode_fec);
		}
		return Decode(in_data.AsSpan(in_data_offset, len), out_pcm.AsSpan(out_pcm_offset), frame_size, decode_fec);
	}

	/// <summary>
	/// Decodes an Opus packet.
	/// </summary>
	/// <param name="in_data">The input payload. This may be empty if the previous packet was lost in transit (when PLC is enabled)</param>
	/// <param name="out_pcm">A buffer to put the output PCM. The output size is (# of samples) * (# of channels).
	/// You can use the OpusPacketInfo helpers to get a hint of the frame size before you decode the packet if you need
	/// exact sizing. Otherwise, the minimum safe buffer size is 5760 samples</param>
	/// <param name="frame_size">The number of samples (per channel) of available space in the output PCM buf.
	/// If this is less than the maximum packet duration (120ms; 5760 for 48khz), this function will
	/// not be capable of decoding some packets. In the case of PLC (data == NULL) or FEC (decode_fec == true),
	/// then frame_size needs to be exactly the duration of the audio that is missing, otherwise the decoder will
	/// not be in an optimal state to decode the next incoming packet. For the PLC and FEC cases, frame_size *must*
	/// be a multiple of 10 ms.</param>
	/// <param name="decode_fec">Indicates that we want to recreate the PREVIOUS (lost) packet using FEC data from THIS packet. Using this packet
	/// recovery scheme, you will actually decode this packet twice, first with decode_fec TRUE and then again with FALSE. If FEC data is not
	/// available in this packet, the decoder will simply generate a best-effort recreation of the lost packet.</param>
	/// <returns>The number of decoded samples</returns>
	public int Decode(ReadOnlySpan<byte> in_data, Span<short> out_pcm, int frame_size, bool decode_fec = false)
	{
		if (frame_size <= 0)
		{
			throw new ArgumentException("Frame size must be > 0");
		}
		try
		{
			int packet_offset;
			int num = opus_decode_native(in_data, 0, in_data.Length, out_pcm, 0, frame_size, decode_fec ? 1 : 0, 0, out packet_offset, 0);
			if (num < 0)
			{
				if (num == -1)
				{
					throw new ArgumentException("OPUS_BAD_ARG while decoding");
				}
				throw new OpusException("An error occurred during decoding: " + CodecHelpers.opus_strerror(num), num);
			}
			return num;
		}
		catch (ArgumentException ex)
		{
			throw new OpusException("public error during decoding: " + ex.Message, -1);
		}
	}

	/// <summary>
	/// Decodes an Opus packet, putting the output data into a floating-point buffer.
	/// </summary>
	/// <param name="in_data">The input payload. This may be NULL if that previous packet was lost in transit (when PLC is enabled)</param>
	/// <param name="in_data_offset">The offset to use when reading the input payload. Usually 0</param>
	/// <param name="len">The number of bytes in the payload</param>
	/// <param name="out_pcm">A buffer to put the output PCM. The output size is (# of samples) * (# of channels).
	/// You can use the OpusPacketInfo helpers to get a hint of the frame size before you decode the packet if you need
	/// exact sizing. Otherwise, the minimum safe buffer size is 5760 samples</param>
	/// <param name="out_pcm_offset">The offset to use when writing to the output buffer</param>
	/// <param name="frame_size">The number of samples (per channel) of available space in the output PCM buf.
	/// If this is less than the maximum packet duration (120ms; 5760 for 48khz), this function will
	/// not be capable of decoding some packets. In the case of PLC (data == NULL) or FEC (decode_fec == true),
	/// then frame_size needs to be exactly the duration of the audio that is missing, otherwise the decoder will
	/// not be in an optimal state to decode the next incoming packet. For the PLC and FEC cases, frame_size *must*
	/// be a multiple of 10 ms.</param>
	/// <param name="decode_fec">Indicates that we want to recreate the PREVIOUS (lost) packet using FEC data from THIS packet. Using this packet
	/// recovery scheme, you will actually decode this packet twice, first with decode_fec TRUE and then again with FALSE. If FEC data is not
	/// available in this packet, the decoder will simply generate a best-effort recreation of the lost packet. In that case,
	/// the length of frame_size must be EXACTLY the length of the audio that was lost, or else the decoder will be in an inconsistent state.</param>
	/// <returns>The number of decoded samples (per channel)</returns>
	[Obsolete("Use Span<> overrides if possible")]
	public int Decode(byte[] in_data, int in_data_offset, int len, float[] out_pcm, int out_pcm_offset, int frame_size, bool decode_fec = false)
	{
		if (in_data == null)
		{
			return Decode(ReadOnlySpan<byte>.Empty, out_pcm.AsSpan(out_pcm_offset), frame_size, decode_fec);
		}
		return Decode(in_data.AsSpan(in_data_offset, len), out_pcm.AsSpan(out_pcm_offset), frame_size, decode_fec);
	}

	/// <summary>
	/// Decodes an Opus packet.
	/// </summary>
	/// <param name="in_data">The input payload. This may be empty if the previous packet was lost in transit (when PLC is enabled)</param>
	/// <param name="out_pcm">A buffer to put the output PCM. The output size is (# of samples) * (# of channels).
	/// You can use the OpusPacketInfo helpers to get a hint of the frame size before you decode the packet if you need
	/// exact sizing. Otherwise, the minimum safe buffer size is 5760 samples</param>
	/// <param name="frame_size">The number of samples (per channel) of available space in the output PCM buf.
	/// If this is less than the maximum packet duration (120ms; 5760 for 48khz), this function will
	/// not be capable of decoding some packets. In the case of PLC (data == NULL) or FEC (decode_fec == true),
	/// then frame_size needs to be exactly the duration of the audio that is missing, otherwise the decoder will
	/// not be in an optimal state to decode the next incoming packet. For the PLC and FEC cases, frame_size *must*
	/// be a multiple of 10 ms.</param>
	/// <param name="decode_fec">Indicates that we want to recreate the PREVIOUS (lost) packet using FEC data from THIS packet. Using this packet
	/// recovery scheme, you will actually decode this packet twice, first with decode_fec TRUE and then again with FALSE. If FEC data is not
	/// available in this packet, the decoder will simply generate a best-effort recreation of the lost packet.</param>
	/// <returns>The number of decoded samples</returns>
	public int Decode(ReadOnlySpan<byte> in_data, Span<float> out_pcm, int frame_size, bool decode_fec = false)
	{
		if (frame_size <= 0)
		{
			throw new ArgumentException("Frame size must be > 0");
		}
		if (!in_data.IsEmpty && in_data.Length > 0 && !decode_fec)
		{
			int numSamples = OpusPacketInfo.GetNumSamples(in_data, Fs);
			if (numSamples <= 0)
			{
				throw new OpusException("An invalid packet was provided (unable to parse # of samples)", -4);
			}
			frame_size = Inlines.IMIN(frame_size, numSamples);
		}
		short[] array = decodeScratch;
		try
		{
			int packet_offset;
			int num = opus_decode_native(in_data, 0, in_data.Length, array, 0, frame_size, decode_fec ? 1 : 0, 0, out packet_offset, 0);
			if (num < 0)
			{
				if (num == -1)
				{
					throw new ArgumentException("OPUS_BAD_ARG when decoding");
				}
				throw new OpusException("An error occurred during decoding: " + CodecHelpers.opus_strerror(num), num);
			}
			if (num > 0)
			{
				for (int i = 0; i < num * channels; i++)
				{
					out_pcm[i] = 3.0517578E-05f * (float)array[i];
				}
			}
			return num;
		}
		catch (ArgumentException ex)
		{
			throw new OpusException("public error during decoding: " + ex.Message, -1);
		}
	}

	/// <summary>
	/// Resets all buffers and prepares this decoder to process a fresh (unrelated) stream
	/// </summary>
	public void ResetState()
	{
		PartialReset();
		Celt_Decoder.ResetState();
		DecodeAPI.silk_InitDecoder(SilkDecoder);
		stream_channels = channels;
		frame_size = Fs / 400;
	}

	/// <inheritdoc />
	public string GetVersionString()
	{
		return CodecHelpers.GetVersionString();
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}
}
