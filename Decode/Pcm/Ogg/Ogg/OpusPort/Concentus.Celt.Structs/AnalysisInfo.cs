namespace Concentus.Celt.Structs;

internal class AnalysisInfo
{
	internal int valid;

	internal float tonality;

	internal float tonality_slope;

	internal float noisiness;

	internal float activity;

	internal float music_prob;

	internal int bandwidth;

	internal AnalysisInfo()
	{
	}

	internal void Assign(AnalysisInfo other)
	{
		valid = other.valid;
		tonality = other.tonality;
		tonality_slope = other.tonality_slope;
		noisiness = other.noisiness;
		activity = other.activity;
		music_prob = other.music_prob;
		bandwidth = other.bandwidth;
	}

	internal void Reset()
	{
		valid = 0;
		tonality = 0f;
		tonality_slope = 0f;
		noisiness = 0f;
		activity = 0f;
		music_prob = 0f;
		bandwidth = 0;
	}
}
