using Concentus.Silk.Enums;

namespace Concentus.Silk.Structs;

/// <summary>
/// Structure for controlling encoder operation
/// </summary>
internal class EncControlState
{
	internal int nChannelsAPI;

	internal int nChannelsInternal;

	internal int API_sampleRate;

	internal int maxInternalSampleRate;

	internal int minInternalSampleRate;

	internal int desiredInternalSampleRate;

	internal int payloadSize_ms;

	internal int bitRate;

	internal int packetLossPercentage;

	internal int complexity;

	internal int useInBandFEC;

	internal int useDTX;

	internal int useCBR;

	internal int maxBits;

	internal int toMono;

	internal int opusCanSwitch;

	internal int reducedDependency;

	internal int internalSampleRate;

	internal int allowBandwidthSwitch;

	internal int inWBmodeWithoutVariableLP;

	internal int stereoWidth_Q14;

	internal int switchReady;

	internal void Reset()
	{
		nChannelsAPI = 0;
		nChannelsInternal = 0;
		API_sampleRate = 0;
		maxInternalSampleRate = 0;
		minInternalSampleRate = 0;
		desiredInternalSampleRate = 0;
		payloadSize_ms = 0;
		bitRate = 0;
		packetLossPercentage = 0;
		complexity = 0;
		useInBandFEC = 0;
		useDTX = 0;
		useCBR = 0;
		maxBits = 0;
		toMono = 0;
		opusCanSwitch = 0;
		reducedDependency = 0;
		internalSampleRate = 0;
		allowBandwidthSwitch = 0;
		inWBmodeWithoutVariableLP = 0;
		stereoWidth_Q14 = 0;
		switchReady = 0;
	}

	/// <summary>
	/// Checks this encoder control struct and returns error code, if any
	/// </summary>
	/// <returns></returns>
	internal int check_control_input()
	{
		if ((API_sampleRate != 8000 && API_sampleRate != 12000 && API_sampleRate != 16000 && API_sampleRate != 24000 && API_sampleRate != 32000 && API_sampleRate != 44100 && API_sampleRate != 48000) || (desiredInternalSampleRate != 8000 && desiredInternalSampleRate != 12000 && desiredInternalSampleRate != 16000) || (maxInternalSampleRate != 8000 && maxInternalSampleRate != 12000 && maxInternalSampleRate != 16000) || (minInternalSampleRate != 8000 && minInternalSampleRate != 12000 && minInternalSampleRate != 16000) || minInternalSampleRate > desiredInternalSampleRate || maxInternalSampleRate < desiredInternalSampleRate || minInternalSampleRate > maxInternalSampleRate)
		{
			return SilkError.SILK_ENC_FS_NOT_SUPPORTED;
		}
		if (payloadSize_ms != 10 && payloadSize_ms != 20 && payloadSize_ms != 40 && payloadSize_ms != 60)
		{
			return SilkError.SILK_ENC_PACKET_SIZE_NOT_SUPPORTED;
		}
		if (packetLossPercentage < 0 || packetLossPercentage > 100)
		{
			return SilkError.SILK_ENC_INVALID_LOSS_RATE;
		}
		if (useDTX < 0 || useDTX > 1)
		{
			return SilkError.SILK_ENC_INVALID_DTX_SETTING;
		}
		if (useCBR < 0 || useCBR > 1)
		{
			return SilkError.SILK_ENC_INVALID_CBR_SETTING;
		}
		if (useInBandFEC < 0 || useInBandFEC > 1)
		{
			return SilkError.SILK_ENC_INVALID_INBAND_FEC_SETTING;
		}
		if (nChannelsAPI < 1 || nChannelsAPI > 2)
		{
			return SilkError.SILK_ENC_INVALID_NUMBER_OF_CHANNELS_ERROR;
		}
		if (nChannelsInternal < 1 || nChannelsInternal > 2)
		{
			return SilkError.SILK_ENC_INVALID_NUMBER_OF_CHANNELS_ERROR;
		}
		if (nChannelsInternal > nChannelsAPI)
		{
			return SilkError.SILK_ENC_INVALID_NUMBER_OF_CHANNELS_ERROR;
		}
		if (complexity < 0 || complexity > 10)
		{
			return SilkError.SILK_ENC_INVALID_COMPLEXITY_SETTING;
		}
		return SilkError.SILK_NO_ERROR;
	}
}
