using System;
using Concentus.Common;
using Concentus.Silk.Structs;

namespace Concentus.Silk;

/// <summary>
/// Voice Activity Detection module for silk codec
/// </summary>
internal static class VoiceActivityDetection
{
	/// <summary>
	/// Weighting factors for tilt measure
	/// </summary>
	private static readonly int[] tiltWeights = new int[4] { 30000, 6000, -12000, -12000 };

	/// <summary>
	/// Initialization of the Silk VAD
	/// </summary>
	/// <param name="psSilk_VAD">O  Pointer to Silk VAD state. Cannot be nullptr</param>
	/// <returns>0 if success</returns>
	internal static int silk_VAD_Init(SilkVADState psSilk_VAD)
	{
		int result = 0;
		psSilk_VAD.Reset();
		for (int i = 0; i < 4; i++)
		{
			psSilk_VAD.NoiseLevelBias[i] = Inlines.silk_max_32(Inlines.silk_DIV32_16(50, (short)(i + 1)), 1);
		}
		for (int i = 0; i < 4; i++)
		{
			psSilk_VAD.NL[i] = Inlines.silk_MUL(100, psSilk_VAD.NoiseLevelBias[i]);
			psSilk_VAD.inv_NL[i] = Inlines.silk_DIV32(int.MaxValue, psSilk_VAD.NL[i]);
		}
		psSilk_VAD.counter = 15;
		for (int i = 0; i < 4; i++)
		{
			psSilk_VAD.NrgRatioSmth_Q8[i] = 25600;
		}
		return result;
	}



	/// <summary>
	/// Noise level estimation
	/// </summary>
	/// <param name="pX">I    subband energies [VAD_N_BANDS]</param>
	/// <param name="psSilk_VAD">I/O  Pointer to Silk VAD state</param>
	internal static void silk_VAD_GetNoiseLevels(int[] pX, SilkVADState psSilk_VAD)
	{
		int b = ((psSilk_VAD.counter < 1000) ? Inlines.silk_DIV32_16(32767, (short)(Inlines.silk_RSHIFT(psSilk_VAD.counter, 4) + 1)) : 0);
		for (int i = 0; i < 4; i++)
		{
			int num = psSilk_VAD.NL[i];
			int num2 = Inlines.silk_ADD_POS_SAT32(pX[i], psSilk_VAD.NoiseLevelBias[i]);
			int num3 = Inlines.silk_DIV32(int.MaxValue, num2);
			int a = ((num2 <= Inlines.silk_LSHIFT(num, 3)) ? ((num2 >= num) ? Inlines.silk_SMULWB(Inlines.silk_SMULWW(num3, num), 2048) : 1024) : 128);
			a = Inlines.silk_max_int(a, b);
			psSilk_VAD.inv_NL[i] = Inlines.silk_SMLAWB(psSilk_VAD.inv_NL[i], num3 - psSilk_VAD.inv_NL[i], a);
			num = Inlines.silk_DIV32(int.MaxValue, psSilk_VAD.inv_NL[i]);
			num = Inlines.silk_min(num, 16777215);
			psSilk_VAD.NL[i] = num;
		}
		psSilk_VAD.counter++;
	}
}
