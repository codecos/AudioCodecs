using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

internal class SilkResamplerState
{
	internal readonly int[] sIIR = new int[6];

	internal readonly int[] sFIR_i32 = new int[36];

	internal readonly short[] sFIR_i16 = new short[36];

	internal readonly short[] delayBuf = new short[48];
	internal readonly int[] downFirWork = new int[520];
	internal readonly short[] iirFirWork = new short[968];

	internal int resampler_function;

	internal int batchSize;

	internal int invRatio_Q16;

	internal int FIR_Order;

	internal int FIR_Fracs;

	internal int Fs_in_kHz;

	internal int Fs_out_kHz;

	internal int inputDelay;

	/// <summary>
	/// POINTER
	/// </summary>
	internal short[] Coefs;

	internal void Reset()
	{
		Arrays.MemSetInt(sIIR, 0, 6);
		Arrays.MemSetInt(sFIR_i32, 0, 36);
		Arrays.MemSetShort(sFIR_i16, 0, 36);
		Arrays.MemSetShort(delayBuf, 0, 48);
		resampler_function = 0;
		batchSize = 0;
		invRatio_Q16 = 0;
		FIR_Order = 0;
		FIR_Fracs = 0;
		Fs_in_kHz = 0;
		Fs_out_kHz = 0;
		inputDelay = 0;
		Coefs = null;
	}

	internal void Assign(SilkResamplerState other)
	{
		resampler_function = other.resampler_function;
		batchSize = other.batchSize;
		invRatio_Q16 = other.invRatio_Q16;
		FIR_Order = other.FIR_Order;
		FIR_Fracs = other.FIR_Fracs;
		Fs_in_kHz = other.Fs_in_kHz;
		Fs_out_kHz = other.Fs_out_kHz;
		inputDelay = other.inputDelay;
		Coefs = other.Coefs;
		Arrays.MemCopy(other.sIIR, 0, sIIR, 0, 6);
		Arrays.MemCopy(other.sFIR_i32, 0, sFIR_i32, 0, 36);
		Arrays.MemCopy(other.sFIR_i16, 0, sFIR_i16, 0, 36);
		Arrays.MemCopy(other.delayBuf, 0, delayBuf, 0, 48);
	}
}
