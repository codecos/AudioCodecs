using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

/// <summary>
/// VAD state
/// </summary>
internal class SilkVADState
{
	/// <summary>
	/// Analysis filterbank state: 0-8 kHz
	/// </summary>
	internal readonly int[] AnaState = new int[2];

	/// <summary>
	/// Analysis filterbank state: 0-4 kHz
	/// </summary>
	internal readonly int[] AnaState1 = new int[2];

	/// <summary>
	/// Analysis filterbank state: 0-2 kHz
	/// </summary>
	internal readonly int[] AnaState2 = new int[2];

	/// <summary>
	/// Subframe energies
	/// </summary>
	internal readonly int[] XnrgSubfr = new int[4];

	/// <summary>
	/// Smoothed energy level in each band
	/// </summary>
	internal readonly int[] NrgRatioSmth_Q8 = new int[4];

	/// <summary>
	/// State of differentiator in the lowest band
	/// </summary>
	internal short HPstate;

	/// <summary>
	/// Noise energy level in each band
	/// </summary>
	internal readonly int[] NL = new int[4];

	/// <summary>
	/// Inverse noise energy level in each band
	/// </summary>
	internal readonly int[] inv_NL = new int[4];

	/// <summary>
	/// Noise level estimator bias/offset
	/// </summary>
	internal readonly int[] NoiseLevelBias = new int[4];

	/// <summary>
	/// Frame counter used in the initial phase
	/// </summary>
	internal int counter;

	internal void Reset()
	{
		Arrays.MemSetInt(AnaState, 0, 2);
		Arrays.MemSetInt(AnaState1, 0, 2);
		Arrays.MemSetInt(AnaState2, 0, 2);
		Arrays.MemSetInt(XnrgSubfr, 0, 4);
		Arrays.MemSetInt(NrgRatioSmth_Q8, 0, 4);
		HPstate = 0;
		Arrays.MemSetInt(NL, 0, 4);
		Arrays.MemSetInt(inv_NL, 0, 4);
		Arrays.MemSetInt(NoiseLevelBias, 0, 4);
		counter = 0;
	}
}
