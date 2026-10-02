using Concentus.Common;

namespace Concentus.Silk;

internal static class DecodePitch
{
	internal static void silk_decode_pitch(short lagIndex, sbyte contourIndex, int[] pitch_lags, int Fs_kHz, int nb_subfr)
	{
		sbyte[][] array = ((Fs_kHz == 8) ? ((nb_subfr != 4) ? Tables.silk_CB_lags_stage2_10_ms : Tables.silk_CB_lags_stage2) : ((nb_subfr != 4) ? Tables.silk_CB_lags_stage3_10_ms : Tables.silk_CB_lags_stage3));
		int num = Inlines.silk_SMULBB(2, Fs_kHz);
		int limit = Inlines.silk_SMULBB(18, Fs_kHz);
		int num2 = num + lagIndex;
		for (int i = 0; i < nb_subfr; i++)
		{
			pitch_lags[i] = num2 + array[i][contourIndex];
			pitch_lags[i] = Inlines.silk_LIMIT(pitch_lags[i], num, limit);
		}
	}
}
