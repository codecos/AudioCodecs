using Concentus.Common;

namespace Concentus.Silk;

internal static class BWExpander
{
	/// <summary>
	/// Chirp (bw expand) LP AR filter (Fixed point implementation)
	/// </summary>
	/// <param name="ar">I/O  AR filter to be expanded (without leading 1)</param>
	/// <param name="d">I length of ar</param>
	/// <param name="chirp_Q16">I    chirp factor (typically in range (0..1) )</param>
	internal static void silk_bwexpander_32(int[] ar, int d, int chirp_Q16)
	{
		int b = chirp_Q16 - 65536;
		for (int i = 0; i < d - 1; i++)
		{
			ar[i] = Inlines.silk_SMULWW(chirp_Q16, ar[i]);
			chirp_Q16 += Inlines.silk_RSHIFT_ROUND(Inlines.silk_MUL(chirp_Q16, b), 16);
		}
		ar[d - 1] = Inlines.silk_SMULWW(chirp_Q16, ar[d - 1]);
	}

	/// <summary>
	/// Chirp (bw expand) LP AR filter (Fixed point implementation)
	/// </summary>
	/// <param name="ar">I/O  AR filter to be expanded (without leading 1)</param>
	/// <param name="d">I length of ar</param>
	/// <param name="chirp_Q16">I    chirp factor (typically in range (0..1) )</param>
	internal static void silk_bwexpander(short[] ar, int d, int chirp_Q16)
	{
		int b = chirp_Q16 - 65536;
		for (int i = 0; i < d - 1; i++)
		{
			ar[i] = (short)Inlines.silk_RSHIFT_ROUND(Inlines.silk_MUL(chirp_Q16, ar[i]), 16);
			chirp_Q16 += Inlines.silk_RSHIFT_ROUND(Inlines.silk_MUL(chirp_Q16, b), 16);
		}
		ar[d - 1] = (short)Inlines.silk_RSHIFT_ROUND(Inlines.silk_MUL(chirp_Q16, ar[d - 1]), 16);
	}
}
