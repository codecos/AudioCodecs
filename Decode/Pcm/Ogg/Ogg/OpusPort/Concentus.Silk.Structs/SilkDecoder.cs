namespace Concentus.Silk.Structs;

using Concentus.Common.CPlusPlus;

/// <summary>
/// Decoder super struct
/// </summary>
internal class SilkDecoder
{
	internal readonly SilkChannelDecoder[] channel_state = new SilkChannelDecoder[2];
	internal readonly BoxedValueInt samplesOutWork = new BoxedValueInt();
	internal readonly BoxedValueInt decodeOnlyMidWork = new BoxedValueInt();
	internal readonly int[] sampleOffsetsWork = new int[2];
	internal readonly int[] stereoWork = new int[2];
	internal readonly int[] stereoPredWork = new int[6];
	internal readonly short[] internalSamplesWork = new short[2 * (960 + 2)];
	internal readonly short[] resampleWork = new short[5760];
	internal readonly short[] lbrrPulsesWork = new short[320];

	internal readonly StereoDecodeState sStereo = new StereoDecodeState();

	internal int nChannelsAPI;

	internal int nChannelsInternal;

	internal int prev_decode_only_middle;

	internal SilkDecoder()
	{
		for (int i = 0; i < 2; i++)
		{
			channel_state[i] = new SilkChannelDecoder();
		}
	}

	internal void Reset()
	{
		for (int i = 0; i < 2; i++)
		{
			channel_state[i].Reset();
		}
		sStereo.Reset();
		nChannelsAPI = 0;
		nChannelsInternal = 0;
		prev_decode_only_middle = 0;
	}
}
