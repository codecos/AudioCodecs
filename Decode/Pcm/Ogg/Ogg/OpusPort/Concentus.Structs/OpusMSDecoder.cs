using System;
using Concentus.Common;
using Concentus.Enums;

namespace Concentus.Structs;

/// <summary>
/// A managed implementation of the Opus multistream decoder.
/// </summary>
public class OpusMSDecoder : IOpusMultiStreamDecoder, IDisposable
{
	internal delegate void opus_copy_channel_out_func<T>(Span<T> dst, int dst_stride, int dst_channel, Span<short> src, int src_ptr, int src_stride, int frame_size);

	internal ChannelLayout layout = new ChannelLayout();

	internal OpusDecoder[] decoders;

	/// <inheritdoc />
	public OpusBandwidth Bandwidth
	{
		get
		{
			if (decoders == null || decoders.Length == 0)
			{
				throw new InvalidOperationException("Decoder not initialized");
			}
			return decoders[0].Bandwidth;
		}
	}

	/// <inheritdoc />
	public int SampleRate
	{
		get
		{
			if (decoders == null || decoders.Length == 0)
			{
				throw new InvalidOperationException("Decoder not initialized");
			}
			return decoders[0].SampleRate;
		}
	}

	/// <inheritdoc />
	public int NumChannels => layout.nb_channels;

	/// <inheritdoc />
	public int Gain
	{
		get
		{
			if (decoders == null || decoders.Length == 0)
			{
				return -6;
			}
			return decoders[0].Gain;
		}
		set
		{
			for (int i = 0; i < layout.nb_streams; i++)
			{
				decoders[i].Gain = value;
			}
		}
	}

	/// <inheritdoc />
	public int LastPacketDuration
	{
		get
		{
			if (decoders == null || decoders.Length == 0)
			{
				return -6;
			}
			return decoders[0].LastPacketDuration;
		}
	}

	/// <inheritdoc />
	public uint FinalRange
	{
		get
		{
			uint num = 0u;
			for (int i = 0; i < layout.nb_streams; i++)
			{
				num ^= decoders[i].FinalRange;
			}
			return num;
		}
	}

	private OpusMSDecoder(int nb_streams, int nb_coupled_streams)
	{
		decoders = new OpusDecoder[nb_streams];
		for (int i = 0; i < nb_streams; i++)
		{
			decoders[i] = new OpusDecoder();
		}
	}

	internal int opus_multistream_decoder_init(int Fs, int channels, int streams, int coupled_streams, byte[] mapping)
	{
		int num = 0;
		if (channels > 255 || channels < 1 || coupled_streams > streams || streams < 1 || coupled_streams < 0 || streams > 255 - coupled_streams)
		{
			throw new ArgumentException("Invalid channel or coupled stream count");
		}
		layout.nb_channels = channels;
		layout.nb_streams = streams;
		layout.nb_coupled_streams = coupled_streams;
		int i;
		for (i = 0; i < layout.nb_channels; i++)
		{
			layout.mapping[i] = mapping[i];
		}
		if (OpusMultistream.validate_layout(layout) == 0)
		{
			throw new ArgumentException("Invalid surround channel layout");
		}
		for (i = 0; i < layout.nb_coupled_streams; i++)
		{
			int num2 = decoders[num].opus_decoder_init(Fs, 2);
			if (num2 != 0)
			{
				return num2;
			}
			num++;
		}
		for (; i < layout.nb_streams; i++)
		{
			int num2 = decoders[num].opus_decoder_init(Fs, 1);
			if (num2 != 0)
			{
				return num2;
			}
			num++;
		}
		return 0;
	}

