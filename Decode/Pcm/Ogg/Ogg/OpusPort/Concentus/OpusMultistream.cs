using Concentus.Structs;

namespace Concentus;

internal static class OpusMultistream
{
	internal static int validate_layout(ChannelLayout layout)
	{
		int num = layout.nb_streams + layout.nb_coupled_streams;
		if (num > 255)
		{
			return 0;
		}
		for (int i = 0; i < layout.nb_channels; i++)
		{
			if (layout.mapping[i] >= num && layout.mapping[i] != byte.MaxValue)
			{
				return 0;
			}
		}
		return 1;
	}

	internal static int get_left_channel(ChannelLayout layout, int stream_id, int prev)
	{
		for (int i = ((prev >= 0) ? (prev + 1) : 0); i < layout.nb_channels; i++)
		{
			if (layout.mapping[i] == stream_id * 2)
			{
				return i;
			}
		}
		return -1;
	}

	internal static int get_right_channel(ChannelLayout layout, int stream_id, int prev)
	{
		for (int i = ((prev >= 0) ? (prev + 1) : 0); i < layout.nb_channels; i++)
		{
			if (layout.mapping[i] == stream_id * 2 + 1)
			{
				return i;
			}
		}
		return -1;
	}

	internal static int get_mono_channel(ChannelLayout layout, int stream_id, int prev)
	{
		for (int i = ((prev >= 0) ? (prev + 1) : 0); i < layout.nb_channels; i++)
		{
			if (layout.mapping[i] == stream_id + layout.nb_coupled_streams)
			{
				return i;
			}
		}
		return -1;
	}
}
