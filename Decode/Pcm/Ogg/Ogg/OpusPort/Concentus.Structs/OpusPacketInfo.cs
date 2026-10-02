using System;
using System.Collections.Generic;
using Concentus.Common.CPlusPlus;
using Concentus.Enums;

namespace Concentus.Structs;

/// <summary>
/// Contains the parsed information from a single Opus packet, such as the bandwidth,
/// number of samples, encoder mode, channel count, etc.
/// </summary>
public class OpusPacketInfo
{
	/// <summary>
	/// The Table of Contents byte for this packet. Contains info about modes, frame length, etc.
	/// </summary>
	public byte TOCByte { get; private set; }

	/// <summary>
	/// The list of subframes in this packet
	/// </summary>
	public IList<byte[]> Frames { get; private set; }

	/// <summary>
	/// The index of the start of the payload within the packet
	/// </summary>
	public int PayloadOffset { get; private set; }

	/// <summary>
	/// Gets the encoded bandwidth of an Opus packet. Note that you are not forced to decode at this bandwidth
	/// </summary>
	/// <returns>An OpusBandwidth value</returns>
	public OpusBandwidth Bandwidth
	{
		get
		{
			OpusBandwidth opusBandwidth;
			if ((TOCByte & 0x80) == 0)
			{
				opusBandwidth = (((TOCByte & 0x60) != 96) ? ((OpusBandwidth)(1101 + ((TOCByte >> 5) & 3))) : (((TOCByte & 0x10) != 0) ? OpusBandwidth.OPUS_BANDWIDTH_FULLBAND : OpusBandwidth.OPUS_BANDWIDTH_SUPERWIDEBAND));
			}
			else
			{
				opusBandwidth = (OpusBandwidth)(1102 + ((TOCByte >> 5) & 3));
				if (opusBandwidth == OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND)
				{
					opusBandwidth = OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND;
				}
			}
			return opusBandwidth;
		}
	}

	/// <summary>
	/// Gets the number of encoded channels of an Opus packet. Note that you are not forced to decode with this channel count.
	/// </summary>
	/// <returns>The number of channels</returns>
	public int NumEncodedChannels
	{
		get
		{
			if ((TOCByte & 4) == 0)
			{
				return 1;
			}
			return 2;
		}
	}

	/// <summary>
	/// Gets the mode that was used to encode this packet.
	/// Normally there is nothing you can really do with this, other than debugging.
	/// </summary>
	/// <returns>The OpusMode used by the encoder</returns>
	public OpusMode EncoderMode
	{
		get
		{
			if ((TOCByte & 0x80) != 0)
			{
				return OpusMode.MODE_CELT_ONLY;
			}
			if ((TOCByte & 0x60) == 96)
			{
				return OpusMode.MODE_HYBRID;
			}
			return OpusMode.MODE_SILK_ONLY;
		}
	}

	private OpusPacketInfo(byte toc, IList<byte[]> frames, int payloadOffset)
	{
		TOCByte = toc;
		Frames = frames;
		PayloadOffset = payloadOffset;
	}

	/// <summary>
	/// Parse an opus packet into a packetinfo object containing one or more frames.
	/// Opus decode will perform this operation internally so most applications do
	/// not need to use this function.
	/// </summary>
	/// <param name="packet">The packet data to be parsed</param>
	/// <param name="packet_offset">The index of the beginning of the packet in the data array (usually 0)</param>
	/// <param name="len">The packet's length</param>
	/// <returns>A parsed packet info struct</returns>
	[Obsolete("Use Span<> overrides if possible")]
	public static OpusPacketInfo ParseOpusPacket(byte[] packet, int packet_offset, int len)
	{
		return ParseOpusPacket(packet.AsSpan(packet_offset, len));
	}

