using Concentus.Celt.Structs;
using Concentus.Common.CPlusPlus;

namespace Concentus.Structs;

internal class TonalityAnalysisState
{
	internal bool enabled;

	internal readonly float[] angle = new float[240];

	internal readonly float[] d_angle = new float[240];

	internal readonly float[] d2_angle = new float[240];

	internal readonly int[] inmem = new int[720];

	internal int mem_fill;

	internal readonly float[] prev_band_tonality = new float[18];

	internal float prev_tonality;

	internal readonly float[][] E = Arrays.InitTwoDimensionalArray<float>(8, 18);

	internal readonly float[] lowE = new float[18];

	internal readonly float[] highE = new float[18];

	internal readonly float[] meanE = new float[21];

	internal readonly float[] mem = new float[32];

	internal readonly float[] cmean = new float[8];

	internal readonly float[] std = new float[9];

	internal float music_prob;

	internal float Etracker;

	internal float lowECount;

	internal int E_count;

	internal int last_music;

	internal int last_transition;

	internal int count;

	internal readonly float[] subframe_mem = new float[3];

	internal int analysis_offset;

	/// Probability of having speech for time i to DETECT_SIZE-1 (and music before).
	///             pspeech[0] is the probability that all frames in the window are speech. 
	internal readonly float[] pspeech = new float[200];

	/// Probability of having music for time i to DETECT_SIZE-1 (and speech before).
	///             pmusic[0] is the probability that all frames in the window are music. 
	internal readonly float[] pmusic = new float[200];

	internal float speech_confidence;

	internal float music_confidence;

	internal int speech_confidence_count;

	internal int music_confidence_count;

	internal int write_pos;

	internal int read_pos;

	internal int read_subframe;

	internal readonly AnalysisInfo[] info = new AnalysisInfo[200];

	internal TonalityAnalysisState()
	{
		for (int i = 0; i < 200; i++)
		{
			info[i] = new AnalysisInfo();
		}
	}

	internal void Reset()
	{
		Arrays.MemSetFloat(angle, 0f, 240);
		Arrays.MemSetFloat(d_angle, 0f, 240);
		Arrays.MemSetFloat(d2_angle, 0f, 240);
		Arrays.MemSetInt(inmem, 0, 720);
		mem_fill = 0;
		Arrays.MemSetFloat(prev_band_tonality, 0f, 18);
		prev_tonality = 0f;
		for (int i = 0; i < 8; i++)
		{
			Arrays.MemSetFloat(E[i], 0f, 18);
		}
		Arrays.MemSetFloat(lowE, 0f, 18);
		Arrays.MemSetFloat(highE, 0f, 18);
		Arrays.MemSetFloat(meanE, 0f, 21);
		Arrays.MemSetFloat(mem, 0f, 32);
		Arrays.MemSetFloat(cmean, 0f, 8);
		Arrays.MemSetFloat(std, 0f, 9);
		music_prob = 0f;
		Etracker = 0f;
		lowECount = 0f;
		E_count = 0;
		last_music = 0;
		last_transition = 0;
		count = 0;
		Arrays.MemSetFloat(subframe_mem, 0f, 3);
		analysis_offset = 0;
		Arrays.MemSetFloat(pspeech, 0f, 200);
		Arrays.MemSetFloat(pmusic, 0f, 200);
		speech_confidence = 0f;
		music_confidence = 0f;
		speech_confidence_count = 0;
		music_confidence_count = 0;
		write_pos = 0;
		read_pos = 0;
		read_subframe = 0;
		for (int j = 0; j < 200; j++)
		{
			info[j].Reset();
		}
	}
}
