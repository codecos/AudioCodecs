namespace Concentus.Silk.Structs;

/// <summary>
/// Noise shaping analysis state
/// </summary>
internal class SilkShapeState
{
	internal sbyte LastGainIndex;

	internal int HarmBoost_smth_Q16;

	internal int HarmShapeGain_smth_Q16;

	internal int Tilt_smth_Q16;

	internal void Reset()
	{
		LastGainIndex = 0;
		HarmBoost_smth_Q16 = 0;
		HarmShapeGain_smth_Q16 = 0;
		Tilt_smth_Q16 = 0;
	}
}