	/// <summary>
	/// Parse an opus packet into a packetinfo object containing one or more frames.
	/// Opus_decode will perform this operation internally so most applications do
	/// not need to use this function.
	/// </summary>
	/// <param name="packet">The packet data to be parsed</param>
	/// <returns>A parsed packet info struct</returns>
	public static OpusPacketInfo ParseOpusPacket(ReadOnlySpan<byte> packet)
	{
		int numFrames = GetNumFrames(packet);
		byte[][] array = new byte[numFrames][];
		int[] array2 = new int[numFrames];
		short[] array3 = new short[numFrames];
		byte out_toc;
		int payload_offset;
		int packet_offset;
		int num = opus_packet_parse_impl(packet, 0, packet.Length, 0, out out_toc, array, array2, 0, array3, 0, out payload_offset, out packet_offset);
		if (num < 0)
		{
			throw new OpusException("An error occurred while parsing the packet", num);
		}
		IList<byte[]> list = new List<byte[]>();
		for (int i = 0; i < numFrames; i++)
		{
			byte[] array4 = new byte[array3[i]];
			array[i].AsSpan(array2[i], array4.Length).CopyTo(array4);
			list.Add(array4);
		}
		return new OpusPacketInfo(out_toc, list, payload_offset);
	}

	/// <summary>
	/// Gets the number of samples per frame from an Opus packet.
	/// </summary>
	/// <param name="Fs">Sampling rate in Hz. This must be a multiple of 400, or inaccurate results will be returned.</param>
	/// <returns>Number of samples per frame</returns>
	public int NumSamplesPerFrame(int Fs)
	{
		int num;
		if ((TOCByte & 0x80) != 0)
		{
			num = (TOCByte >> 3) & 3;
			return (Fs << num) / 400;
		}
		if ((TOCByte & 0x60) == 96)
		{
			return ((TOCByte & 8) != 0) ? (Fs / 50) : (Fs / 100);
		}
		num = (TOCByte >> 3) & 3;
		if (num == 3)
		{
			return Fs * 60 / 1000;
		}
		return (Fs << num) / 100;
	}

	/// <summary>
	/// Gets the number of samples per frame from an Opus packet.
	/// </summary>
	/// <param name="packet">Opus packet. This must contain at least one byte of data</param>
	/// <param name="Fs">Sampling rate in Hz. This must be a multiple of 400, or inaccurate results will be returned.</param>
	/// <returns>Number of samples per frame</returns>
	public static int GetNumSamplesPerFrame(ReadOnlySpan<byte> packet, int Fs)
	{
		int num;
		if ((packet[0] & 0x80) != 0)
		{
			num = (packet[0] >> 3) & 3;
			return (Fs << num) / 400;
		}
		if ((packet[0] & 0x60) == 96)
		{
			return ((packet[0] & 8) != 0) ? (Fs / 50) : (Fs / 100);
		}
		num = (packet[0] >> 3) & 3;
		if (num == 3)
		{
			return Fs * 60 / 1000;
		}
		return (Fs << num) / 100;
	}

	/// <summary>
	/// Gets the encoded bandwidth of an Opus packet. Note that you are not forced to decode at this bandwidth
	/// </summary>
	/// <param name="packet">An Opus packet (must be at least 1 byte)</param>.
	/// <returns>An OpusBandwidth value</returns>
	public static OpusBandwidth GetBandwidth(ReadOnlySpan<byte> packet)
	{
		OpusBandwidth opusBandwidth;
		if ((packet[0] & 0x80) == 0)
		{
			opusBandwidth = (((packet[0] & 0x60) != 96) ? ((OpusBandwidth)(1101 + ((packet[0] >> 5) & 3))) : (((packet[0] & 0x10) != 0) ? OpusBandwidth.OPUS_BANDWIDTH_FULLBAND : OpusBandwidth.OPUS_BANDWIDTH_SUPERWIDEBAND));
		}
		else
		{
			opusBandwidth = (OpusBandwidth)(1102 + ((packet[0] >> 5) & 3));
			if (opusBandwidth == OpusBandwidth.OPUS_BANDWIDTH_MEDIUMBAND)
			{
				opusBandwidth = OpusBandwidth.OPUS_BANDWIDTH_NARROWBAND;
			}
		}
		return opusBandwidth;
	}

	/// <summary>
	/// Gets the number of encoded channels of an Opus packet. Note that you are not forced to decode with this channel count.
	/// </summary>
	/// <param name="packet">An opus packet (must be at least 1 byte)</param>
	/// <returns>The number of channels</returns>
	public static int GetNumEncodedChannels(ReadOnlySpan<byte> packet)
	{
		if ((packet[0] & 4) == 0)
		{
			return 1;
		}
		return 2;
	}

