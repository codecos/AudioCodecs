using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

/// <summary>
/// Decoder control
/// </summary>
internal class SilkDecoderControl
{
	internal readonly int[] pitchL = new int[4];

	internal readonly int[] Gains_Q16 = new int[4];

	internal readonly short[][] PredCoef_Q12 = Arrays.InitTwoDimensionalArray<short>(2, 16);

	internal readonly short[] LTPCoef_Q14 = new short[20];

	internal int LTP_scale_Q14;

	internal void Reset()
	{
		Arrays.MemSetInt(pitchL, 0, 4);
		Arrays.MemSetInt(Gains_Q16, 0, 4);
		Arrays.MemSetShort(PredCoef_Q12[0], 0, 16);
		Arrays.MemSetShort(PredCoef_Q12[1], 0, 16);
		Arrays.MemSetShort(LTPCoef_Q14, 0, 20);
		LTP_scale_Q14 = 0;
	}
}
