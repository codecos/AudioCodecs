namespace Concentus.Enums;

internal static class OpusBandwidthHelpers
{
	public static int GetOrdinal(OpusBandwidth bw)
	{
		return (int)(bw - 1101);
	}

	public static OpusBandwidth MIN(OpusBandwidth a, OpusBandwidth b)
	{
		if (a < b)
		{
			return a;
		}
		return b;
	}

	public static OpusBandwidth MAX(OpusBandwidth a, OpusBandwidth b)
	{
		if (a > b)
		{
			return a;
		}
		return b;
	}
}
