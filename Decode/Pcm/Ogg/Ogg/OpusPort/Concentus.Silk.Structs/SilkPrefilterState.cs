using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

/// <summary>
/// Prefilter state
/// </summary>
internal class SilkPrefilterState
{
	internal readonly short[] sLTP_shp = new short[512];

	internal readonly int[] sAR_shp = new int[17];

	internal int sLTP_shp_buf_idx;

	internal int sLF_AR_shp_Q12;

	internal int sLF_MA_shp_Q12;

	internal int sHarmHP_Q2;

	internal int rand_seed;

	internal int lagPrev;

	internal SilkPrefilterState()
	{
	}

	internal void Reset()
	{
		Arrays.MemSetShort(sLTP_shp, 0, 512);
		Arrays.MemSetInt(sAR_shp, 0, 17);
		sLTP_shp_buf_idx = 0;
		sLF_AR_shp_Q12 = 0;
		sLF_MA_shp_Q12 = 0;
		sHarmHP_Q2 = 0;
		rand_seed = 0;
		lagPrev = 0;
	}
}
