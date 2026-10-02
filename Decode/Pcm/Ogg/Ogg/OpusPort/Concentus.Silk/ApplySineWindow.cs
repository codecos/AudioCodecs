using Concentus.Common;

namespace Concentus.Silk;

internal static class ApplySineWindow
{
	private static readonly short[] freq_table_Q16 = new short[27]
	{
		12111, 9804, 8235, 7100, 6239, 5565, 5022, 4575, 4202, 3885,
		3612, 3375, 3167, 2984, 2820, 2674, 2542, 2422, 2313, 2214,
		2123, 2038, 1961, 1889, 1822, 1760, 1702
	};

	internal static void silk_apply_sine_window(short[] px_win, int px_win_ptr, short[] px, int px_ptr, int win_type, int length)
	{
		int num = (length >> 2) - 4;
		int num2 = freq_table_Q16[num];
		int num3 = Inlines.silk_SMULWB(num2, -num2);
		int num4;
		int num5;
		if (win_type == 1)
		{
			num4 = 0;
			num5 = num2 + Inlines.silk_RSHIFT(length, 3);
		}
		else
		{
			num4 = 65536;
			num5 = 65536 + Inlines.silk_RSHIFT(num3, 1) + Inlines.silk_RSHIFT(length, 4);
		}
		for (num = 0; num < length; num += 4)
		{
			int num6 = px_win_ptr + num;
			int num7 = px_ptr + num;
			px_win[num6] = (short)Inlines.silk_SMULWB(Inlines.silk_RSHIFT(num4 + num5, 1), px[num7]);
			px_win[num6 + 1] = (short)Inlines.silk_SMULWB(num5, px[num7 + 1]);
			num4 = Inlines.silk_SMULWB(num5, num3) + Inlines.silk_LSHIFT(num5, 1) - num4 + 1;
			num4 = Inlines.silk_min(num4, 65536);
			px_win[num6 + 2] = (short)Inlines.silk_SMULWB(Inlines.silk_RSHIFT(num4 + num5, 1), px[num7 + 2]);
			px_win[num6 + 3] = (short)Inlines.silk_SMULWB(num4, px[num7 + 3]);
			num5 = Inlines.silk_SMULWB(num4, num3) + Inlines.silk_LSHIFT(num4, 1) - num5;
			num5 = Inlines.silk_min(num5, 65536);
		}
	}
}
