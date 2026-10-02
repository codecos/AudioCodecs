namespace Concentus.Silk;

internal class TuningParameters
{
	internal const int BITRESERVOIR_DECAY_TIME_MS = 500;

	internal const float FIND_PITCH_WHITE_NOISE_FRACTION = 0.001f;

	internal const float FIND_PITCH_BANDWIDTH_EXPANSION = 0.99f;

	internal const float FIND_LPC_COND_FAC = 1E-05f;

	internal const float FIND_LTP_COND_FAC = 1E-05f;

	internal const float LTP_DAMPING = 0.05f;

	internal const float LTP_SMOOTHING = 0.1f;

	internal const float MU_LTP_QUANT_NB = 0.03f;

	internal const float MU_LTP_QUANT_MB = 0.025f;

	internal const float MU_LTP_QUANT_WB = 0.02f;

	internal const float MAX_SUM_LOG_GAIN_DB = 250f;

	internal const float VARIABLE_HP_SMTH_COEF1 = 0.1f;

	internal const float VARIABLE_HP_SMTH_COEF2 = 0.015f;

	internal const float VARIABLE_HP_MAX_DELTA_FREQ = 0.4f;

	internal const int VARIABLE_HP_MIN_CUTOFF_HZ = 60;

	internal const int VARIABLE_HP_MAX_CUTOFF_HZ = 100;

	internal const float SPEECH_ACTIVITY_DTX_THRES = 0.05f;

	internal const float LBRR_SPEECH_ACTIVITY_THRES = 0.3f;

	internal const float BG_SNR_DECR_dB = 2f;

	internal const float HARM_SNR_INCR_dB = 2f;

	internal const float SPARSE_SNR_INCR_dB = 2f;

	internal const float SPARSENESS_THRESHOLD_QNT_OFFSET = 0.75f;

	internal const float WARPING_MULTIPLIER = 0.015f;

	internal const float SHAPE_WHITE_NOISE_FRACTION = 5E-05f;

	internal const float BANDWIDTH_EXPANSION = 0.95f;

	internal const float LOW_RATE_BANDWIDTH_EXPANSION_DELTA = 0.01f;

	internal const float LOW_RATE_HARMONIC_BOOST = 0.1f;

	internal const float LOW_INPUT_QUALITY_HARMONIC_BOOST = 0.1f;

	internal const float HARMONIC_SHAPING = 0.3f;

	internal const float HIGH_RATE_OR_LOW_QUALITY_HARMONIC_SHAPING = 0.2f;

	internal const float HP_NOISE_COEF = 0.25f;

	internal const float HARM_HP_NOISE_COEF = 0.35f;

	internal const float INPUT_TILT = 0.05f;

	internal const float HIGH_RATE_INPUT_TILT = 0.1f;

	internal const float LOW_FREQ_SHAPING = 4f;

	internal const float LOW_QUALITY_LOW_FREQ_SHAPING_DECR = 0.5f;

	internal const float SUBFR_SMTH_COEF = 0.4f;

	internal const float LAMBDA_OFFSET = 1.2f;

	internal const float LAMBDA_SPEECH_ACT = -0.2f;

	internal const float LAMBDA_DELAYED_DECISIONS = -0.05f;

	internal const float LAMBDA_INPUT_QUALITY = -0.1f;

	internal const float LAMBDA_CODING_QUALITY = -0.2f;

	internal const float LAMBDA_QUANT_OFFSET = 0.8f;

	internal const int REDUCE_BITRATE_10_MS_BPS = 2200;

	internal const int MAX_BANDWIDTH_SWITCH_DELAY_MS = 5000;
}
