using System;
using Concentus.Common.CPlusPlus;

namespace Concentus.Common;

internal class EntropyCoder
{
	private const int EC_WINDOW_SIZE = 32;

	/// *The number of bits to use for the range-coded part of uint integers.*/
	private const int EC_UINT_BITS = 8;

	/// *The resolution of fractional-precision bit usage measurements, i.e.,
	internal const int BITRES = 3;

	private const int EC_SYM_BITS = 8;

	private const int EC_CODE_BITS = 32;

	private const uint EC_SYM_MAX = 255u;

	private const uint EC_CODE_SHIFT = 23u;

	private const uint EC_CODE_TOP = 2147483648u;

	private const uint EC_CODE_BOT = 8388608u;

	private const int EC_CODE_EXTRA = 7;

	internal uint storage;

	internal uint end_offs;

	internal uint end_window;

	internal int nend_bits;

	internal int nbits_total;

	internal uint offs;

	internal uint rng;

	internal uint val;

	internal uint ext;

	internal int rem;

	internal int error;

	private static readonly uint[] correction = new uint[8] { 35733u, 38967u, 42495u, 46340u, 50535u, 55109u, 60097u, 65535u };

	internal EntropyCoder()
	{
		Reset();
	}

	internal void Reset()
	{
		storage = 0u;
		end_offs = 0u;
		end_window = 0u;
		nend_bits = 0;
		offs = 0u;
		rng = 0u;
		val = 0u;
		ext = 0u;
		rem = 0;
		error = 0;
	}

	internal void Assign(EntropyCoder other)
	{
		storage = other.storage;
		end_offs = other.end_offs;
		end_window = other.end_window;
		nend_bits = other.nend_bits;
		nbits_total = other.nbits_total;
		offs = other.offs;
		rng = other.rng;
		val = other.val;
		ext = other.ext;
		rem = other.rem;
		error = other.error;
	}

	internal int read_byte(ReadOnlySpan<byte> buf)
	{
		if (offs >= storage)
		{
			return 0;
		}
		return buf[(int)offs++];
	}

	internal int read_byte_from_end(ReadOnlySpan<byte> buf)
	{
		if (end_offs >= storage)
		{
			return 0;
		}
		return buf[(int)(storage - ++end_offs)];
	}

	internal int write_byte(Span<byte> buf, uint _value)
	{
		if (offs + end_offs >= storage)
		{
			return -1;
		}
		buf[(int)offs++] = (byte)_value;
		return 0;
	}

	internal int write_byte_at_end(Span<byte> buf, uint _value)
	{
		if (offs + end_offs >= storage)
		{
			return -1;
		}
		buf[(int)(storage - ++end_offs)] = (byte)_value;
		return 0;
	}

	/// <summary>
	/// Normalizes the contents of val and rng so that rng lies entirely in the high-order symbol.
	/// </summary>
	internal void dec_normalize(ReadOnlySpan<byte> buf)
	{
		while (rng <= 8388608)
		{
			nbits_total += 8;
			rng <<= 8;
			int num = rem;
			rem = read_byte(buf);
			num = ((num << 8) | rem) >> 1;
			val = (uint)((int)((val << 8) + (0xFFuL & (ulong)(~num))) & 0x7FFFFFFF);
		}
	}

	internal void dec_init(ReadOnlySpan<byte> buf, uint _storage)
	{
		storage = _storage;
		end_offs = 0u;
		end_window = 0u;
		nend_bits = 0;
		nbits_total = 9;
		offs = 0u;
		rng = 128u;
		rem = read_byte(buf);
		val = rng - 1 - (uint)(rem >> 1);
		error = 0;
		dec_normalize(buf);
	}

	internal uint decode(uint _ft)
	{
		ext = rng / _ft;
		uint num = val / ext;
		return _ft - Inlines.EC_MINI(num + 1, _ft);
	}