	/// <summary>
	/// Gets the number of frames in an Opus packet.
	/// </summary>
	/// <param name="packet">An Opus packet</param>
	/// <returns>The number of frames in the packet</returns>
	public static int GetNumFrames(ReadOnlySpan<byte> packet)
	{
		if (packet.Length < 1)
		{
			return -1;
		}
		switch (packet[0] & 3)
		{
		case 0:
			return 1;
		default:
			return 2;
		case 3:
			if (packet.Length < 2)
			{
				return -4;
			}
			return packet[1] & 0x3F;
		}
	}

	/// <summary>
	/// Gets the number of samples of an Opus packet.
	/// </summary>
	/// <param name="packet">An Opus packet</param>
	/// <param name="Fs">The decoder's sampling rate in Hz. This must be a multiple of 400</param>
	/// <returns>The size of the PCM samples that this packet will be decoded to at the specified sample rate</returns>
	public static int GetNumSamples(ReadOnlySpan<byte> packet, int Fs)
	{
		int numFrames = GetNumFrames(packet);
		if (numFrames < 0)
		{
			return numFrames;
		}
		int num = numFrames * GetNumSamplesPerFrame(packet, Fs);
		if (num * 25 > Fs * 3)
		{
			return -4;
		}
		return num;
	}

	/// <summary>
	/// Gets the number of samples of an Opus packet.
	/// </summary>
	/// <param name="dec">Your current decoder state</param>
	/// <param name="packet">An Opus packet</param>
	/// <param name="packet_offset">The start offset in the array for reading the packet from</param>
	/// <param name="len">The packet's length</param>
	/// <returns>The size of the PCM samples that this packet will be decoded to by the specified decoder</returns>
	[Obsolete("Use Span<> overrides if possible")]
	public static int GetNumSamples(OpusDecoder dec, byte[] packet, int packet_offset, int len)
	{
		return GetNumSamples(packet.AsSpan(packet_offset, len), dec.Fs);
	}

	/// <summary>
	/// Gets the number of samples of an Opus packet.
	/// </summary>
	/// <param name="dec">Your current decoder state</param>
	/// <param name="packet">An Opus packet</param>
	/// <returns>The size of the PCM samples that this packet will be decoded to by the specified decoder</returns>
	public static int GetNumSamples(OpusDecoder dec, ReadOnlySpan<byte> packet)
	{
		return GetNumSamples(packet, dec.Fs);
	}

	/// <summary>
	/// Gets the mode that was used to encode this packet.
	/// Normally there is nothing you can really do with this, other than debugging.
	/// </summary>
	/// <param name="packet">An Opus packet</param>
	/// <returns>The OpusMode used by the encoder</returns>
	public static OpusMode GetEncoderMode(ReadOnlySpan<byte> packet)
	{
		if ((packet[0] & 0x80) != 0)
		{
			return OpusMode.MODE_CELT_ONLY;
		}
		if ((packet[0] & 0x60) == 96)
		{
			return OpusMode.MODE_HYBRID;
		}
		return OpusMode.MODE_SILK_ONLY;
	}

	internal static int encode_size(int size, Span<byte> data, int data_ptr)
	{
		if (size < 252)
		{
			data[data_ptr] = (byte)size;
			return 1;
		}
		data[data_ptr] = (byte)(252 + (size & 3));
		data[data_ptr + 1] = (byte)(size - data[data_ptr] >> 2);
		return 2;
	}

	internal static int parse_size(ReadOnlySpan<byte> data, int data_ptr, int len, ref short size)
	{
		if (len < 1)
		{
			size = -1;
			return -1;
		}
		if (data[data_ptr] < 252)
		{
			size = data[data_ptr];
			return 1;
		}
		if (len < 2)
		{
			size = -1;
			return -1;
		}
		size = (short)(4 * data[data_ptr + 1] + data[data_ptr]);
		return 2;
	}

