using System;
using Concentus.Common;

namespace Concentus.Celt;

internal static class Laplace
{
	private const int LAPLACE_LOG_MINP = 0;

	private const uint LAPLACE_MINP = 1u;

	private const int LAPLACE_NMIN = 16;

	internal static uint ec_laplace_get_freq1(uint fs0, int decay)
	{
		return (uint)((32736 - fs0) * (16384 - decay) >> 15);
	}

	internal static void ec_laplace_encode(EntropyCoder enc, Span<byte> encodedData, ref int value, uint fs, int decay)
	{
		int num = value;
		uint num2 = 0u;
		if (num != 0)
		{
			int num3 = 0 - ((num < 0) ? 1 : 0);
			num = (num + num3) ^ num3;
			num2 = fs;
			fs = ec_laplace_get_freq1(fs, decay);
			int num4 = 1;
			while (fs != 0 && num4 < num)
			{
				fs *= 2;
				num2 += fs + 2;
				fs = (uint)(fs * decay >> 15);
				num4++;
			}
			if (fs == 0)
			{
				int num5 = (int)(32768 - num2 + 1 - 1);
				num5 = num5 - num3 >> 1;
				int num6 = Inlines.IMIN(num - num4, num5 - 1);
				num2 += (uint)(2 * num6 + 1 + num3);
				fs = Inlines.IMIN(1u, 32768 - num2);
				value = (num4 + num6 + num3) ^ num3;
			}
			else
			{
				fs++;
				num2 += (uint)(int)(fs & ~num3);
			}
		}
		enc.encode_bin(encodedData, num2, num2 + fs, 15u);
	}

	internal static int ec_laplace_decode(EntropyCoder dec, ReadOnlySpan<byte> encodedData, uint fs, int decay)
	{
		int num = 0;
		uint num2 = dec.decode_bin(15u);
		uint num3 = 0u;
		if (num2 >= fs)
		{
			num++;
			num3 = fs;
			fs = ec_laplace_get_freq1(fs, decay) + 1;
			while (fs > 1 && num2 >= num3 + 2 * fs)
			{
				fs *= 2;
				num3 += fs;
				fs = (uint)((fs - 2) * decay >> 15);
				fs++;
				num++;
			}
			if (fs <= 1)
			{
				int num4 = (int)(num2 - num3) >> 1;
				num += num4;
				num3 += (uint)(2 * num4);
			}
			if (num2 < num3 + fs)
			{
				num = -num;
			}
			else
			{
				num3 += fs;
			}
		}
		dec.dec_update(encodedData, num3, Inlines.IMIN(num3 + fs, 32768u), 32768u);
		return num;
	}
}