	internal uint decode_bin(uint _bits)
	{
		ext = rng >> (int)_bits;
		uint num = val / ext;
		return (uint)(1 << (int)_bits) - Inlines.EC_MINI(num + 1, (uint)(1 << (int)_bits));
	}

	internal void dec_update(ReadOnlySpan<byte> buf, uint _fl, uint _fh, uint _ft)
	{
		uint num = ext * (_ft - _fh);
		val -= num;
		rng = ((_fl != 0) ? (ext * (_fh - _fl)) : (rng - num));
		dec_normalize(buf);
	}

	/// <summary>
	/// The probability of having a "one" is 1/(1&lt;&lt;_logp).
	/// </summary>
	/// <param name="_logp"></param>
	/// <returns></returns>
	internal int dec_bit_logp(ReadOnlySpan<byte> buf, uint _logp)
	{
		uint num = rng;
		uint num2 = val;
		uint num3 = num >> (int)_logp;
		int num4 = ((num2 < num3) ? 1 : 0);
		if (num4 == 0)
		{
			val = num2 - num3;
		}
		rng = ((num4 != 0) ? num3 : (num - num3));
		dec_normalize(buf);
		return num4;
	}

	internal int dec_icdf(ReadOnlySpan<byte> buf, byte[] _icdf, uint _ftb)
	{
		uint num = rng;
		uint num2 = val;
		uint num3 = num >> (int)_ftb;
		int num4 = -1;
		uint num5;
		do
		{
			num5 = num;
			num = num3 * _icdf[++num4];
		}
		while (num2 < num);
		val = num2 - num;
		rng = num5 - num;
		dec_normalize(buf);
		return num4;
	}

	internal int dec_icdf(ReadOnlySpan<byte> buf, byte[] _icdf, int _icdf_offset, uint _ftb)
	{
		uint num = rng;
		uint num2 = val;
		uint num3 = num >> (int)_ftb;
		int num4 = _icdf_offset - 1;
		uint num5;
		do
		{
			num5 = num;
			num = num3 * _icdf[++num4];
		}
		while (num2 < num);
		val = num2 - num;
		rng = num5 - num;
		dec_normalize(buf);
		return num4 - _icdf_offset;
	}

	internal uint dec_uint(ReadOnlySpan<byte> buf, uint _ft)
	{
		_ft--;
		int num = Inlines.EC_ILOG(_ft);
		uint num2;
		if (num > 8)
		{
			num -= 8;
			uint ft = (_ft >> num) + 1;
			num2 = decode(ft);
			dec_update(buf, num2, num2 + 1, ft);
			uint num3 = (num2 << num) | dec_bits(buf, (uint)num);
			if (num3 <= _ft)
			{
				return num3;
			}
			error = 1;
			return _ft;
		}
		_ft++;
		num2 = decode(_ft);
		dec_update(buf, num2, num2 + 1, _ft);
		return num2;
	}

	internal uint dec_bits(ReadOnlySpan<byte> buf, uint _bits)
	{
		uint num = end_window;
		int num2 = nend_bits;
		if ((uint)num2 < _bits)
		{
			do
			{
				num |= (uint)(read_byte_from_end(buf) << num2);
				num2 += 8;
			}
			while (num2 <= 24);
		}
		int result = (int)num & ((1 << (int)_bits) - 1);
		num >>= (int)_bits;
		num2 -= (int)_bits;
		end_window = num;
		nend_bits = num2;
		nbits_total += (int)_bits;
		return (uint)result;
	}

	/// <summary>
	/// Outputs a symbol, with a carry bit.
	/// If there is a potential to propagate a carry over several symbols, they are
	/// buffered until it can be determined whether or not an actual carry will
	/// occur.
	/// If the counter for the buffered symbols overflows, then the stream becomes
	/// undecodable.
	/// This gives a theoretical limit of a few billion symbols in a single packet on
	/// 32-bit systems.
	/// The alternative is to truncate the range in order to force a carry, but
	/// requires similar carry tracking in the decoder, needlessly slowing it down.
	/// </summary>
	/// <param name="_c"></param>
	internal void enc_carry_out(Span<byte> buf, int _c)
	{
		if ((long)_c != 255)
		{
			int num = _c >> 8;
			if (rem >= 0)
			{
				error |= write_byte(buf, (uint)(rem + num));
			}
			if (ext != 0)
			{
				uint value = (uint)((255 + num) & 0xFF);
				do
				{
					error |= write_byte(buf, value);
				}
				while (--ext != 0);
			}
			rem = _c & 0xFF;
		}
		else
		{
			ext++;
		}
	}