	/// <summary>
	/// Creates a new multichannel decoder
	/// </summary>
	/// <param name="Fs"></param>
	/// <param name="channels"></param>
	/// <param name="streams"></param>
	/// <param name="coupled_streams"></param>
	/// <param name="mapping">A channel mapping describing which streams go to which channels: see <seealso href="https://opus-codec.org/docs/opus_api-1.5/group__opus__multistream.html" /></param>
	/// <returns></returns>
	[Obsolete("Use OpusCodecFactory methods which can give you native code if supported by your platform")]
	public OpusMSDecoder(int Fs, int channels, int streams, int coupled_streams, byte[] mapping)
		: this(streams, coupled_streams)
	{
		if (channels > 255 || channels < 1 || coupled_streams > streams || streams < 1 || coupled_streams < 0 || streams > 255 - coupled_streams)
		{
			throw new ArgumentException("Invalid channel / stream configuration");
		}
		int num = opus_multistream_decoder_init(Fs, channels, streams, coupled_streams, mapping);
		switch (num)
		{
		case -1:
			throw new ArgumentException("Bad argument while creating MS decoder");
		default:
			throw new OpusException("Could not create MS decoder: " + CodecHelpers.opus_strerror(num), num);
		case 0:
			break;
		}
	}

	internal static int opus_multistream_packet_validate(ReadOnlySpan<byte> data, int nb_streams, int Fs)
	{
		short[] array = new short[48];
		int num = 0;
		int num2 = 0;
		int num3 = data.Length;
		for (int i = 0; i < nb_streams; i++)
		{
			if (data.Length <= 0)
			{
				return -4;
			}
			byte out_toc;
			int payload_offset;
			int packet_offset;
			int num4 = OpusPacketInfo.opus_packet_parse_impl(data, num2, num3, (i != nb_streams - 1) ? 1 : 0, out out_toc, null, null, 0, array, 0, out payload_offset, out packet_offset);
			if (num4 < 0)
			{
				return num4;
			}
			int numSamples = OpusPacketInfo.GetNumSamples(data.Slice(num2, packet_offset), Fs);
			if (i != 0 && num != numSamples)
			{
				return -4;
			}
			num = numSamples;
			num2 += packet_offset;
			num3 -= packet_offset;
		}
		return num;
	}

	internal int opus_multistream_decode_native<T>(ReadOnlySpan<byte> data, Span<T> pcm, opus_copy_channel_out_func<T> copy_channel_out, int frame_size, int decode_fec, int soft_clip)
	{
		int num = 0;
		int sampleRate = SampleRate;
		frame_size = Inlines.IMIN(frame_size, sampleRate / 25 * 3);
		short[] array = new short[2 * frame_size];
		int num2 = 0;
		if (data.Length == 0)
		{
			num = 1;
		}
		if (data.Length < 0)
		{
			return -1;
		}
		if (num == 0 && data.Length < 2 * layout.nb_streams - 1)
		{
			return -4;
		}
		if (num == 0)
		{
			int num3 = opus_multistream_packet_validate(data, layout.nb_streams, sampleRate);
			if (num3 < 0)
			{
				return num3;
			}
			if (num3 > frame_size)
			{
				return -2;
			}
		}
		int num4 = 0;
		int num5 = data.Length;
		for (int i = 0; i < layout.nb_streams; i++)
		{
			OpusDecoder opusDecoder = decoders[num2++];
			if (num == 0 && data.Length <= 0)
			{
				return -3;
			}
			int packet_offset;
			int num6 = opusDecoder.opus_decode_native(data, num4, num5, array, 0, frame_size, decode_fec, (i != layout.nb_streams - 1) ? 1 : 0, out packet_offset, soft_clip);
			num4 += packet_offset;
			num5 -= packet_offset;
			if (num6 <= 0)
			{
				return num6;
			}
			frame_size = num6;
			if (i < layout.nb_coupled_streams)
			{
				int prev = -1;
				int num7;
				while ((num7 = OpusMultistream.get_left_channel(layout, i, prev)) != -1)
				{
					copy_channel_out(pcm, layout.nb_channels, num7, array, 0, 2, frame_size);
					prev = num7;
				}
				prev = -1;
				while ((num7 = OpusMultistream.get_right_channel(layout, i, prev)) != -1)
				{
					copy_channel_out(pcm, layout.nb_channels, num7, array, 1, 2, frame_size);
					prev = num7;
				}
			}
			else
			{
				int prev2 = -1;
				int num8;
				while ((num8 = OpusMultistream.get_mono_channel(layout, i, prev2)) != -1)
				{
					copy_channel_out(pcm, layout.nb_channels, num8, array, 0, 1, frame_size);
					prev2 = num8;
				}
			}
		}
		for (int j = 0; j < layout.nb_channels; j++)
		{
			if (layout.mapping[j] == byte.MaxValue)
			{
				copy_channel_out(pcm, layout.nb_channels, j, null, 0, 0, frame_size);
			}
		}
		return frame_size;
	}

