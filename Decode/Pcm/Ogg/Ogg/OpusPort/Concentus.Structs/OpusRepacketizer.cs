using System;
using Concentus.Common.CPlusPlus;

namespace Concentus.Structs;

public class OpusRepacketizer
{
	internal byte toc;

	internal int nb_frames;

	internal readonly byte[][] frames = new byte[48][];

	internal readonly int[] frames_ptrs = new int[48];

	internal readonly short[] len = new short[48];

	internal int framesize;

	/// (Re)initializes a previously allocated repacketizer state.
	/// The state must be at least the size returned by opus_repacketizer_get_size().
	/// This can be used for applications which use their own allocator instead of
	/// malloc().
	/// It must also be called to reset the queue of packets waiting to be
	/// repacketized, which is necessary if the maximum packet duration of 120 ms
	/// is reached or if you wish to submit packets with a different Opus
	/// configuration (coding mode, audio bandwidth, frame size, or channel count).
	/// Failure to do so will prevent a new packet from being added with
	/// opus_repacketizer_cat().
	/// @see opus_repacketizer_create
	/// @see opus_repacketizer_get_size
	/// @see opus_repacketizer_cat
	/// @param rp <tt>OpusRepacketizer*</tt>: The repacketizer state to
	///                                       (re)initialize.
	public void Reset()
	{
		nb_frames = 0;
	}

	/// <summary>
	/// Creates a new repacketizer
	/// </summary>
	public OpusRepacketizer()
	{
		Reset();
	}

	internal int opus_repacketizer_cat_impl(Span<byte> data, int data_ptr, int len, int self_delimited)
	{
		if (len < 1)
		{
			return -4;
		}
		if (nb_frames == 0)
		{
			toc = data[data_ptr];
			framesize = OpusPacketInfo.GetNumSamplesPerFrame(data.Slice(data_ptr), 8000);
		}
		else if ((toc & 0xFC) != (data[data_ptr] & 0xFC))
		{
			return -4;
		}
		int numFrames = OpusPacketInfo.GetNumFrames(data.Slice(data_ptr, len));
		if (numFrames < 1)
		{
			return -4;
		}
		if ((numFrames + nb_frames) * framesize > 960)
		{
			return -4;
		}
		byte out_toc;
		int payload_offset;
		int num = OpusPacketInfo.opus_packet_parse_impl(data, data_ptr, len, self_delimited, out out_toc, frames, frames_ptrs, nb_frames, this.len, nb_frames, out payload_offset, out payload_offset);
		if (num < 1)
		{
			return num;
		}
		nb_frames += numFrames;
		return 0;
	}

	/// opus_repacketizer_cat. Add a packet to the current repacketizer state.
	///  This packet must match the configuration of any packets already submitted
	///  for repacketization since the last call to opus_repacketizer_init().
	///  This means that it must have the same coding mode, audio bandwidth, frame
	///  size, and channel count.
	///  This can be checked in advance by examining the top 6 bits of the first
	///  byte of the packet, and ensuring they match the top 6 bits of the first
	///  byte of any previously submitted packet.
	///  The total duration of audio in the repacketizer state also must not exceed
	///  120 ms, the maximum duration of a single packet, after adding this packet.
	///
	///  The contents of the current repacketizer state can be extracted into new
	///  packets using opus_repacketizer_out() or opus_repacketizer_out_range().
	///
	///  In order to add a packet with a different configuration or to add more
	///  audio beyond 120 ms, you must clear the repacketizer state by calling
	///  opus_repacketizer_init().
	///  If a packet is too large to add to the current repacketizer state, no part
	///  of it is added, even if it contains multiple frames, some of which might
	///  fit.
	///  If you wish to be able to add parts of such packets, you should first use
	///  another repacketizer to split the packet into pieces and add them
	///  individually.
	///  @see opus_repacketizer_out_range
	///  @see opus_repacketizer_out
	///  @see opus_repacketizer_init
	///  @param rp <tt>OpusRepacketizer*</tt>: The repacketizer state to which to
	///                                        add the packet.
	///  @param[in] data <tt>const unsigned char*</tt>: The packet data.
	///                                                 The application must ensure
	///                                                 this pointer remains valid
	///                                                 until the next call to
	///                                                 opus_repacketizer_init() or
	///                                                 opus_repacketizer_destroy().
	///  @param len <tt>opus_int32</tt>: The number of bytes in the packet data.
	///  @returns An error code indicating whether or not the operation succeeded.
	///  @retval #OPUS_OK The packet's contents have been added to the repacketizer
	///                   state.
	///  @retval #OPUS_INVALID_PACKET The packet did not have a valid TOC sequence,
	///                               the packet's TOC sequence was not compatible
	///                               with previously submitted packets (because
	///                               the coding mode, audio bandwidth, frame size,
	///                               or channel count did not match), or adding
	///                               this packet would increase the total amount of
	///                               audio stored in the repacketizer state to more
	///                               than 120 ms.
	public int AddPacket(Span<byte> data, int data_offset, int len)
	{
		return opus_repacketizer_cat_impl(data, data_offset, len, 0);
	}

