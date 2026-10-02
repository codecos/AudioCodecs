using Concentus.Common.CPlusPlus;

namespace Concentus.Silk.Structs;

/// <summary>
/// Struct for TOC (Table of Contents)
/// </summary>
internal class TOCStruct
{
	/// <summary>
	/// Voice activity for packet
	/// </summary>
	internal int VADFlag;

	/// <summary>
	/// Voice activity for each frame in packet
	/// </summary>
	internal readonly int[] VADFlags = new int[3];

	/// <summary>
	/// Flag indicating if packet contains in-band FEC
	/// </summary>
	internal int inbandFECFlag;

	internal void Reset()
	{
		VADFlag = 0;
		Arrays.MemSetInt(VADFlags, 0, 3);
		inbandFECFlag = 0;
	}
}
