using Concentus.Common;

namespace Concentus.Silk;

internal static class RegularizeCorrelations
{
	internal static void silk_regularize_correlations(int[] XX, int XX_ptr, int[] xx, int xx_ptr, int noise, int D)
	{
		for (int i = 0; i < D; i++)
		{
			Inlines.MatrixSet(XX, XX_ptr, i, i, D, Inlines.silk_ADD32(Inlines.MatrixGet(XX, XX_ptr, i, i, D), noise));
		}
		xx[xx_ptr] += noise;
	}
}
