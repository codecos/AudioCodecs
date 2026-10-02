using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

internal class StereoEncodeState
{
	internal readonly short[] pred_prev_Q13 = new short[2];

	internal readonly short[] sMid = new short[2];

	internal readonly short[] sSide = new short[2];

	internal readonly int[] mid_side_amp_Q0 = new int[4];

	internal short smth_width_Q14;

	internal short width_prev_Q14;

	internal short silent_side_len;

	internal readonly sbyte[][][] predIx = Arrays.InitThreeDimensionalArray<sbyte>(3, 2, 3);

	internal readonly sbyte[] mid_only_flags = new sbyte[3];

	internal void Reset()
	{
		Arrays.MemSetShort(pred_prev_Q13, 0, 2);
		Arrays.MemSetShort(sMid, 0, 2);
		Arrays.MemSetShort(sSide, 0, 2);
		Arrays.MemSetInt(mid_side_amp_Q0, 0, 4);
		smth_width_Q14 = 0;
		width_prev_Q14 = 0;
		silent_side_len = 0;
		for (int i = 0; i < 3; i++)
		{
			for (int j = 0; j < 2; j++)
			{
				Arrays.MemSetSbyte(predIx[i][j], 0, 3);
			}
		}
		Arrays.MemSetSbyte(mid_only_flags, 0, 3);
	}
}