	internal static int opus_packet_parse_impl(ReadOnlySpan<byte> data, int data_ptr, int len, int self_delimited, out byte out_toc, byte[][] frames, Span<int> frames_ptrs, int frames_ptr, Span<short> sizes, int sizes_ptr, out int payload_offset, out int packet_offset)
	{
		int num = 0;
		int num2 = data_ptr;
		out_toc = 0;
		payload_offset = 0;
		packet_offset = 0;
		if (sizes.IsEmpty || len < 0)
		{
			return -1;
		}
		if (len == 0)
		{
			return -4;
		}
		int numSamplesPerFrame = GetNumSamplesPerFrame(data.Slice(data_ptr), 48000);
		int num3 = 0;
		byte b = data[data_ptr++];
		len--;
		int num4 = len;
		int num5;
		switch (b & 3)
		{
		case 0:
			num5 = 1;
			break;
		case 1:
			num5 = 2;
			num3 = 1;
			if (self_delimited == 0)
			{
				if ((len & 1) != 0)
				{
					return -4;
				}
				num4 = len / 2;
				sizes[sizes_ptr] = (short)num4;
			}
			break;
		case 2:
		{
			num5 = 2;
			int num8 = parse_size(data, data_ptr, len, ref sizes[sizes_ptr]);
			len -= num8;
			if (sizes[sizes_ptr] < 0 || sizes[sizes_ptr] > len)
			{
				return -4;
			}
			data_ptr += num8;
			num4 = len - sizes[sizes_ptr];
			break;
		}
		default:
		{
			if (len < 1)
			{
				return -4;
			}
			byte b2 = data[data_ptr++];
			num5 = b2 & 0x3F;
			if (num5 <= 0 || numSamplesPerFrame * num5 > 5760)
			{
				return -4;
			}
			len--;
			if ((b2 & 0x40) != 0)
			{
				int num6;
				do
				{
					if (len <= 0)
					{
						return -4;
					}
					num6 = data[data_ptr++];
					len--;
					int num7 = ((num6 == 255) ? 254 : num6);
					len -= num7;
					num += num7;
				}
				while (num6 == 255);
			}
			if (len < 0)
			{
				return -4;
			}
			num3 = (((b2 & 0x80) == 0) ? 1 : 0);
			if (num3 == 0)
			{
				num4 = len;
				for (int i = 0; i < num5 - 1; i++)
				{
					int num8 = parse_size(data, data_ptr, len, ref sizes[sizes_ptr + i]);
					len -= num8;
					if (sizes[sizes_ptr + i] < 0 || sizes[sizes_ptr + i] > len)
					{
						return -4;
					}
					data_ptr += num8;
					num4 -= num8 + sizes[sizes_ptr + i];
				}
				if (num4 < 0)
				{
					return -4;
				}
			}
			else if (self_delimited == 0)
			{
				num4 = len / num5;
				if (num4 * num5 != len)
				{
					return -4;
				}
				for (int i = 0; i < num5 - 1; i++)
				{
					sizes[sizes_ptr + i] = (short)num4;
				}
			}
			break;
		}
		}
		if (self_delimited != 0)
		{
			int num8 = parse_size(data, data_ptr, len, ref sizes[sizes_ptr + num5 - 1]);
			len -= num8;
			if (sizes[sizes_ptr + num5 - 1] < 0 || sizes[sizes_ptr + num5 - 1] > len)
			{
				return -4;
			}
			data_ptr += num8;
			if (num3 != 0)
			{
				if (sizes[sizes_ptr + num5 - 1] * num5 > len)
				{
					return -4;
				}
				for (int i = 0; i < num5 - 1; i++)
				{
					sizes[sizes_ptr + i] = sizes[sizes_ptr + num5 - 1];
				}
			}
			else if (num8 + sizes[sizes_ptr + num5 - 1] > num4)
			{
				return -4;
			}
		}
		else
		{
			if (num4 > 1275)
			{
				return -4;
			}
			sizes[sizes_ptr + num5 - 1] = (short)num4;
		}
		payload_offset = data_ptr - num2;
		for (int i = 0; i < num5; i++)
		{
			if (frames != null)
			{
				byte[] array = new byte[data.Length];
				data.CopyTo(array.AsSpan());
				frames[frames_ptr + i] = array;
			}
			if (!frames_ptrs.IsEmpty)
			{
				frames_ptrs[frames_ptr + i] = data_ptr;
			}
			data_ptr += sizes[sizes_ptr + i];
		}
		packet_offset = num + (data_ptr - num2);
		out_toc = b;
		return num5;
	}
}
