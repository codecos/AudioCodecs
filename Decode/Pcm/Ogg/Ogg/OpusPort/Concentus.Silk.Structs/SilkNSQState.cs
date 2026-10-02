using System;
using Concentus.Common;
using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

/// <summary>
/// Noise shaping quantization state
/// </summary>
internal class SilkNSQState
{
	private class NSQ_del_dec_struct
	{
		internal readonly int[] sLPC_Q14 = new int[80 + SilkConstants.NSQ_LPC_BUF_LENGTH];

		internal readonly int[] RandState = new int[32];

		internal readonly int[] Q_Q10 = new int[32];

		internal readonly int[] Xq_Q14 = new int[32];

		internal readonly int[] Pred_Q15 = new int[32];

		internal readonly int[] Shape_Q14 = new int[32];

		internal int[] sAR2_Q14;

		internal int LF_AR_Q14;

		internal int Seed;

		internal int SeedInit;

		internal int RD_Q10;

		internal NSQ_del_dec_struct(int shapingOrder)
		{
			sAR2_Q14 = new int[shapingOrder];
		}

		internal void PartialCopyFrom(NSQ_del_dec_struct other, int q14Offset)
		{
			Buffer.BlockCopy(other.sLPC_Q14, q14Offset * 4, sLPC_Q14, q14Offset * 4, (80 + SilkConstants.NSQ_LPC_BUF_LENGTH - q14Offset) * 4);
			Buffer.BlockCopy(other.RandState, 0, RandState, 0, 128);
			Buffer.BlockCopy(other.Q_Q10, 0, Q_Q10, 0, 128);
			Buffer.BlockCopy(other.Xq_Q14, 0, Xq_Q14, 0, 128);
			Buffer.BlockCopy(other.Pred_Q15, 0, Pred_Q15, 0, 128);
			Buffer.BlockCopy(other.Shape_Q14, 0, Shape_Q14, 0, 128);
			Buffer.BlockCopy(other.sAR2_Q14, 0, sAR2_Q14, 0, sAR2_Q14.Length * 4);
			LF_AR_Q14 = other.LF_AR_Q14;
			Seed = other.Seed;
			SeedInit = other.SeedInit;
			RD_Q10 = other.RD_Q10;
		}

		internal void Assign(NSQ_del_dec_struct other)
		{
			PartialCopyFrom(other, 0);
		}
	}

	private struct NSQ_sample_struct
	{
		internal int Q_Q10;

		internal int RD_Q10;

		internal int xq_Q14;

		internal int LF_AR_Q14;

		internal int sLTP_shp_Q14;

		internal int LPC_exc_Q14;
	}

	/// <summary>
	/// Buffer for quantized output signal
	/// </summary>
	internal readonly short[] xq = new short[640];

	internal readonly int[] sLTP_shp_Q14 = new int[640];

	internal readonly int[] sLPC_Q14 = new int[80 + SilkConstants.NSQ_LPC_BUF_LENGTH];

	internal readonly int[] sAR2_Q14 = new int[16];

	internal int sLF_AR_shp_Q14;

	internal int lagPrev;

	internal int sLTP_buf_idx;

	internal int sLTP_shp_buf_idx;

	internal int rand_seed;

	internal int prev_gain_Q16;

	internal int rewhite_flag;

	internal void Reset()
	{
		Arrays.MemSetShort(xq, 0, 640);
		Arrays.MemSetInt(sLTP_shp_Q14, 0, 640);
		Arrays.MemSetInt(sLPC_Q14, 0, 80 + SilkConstants.NSQ_LPC_BUF_LENGTH);
		Arrays.MemSetInt(sAR2_Q14, 0, 16);
		sLF_AR_shp_Q14 = 0;
		lagPrev = 0;
		sLTP_buf_idx = 0;
		sLTP_shp_buf_idx = 0;
		rand_seed = 0;
		prev_gain_Q16 = 0;
		rewhite_flag = 0;
	}

	internal void Assign(SilkNSQState other)
	{
		sLF_AR_shp_Q14 = other.sLF_AR_shp_Q14;
		lagPrev = other.lagPrev;
		sLTP_buf_idx = other.sLTP_buf_idx;
		sLTP_shp_buf_idx = other.sLTP_shp_buf_idx;
		rand_seed = other.rand_seed;
		prev_gain_Q16 = other.prev_gain_Q16;
		rewhite_flag = other.rewhite_flag;
		Arrays.MemCopy(other.xq, 0, xq, 0, 640);
		Arrays.MemCopy(other.sLTP_shp_Q14, 0, sLTP_shp_Q14, 0, 640);
		Arrays.MemCopy(other.sLPC_Q14, 0, sLPC_Q14, 0, 80 + SilkConstants.NSQ_LPC_BUF_LENGTH);
		Arrays.MemCopy(other.sAR2_Q14, 0, sAR2_Q14, 0, 16);
	}








}
