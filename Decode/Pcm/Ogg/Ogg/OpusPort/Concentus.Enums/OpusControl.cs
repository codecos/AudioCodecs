namespace Concentus.Enums;

/// <summary>
/// These are the actual Encoder CTL ID numbers.
/// They should not be used directly by applications.
/// In general, SETs should be even and GETs should be odd.
/// </summary>
internal static class OpusControl
{
	internal const int OPUS_SET_APPLICATION_REQUEST = 4000;

	internal const int OPUS_GET_APPLICATION_REQUEST = 4001;

	internal const int OPUS_SET_BITRATE_REQUEST = 4002;

	internal const int OPUS_GET_BITRATE_REQUEST = 4003;

	internal const int OPUS_SET_MAX_BANDWIDTH_REQUEST = 4004;

	internal const int OPUS_GET_MAX_BANDWIDTH_REQUEST = 4005;

	internal const int OPUS_SET_VBR_REQUEST = 4006;

	internal const int OPUS_GET_VBR_REQUEST = 4007;

	internal const int OPUS_SET_BANDWIDTH_REQUEST = 4008;

	internal const int OPUS_GET_BANDWIDTH_REQUEST = 4009;

	internal const int OPUS_SET_COMPLEXITY_REQUEST = 4010;

	internal const int OPUS_GET_COMPLEXITY_REQUEST = 4011;

	internal const int OPUS_SET_INBAND_FEC_REQUEST = 4012;

	internal const int OPUS_GET_INBAND_FEC_REQUEST = 4013;

	internal const int OPUS_SET_PACKET_LOSS_PERC_REQUEST = 4014;

	internal const int OPUS_GET_PACKET_LOSS_PERC_REQUEST = 4015;

	internal const int OPUS_SET_DTX_REQUEST = 4016;

	internal const int OPUS_GET_DTX_REQUEST = 4017;

	internal const int OPUS_SET_VBR_CONSTRAINT_REQUEST = 4020;

	internal const int OPUS_GET_VBR_CONSTRAINT_REQUEST = 4021;

	internal const int OPUS_SET_FORCE_CHANNELS_REQUEST = 4022;

	internal const int OPUS_GET_FORCE_CHANNELS_REQUEST = 4023;

	internal const int OPUS_SET_SIGNAL_REQUEST = 4024;

	internal const int OPUS_GET_SIGNAL_REQUEST = 4025;

	internal const int OPUS_GET_LOOKAHEAD_REQUEST = 4027;

	internal const int OPUS_GET_SAMPLE_RATE_REQUEST = 4029;

	internal const int OPUS_GET_FINAL_RANGE_REQUEST = 4031;

	internal const int OPUS_GET_PITCH_REQUEST = 4033;

	internal const int OPUS_SET_GAIN_REQUEST = 4034;

	internal const int OPUS_GET_GAIN_REQUEST = 4045;

	internal const int OPUS_SET_LSB_DEPTH_REQUEST = 4036;

	internal const int OPUS_GET_LSB_DEPTH_REQUEST = 4037;

	internal const int OPUS_GET_LAST_PACKET_DURATION_REQUEST = 4039;

	internal const int OPUS_SET_EXPERT_FRAME_DURATION_REQUEST = 4040;

	internal const int OPUS_GET_EXPERT_FRAME_DURATION_REQUEST = 4041;

	internal const int OPUS_SET_PREDICTION_DISABLED_REQUEST = 4042;

	internal const int OPUS_GET_PREDICTION_DISABLED_REQUEST = 4043;

	/// <summary>
	/// Resets the codec state to be equivalent to a freshly initialized state.
	/// This should be called when switching streams in order to prevent
	/// the back to back decoding from giving different results from
	/// one at a time decoding.
	/// </summary>
	internal const int OPUS_RESET_STATE = 4028;

	internal const int OPUS_SET_VOICE_RATIO_REQUEST = 11018;

	internal const int OPUS_GET_VOICE_RATIO_REQUEST = 11019;

	internal const int OPUS_SET_FORCE_MODE_REQUEST = 11002;
}