	internal void enc_normalize(Span<byte> buf)
	{
		while (rng <= 8388608)
		{
			enc_carry_out(buf, (int)(val >> 23));
			val = (val << 8) & 0x7FFFFFFF;
			rng <<= 8;
			nbits_total += 8;
		}
	}

	internal void enc_init(uint _size)
	{
		end_offs = 0u;
		end_window = 0u;
		nend_bits = 0;
		nbits_total = 33;
		offs = 0u;
		rng = 2147483648u;
		rem = -1;
		val = 0u;
		ext = 0u;
		storage = _size;
		error = 0;
	}

	internal void encode(Span<byte> buf, uint _fl, uint _fh, uint _ft)
	{
		uint num = rng / _ft;
		if (_fl != 0)
		{
			val += rng - num * (_ft - _fl);
			rng = num * (_fh - _fl);
		}
		else
		{
			rng -= num * (_ft - _fh);
		}
		enc_normalize(buf);
	}

	internal void encode_bin(Span<byte> buf, uint _fl, uint _fh, uint _bits)
	{
		uint num = rng >> (int)_bits;
		if (_fl != 0)
		{
			val += (uint)((int)rng - (int)num * ((1 << (int)_bits) - (int)_fl));
			rng = num * (_fh - _fl);
		}
		else
		{
			rng -= (uint)((int)num * ((1 << (int)_bits) - (int)_fh));
		}
		enc_normalize(buf);
	}

	internal void enc_bit_logp(Span<byte> buf, int _val, uint _logp)
	{
		uint num = rng;
		uint num2 = val;
		uint num3 = num >> (int)_logp;
		num -= num3;
		if (_val != 0)
		{
			val = num2 + num;
		}
		rng = ((_val != 0) ? num3 : num);
		enc_normalize(buf);
	}

	internal void enc_icdf(Span<byte> buf, int _s, byte[] _icdf, uint _ftb)
	{
		uint num = rng >> (int)_ftb;
		if (_s > 0)
		{
			val += rng - num * _icdf[_s - 1];
			rng = num * (uint)(_icdf[_s - 1] - _icdf[_s]);
		}
		else
		{
			rng -= num * _icdf[_s];
		}
		enc_normalize(buf);
	}

	internal void enc_icdf(Span<byte> buf, int _s, byte[] _icdf, int icdf_ptr, uint _ftb)
	{
		uint num = rng >> (int)_ftb;
		if (_s > 0)
		{
			val += rng - num * _icdf[icdf_ptr + _s - 1];
			rng = num * (uint)(_icdf[icdf_ptr + _s - 1] - _icdf[icdf_ptr + _s]);
		}
		else
		{
			rng -= num * _icdf[icdf_ptr + _s];
		}
		enc_normalize(buf);
	}

	internal void enc_uint(Span<byte> buf, uint _fl, uint _ft)
	{
		_ft--;
		int num = Inlines.EC_ILOG(_ft);
		if (num > 8)
		{
			num -= 8;
			uint ft = (_ft >> num) + 1;
			uint num2 = _fl >> num;
			encode(buf, num2, num2 + 1, ft);
			enc_bits(buf, _fl & (uint)((1 << num) - 1), (uint)num);
		}
		else
		{
			encode(buf, _fl, _fl + 1, _ft + 1);
		}
	}