	/// Return the total number of frames contained in packet data submitted to
	/// the repacketizer state so far via opus_repacketizer_cat() since the last
	/// call to opus_repacketizer_init() or opus_repacketizer_create().
	/// This defines the valid range of packets that can be extracted with
	/// opus_repacketizer_out_range() or opus_repacketizer_out().
	/// @param rp <tt>OpusRepacketizer*</tt>: The repacketizer state containing the
	///                                       frames.
	/// @returns The total number of frames contained in the packet data submitted
	///          to the repacketizer state.
	public int GetNumFrames()
	{
		return nb_frames;
	}

	internal int opus_repacketizer_out_range_impl(int begin, int end, Span<byte> data, int data_ptr, int maxlen, int self_delimited, int pad)
	{
		if (begin < 0 || begin >= end || end > nb_frames)
		{
			return -1;
		}
		int num = end - begin;
		int num2 = ((self_delimited != 0) ? (1 + ((len[num - 1] >= 252) ? 1 : 0)) : 0);
		int num3 = data_ptr;
		switch (num)
		{
		case 1:
			num2 += len[0] + 1;
			if (num2 > maxlen)
			{
				return -2;
			}
			data[num3++] = (byte)(toc & 0xFC);
			break;
		case 2:
			if (len[1] == len[0])
			{
				num2 += 2 * len[0] + 1;
				if (num2 > maxlen)
				{
					return -2;
				}
				data[num3++] = (byte)((toc & 0xFC) | 1);
				break;
			}
			num2 += len[0] + len[1] + 2 + ((len[0] >= 252) ? 1 : 0);
			if (num2 > maxlen)
			{
				return -2;
			}
			data[num3++] = (byte)((toc & 0xFC) | 2);
			num3 += OpusPacketInfo.encode_size(len[0], data, num3);
			break;
		}
		if (num > 2 || (pad != 0 && num2 < maxlen))
		{
			int num4 = 0;
			num3 = data_ptr;
			num2 = ((self_delimited != 0) ? (1 + ((len[num - 1] >= 252) ? 1 : 0)) : 0);
			int num5 = 0;
			for (int i = 1; i < num; i++)
			{
				if (len[i] != len[0])
				{
					num5 = 1;
					break;
				}
			}
			if (num5 != 0)
			{
				num2 += 2;
				for (int i = 0; i < num - 1; i++)
				{
					num2 += 1 + ((len[i] >= 252) ? 1 : 0) + len[i];
				}
				num2 += len[num - 1];
				if (num2 > maxlen)
				{
					return -2;
				}
				data[num3++] = (byte)((toc & 0xFC) | 3);
				data[num3++] = (byte)(num | 0x80);
			}
			else
			{
				num2 += num * len[0] + 2;
				if (num2 > maxlen)
				{
					return -2;
				}
				data[num3++] = (byte)((toc & 0xFC) | 3);
				data[num3++] = (byte)num;
			}
			num4 = ((pad != 0) ? (maxlen - num2) : 0);
			if (num4 != 0)
			{
				data[data_ptr + 1] |= 64;
				int num6 = (num4 - 1) / 255;
				for (int i = 0; i < num6; i++)
				{
					data[num3++] = byte.MaxValue;
				}
				data[num3++] = (byte)(num4 - 255 * num6 - 1);
				num2 += num4;
			}
			if (num5 != 0)
			{
				for (int i = 0; i < num - 1; i++)
				{
					num3 += OpusPacketInfo.encode_size(len[i], data, num3);
				}
			}
		}
		if (self_delimited != 0)
		{
			int num7 = OpusPacketInfo.encode_size(len[num - 1], data, num3);
			num3 += num7;
		}
		for (int i = begin; i < num + begin; i++)
		{
			frames[i].AsSpan(frames_ptrs[i], len[i]).CopyTo(data.Slice(num3));
			num3 += len[i];
		}
		if (pad != 0)
		{
			while (num3 < data_ptr + maxlen)
			{
				data[num3++] = 0;
			}
		}
		return num2;
	}

