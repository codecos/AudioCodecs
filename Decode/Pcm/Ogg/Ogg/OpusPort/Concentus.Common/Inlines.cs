using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Concentus.Common;

internal static class Inlines
{
	private const MethodImplOptions INLINE_ATTR = MethodImplOptions.AggressiveInlining;

	private static readonly short[] sqrt_C = new short[5] { 23175, 11561, -3011, 1699, -664 };

	private const short log2_C0 = -6793;

	[Conditional("DEBUG")]
	internal static void OpusAssert(bool condition, string message = "Unknown error")
	{
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16SU(int a, int b)
	{
		return (short)a * (ushort)b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16SU(short a, ushort b)
	{
		return a * b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16SU(int a, uint b)
	{
		return a * (int)b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_32_Q16(short a, int b)
	{
		return ADD32(MULT16_16(a, SHR(b, 16)), SHR(MULT16_16SU(a, b & 0xFFFF), 16));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_32_Q16(int a, int b)
	{
		return ADD32(MULT16_16(a, SHR(b, 16)), SHR(MULT16_16SU(a, b & 0xFFFF), 16));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_32_P16(short a, int b)
	{
		return ADD32(MULT16_16(a, SHR(b, 16)), PSHR(MULT16_16SU(a, b & 0xFFFF), 16));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_32_P16(int a, int b)
	{
		return ADD32(MULT16_16(a, SHR(b, 16)), PSHR(MULT16_16SU(a, b & 0xFFFF), 16));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_32_Q15(short a, int b)
	{
		return (a * (b >> 16) << 1) + (a * (b & 0xFFFF) >> 15);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_32_Q15(int a, int b)
	{
		return (a * (b >> 16) << 1) + (a * (b & 0xFFFF) >> 15);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT32_32_Q31(int a, int b)
	{
		return ADD32(ADD32(SHL(MULT16_16(SHR(a, 16), SHR(b, 16)), 1), SHR(MULT16_16SU(SHR(a, 16), b & 0xFFFF), 15)), SHR(MULT16_16SU(SHR(b, 16), a & 0xFFFF), 15));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short QCONST16(float x, int bits)
	{
		return (short)(0.5 + (double)(x * (float)(1 << bits)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int QCONST32(float x, int bits)
	{
		return (int)(0.5 + (double)(x * (float)(1 << bits)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short NEG16(short x)
	{
		return (short)(-x);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int NEG16(int x)
	{
		return -x;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int NEG32(int x)
	{
		return -x;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short EXTRACT16(int x)
	{
		return (short)x;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int EXTEND32(short x)
	{
		return x;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int EXTEND32(int x)
	{
		return x;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short SHR16(short a, int shift)
	{
		return (short)(a >> shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int SHR16(int a, int shift)
	{
		return a >> shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short SHL16(short a, int shift)
	{
		return (short)((ushort)a << shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int SHL16(int a, int shift)
	{
		return a << shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int SHR32(int a, int shift)
	{
		return a >> shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int SHL32(int a, int shift)
	{
		return a << shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int PSHR32(int a, int shift)
	{
		return SHR32(a + (EXTEND32(1) << shift >> 1), shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short PSHR16(short a, int shift)
	{
		return SHR16((short)(a + (1 << shift >> 1)), shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int PSHR16(int a, int shift)
	{
		return SHR32(a + (1 << shift >> 1), shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int VSHR32(int a, int shift)
	{
		if (shift <= 0)
		{
			return SHL32(a, -shift);
		}
		return SHR32(a, shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int SHR(int a, int shift)
	{
		return a >> shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int SHL(int a, int shift)
	{
		return SHL32(a, shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int SHR(short a, int shift)
	{
		return a >> shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int SHL(short a, int shift)
	{
		return SHL32(a, shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static int PSHR(int a, int shift)
	{
		return SHR(a + (EXTEND32(1) << shift >> 1), shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int SATURATE(int x, int a)
	{
		if (x <= a)
		{
			if (x >= -a)
			{
				return x;
			}
			return -a;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short SATURATE16(int x)
	{
		return EXTRACT16((x > 32767) ? 32767 : ((x < -32768) ? (-32768) : x));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short ROUND16(short x, short a)
	{
		return EXTRACT16(PSHR32(x, a));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int ROUND16(int x, int a)
	{
		return PSHR32(x, a);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int PDIV32(int a, int b)
	{
		return a / b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short HALF16(short x)
	{
		return SHR16(x, 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int HALF16(int x)
	{
		return SHR32(x, 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int HALF32(int x)
	{
		return SHR32(x, 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short ADD16(short a, short b)
	{
		return (short)(a + b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int ADD16(int a, int b)
	{
		return a + b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short SUB16(short a, short b)
	{
		return (short)(a - b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int SUB16(int a, int b)
	{
		return a - b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int ADD32(int a, int b)
	{
		return a + b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int SUB32(int a, int b)
	{
		return a - b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MULT16_16_16(short a, short b)
	{
		return (short)(a * b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_16(int a, int b)
	{
		return a * b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16(int a, int b)
	{
		return a * b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16(short a, short b)
	{
		return a * b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAC16_16(short c, short a, short b)
	{
		return c + a * b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAC16_16(int c, short a, short b)
	{
		return c + a * b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAC16_16(int c, int a, int b)
	{
		return c + a * b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAC16_32_Q15(int c, short a, short b)
	{
		return ADD32(c, ADD32(MULT16_16(a, SHR(b, 15)), SHR(MULT16_16(a, b & 0x7FFF), 15)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAC16_32_Q15(int c, int a, int b)
	{
		return ADD32(c, ADD32(MULT16_16(a, SHR(b, 15)), SHR(MULT16_16(a, b & 0x7FFF), 15)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAC16_32_Q16(int c, short a, short b)
	{
		return ADD32(c, ADD32(MULT16_16(a, SHR(b, 16)), SHR(MULT16_16SU(a, b & 0xFFFF), 16)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAC16_32_Q16(int c, int a, int b)
	{
		return ADD32(c, ADD32(MULT16_16(a, SHR(b, 16)), SHR(MULT16_16SU(a, b & 0xFFFF), 16)));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_Q11_32(short a, short b)
	{
		return SHR(MULT16_16(a, b), 11);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_Q11_32(int a, int b)
	{
		return SHR(MULT16_16(a, b), 11);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MULT16_16_Q11(short a, short b)
	{
		return (short)SHR(MULT16_16(a, b), 11);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_Q11(int a, int b)
	{
		return SHR(MULT16_16(a, b), 11);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MULT16_16_Q13(short a, short b)
	{
		return (short)SHR(MULT16_16(a, b), 13);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_Q13(int a, int b)
	{
		return SHR(MULT16_16(a, b), 13);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MULT16_16_Q14(short a, short b)
	{
		return (short)SHR(MULT16_16(a, b), 14);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_Q14(int a, int b)
	{
		return SHR(MULT16_16(a, b), 14);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MULT16_16_Q15(short a, short b)
	{
		return (short)SHR(MULT16_16(a, b), 15);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_Q15(int a, int b)
	{
		return SHR(MULT16_16(a, b), 15);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MULT16_16_P13(short a, short b)
	{
		return (short)SHR(ADD32(4096, MULT16_16(a, b)), 13);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_P13(int a, int b)
	{
		return SHR(ADD32(4096, MULT16_16(a, b)), 13);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MULT16_16_P14(short a, short b)
	{
		return (short)SHR(ADD32(8192, MULT16_16(a, b)), 14);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_P14(int a, int b)
	{
		return SHR(ADD32(8192, MULT16_16(a, b)), 14);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MULT16_16_P15(short a, short b)
	{
		return (short)SHR(ADD32(16384, MULT16_16(a, b)), 15);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MULT16_16_P15(int a, int b)
	{
		return SHR(ADD32(16384, MULT16_16(a, b)), 15);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short DIV32_16(int a, short b)
	{
		return (short)(a / b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int DIV32_16(int a, int b)
	{
		return a / b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int DIV32(int a, int b)
	{
		return a / b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short SAT16(int x)
	{
		return (x > 32767) ? short.MaxValue : ((x < -32768) ? short.MinValue : ((short)x));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short SIG2WORD16(int x)
	{
		x = PSHR32(x, 12);
		x = MAX32(x, -32768);
		x = MIN32(x, 32767);
		return EXTRACT16(x);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MIN(short a, short b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MAX(short a, short b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MIN16(short a, short b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short MAX16(short a, short b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MIN16(int a, int b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAX16(int a, int b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static float MIN16(float a, float b)
	{
		if (!(a < b))
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static float MAX16(float a, float b)
	{
		if (!(a > b))
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MIN(int a, int b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAX(int a, int b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int IMIN(int a, int b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint IMIN(uint a, uint b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int IMAX(int a, int b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MIN32(int a, int b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MAX32(int a, int b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static float MIN32(float a, float b)
	{
		if (!(a < b))
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static float MAX32(float a, float b)
	{
		if (!(a > b))
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int ABS16(int x)
	{
		if (x >= 0)
		{
			return x;
		}
		return -x;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static float ABS16(float x)
	{
		if (!(x < 0f))
		{
			return x;
		}
		return 0f - x;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short ABS16(short x)
	{
		return (short)((x < 0) ? (-x) : x);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int ABS32(int x)
	{
		if (x >= 0)
		{
			return x;
		}
		return -x;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint celt_udiv(uint n, uint d)
	{
		return n / d;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_udiv(int n, int d)
	{
		return n / d;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_sudiv(int n, int d)
	{
		return n / d;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_div(int a, int b)
	{
		return MULT32_32_Q31(a, celt_rcp(b));
	}

	/// Integer log in base2. Undefined for zero and negative numbers 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_ilog2(int x)
	{
		return EC_ILOG((uint)x) - 1;
	}

	/// Integer log in base2. Defined for zero, but not for negative numbers 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_zlog2(int x)
	{
		if (x > 0)
		{
			return celt_ilog2(x);
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_maxabs16(Span<int> x, int x_ptr, int len)
	{
		int num = 0;
		int num2 = 0;
		for (int i = x_ptr; i < len + x_ptr; i++)
		{
			num = MAX32(num, x[i]);
			num2 = MIN32(num2, x[i]);
		}
		return MAX32(EXTEND32(num), -EXTEND32(num2));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_maxabs32(Span<int> x, int len)
	{
		int a = 0;
		int num = 0;
		for (int i = 0; i < len; i++)
		{
			a = MAX32(a, x[i]);
			num = MIN32(num, x[i]);
		}
		return MAX32(a, -num);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_maxabs32(Span<int> x, int x_ptr, int len)
	{
		int a = 0;
		int num = 0;
		for (int i = x_ptr; i < x_ptr + len; i++)
		{
			a = MAX32(a, x[i]);
			num = MIN32(num, x[i]);
		}
		return MAX32(a, -num);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short celt_maxabs32(Span<short> x, int x_ptr, int len)
	{
		short a = 0;
		short num = 0;
		for (int i = x_ptr; i < x_ptr + len; i++)
		{
			a = MAX16(a, x[i]);
			num = MIN16(num, x[i]);
		}
		return MAX(a, (short)(-num));
	}

	/// <summary>
	/// Multiplies two 16-bit fractional values. Bit-exactness of this macro is important
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int FRAC_MUL16(int a, int b)
	{
		return 16384 + (short)a * (short)b >> 15;
	}

	/// <summary>
	/// Compute floor(sqrt(_val)) with exact arithmetic.
	/// This has been tested on all possible 32-bit inputs.
	/// </summary>
	/// <param name="_val"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint isqrt32(uint _val)
	{
		uint num = 0u;
		int num2 = EC_ILOG(_val) - 1 >> 1;
		uint num3 = (uint)(1 << num2);
		do
		{
			uint num4 = (num << 1) + num3 << num2;
			if (num4 <= _val)
			{
				num += num3;
				_val -= num4;
			}
			num3 >>= 1;
			num2--;
		}
		while (num2 >= 0);
		return num;
	}

	/// Sqrt approximation (QX input, QX/2 output) 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_sqrt(int x)
	{
		if (x == 0)
		{
			return 0;
		}
		if (x >= 1073741824)
		{
			return 32767;
		}
		int num = (celt_ilog2(x) >> 1) - 7;
		x = VSHR32(x, 2 * num);
		short a = (short)(x - 32768);
		return VSHR32(ADD16(sqrt_C[0], MULT16_16_Q15(a, ADD16(sqrt_C[1], MULT16_16_Q15(a, ADD16(sqrt_C[2], MULT16_16_Q15(a, ADD16(sqrt_C[3], MULT16_16_Q15(a, sqrt_C[4])))))))), 7 - num);
	}

	/// Reciprocal approximation (Q15 input, Q16 output) 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_rcp(int x)
	{
		int num = celt_ilog2(x);
		int b = VSHR32(x, num - 15) - 32768;
		int a = ADD16(30840, MULT16_16_Q15(-15420, b));
		a = SUB16(a, MULT16_16_Q15(a, ADD16(MULT16_16_Q15(a, b), ADD16(a, -32768))));
		a = SUB16(a, ADD16(1, MULT16_16_Q15(a, ADD16(MULT16_16_Q15(a, b), ADD16(a, -32768)))));
		return VSHR32(EXTEND32(a), num - 16);
	}

	/// Reciprocal sqrt approximation in the range [0.25,1) (Q16 in, Q14 out) 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_rsqrt_norm(int x)
	{
		int num = x - 32768;
		int num2 = ADD16(23557, MULT16_16_Q15(num, ADD16(-13490, MULT16_16_Q15(num, 6713))));
		int num3 = MULT16_16_Q15(num2, num2);
		int a = SHL16(SUB16(ADD16(MULT16_16_Q15(num3, num), num3), 16384), 1);
		return ADD16(num2, MULT16_16_Q15(num2, MULT16_16_Q15(a, SUB16(MULT16_16_Q15(a, 12288), 16384))));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int frac_div32(int a, int b)
	{
		int shift = celt_ilog2(b) - 29;
		a = VSHR32(a, shift);
		b = VSHR32(b, shift);
		int a2 = ROUND16(celt_rcp(ROUND16(b, 16)), 3);
		int a3 = MULT16_32_Q15(a2, a);
		int b2 = PSHR32(a, 2) - MULT32_32_Q31(a3, b);
		a3 = ADD32(a3, SHL32(MULT16_32_Q15(a2, b2), 2));
		if (a3 >= 536870912)
		{
			return int.MaxValue;
		}
		if (a3 <= -536870912)
		{
			return -2147483647;
		}
		return SHL32(a3, 2);
	}

	/// Base-2 logarithm approximation (log2(x)). (Q14 input, Q10 output) 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_log2(int x)
	{
		if (x == 0)
		{
			return -32767;
		}
		int num = celt_ilog2(x);
		int a = VSHR32(x, num - 15) - 32768 - 16384;
		int a2 = ADD16(-6793, MULT16_16_Q15(a, ADD16(15746, MULT16_16_Q15(a, ADD16(-5217, MULT16_16_Q15(a, ADD16(2545, MULT16_16_Q15(a, -1401))))))));
		return SHL16((short)(num - 13), 10) + SHR16(a2, 4);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_exp2_frac(int x)
	{
		int num = SHL16(x, 4);
		return ADD16(16383, MULT16_16_Q15(num, ADD16(22804, MULT16_16_Q15(num, ADD16(14819, MULT16_16_Q15(10204, num))))));
	}

	/// Base-2 exponential approximation (2^x). (Q10 input, Q16 output) 
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_exp2(int x)
	{
		int num = SHR16(x, 10);
		if (num > 14)
		{
			return 2130706432;
		}
		if (num < -15)
		{
			return 0;
		}
		return VSHR32(EXTEND32((int)(short)celt_exp2_frac((short)(x - SHL16((short)num, 10)))), -num - 2);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_atan01(int x)
	{
		return MULT16_16_P15(x, ADD32(32767, MULT16_16_P15(x, ADD32(-21, MULT16_16_P15(x, ADD32(-11943, MULT16_16_P15(4936, x)))))));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_atan2p(int y, int x)
	{
		if (y < x)
		{
			int num = celt_div(SHL32(EXTEND32(y), 15), x);
			if (num >= 32767)
			{
				num = 32767;
			}
			return SHR32(celt_atan01(EXTRACT16(num)), 1);
		}
		int num2 = celt_div(SHL32(EXTEND32(x), 15), y);
		if (num2 >= 32767)
		{
			num2 = 32767;
		}
		return 25736 - SHR16(celt_atan01(EXTRACT16(num2)), 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int celt_cos_norm(int x)
	{
		x &= 0x1FFFF;
		if (x > SHL32(EXTEND32(1), 16))
		{
			x = SUB32(SHL32(EXTEND32(1), 17), x);
		}
		if ((x & 0x7FFF) != 0)
		{
			if (x < SHL32(EXTEND32(1), 15))
			{
				return _celt_cos_pi_2(EXTRACT16(x));
			}
			return NEG32(_celt_cos_pi_2(EXTRACT16(65536 - x)));
		}
		if ((x & 0xFFFF) != 0)
		{
			return 0;
		}
		if ((x & 0x1FFFF) != 0)
		{
			return -32767;
		}
		return 32767;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int _celt_cos_pi_2(int x)
	{
		int num = MULT16_16_P15(x, x);
		return ADD32(1, MIN32(32766, ADD32(SUB16(32767, num), MULT16_16_P15(num, ADD32(-7651, MULT16_16_P15(num, ADD32(8277, MULT16_16_P15(-626, num))))))));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short FLOAT2INT16(float x)
	{
		x *= 32768f;
		if (x < -32768f)
		{
			x = -32768f;
		}
		if (x > 32767f)
		{
			x = 32767f;
		}
		return (short)x;
	}

	/// <summary>
	/// Rotate a32 right by 'rot' bits. Negative rot values result in rotating
	/// left. Output is 32bit int.
	/// </summary>
	/// <param name="a32"></param>
	/// <param name="rot"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ROR32(int a32, int rot)
	{
		return (int)silk_ROR32((uint)a32, rot);
	}

	/// <summary>
	/// Rotate a32 right by 'rot' bits. Negative rot values result in rotating
	/// left. Output is 32bit uint.
	/// </summary>
	/// <param name="a32"></param>
	/// <param name="rot"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint silk_ROR32(uint a32, int rot)
	{
		int num = -rot;
		if (rot == 0)
		{
			return a32;
		}
		if (rot < 0)
		{
			return (a32 << num) | (a32 >> 32 - num);
		}
		return (a32 << 32 - rot) | (a32 >> rot);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_MUL(int a32, int b32)
	{
		return a32 * b32;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint silk_MUL_uint(uint a32, uint b32)
	{
		return a32 * b32;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_MLA(int a32, int b32, int c32)
	{
		return silk_ADD32(a32, b32 * c32);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_MLA_uint(uint a32, uint b32, uint c32)
	{
		return (int)silk_ADD32(a32, b32 * c32);
	}

	/// <summary>
	/// ((a32 &gt;&gt; 16)  * (b32 &gt;&gt; 16))
	/// </summary>
	/// <param name="a32"></param>
	/// <param name="b32"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMULTT(int a32, int b32)
	{
		return (a32 >> 16) * (b32 >> 16);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMLATT(int a32, int b32, int c32)
	{
		return silk_ADD32(a32, (b32 >> 16) * (c32 >> 16));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_SMLALBB(long a64, short b16, short c16)
	{
		return silk_ADD64(a64, b16 * c16);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_SMULL(int a32, int b32)
	{
		return (long)a32 * (long)b32;
	}

	/// <summary>
	/// Adds two signed 32-bit values in a way that can overflow, while not relying on undefined behaviour
	/// (just standard two's complement implementation-specific behaviour)
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD32_ovflw(int a, int b)
	{
		return a + b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD32_ovflw(uint a, uint b)
	{
		return (int)(a + b);
	}

	/// <summary>
	/// Subtracts two signed 32-bit values in a way that can overflow, while not relying on undefined behaviour
	/// (just standard two's complement implementation-specific behaviour)
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SUB32_ovflw(int a, int b)
	{
		return a - b;
	}

	/// <summary>
	/// Multiply-accumulate macros that allow overflow in the addition (ie, no asserts in debug mode)
	/// </summary>
	/// <param name="a32"></param>
	/// <param name="b32"></param>
	/// <param name="c32"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_MLA_ovflw(int a32, int b32, int c32)
	{
		return silk_ADD32_ovflw((uint)a32, (uint)(b32 * c32));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMLABB_ovflw(int a32, int b32, int c32)
	{
		return silk_ADD32_ovflw(a32, (short)b32 * (short)c32);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMULBB(int a32, int b32)
	{
		return (short)a32 * (short)b32;
	}

	/// <summary>
	/// (a32 * (int)((short)(b32))) &gt;&gt; 16 output have to be 32bit int
	/// </summary>
	/// <param name="a32"></param>
	/// <param name="b32"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMULWB(int a32, int b32)
	{
		return (int)((long)a32 * (long)(short)b32 >> 16);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMLABB(int a32, int b32, int c32)
	{
		return a32 + (short)b32 * (short)c32;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_DIV32_16(int a32, int b32)
	{
		return a32 / b32;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_DIV32(int a32, int b32)
	{
		return a32 / b32;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_ADD16(short a, short b)
	{
		return (short)(a + b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD32(int a, int b)
	{
		return a + b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint silk_ADD32(uint a, uint b)
	{
		return a + b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_ADD64(long a, long b)
	{
		return a + b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_SUB16(short a, short b)
	{
		return (short)(a - b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SUB32(int a, int b)
	{
		return a - b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_SUB64(long a, long b)
	{
		return a - b;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SAT8(int a)
	{
		if (a <= 255)
		{
			if (a >= 0)
			{
				return a;
			}
			return 0;
		}
		return 255;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SAT16(int a)
	{
		if (a <= 32767)
		{
			if (a >= -32768)
			{
				return a;
			}
			return -32768;
		}
		return 32767;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SAT32(long a)
	{
		if (a <= int.MaxValue)
		{
			if (a >= int.MinValue)
			{
				return (int)a;
			}
			return int.MinValue;
		}
		return int.MaxValue;
	}

	/// <summary>
	/// //////////////////
	/// </summary>
	/// <param name="a16"></param>
	/// <param name="b16"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_ADD_SAT16(short a16, short b16)
	{
		return (short)silk_SAT16(silk_ADD32(a16, b16));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD_SAT32(int a32, int b32)
	{
		if (((a32 + b32) & int.MinValue) != 0)
		{
			if (((a32 | b32) & 0x80000000u) != 0L)
			{
				return a32 + b32;
			}
			return int.MaxValue;
		}
		if ((a32 & b32 & 0x80000000u) == 0L)
		{
			return a32 + b32;
		}
		return int.MinValue;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_ADD_SAT64(long a64, long b64)
	{
		if (((a64 + b64) & long.MinValue) != 0L)
		{
			if (((a64 | b64) & long.MinValue) != 0L)
			{
				return a64 + b64;
			}
			return long.MaxValue;
		}
		if ((a64 & b64 & long.MinValue) == 0L)
		{
			return a64 + b64;
		}
		return long.MinValue;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_SUB_SAT16(short a16, short b16)
	{
		return (short)silk_SAT16(silk_SUB32(a16, b16));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SUB_SAT32(int a32, int b32)
	{
		if (((a32 - b32) & int.MinValue) != 0)
		{
			if (((a32 ^ 0x80000000u) & b32 & 0x80000000u) == 0L)
			{
				return a32 - b32;
			}
			return int.MaxValue;
		}
		if ((a32 & (b32 ^ 0x80000000u) & 0x80000000u) == 0L)
		{
			return a32 - b32;
		}
		return int.MinValue;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_SUB_SAT64(long a64, long b64)
	{
		if (((a64 - b64) & long.MinValue) != 0L)
		{
			if (((a64 ^ long.MinValue) & b64 & long.MinValue) == 0L)
			{
				return a64 - b64;
			}
			return long.MaxValue;
		}
		if ((a64 & (b64 ^ long.MinValue) & long.MinValue) == 0L)
		{
			return a64 - b64;
		}
		return long.MinValue;
	}

	/// <summary>
	/// Add with saturation for positive input values
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static sbyte silk_ADD_POS_SAT8(sbyte a, sbyte b)
	{
		return (sbyte)((((a + b) & 0x80) != 0) ? 127 : (a + b));
	}

	/// <summary>
	/// Add with saturation for positive input values
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_ADD_POS_SAT16(short a, short b)
	{
		return (short)((((a + b) & 0x8000) != 0) ? 32767 : (a + b));
	}

	/// <summary>
	/// Add with saturation for positive input values
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD_POS_SAT32(int a, int b)
	{
		if (((a + b) & 0x80000000u) == 0L)
		{
			return a + b;
		}
		return int.MaxValue;
	}

	/// <summary>
	/// Add with saturation for positive input values
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_ADD_POS_SAT64(long a, long b)
	{
		if (((a + b) & long.MinValue) == 0L)
		{
			return a + b;
		}
		return long.MaxValue;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static sbyte silk_LSHIFT8(sbyte a, int shift)
	{
		return (sbyte)(a << shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_LSHIFT16(short a, int shift)
	{
		return (short)(a << shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_LSHIFT32(int a, int shift)
	{
		return a << shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_LSHIFT64(long a, int shift)
	{
		return a << shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_LSHIFT(int a, int shift)
	{
		return a << shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_LSHIFT_ovflw(int a, int shift)
	{
		return a << shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint silk_LSHIFT_uint(uint a, int shift)
	{
		return a << shift;
	}

	/// <summary>
	/// saturates before shifting
	/// </summary>
	/// <param name="a"></param>
	/// <param name="shift"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_LSHIFT_SAT32(int a, int shift)
	{
		return silk_LSHIFT32(silk_LIMIT(a, silk_RSHIFT32(int.MinValue, shift), silk_RSHIFT32(int.MaxValue, shift)), shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static sbyte silk_RSHIFT8(sbyte a, int shift)
	{
		return (sbyte)(a >> shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_RSHIFT16(short a, int shift)
	{
		return (short)(a >> shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_RSHIFT32(int a, int shift)
	{
		return a >> shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_RSHIFT(int a, int shift)
	{
		return a >> shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_RSHIFT64(long a, int shift)
	{
		return a >> shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint silk_RSHIFT_uint(uint a, int shift)
	{
		return a >> shift;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD_LSHIFT(int a, int b, int shift)
	{
		return a + (b << shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD_LSHIFT32(int a, int b, int shift)
	{
		return a + (b << shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint silk_ADD_LSHIFT_uint(uint a, uint b, int shift)
	{
		return a + (b << shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD_RSHIFT(int a, int b, int shift)
	{
		return a + (b >> shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_ADD_RSHIFT32(int a, int b, int shift)
	{
		return a + (b >> shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint silk_ADD_RSHIFT_uint(uint a, uint b, int shift)
	{
		return a + (b >> shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SUB_LSHIFT32(int a, int b, int shift)
	{
		return a - (b << shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SUB_RSHIFT32(int a, int b, int shift)
	{
		return a - (b >> shift);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_RSHIFT_ROUND(int a, int shift)
	{
		if (shift != 1)
		{
			return (a >> shift - 1) + 1 >> 1;
		}
		return (a >> 1) + (a & 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_RSHIFT_ROUND64(long a, int shift)
	{
		if (shift != 1)
		{
			return (a >> shift - 1) + 1 >> 1;
		}
		return (a >> 1) + (a & 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_min(int a, int b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_max(int a, int b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static float silk_min(float a, float b)
	{
		if (!(a < b))
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static float silk_max(float a, float b)
	{
		if (!(a > b))
		{
			return b;
		}
		return a;
	}

	/// <summary>
	/// Macro to convert floating-point constants to fixed-point by applying a scalar factor
	/// Because of limitations of the C# JIT, this macro is actually evaluated at runtime and therefore should not be used if you want to maximize performance
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int SILK_CONST(float number, int scale)
	{
		return (int)((double)(number * (float)(1L << scale)) + 0.5);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_min_int(int a, int b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_min_16(short a, short b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_min_32(int a, int b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_min_64(long a, long b)
	{
		if (a >= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_max_int(int a, int b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_max_16(short a, short b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_max_32(int a, int b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_max_64(long a, long b)
	{
		if (a <= b)
		{
			return b;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static float silk_LIMIT(float a, float limit1, float limit2)
	{
		if (!(limit1 > limit2))
		{
			if (!(a > limit2))
			{
				if (!(a < limit1))
				{
					return a;
				}
				return limit1;
			}
			return limit2;
		}
		if (!(a > limit1))
		{
			if (!(a < limit2))
			{
				return a;
			}
			return limit2;
		}
		return limit1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_LIMIT(int a, int limit1, int limit2)
	{
		return silk_LIMIT_32(a, limit1, limit2);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_LIMIT_int(int a, int limit1, int limit2)
	{
		return silk_LIMIT_32(a, limit1, limit2);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static short silk_LIMIT_16(short a, short limit1, short limit2)
	{
		if (limit1 <= limit2)
		{
			if (a <= limit2)
			{
				if (a >= limit1)
				{
					return a;
				}
				return limit1;
			}
			return limit2;
		}
		if (a <= limit1)
		{
			if (a >= limit2)
			{
				return a;
			}
			return limit2;
		}
		return limit1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_LIMIT_32(int a, int limit1, int limit2)
	{
		if (limit1 <= limit2)
		{
			if (a <= limit2)
			{
				if (a >= limit1)
				{
					return a;
				}
				return limit1;
			}
			return limit2;
		}
		if (a <= limit1)
		{
			if (a >= limit2)
			{
				return a;
			}
			return limit2;
		}
		return limit1;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_abs(int a)
	{
		if (a <= 0)
		{
			return -a;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_abs_int16(int a)
	{
		return (a ^ (a >> 15)) - (a >> 15);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_abs_int32(int a)
	{
		return (a ^ (a >> 31)) - (a >> 31);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_abs_int64(long a)
	{
		if (a <= 0)
		{
			return -a;
		}
		return a;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_sign(int a)
	{
		return (a > 0) ? 1 : ((a < 0) ? (-1) : 0);
	}

	/// <summary>
	/// PSEUDO-RANDOM GENERATOR
	/// Make sure to store the result as the seed for the next call (also in between
	/// frames), otherwise result won't be random at all. When only using some of the
	/// bits, take the most significant bits by right-shifting.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_RAND(int seed)
	{
		return silk_MLA_ovflw(907633515, seed, 196314165);
	}

	/// <summary>
	/// silk_SMMUL: Signed top word multiply.
	/// </summary>
	/// <param name="a32"></param>
	/// <param name="b32"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMMUL(int a32, int b32)
	{
		return (int)silk_RSHIFT64(silk_SMULL(a32, b32), 32);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMLAWT(int a32, int b32, int c32)
	{
		return a32 + (b32 >> 16) * (c32 >> 16) + ((b32 & 0xFFFF) * (c32 >> 16) >> 16);
	}

	/// <summary>
	/// Divide two int32 values and return result as int32 in a given Q-domain
	/// </summary>
	/// <param name="a32">I    numerator (Q0)</param>
	/// <param name="b32">I    denominator (Q0)</param>
	/// <param name="Qres">I    Q-domain of result (&gt;= 0)</param>
	/// <returns>O    returns a good approximation of "(a32 &lt;&lt; Qres) / b32"</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_DIV32_varQ(int a32, int b32, int Qres)
	{
		int num = silk_CLZ32(silk_abs(a32)) - 1;
		int num2 = silk_LSHIFT(a32, num);
		int num3 = silk_CLZ32(silk_abs(b32)) - 1;
		int num4 = silk_LSHIFT(b32, num3);
		int num5 = silk_DIV32_16(536870911, silk_RSHIFT(num4, 16));
		int num6 = silk_SMULWB(num2, num5);
		num2 = silk_SUB32_ovflw(num2, silk_LSHIFT_ovflw(silk_SMMUL(num4, num6), 3));
		num6 = silk_SMLAWB(num6, num2, num5);
		int num7 = 29 + num - num3 - Qres;
		if (num7 < 0)
		{
			return silk_LSHIFT_SAT32(num6, -num7);
		}
		if (num7 < 32)
		{
			return silk_RSHIFT(num6, num7);
		}
		return 0;
	}

	/// <summary>
	/// Invert int32 value and return result as int32 in a given Q-domain
	/// </summary>
	/// <param name="b32">I    denominator (Q0)</param>
	/// <param name="Qres">I    Q-domain of result (&gt; 0)</param>
	/// <returns>a good approximation of "(1 &lt;&lt; Qres) / b32"</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_INVERSE32_varQ(int b32, int Qres)
	{
		int num = silk_CLZ32(silk_abs(b32)) - 1;
		int num2 = silk_LSHIFT(b32, num);
		int num3 = silk_DIV32_16(536870911, (short)silk_RSHIFT(num2, 16));
		int a = silk_LSHIFT(num3, 16);
		int b33 = silk_LSHIFT(536870912 - silk_SMULWB(num2, num3), 3);
		a = silk_SMLAWW(a, b33, num3);
		int num4 = 61 - num - Qres;
		if (num4 <= 0)
		{
			return silk_LSHIFT_SAT32(a, -num4);
		}
		if (num4 < 32)
		{
			return silk_RSHIFT(a, num4);
		}
		return 0;
	}

	/// <summary>
	/// a32 + (b32 * (int)((short)(c32))) &gt;&gt; 16 output have to be 32bit int
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMLAWB(int a32, int b32, int c32)
	{
		return a32 + silk_SMULWB(b32, c32);
	}

	/// * (a32 * (b32 &gt;&gt; 16)) &gt;&gt; 16 */
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMULWT(int a32, int b32)
	{
		return (a32 >> 16) * (b32 >> 16) + ((a32 & 0xFFFF) * (b32 >> 16) >> 16);
	}

	/// * (int)((short)(a32)) * (b32 &gt;&gt; 16) */
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMULBT(int a32, int b32)
	{
		return (short)a32 * (b32 >> 16);
	}

	/// * a32 + (int)((short)(b32)) * (c32 &gt;&gt; 16) */
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMLABT(int a32, int b32, int c32)
	{
		return a32 + (short)b32 * (c32 >> 16);
	}

	/// * a64 + (b32 * c32) */
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_SMLAL(long a64, int b32, int c32)
	{
		return silk_ADD64(a64, (long)b32 * (long)c32);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void MatrixSet<T>(T[] Matrix_base_adr, int Matrix_ptr, int row, int column, int N, T value)
	{
		Matrix_base_adr[Matrix_ptr + row * N + column] = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MatrixGetPointer(int row, int column, int N)
	{
		return row * N + column;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static T MatrixGet<T>(T[] Matrix_base_adr, int row, int column, int N)
	{
		return Matrix_base_adr[row * N + column];
	}

	internal static T MatrixGet<T>(T[] Matrix_base_adr, int matrix_ptr, int row, int column, int N)
	{
		return Matrix_base_adr[matrix_ptr + row * N + column];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void MatrixSet<T>(T[] Matrix_base_adr, int row, int column, int N, T value)
	{
		Matrix_base_adr[row * N + column] = value;
	}

	/// <summary>
	/// (a32 * b32) &gt;&gt; 16
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMULWW(int a32, int b32)
	{
		return silk_MLA(silk_SMULWB(a32, b32), a32, silk_RSHIFT_ROUND(b32, 16));
	}

	/// <summary>
	/// a32 + ((b32 * c32) &gt;&gt; 16)
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SMLAWW(int a32, int b32, int c32)
	{
		return silk_MLA(silk_SMLAWB(a32, b32, c32), b32, silk_RSHIFT_ROUND(c32, 16));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_CLZ64(long input)
	{
		int num = (int)silk_RSHIFT64(input, 32);
		if (num == 0)
		{
			return 32 + silk_CLZ32((int)input);
		}
		return silk_CLZ32(num);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_CLZ32(int in32)
	{
		if (in32 != 0)
		{
			return 32 - EC_ILOG((uint)in32);
		}
		return 32;
	}

	/// <summary>
	/// Get number of leading zeros and fractional part (the bits right after the leading one)
	/// </summary>
	/// <param name="input">input</param>
	/// <param name="lz">number of leading zeros</param>
	/// <param name="frac_Q7">the 7 bits right after the leading one</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void silk_CLZ_FRAC(int input, out int lz, out int frac_Q7)
	{
		frac_Q7 = silk_ROR32(input, 24 - (lz = silk_CLZ32(input))) & 0x7F;
	}

	/// <summary>
	/// Approximation of square root.
	/// Accuracy: +/- 10%  for output values &gt; 15
	///           +/- 2.5% for output values &gt; 120
	/// </summary>
	/// <param name="x"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_SQRT_APPROX(int x)
	{
		if (x <= 0)
		{
			return 0;
		}
		silk_CLZ_FRAC(x, out var lz, out var frac_Q);
		int num = (((lz & 1) == 0) ? 46214 : 32768);
		num >>= silk_RSHIFT(lz, 1);
		return silk_SMLAWB(num, num, silk_SMULBB(213, frac_Q));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int MUL32_FRAC_Q(int a32, int b32, int Q)
	{
		return (int)silk_RSHIFT_ROUND64(silk_SMULL(a32, b32), Q);
	}

	/// <summary>
	/// Approximation of 128 * log2() (very close inverse of silk_log2lin())
	/// Convert input to a log scale
	/// </summary>
	/// <param name="inLin">(I) input in linear scale</param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_lin2log(int inLin)
	{
		silk_CLZ_FRAC(inLin, out var lz, out var frac_Q);
		return silk_LSHIFT(31 - lz, 7) + silk_SMLAWB(frac_Q, silk_MUL(frac_Q, 128 - frac_Q), 179);
	}

	/// <summary>
	/// Approximation of 2^() (very close inverse of silk_lin2log())
	/// Convert input to a linear scale
	/// </summary>
	/// <param name="inLog_Q7">input on log scale</param>
	/// <returns>Linearized value</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_log2lin(int inLog_Q7)
	{
		if (inLog_Q7 < 0)
		{
			return 0;
		}
		if (inLog_Q7 >= 3967)
		{
			return int.MaxValue;
		}
		int num = silk_LSHIFT(1, silk_RSHIFT(inLog_Q7, 7));
		int num2 = inLog_Q7 & 0x7F;
		if (inLog_Q7 < 2048)
		{
			return silk_ADD_RSHIFT32(num, silk_MUL(num, silk_SMLAWB(num2, silk_SMULBB(num2, 128 - num2), -174)), 7);
		}
		return silk_MLA(num, silk_RSHIFT(num, 7), silk_SMLAWB(num2, silk_SMULBB(num2, 128 - num2), -174));
	}

	/// <summary>
	/// Interpolate two vectors
	/// </summary>
	/// <param name="xi">(O) interpolated vector [MAX_LPC_ORDER]</param>
	/// <param name="x0">(I) first vector [MAX_LPC_ORDER]</param>
	/// <param name="x1">(I) second vector [MAX_LPC_ORDER]</param>
	/// <param name="ifact_Q2">(I) interp. factor, weight on 2nd vector</param>
	/// <param name="d">(I) number of parameters</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void silk_interpolate(short[] xi, short[] x0, short[] x1, int ifact_Q2, int d)
	{
		for (int i = 0; i < d; i++)
		{
			xi[i] = (short)silk_ADD_RSHIFT(x0[i], silk_SMULBB(x1[i] - x0[i], ifact_Q2), 2);
		}
	}

	/// <summary>
	/// Inner product with bit-shift
	/// </summary>
	/// <param name="inVec1">I input vector 1</param>
	/// <param name="inVec2">I input vector 2</param>
	/// <param name="scale">I number of bits to shift</param>
	/// <param name="len">I vector lengths</param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_inner_prod_aligned_scale(short[] inVec1, short[] inVec2, int scale, int len)
	{
		int num = 0;
		for (int i = 0; i < len; i++)
		{
			num = silk_ADD_RSHIFT32(num, silk_SMULBB(inVec1[i], inVec2[i]), scale);
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void silk_scale_copy_vector16(Span<short> data_out, int data_out_ptr, Span<short> data_in, int data_in_ptr, int gain_Q16, int dataSize)
	{
		for (int i = 0; i < dataSize; i++)
		{
			data_out[data_out_ptr + i] = (short)silk_SMULWB(gain_Q16, data_in[data_in_ptr + i]);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void silk_scale_vector32_Q26_lshift_18(int[] data1, int data1_ptr, int gain_Q26, int dataSize)
	{
		for (int i = data1_ptr; i < data1_ptr + dataSize; i++)
		{
			data1[i] = (int)silk_RSHIFT64(silk_SMULL(data1[i], gain_Q26), 8);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_inner_prod(short[] inVec1, int inVec1_ptr, short[] inVec2, int inVec2_ptr, int len)
	{
		int num = 0;
		for (int i = 0; i < len; i++)
		{
			num = MAC16_16(num, inVec1[inVec1_ptr + i], inVec2[inVec2_ptr + i]);
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int silk_inner_prod_self(short[] inVec, int inVec_ptr, int len)
	{
		int num = 0;
		for (int i = inVec_ptr; i < inVec_ptr + len; i++)
		{
			num = MAC16_16(num, inVec[i], inVec[i]);
		}
		return num;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static long silk_inner_prod16_aligned_64(short[] inVec1, int inVec1_ptr, short[] inVec2, int inVec2_ptr, int len)
	{
		long num = 0L;
		for (int i = 0; i < len; i++)
		{
			num = silk_SMLALBB(num, inVec1[inVec1_ptr + i], inVec2[inVec2_ptr + i]);
		}
		return num;
	}

	/// <summary>
	/// returns the value that has fewer higher-order bits, ignoring sign bit (? I think?)
	/// </summary>
	/// <param name="a"></param>
	/// <param name="b"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static uint EC_MINI(uint a, uint b)
	{
		return a + ((b - a) & (uint)((b < a) ? (-1) : 0));
	}

	/// <summary>
	/// Counts leading zeroes
	/// </summary>
	/// <param name="x"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int EC_CLZ(uint x)
	{
		if (x == 0)
		{
			return 0;
		}
		x |= x >> 1;
		x |= x >> 2;
		x |= x >> 4;
		x |= x >> 8;
		x |= x >> 16;
		uint num = x - ((x >> 1) & 0x55555555);
		num = ((num >> 2) & 0x33333333) + (num & 0x33333333);
		num = ((num >> 4) + num) & 0xF0F0F0F;
		num += num >> 8;
		num += num >> 16;
		num &= 0x3F;
		return (int)(1 - num);
	}

	/// <summary>
	/// returns inverse base-2 log of a value
	/// </summary>
	/// <param name="x"></param>
	/// <returns></returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int EC_ILOG(uint x)
	{
		if (x == 0)
		{
			return 1;
		}
		x |= x >> 1;
		x |= x >> 2;
		x |= x >> 4;
		x |= x >> 8;
		x |= x >> 16;
		uint num = x - ((x >> 1) & 0x55555555);
		num = ((num >> 2) & 0x33333333) + (num & 0x33333333);
		num = ((num >> 4) + num) & 0xF0F0F0F;
		num += num >> 8;
		num += num >> 16;
		return (int)(num & 0x3F);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int abs(int a)
	{
		if (a < 0)
		{
			return -a;
		}
		return a;
	}
}
