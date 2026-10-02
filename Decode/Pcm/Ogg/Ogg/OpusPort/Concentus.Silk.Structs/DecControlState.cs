namespace Concentus.Silk.Structs;

/// <summary>
/// Structure for controlling decoder operation and reading decoder status
/// </summary>
internal class DecControlState
{
	internal int nChannelsAPI;

	internal int nChannelsInternal;

	internal int API_sampleRate;

	internal int internalSampleRate;

	internal int payloadSize_ms;

	internal int prevPitchLag;

	internal void Reset()
	{
		nChannelsAPI = 0;
		nChannelsInternal = 0;
		API_sampleRate = 0;
		internalSampleRate = 0;
		payloadSize_ms = 0;
		prevPitchLag = 0;
	}
}
