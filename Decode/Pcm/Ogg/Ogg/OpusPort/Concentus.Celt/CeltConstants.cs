namespace Concentus.Celt;

internal static class CeltConstants
{
	internal const int Q15ONE = 32767;

	internal const float CELT_SIG_SCALE = 32768f;

	internal const int SIG_SHIFT = 12;

	internal const int NORM_SCALING = 16384;

	internal const int DB_SHIFT = 10;

	internal const int EPSILON = 1;

	internal const int VERY_SMALL = 0;

	internal const short VERY_LARGE16 = short.MaxValue;

	internal const short Q15_ONE = short.MaxValue;

	internal const int COMBFILTER_MAXPERIOD = 1024;

	internal const int COMBFILTER_MINPERIOD = 15;

	internal const int DECODE_BUFFER_SIZE = 2048;

	internal const int BITALLOC_SIZE = 11;

	internal const int MAX_PERIOD = 1024;

	internal const int TOTAL_MODES = 1;

	internal const int MAX_PSEUDO = 40;

	internal const int LOG_MAX_PSEUDO = 6;

	internal const int CELT_MAX_PULSES = 128;

	internal const int MAX_FINE_BITS = 8;

	internal const int FINE_OFFSET = 21;

	internal const int QTHETA_OFFSET = 4;

	internal const int QTHETA_OFFSET_TWOPHASE = 16;

	internal const int PLC_PITCH_LAG_MAX = 720;

	internal const int PLC_PITCH_LAG_MIN = 100;

	internal const int LPC_ORDER = 24;
}
