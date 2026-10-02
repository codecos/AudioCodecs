namespace Concentus.Celt.Structs;

internal class FFTState
{
	internal int nfft;

	internal short scale;

	internal int scale_shift;

	internal int shift;

	internal short[] factors = new short[16];

	internal short[] bitrev;

	internal short[] twiddles;
}
