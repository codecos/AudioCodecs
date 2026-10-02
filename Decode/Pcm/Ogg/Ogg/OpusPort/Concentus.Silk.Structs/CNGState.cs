using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

/// <summary>
/// Struct for CNG
/// </summary>
internal class CNGState
{
	internal readonly int[] CNG_exc_buf_Q14 = new int[320];

	internal readonly short[] CNG_smth_NLSF_Q15 = new short[16];

	internal readonly int[] CNG_synth_state = new int[16];

	internal int CNG_smth_Gain_Q16;

	internal int rand_seed;

	internal int fs_kHz;

	internal void Reset()
	{
		Arrays.MemSetInt(CNG_exc_buf_Q14, 0, 320);
		Arrays.MemSetShort(CNG_smth_NLSF_Q15, 0, 16);
		Arrays.MemSetInt(CNG_synth_state, 0, 16);
		CNG_smth_Gain_Q16 = 0;
		rand_seed = 0;
		fs_kHz = 0;
	}
}
