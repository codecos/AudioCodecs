using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

internal class StereoDecodeState
{
	internal readonly short[] pred_prev_Q13 = new short[2];

	internal readonly short[] sMid = new short[2];

	internal readonly short[] sSide = new short[2];

	internal void Reset()
	{
		Arrays.MemSetShort(pred_prev_Q13, 0, 2);
		Arrays.MemSetShort(sMid, 0, 2);
		Arrays.MemSetShort(sSide, 0, 2);
	}
}
