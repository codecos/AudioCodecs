using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

/// <summary>
/// Struct for Packet Loss Concealment
/// </summary>
internal class PLCStruct
{
	internal int pitchL_Q8;

	internal readonly short[] LTPCoef_Q14 = new short[5];

	internal readonly short[] prevLPC_Q12 = new short[16];

	internal int last_frame_lost;

	internal int rand_seed;

	internal short randScale_Q14;

	internal int conc_energy;

	internal int conc_energy_shift;

	internal short prevLTP_scale_Q14;

	internal readonly int[] prevGain_Q16 = new int[2];

	internal int fs_kHz;

	internal int nb_subfr;

	internal int subfr_length;

	internal void Reset()
	{
		pitchL_Q8 = 0;
		Arrays.MemSetShort(LTPCoef_Q14, 0, 5);
		Arrays.MemSetShort(prevLPC_Q12, 0, 16);
		last_frame_lost = 0;
		rand_seed = 0;
		randScale_Q14 = 0;
		conc_energy = 0;
		conc_energy_shift = 0;
		prevLTP_scale_Q14 = 0;
		Arrays.MemSetInt(prevGain_Q16, 0, 2);
		fs_kHz = 0;
		nb_subfr = 0;
		subfr_length = 0;
	}
}
