namespace Concentus.Silk.Enums;

/// <summary>
/// Represents error messages from a silk encoder/decoder
/// </summary>
internal static class SilkError
{
	internal static int SILK_NO_ERROR = 0;

	internal static int SILK_ENC_INPUT_INVALID_NO_OF_SAMPLES = -101;

	internal static int SILK_ENC_FS_NOT_SUPPORTED = -102;

	internal static int SILK_ENC_PACKET_SIZE_NOT_SUPPORTED = -103;

	internal static int SILK_ENC_PAYLOAD_BUF_TOO_SHORT = -104;

	internal static int SILK_ENC_INVALID_LOSS_RATE = -105;

	internal static int SILK_ENC_INVALID_COMPLEXITY_SETTING = -106;

	internal static int SILK_ENC_INVALID_INBAND_FEC_SETTING = -107;

	internal static int SILK_ENC_INVALID_DTX_SETTING = -108;

	internal static int SILK_ENC_INVALID_CBR_SETTING = -109;

	internal static int SILK_ENC_INTERNAL_ERROR = -110;

	internal static int SILK_ENC_INVALID_NUMBER_OF_CHANNELS_ERROR = -111;

	internal static int SILK_DEC_INVALID_SAMPLING_FREQUENCY = -200;

	internal static int SILK_DEC_PAYLOAD_TOO_LARGE = -201;

	internal static int SILK_DEC_PAYLOAD_ERROR = -202;

	internal static int SILK_DEC_INVALID_FRAME_SIZE = -203;
}
