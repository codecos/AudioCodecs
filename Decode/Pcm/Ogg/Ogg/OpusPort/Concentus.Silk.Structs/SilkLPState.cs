using Concentus.Common;

namespace Concentus.Silk.Structs;

/// <summary>
/// Variable cut-off low-pass filter state
/// </summary>
internal class SilkLPState
{
	/// <summary>
	/// Low pass filter state
	/// </summary>
	internal readonly int[] In_LP_State = new int[2];

	/// <summary>
	/// Counter which is mapped to a cut-off frequency
	/// </summary>
	internal int transition_frame_no;

	/// <summary>
	/// Operating mode, &lt;0: switch down, &gt;0: switch up; 0: do nothing
	/// </summary>
	internal int mode;

	internal void Reset()
	{
		In_LP_State[0] = 0;
		In_LP_State[1] = 0;
		transition_frame_no = 0;
		mode = 0;
	}

	internal void silk_LP_variable_cutoff(short[] frame, int frame_ptr, int frame_length)
	{
		int[] b_Q = new int[3];
		int[] a_Q = new int[2];
		int num = 0;
		int num2 = 0;
		if (mode != 0)
		{
			num = Inlines.silk_LSHIFT(256 - transition_frame_no, 10);
			num2 = Inlines.silk_RSHIFT(num, 16);
			num -= Inlines.silk_LSHIFT(num2, 16);
			Filters.silk_LP_interpolate_filter_taps(b_Q, a_Q, num2, num);
			transition_frame_no = Inlines.silk_LIMIT(transition_frame_no + mode, 0, 256);
			Filters.silk_biquad_alt(frame, frame_ptr, b_Q, a_Q, In_LP_State, frame, frame_ptr, frame_length, 1);
		}
	}
}