	/// Construct a new packet from data previously submitted to the repacketizer
	/// state via opus_repacketizer_cat().
	/// @param rp <tt>OpusRepacketizer*</tt>: The repacketizer state from which to
	///                                       construct the new packet.
	/// @param begin <tt>int</tt>: The index of the first frame in the current
	///                            repacketizer state to include in the output.
	/// @param end <tt>int</tt>: One past the index of the last frame in the
	///                          current repacketizer state to include in the
	///                          output.
	/// @param[out] data <tt>const unsigned char*</tt>: The buffer in which to
	///                                                 store the output packet.
	/// @param maxlen <tt>opus_int32</tt>: The maximum number of bytes to store in
	///                                    the output buffer. In order to guarantee
	///                                    success, this should be at least
	///                                    <code>1276</code> for a single frame,
	///                                    or for multiple frames,
	///                                    <code>1277*(end-begin)</code>.
	///                                    However, <code>1*(end-begin)</code> plus
	///                                    the size of all packet data submitted to
	///                                    the repacketizer since the last call to
	///                                    opus_repacketizer_init() or
	///                                    opus_repacketizer_create() is also
	///                                    sufficient, and possibly much smaller.
	/// @returns The total size of the output packet on success, or an error code
	///          on failure.
	/// @retval #OPUS_BAD_ARG <code>[begin,end)</code> was an invalid range of
	///                       frames (begin &lt; 0, begin &gt;= end, or end &gt;
	///                       opus_repacketizer_get_nb_frames()).
	/// @retval #OPUS_BUFFER_TOO_SMALL \a maxlen was insufficient to contain the
	///                                complete output packet.
	public int CreatePacket(int begin, int end, byte[] data, int data_offset, int maxlen)
	{
		return opus_repacketizer_out_range_impl(begin, end, data, data_offset, maxlen, 0, 0);
	}

	/// Construct a new packet from data previously submitted to the repacketizer
	/// state via opus_repacketizer_cat().
	/// This is a convenience routine that returns all the data submitted so far
	/// in a single packet.
	/// It is equivalent to calling
	/// @code
	/// opus_repacketizer_out_range(rp, 0, opus_repacketizer_get_nb_frames(rp),
	///                             data, maxlen)
	/// @endcode
	/// @param rp <tt>OpusRepacketizer*</tt>: The repacketizer state from which to
	///                                       construct the new packet.
	/// @param[out] data <tt>const unsigned char*</tt>: The buffer in which to
	///                                                 store the output packet.
	/// @param maxlen <tt>opus_int32</tt>: The maximum number of bytes to store in
	///                                    the output buffer. In order to guarantee
	///                                    success, this should be at least
	///                                    <code>1277*opus_repacketizer_get_nb_frames(rp)</code>.
	///                                    However,
	///                                    <code>1*opus_repacketizer_get_nb_frames(rp)</code>
	///                                    plus the size of all packet data
	///                                    submitted to the repacketizer since the
	///                                    last call to opus_repacketizer_init() or
	///                                    opus_repacketizer_create() is also
	///                                    sufficient, and possibly much smaller.
	/// @returns The total size of the output packet on success, or an error code
	///          on failure.
	/// @retval #OPUS_BUFFER_TOO_SMALL \a maxlen was insufficient to contain the
	///                                complete output packet.
	public int CreatePacket(byte[] data, int data_offset, int maxlen)
	{
		return opus_repacketizer_out_range_impl(0, nb_frames, data, data_offset, maxlen, 0, 0);
	}

	/// Pads a given Opus packet to a larger size (possibly changing the TOC sequence).
	/// @param[in,out] data <tt>const unsigned char*</tt>: The buffer containing the
	///                                                   packet to pad.
	/// @param len <tt>opus_int32</tt>: The size of the packet.
	///                                 This must be at least 1.
	/// @param new_len <tt>opus_int32</tt>: The desired size of the packet after padding.
	///                                 This must be at least as large as len.
	/// @returns an error code
	/// @retval #OPUS_OK \a on success.
	/// @retval #OPUS_BAD_ARG \a len was less than 1 or new_len was less than len.
	/// @retval #OPUS_INVALID_PACKET \a data did not contain a valid Opus packet.
	public static int PadPacket(Span<byte> data, int data_offset, int len, int new_len)
	{
		OpusRepacketizer opusRepacketizer = new OpusRepacketizer();
		if (len < 1)
		{
			return -1;
		}
		if (len == new_len)
		{
			return 0;
		}
		if (len > new_len)
		{
			return -1;
		}
		opusRepacketizer.Reset();
		Arrays.MemMoveByte(data, data_offset, data_offset + new_len - len, len);
		opusRepacketizer.AddPacket(data, data_offset + new_len - len, len);
		int num = opusRepacketizer.opus_repacketizer_out_range_impl(0, opusRepacketizer.nb_frames, data, data_offset, new_len, 0, 1);
		if (num > 0)
		{
			return 0;
		}
		return num;
	}

