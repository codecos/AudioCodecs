using System;
using Concentus.Common;

namespace Concentus.Celt;

internal static class CWRS
{
	internal static readonly uint[] CELT_PVQ_U_ROW = new uint[15]
	{
		0u, 176u, 351u, 525u, 698u, 870u, 1041u, 1131u, 1178u, 1207u,
		1226u, 1240u, 1248u, 1254u, 1257u
	};

	private static uint CELT_PVQ_U(int _n, int _k)
	{
		return Tables.CELT_PVQ_U_DATA[CELT_PVQ_U_ROW[Inlines.IMIN(_n, _k)] + Inlines.IMAX(_n, _k)];
	}

	private static uint CELT_PVQ_V(int _n, int _k)
	{
		return CELT_PVQ_U(_n, _k) + CELT_PVQ_U(_n, _k + 1);
	}

	internal static uint icwrs(int _n, int[] _y)
	{
		int num = _n - 1;
		uint num2 = ((_y[num] < 0) ? 1u : 0u);
		int num3 = Inlines.abs(_y[num]);
		do
		{
			num--;
			num2 += CELT_PVQ_U(_n - num, num3);
			num3 += Inlines.abs(_y[num]);
			if (_y[num] < 0)
			{
				num2 += CELT_PVQ_U(_n - num, num3 + 1);
			}
		}
		while (num > 0);
		return num2;
	}

	internal static void encode_pulses(int[] _y, int _n, int _k, EntropyCoder _enc, Span<byte> encodedData)
	{
		_enc.enc_uint(encodedData, icwrs(_n, _y), CELT_PVQ_V(_n, _k));
	}

	internal static int cwrsi(int _n, int _k, uint _i, int[] _y)
	{
		int c = 0;
		int num = 0;
		uint num3;
		int num5;
		int num4;
		short num7;
		while (_n > 2)
		{
			if (_k >= _n)
			{
				uint num2 = CELT_PVQ_U_ROW[_n];
				num3 = Tables.CELT_PVQ_U_DATA[num2 + _k + 1];
				num4 = 0 - ((_i >= num3) ? 1 : 0);
				_i -= num3 & (uint)num4;
				num5 = _k;
				uint num6 = Tables.CELT_PVQ_U_DATA[num2 + _n];
				if (num6 > _i)
				{
					_k = _n;
					do
					{
						num3 = Tables.CELT_PVQ_U_DATA[CELT_PVQ_U_ROW[--_k] + _n];
					}
					while (num3 > _i);
				}
				else
				{
					for (num3 = Tables.CELT_PVQ_U_DATA[num2 + _k]; num3 > _i; num3 = Tables.CELT_PVQ_U_DATA[num2 + _k])
					{
						_k--;
					}
				}
				_i -= num3;
				num7 = (short)((num5 - _k + num4) ^ num4);
				_y[num++] = num7;
				c = Inlines.MAC16_16(c, num7, num7);
			}
			else
			{
				num3 = Tables.CELT_PVQ_U_DATA[CELT_PVQ_U_ROW[_k] + _n];
				uint num6 = Tables.CELT_PVQ_U_DATA[CELT_PVQ_U_ROW[_k + 1] + _n];
				if (num3 <= _i && _i < num6)
				{
					_i -= num3;
					_y[num++] = 0;
				}
				else
				{
					num4 = 0 - ((_i >= num6) ? 1 : 0);
					_i -= num6 & (uint)num4;
					num5 = _k;
					do
					{
						num3 = Tables.CELT_PVQ_U_DATA[CELT_PVQ_U_ROW[--_k] + _n];
					}
					while (num3 > _i);
					_i -= num3;
					num7 = (short)((num5 - _k + num4) ^ num4);
					_y[num++] = num7;
					c = Inlines.MAC16_16(c, num7, num7);
				}
			}
			_n--;
		}
		num3 = (uint)(2 * _k + 1);
		num4 = 0 - ((_i >= num3) ? 1 : 0);
		_i -= num3 & (uint)num4;
		num5 = _k;
		_k = (int)(_i + 1 >> 1);
		if (_k != 0)
		{
			_i -= (uint)(2 * _k - 1);
		}
		num7 = (short)((num5 - _k + num4) ^ num4);
		_y[num++] = num7;
		c = Inlines.MAC16_16(c, num7, num7);
		num4 = (int)(0 - _i);
		num7 = (short)(_y[num] = (short)((_k + num4) ^ num4));
		return Inlines.MAC16_16(c, num7, num7);
	}

	internal static int decode_pulses(int[] _y, int _n, int _k, EntropyCoder _dec, ReadOnlySpan<byte> encodedData)
	{
		return cwrsi(_n, _k, _dec.dec_uint(encodedData, CELT_PVQ_V(_n, _k)), _y);
	}
}
