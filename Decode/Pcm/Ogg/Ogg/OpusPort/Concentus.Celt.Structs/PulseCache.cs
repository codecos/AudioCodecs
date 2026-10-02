namespace Concentus.Celt.Structs;

internal class PulseCache
{
	internal int size;

	internal short[] index;

	internal byte[] bits;

	internal byte[] caps;

	internal void Reset()
	{
		size = 0;
		index = null;
		bits = null;
		caps = null;
	}
}
