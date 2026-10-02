using System;

namespace Concentus.Structs;

internal class ChannelLayout
{
	internal int nb_channels;

	internal int nb_streams;

	internal int nb_coupled_streams;

	internal readonly byte[] mapping = new byte[256];

	internal void Reset()
	{
		nb_channels = 0;
		nb_streams = 0;
		nb_coupled_streams = 0;
		mapping.AsSpan().Clear();
	}
}