	internal void enc_bits(Span<byte> buf, uint _fl, uint _bits)
	{
		uint num = end_window;
		int num2 = nend_bits;
		if (num2 + _bits > 32)
		{
			do
			{
				error |= write_byte_at_end(buf, num & 0xFF);
				num >>= 8;
				num2 -= 8;
			}
			while (num2 >= 8);
		}
		num |= _fl << num2;
		num2 += (int)_bits;
		end_window = num;
		nend_bits = num2;
		nbits_total += (int)_bits;
	}

	internal void enc_patch_initial_bits(Span<byte> buf, uint _val, uint _nbits)
	{
		int num = (int)(8 - _nbits);
		uint num2 = (uint)((1 << (int)_nbits) - 1 << num);
		if (offs != 0)
		{
			buf[0] = (byte)((buf[0] & ~num2) | (_val << num));
		}
		else if (rem >= 0)
		{
			rem = (int)((((uint)rem & ~num2) | _val) << num);
		}
		else if (rng <= (uint)(int.MinValue >>> (int)_nbits))
		{
			val = (val & ~(num2 << 23)) | (_val << (int)(23L + (long)num));
		}
		else
		{
			error = -1;
		}
	}

	internal void enc_shrink(Span<byte> buf, uint _size)
	{
		Arrays.MemMoveByte(buf, (int)(_size - end_offs), (int)(storage - end_offs), (int)end_offs);
		storage = _size;
	}

	internal uint range_bytes()
	{
		return offs;
	}

	internal int get_error()
	{
		return error;
	}

	/// <summary>
	/// Returns the number of bits "used" by the encoded or decoded symbols so far.
	/// This same number can be computed in either the encoder or the decoder, and is
	/// suitable for making coding decisions.
	/// This will always be slightly larger than the exact value (e.g., all
	/// rounding error is in the positive direction).
	/// </summary>
	/// <returns>The number of bits.</returns>
	internal int tell()
	{
		return nbits_total - Inlines.EC_ILOG(rng);
	}

	/// <summary>
	/// This is a faster version of ec_tell_frac() that takes advantage
	/// of the low(1/8 bit) resolution to use just a linear function
	/// followed by a lookup to determine the exact transition thresholds.
	/// </summary>
	/// <returns></returns>
	internal uint tell_frac()
	{
		int num = nbits_total << 3;
		int num2 = Inlines.EC_ILOG(rng);
		int num3 = (int)(rng >> num2 - 16);
		uint num4 = (uint)((num3 >> 12) - 8);
		num4 += ((num3 > correction[num4]) ? 1u : 0u);
		num2 = (int)((num2 << 3) + num4);
		return (uint)(num - num2);
	}

	internal void enc_done(Span<byte> buf)
	{
		int num = 32 - Inlines.EC_ILOG(rng);
		uint num2 = 2147483647u >> num;
		uint num3 = (val + num2) & ~num2;
		if ((num3 | num2) >= val + rng)
		{
			num++;
			num2 >>= 1;
			num3 = (val + num2) & ~num2;
		}
		while (num > 0)
		{
			enc_carry_out(buf, (int)(num3 >> 23));
			num3 = (num3 << 8) & 0x7FFFFFFF;
			num -= 8;
		}
		if (rem >= 0 || ext != 0)
		{
			enc_carry_out(buf, 0);
		}
		uint num4 = end_window;
		int num5;
		for (num5 = nend_bits; num5 >= 8; num5 -= 8)
		{
			error |= write_byte_at_end(buf, num4 & 0xFF);
			num4 >>= 8;
		}
		if (error != 0)
		{
			return;
		}
		Arrays.MemSetWithOffset<byte>(buf, 0, (int)offs, (int)(storage - offs - end_offs));
		if (num5 <= 0)
		{
			return;
		}
		if (end_offs >= storage)
		{
			error = -1;
			return;
		}
		num = -num;
		if (offs + end_offs >= storage && num < num5)
		{
			num4 &= (uint)((1 << num) - 1);
			error = -1;
		}
		buf[(int)(storage - end_offs - 1)] |= (byte)num4;
	}
}
