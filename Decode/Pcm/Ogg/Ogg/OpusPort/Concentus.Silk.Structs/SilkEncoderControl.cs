using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

internal class SilkEncoderControl
{
	internal readonly int[] Gains_Q16 = new int[4];

	internal readonly short[][] PredCoef_Q12 = Arrays.InitTwoDimensionalArray<short>(2, 16);

	internal readonly short[] LTPCoef_Q14 = new short[20];

	internal int LTP_scale_Q14;

	internal readonly int[] pitchL = new int[4];

	internal readonly short[] AR1_Q13 = new short[64];

	internal readonly short[] AR2_Q13 = new short[64];

	internal readonly int[] LF_shp_Q14 = new int[4];

	internal readonly int[] GainsPre_Q14 = new int[4];

	internal readonly int[] HarmBoost_Q14 = new int[4];

	internal readonly int[] Tilt_Q14 = new int[4];

	internal readonly int[] HarmShapeGain_Q14 = new int[4];

	internal int Lambda_Q10;

	internal int input_quality_Q14;

	internal int coding_quality_Q14;

	internal int sparseness_Q8;

	internal int predGain_Q16;

	internal int LTPredCodGain_Q7;

	internal readonly int[] ResNrg = new int[4];

	internal readonly int[] ResNrgQ = new int[4];

	internal readonly int[] GainsUnq_Q16 = new int[4];

	internal sbyte lastGainIndexPrev;

	internal void Reset()
	{
		Arrays.MemSetInt(Gains_Q16, 0, 4);
		Arrays.MemSetShort(PredCoef_Q12[0], 0, 16);
		Arrays.MemSetShort(PredCoef_Q12[1], 0, 16);
		Arrays.MemSetShort(LTPCoef_Q14, 0, 20);
		LTP_scale_Q14 = 0;
		Arrays.MemSetInt(pitchL, 0, 4);
		Arrays.MemSetShort(AR1_Q13, 0, 64);
		Arrays.MemSetShort(AR2_Q13, 0, 64);
		Arrays.MemSetInt(LF_shp_Q14, 0, 4);
		Arrays.MemSetInt(GainsPre_Q14, 0, 4);
		Arrays.MemSetInt(HarmBoost_Q14, 0, 4);
		Arrays.MemSetInt(Tilt_Q14, 0, 4);
		Arrays.MemSetInt(HarmShapeGain_Q14, 0, 4);
		Lambda_Q10 = 0;
		input_quality_Q14 = 0;
		coding_quality_Q14 = 0;
		sparseness_Q8 = 0;
		predGain_Q16 = 0;
		LTPredCodGain_Q7 = 0;
		Arrays.MemSetInt(ResNrg, 0, 4);
		Arrays.MemSetInt(ResNrgQ, 0, 4);
		Arrays.MemSetInt(GainsUnq_Q16, 0, 4);
		lastGainIndexPrev = 0;
	}
}