	/// Remove all padding from a given Opus packet and rewrite the TOC sequence to
	/// minimize space usage.
	/// @param[in,out] data <tt>const unsigned char*</tt>: The buffer containing the
	///                                                   packet to strip.
	/// @param len <tt>opus_int32</tt>: The size of the packet.
	///                                 This must be at least 1.
	/// @returns The new size of the output packet on success, or an error code
	///          on failure.
	/// @retval #OPUS_BAD_ARG \a len was less than 1.
	/// @retval #OPUS_INVALID_PACKET \a data did not contain a valid Opus packet.
	public static int UnpadPacket(byte[] data, int data_offset, int len)
	{
		if (len < 1)
		{
			return -1;
		}
		OpusRepacketizer opusRepacketizer = new OpusRepacketizer();
		opusRepacketizer.Reset();
		int num = opusRepacketizer.AddPacket(data, data_offset, len);
		if (num < 0)
		{
			return num;
		}
		return opusRepacketizer.opus_repacketizer_out_range_impl(0, opusRepacketizer.nb_frames, data, data_offset, len, 0, 0);
	}

	/// Pads a given Opus multi-stream packet to a larger size (possibly changing the TOC sequence).
	/// @param[in,out] data <tt>const unsigned char*</tt>: The buffer containing the
	///                                                   packet to pad.
	/// @param len <tt>opus_int32</tt>: The size of the packet.
	///                                 This must be at least 1.
	/// @param new_len <tt>opus_int32</tt>: The desired size of the packet after padding.
	///                                 This must be at least 1.
	/// @param nb_streams <tt>opus_int32</tt>: The number of streams (not channels) in the packet.
	///                                 This must be at least as large as len.
	/// @returns an error code
	/// @retval #OPUS_OK \a on success.
	/// @retval #OPUS_BAD_ARG \a len was less than 1.
	/// @retval #OPUS_INVALID_PACKET \a data did not contain a valid Opus packet.
	public static int PadMultistreamPacket(byte[] data, int data_offset, int len, int new_len, int nb_streams)
	{
		short[] array = new short[48];
		if (len < 1)
		{
			return -1;
		}
		if (len == new_len)
		{
			return 0;
		}
		if (len > new_len)
		{
			return -1;
		}
		int num = new_len - len;
		for (int i = 0; i < nb_streams - 1; i++)
		{
			if (len <= 0)
			{
				return -4;
			}
			byte out_toc;
			int payload_offset;
			int packet_offset;
			int num2 = OpusPacketInfo.opus_packet_parse_impl(data, data_offset, len, 1, out out_toc, null, null, 0, array, 0, out payload_offset, out packet_offset);
			if (num2 < 0)
			{
				return num2;
			}
			data_offset += packet_offset;
			len -= packet_offset;
		}
		return PadPacket(data, data_offset, len, len + num);
	}

	/// Remove all padding from a given Opus multi-stream packet and rewrite the TOC sequence to
	/// minimize space usage.
	/// @param[in,out] data <tt>const unsigned char*</tt>: The buffer containing the
	///                                                   packet to strip.
	/// @param len <tt>opus_int32</tt>: The size of the packet.
	///                                 This must be at least 1.
	/// @param nb_streams <tt>opus_int32</tt>: The number of streams (not channels) in the packet.
	///                                 This must be at least 1.
	/// @returns The new size of the output packet on success, or an error code
	///          on failure.
	/// @retval #OPUS_BAD_ARG \a len was less than 1 or new_len was less than len.
	/// @retval #OPUS_INVALID_PACKET \a data did not contain a valid Opus packet.
	public static int UnpadMultistreamPacket(byte[] data, int data_offset, int len, int nb_streams)
	{
		short[] array = new short[48];
		OpusRepacketizer opusRepacketizer = new OpusRepacketizer();
		if (len < 1)
		{
			return -1;
		}
		int num = data_offset;
		int num2 = 0;
		for (int i = 0; i < nb_streams; i++)
		{
			int self_delimited = ((i != nb_streams) ? 1 : 0) - 1;
			if (len <= 0)
			{
				return -4;
			}
			opusRepacketizer.Reset();
			int num3 = OpusPacketInfo.opus_packet_parse_impl(data, data_offset, len, self_delimited, out var _, null, null, 0, array, 0, out var _, out var packet_offset);
			if (num3 < 0)
			{
				return num3;
			}
			num3 = opusRepacketizer.opus_repacketizer_cat_impl(data, data_offset, packet_offset, self_delimited);
			if (num3 < 0)
			{
				return num3;
			}
			num3 = opusRepacketizer.opus_repacketizer_out_range_impl(0, opusRepacketizer.nb_frames, data, num, len, self_delimited, 0);
			if (num3 < 0)
			{
				return num3;
			}
			num2 += num3;
			num += num3;
			data_offset += packet_offset;
			len -= packet_offset;
		}
		return num2;
	}
}
