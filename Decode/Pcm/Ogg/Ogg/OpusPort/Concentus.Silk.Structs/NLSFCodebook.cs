namespace Concentus.Silk.Structs;

/// <summary>
/// Structure containing NLSF codebook
/// </summary>
internal class NLSFCodebook
{
	internal short nVectors;

	internal short order;

	/// <summary>
	/// Quantization step size
	/// </summary>
	internal short quantStepSize_Q16;

	/// <summary>
	/// Inverse quantization step size
	/// </summary>
	internal short invQuantStepSize_Q6;

	/// <summary>
	/// POINTER
	/// </summary>
	internal byte[] CB1_NLSF_Q8;

	/// <summary>
	/// POINTER
	/// </summary>
	internal byte[] CB1_iCDF;

	/// <summary>
	/// POINTER to Backward predictor coefs [ order ]
	/// </summary>
	internal byte[] pred_Q8;

	/// <summary>
	/// POINTER to Indices to entropy coding tables [ order ]
	/// </summary>
	internal byte[] ec_sel;

	/// <summary>
	/// POINTER
	/// </summary>
	internal byte[] ec_iCDF;

	/// <summary>
	/// POINTER
	/// </summary>
	internal byte[] ec_Rates_Q5;

	/// <summary>
	/// POINTER
	/// </summary>
	internal short[] deltaMin_Q15;

	internal void Reset()
	{
		nVectors = 0;
		order = 0;
		quantStepSize_Q16 = 0;
		invQuantStepSize_Q6 = 0;
		CB1_NLSF_Q8 = null;
		CB1_iCDF = null;
		pred_Q8 = null;
		ec_sel = null;
		ec_iCDF = null;
		ec_Rates_Q5 = null;
		deltaMin_Q15 = null;
	}
}