	internal static void opus_copy_channel_out_float(Span<float> dst, int dst_stride, int dst_channel, Span<short> src, int src_ptr, int src_stride, int frame_size)
	{
		if (!src.IsEmpty)
		{
			for (int i = 0; i < frame_size; i++)
			{
				dst[i * dst_stride + dst_channel] = 3.0517578E-05f * (float)src[i * src_stride + src_ptr];
			}
		}
		else
		{
			for (int i = 0; i < frame_size; i++)
			{
				dst[i * dst_stride + dst_channel] = 0f;
			}
		}
	}

	internal static void opus_copy_channel_out_short(Span<short> dst, int dst_stride, int dst_channel, Span<short> src, int src_ptr, int src_stride, int frame_size)
	{
		if (!src.IsEmpty)
		{
			for (int i = 0; i < frame_size; i++)
			{
				dst[i * dst_stride + dst_channel] = src[i * src_stride + src_ptr];
			}
		}
		else
		{
			for (int i = 0; i < frame_size; i++)
			{
				dst[i * dst_stride + dst_channel] = 0;
			}
		}
	}

	/// <inheritdoc />
	[Obsolete("Use Span<> overrides if possible")]
	public int DecodeMultistream(byte[] data, int data_offset, int len, short[] out_pcm, int out_pcm_offset, int frame_size, bool decode_fec)
	{
		if (data == null)
		{
			return DecodeMultistream(ReadOnlySpan<byte>.Empty, out_pcm.AsSpan(out_pcm_offset), frame_size, decode_fec);
		}
		return DecodeMultistream(data.AsSpan(data_offset, len), out_pcm.AsSpan(out_pcm_offset), frame_size, decode_fec);
	}

	/// <inheritdoc />
	public int DecodeMultistream(ReadOnlySpan<byte> data, Span<short> out_pcm, int frame_size, bool decode_fec)
	{
		int num = opus_multistream_decode_native(data, out_pcm, opus_copy_channel_out_short, frame_size, decode_fec ? 1 : 0, 0);
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

	/// <inheritdoc />
	[Obsolete("Use Span<> overrides if possible")]
	public int DecodeMultistream(byte[] data, int data_offset, int len, float[] out_pcm, int out_pcm_offset, int frame_size, bool decode_fec)
	{
		if (data == null)
		{
			return DecodeMultistream(ReadOnlySpan<byte>.Empty, out_pcm.AsSpan(out_pcm_offset), frame_size, decode_fec);
		}
		return DecodeMultistream(data.AsSpan(data_offset, len), out_pcm.AsSpan(out_pcm_offset), frame_size, decode_fec);
	}

	/// <inheritdoc />
	public int DecodeMultistream(ReadOnlySpan<byte> data, Span<float> out_pcm, int frame_size, bool decode_fec)
	{
		int num = opus_multistream_decode_native(data, out_pcm, opus_copy_channel_out_float, frame_size, decode_fec ? 1 : 0, 0);
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

	/// <inheritdoc />
	public void ResetState()
	{
		for (int i = 0; i < layout.nb_streams; i++)
		{
			decoders[i].ResetState();
		}
	}

	/// <inheritdoc />
	public string GetVersionString()
	{
		return CodecHelpers.GetVersionString();
	}

	/// <summary>
	/// Gets the internal decoder state of one of the multichannel stream's decoders, indicated by stream ID.
	/// </summary>
	/// <param name="streamId">The stream ID to fetch.</param>
	/// <returns>The decoder for that stream ID.</returns>
	public OpusDecoder GetMultistreamDecoderState(int streamId)
	{
		return decoders[streamId];
	}

	/// <inheritdoc />
	public void Dispose()
	{
	}
}
