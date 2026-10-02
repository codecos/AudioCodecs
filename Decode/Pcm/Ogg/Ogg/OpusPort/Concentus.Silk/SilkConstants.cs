using System;

namespace Concentus.Silk;

internal static class SilkConstants
{
	internal const int ENCODER_NUM_CHANNELS = 2;

	internal const int DECODER_NUM_CHANNELS = 2;

	internal const int MAX_FRAMES_PER_PACKET = 3;

	internal const int MIN_TARGET_RATE_BPS = 5000;

	internal const int MAX_TARGET_RATE_BPS = 80000;

	internal const int TARGET_RATE_TAB_SZ = 8;

	internal const int LBRR_NB_MIN_RATE_BPS = 12000;

	internal const int LBRR_MB_MIN_RATE_BPS = 14000;

	internal const int LBRR_WB_MIN_RATE_BPS = 16000;

	internal const int NB_SPEECH_FRAMES_BEFORE_DTX = 10;

	internal const int MAX_CONSECUTIVE_DTX = 20;

	internal const int MAX_FS_KHZ = 16;

	internal const int MAX_API_FS_KHZ = 48;

	internal const int TYPE_NO_VOICE_ACTIVITY = 0;

	internal const int TYPE_UNVOICED = 1;

	internal const int TYPE_VOICED = 2;

	internal const int CODE_INDEPENDENTLY = 0;

	internal const int CODE_INDEPENDENTLY_NO_LTP_SCALING = 1;

	internal const int CODE_CONDITIONALLY = 2;

	internal const int STEREO_QUANT_TAB_SIZE = 16;

	internal const int STEREO_QUANT_SUB_STEPS = 5;

	internal const int STEREO_INTERP_LEN_MS = 8;

	internal const float STEREO_RATIO_SMOOTH_COEF = 0.01f;

	internal const int PITCH_EST_MIN_LAG_MS = 2;

	internal const int PITCH_EST_MAX_LAG_MS = 18;

	internal const int MAX_NB_SUBFR = 4;

	internal const int LTP_MEM_LENGTH_MS = 20;

	internal const int SUB_FRAME_LENGTH_MS = 5;

	internal const int MAX_SUB_FRAME_LENGTH = 80;

	internal const int MAX_FRAME_LENGTH_MS = 20;

	internal const int MAX_FRAME_LENGTH = 320;

	internal const int LA_PITCH_MS = 2;

	internal const int LA_PITCH_MAX = 32;

	internal const int MAX_FIND_PITCH_LPC_ORDER = 16;

	internal const int FIND_PITCH_LPC_WIN_MS = 24;

	internal const int FIND_PITCH_LPC_WIN_MS_2_SF = 14;

	internal const int FIND_PITCH_LPC_WIN_MAX = 384;

	internal const int LA_SHAPE_MS = 5;

	internal const int LA_SHAPE_MAX = 80;

	internal const int SHAPE_LPC_WIN_MAX = 240;

	internal const int MIN_QGAIN_DB = 2;

	internal const int MAX_QGAIN_DB = 88;

	internal const int N_LEVELS_QGAIN = 64;

	internal const int MAX_DELTA_GAIN_QUANT = 36;

	internal const int MIN_DELTA_GAIN_QUANT = -4;

	internal const short OFFSET_VL_Q10 = 32;

	internal const short OFFSET_VH_Q10 = 100;

	internal const short OFFSET_UVL_Q10 = 100;

	internal const short OFFSET_UVH_Q10 = 240;

	internal const int QUANT_LEVEL_ADJUST_Q10 = 80;

	internal const int MAX_LPC_STABILIZE_ITERATIONS = 16;

	internal const float MAX_PREDICTION_POWER_GAIN = 10000f;

	internal const float MAX_PREDICTION_POWER_GAIN_AFTER_RESET = 100f;

	internal const int SILK_MAX_ORDER_LPC = 16;

	internal const int MAX_LPC_ORDER = 16;

	internal const int MIN_LPC_ORDER = 10;

	internal const int LTP_ORDER = 5;

	internal const int NB_LTP_CBKS = 3;

	internal const int USE_HARM_SHAPING = 1;

	internal const int MAX_SHAPE_LPC_ORDER = 16;

	internal const int HARM_SHAPE_FIR_TAPS = 3;

	internal const int MAX_DEL_DEC_STATES = 4;

	internal const int LTP_BUF_LENGTH = 512;

	internal const int LTP_MASK = 511;

	internal const int DECISION_DELAY = 32;

	internal const int DECISION_DELAY_MASK = 31;

	internal const int SHELL_CODEC_FRAME_LENGTH = 16;

	internal const int LOG2_SHELL_CODEC_FRAME_LENGTH = 4;

	internal const int MAX_NB_SHELL_BLOCKS = 20;

	internal const int N_RATE_LEVELS = 10;

	internal const int SILK_MAX_PULSES = 16;

	internal const int MAX_MATRIX_SIZE = 16;

	internal static readonly int NSQ_LPC_BUF_LENGTH = Math.Max(16, 32);

	internal const int VAD_N_BANDS = 4;

