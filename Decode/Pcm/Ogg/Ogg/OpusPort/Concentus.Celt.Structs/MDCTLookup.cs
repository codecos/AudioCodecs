namespace Concentus.Celt.Structs;

internal class MDCTLookup
{
	internal int n;

	internal int maxshift;

	internal FFTState[] kfft = new FFTState[4];

	internal short[] trig;

	internal MDCTLookup()
	{
	}
}
