namespace Concentus.Celt.Structs;

internal class CeltMode
{
	internal int Fs;

	internal int overlap;

	internal int nbEBands;

	internal int effEBands;

	internal int[] preemph = new int[4];

	/// <summary>
	/// Definition for each "pseudo-critical band"
	/// </summary>
	internal short[] eBands;

	internal int maxLM;

	internal int nbShortMdcts;

	internal int shortMdctSize;

	/// <summary>
	/// Number of lines in allocVectors
	/// </summary>
	internal int nbAllocVectors;

	/// <summary>
	/// Number of bits in each band for several rates
	/// </summary>
	internal byte[] allocVectors;

	internal short[] logN;

	internal int[] window;

	internal MDCTLookup mdct = new MDCTLookup();

	internal PulseCache cache = new PulseCache();

	internal static readonly CeltMode mode48000_960_120 = new CeltMode
	{
		Fs = 48000,
		overlap = 120,
		nbEBands = 21,
		effEBands = 21,
		preemph = new int[4] { 27853, 0, 4096, 8192 },
		eBands = Tables.eband5ms,
		maxLM = 3,
		nbShortMdcts = 8,
		shortMdctSize = 120,
		nbAllocVectors = 11,
		allocVectors = Tables.band_allocation,
		logN = Tables.logN400,
		window = Tables.window120,
		mdct = new MDCTLookup
		{
			n = 1920,
			maxshift = 3,
			kfft = new FFTState[4]
			{
				Tables.fft_state48000_960_0,
				Tables.fft_state48000_960_1,
				Tables.fft_state48000_960_2,
				Tables.fft_state48000_960_3
			},
			trig = Tables.mdct_twiddles960
		},
		cache = new PulseCache
		{
			size = 392,
			index = Tables.cache_index50,
			bits = Tables.cache_bits50,
			caps = Tables.cache_caps50
		}
	};

	private CeltMode()
	{
	}
}