	internal const int VAD_INTERNAL_SUBFRAMES_LOG2 = 2;

	internal const int VAD_INTERNAL_SUBFRAMES = 4;

	internal const int VAD_NOISE_LEVEL_SMOOTH_COEF_Q16 = 1024;

	internal const int VAD_NOISE_LEVELS_BIAS = 50;

	internal const int VAD_NEGATIVE_OFFSET_Q5 = 128;

	internal const int VAD_SNR_FACTOR_Q16 = 45000;

	internal const int VAD_SNR_SMOOTH_COEF_Q18 = 4096;

	internal const int LSF_COS_TAB_SZ = 128;

	internal const int NLSF_W_Q = 2;

	internal const int NLSF_VQ_MAX_VECTORS = 32;

	internal const int NLSF_VQ_MAX_SURVIVORS = 32;

	internal const int NLSF_QUANT_MAX_AMPLITUDE = 4;

	internal const int NLSF_QUANT_MAX_AMPLITUDE_EXT = 10;

	internal const float NLSF_QUANT_LEVEL_ADJ = 0.1f;

	internal const int NLSF_QUANT_DEL_DEC_STATES_LOG2 = 2;

	internal const int NLSF_QUANT_DEL_DEC_STATES = 4;

	internal const int TRANSITION_TIME_MS = 5120;

	internal const int TRANSITION_NB = 3;

	internal const int TRANSITION_NA = 2;

	internal const int TRANSITION_INT_NUM = 5;

	internal const int TRANSITION_FRAMES = 256;

	internal const int TRANSITION_INT_STEPS = 64;

	internal const int BWE_AFTER_LOSS_Q16 = 63570;

	internal const int CNG_BUF_MASK_MAX = 255;

	internal const int CNG_GAIN_SMTH_Q16 = 4634;

	internal const int CNG_NLSF_SMTH_Q16 = 16348;

	internal const int PE_MAX_FS_KHZ = 16;

	internal const int PE_MAX_NB_SUBFR = 4;

	internal const int PE_SUBFR_LENGTH_MS = 5;

	internal const int PE_LTP_MEM_LENGTH_MS = 20;

	internal const int PE_MAX_FRAME_LENGTH_MS = 40;

	internal const int PE_MAX_FRAME_LENGTH = 640;

	internal const int PE_MAX_FRAME_LENGTH_ST_1 = 160;

	internal const int PE_MAX_FRAME_LENGTH_ST_2 = 320;

	internal const int PE_MAX_LAG_MS = 18;

	internal const int PE_MIN_LAG_MS = 2;

	internal const int PE_MAX_LAG = 288;

	internal const int PE_MIN_LAG = 32;

	internal const int PE_D_SRCH_LENGTH = 24;

	internal const int PE_NB_STAGE3_LAGS = 5;

	internal const int PE_NB_CBKS_STAGE2 = 3;

	internal const int PE_NB_CBKS_STAGE2_EXT = 11;

	internal const int PE_NB_CBKS_STAGE3_MAX = 34;

	internal const int PE_NB_CBKS_STAGE3_MID = 24;

	internal const int PE_NB_CBKS_STAGE3_MIN = 16;

	internal const int PE_NB_CBKS_STAGE3_10MS = 12;

	internal const int PE_NB_CBKS_STAGE2_10MS = 3;

	internal const float PE_SHORTLAG_BIAS = 0.2f;

	internal const float PE_PREVLAG_BIAS = 0.2f;

	internal const float PE_FLATCONTOUR_BIAS = 0.05f;

	internal const int SILK_PE_MIN_COMPLEX = 0;

	internal const int SILK_PE_MID_COMPLEX = 1;

	internal const int SILK_PE_MAX_COMPLEX = 2;

	internal const float BWE_COEF = 0.99f;

	internal const int V_PITCH_GAIN_START_MIN_Q14 = 11469;

	internal const int V_PITCH_GAIN_START_MAX_Q14 = 15565;

	internal const int MAX_PITCH_LAG_MS = 18;

	internal const int RAND_BUF_SIZE = 128;

	internal const int RAND_BUF_MASK = 127;

	internal const int LOG2_INV_LPC_GAIN_HIGH_THRES = 3;

	internal const int LOG2_INV_LPC_GAIN_LOW_THRES = 8;

	internal const int PITCH_DRIFT_FAC_Q16 = 655;

	internal const int SILK_RESAMPLER_MAX_FIR_ORDER = 36;

	internal const int SILK_RESAMPLER_MAX_IIR_ORDER = 6;

	internal const int RESAMPLER_DOWN_ORDER_FIR0 = 18;

	internal const int RESAMPLER_DOWN_ORDER_FIR1 = 24;

	internal const int RESAMPLER_DOWN_ORDER_FIR2 = 36;

	internal const int RESAMPLER_ORDER_FIR_12 = 8;

	internal const int RESAMPLER_MAX_BATCH_SIZE_MS = 10;

	internal const int RESAMPLER_MAX_FS_KHZ = 48;

	internal const int RESAMPLER_MAX_BATCH_SIZE_IN = 480;

	internal const int SILK_MAX_FRAMES_PER_PACKET = 3;
}
