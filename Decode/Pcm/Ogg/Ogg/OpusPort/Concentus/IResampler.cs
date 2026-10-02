using System;

namespace Concentus;

/// <summary>
/// Represents an audio resampler which can process single-channel or interleaved-channel inputs
/// in either int16 or float32 formats.
/// </summary>
public interface IResampler : IDisposable
{
	/// <summary>
	/// Get the latency introduced by the resampler measured in input samples.
	/// </summary>
	int InputLatency { get; }

	/// <summary>
	/// Gets or sets the input stride.
	/// </summary>
	int InputStride { get; set; }

	/// <summary>
	/// Gets the latency introduced by the resampler measured in output samples.
	/// </summary>
	int OutputLatencySamples { get; }

	/// <summary>
	/// Gets the latency introduced by the resampler.
	/// </summary>
	TimeSpan OutputLatency { get; }

	/// <summary>
	/// Gets or sets the output stride.
	/// </summary>
	int OutputStride { get; set; }

	/// <summary>
	/// Gets or sets the resampling quality between 0 and 10, where 0 has poor 
	/// quality and 10 has very high quality.
	/// </summary>
	int Quality { get; set; }

	/// <summary>
	/// Gets the current resampling ratio. This will be reduced to the least common denominator
	/// </summary>
	/// <param name="ratio_num">(Output) numerator of the sampling rate ratio</param>
	/// <param name="ratio_den">(Output) denominator of the sampling rate ratio</param>
	void GetRateFraction(out int ratio_num, out int ratio_den);

	/// <summary>
	/// Get the current input/output sampling rates (integer value).
	/// </summary>
	/// <param name="in_rate">(Output) Sampling rate of input</param>
	/// <param name="out_rate">(Output) Sampling rate of output</param>
	void GetRates(out int in_rate, out int out_rate);

	/// <summary>
	/// Clears the resampler buffers so a new (unrelated) stream can be processed.
	/// </summary>
	void ResetMem();

	/// <summary>
	/// Make sure that the first samples to go out of the resamplers don't have 
	/// leading zeros. This is only useful before starting to use a newly created
	/// resampler. It is recommended to use that when resampling an audio file, as
	/// it will generate a file with the same length.For real-time processing,
	/// it is probably easier not to use this call (so that the output duration
	/// is the same for the first frame).
	/// </summary>
	void SkipZeroes();

	/// <summary>
	/// Resample a float32 sample array. The input and output buffers must *not* overlap
	/// </summary>
	/// <param name="channel_index">The index of the channel to process (for multichannel input, 0 otherwise)</param>
	/// <param name="input">Input buffer</param>
	/// <param name="in_len">Number of input samples in the input buffer. After this function returns, this value
	/// will be set to the number of input samples actually processed</param>
	/// <param name="output">Output buffer</param>
	/// <param name="out_len">Size of the output buffer. After this function returns, this value will be set to the number
	/// of output samples actually produced</param>
	void Process(int channel_index, Span<float> input, ref int in_len, Span<float> output, ref int out_len);

	/// <summary>
	/// Resample an int16 sample array. The input and output buffers must *not* overlap
	/// </summary>
	/// <param name="channel_index">The index of the channel to process (for multichannel input, 0 otherwise)</param>
	/// <param name="input">Input buffer</param>
	/// <param name="in_len">Number of input samples in the input buffer. After this function returns, this value
	/// will be set to the number of input samples actually processed</param>
	/// <param name="output">Output buffer</param>
	/// <param name="out_len">Size of the output buffer. After this function returns, this value will be set to the number
	/// of output samples actually produced</param>
	void Process(int channel_index, Span<short> input, ref int in_len, Span<short> output, ref int out_len);

	/// <summary>
	/// Resamples an interleaved float32 array. The stride is automatically determined by the number of channels of the resampler.
	/// </summary>
	/// <param name="input">Input buffer</param>
	/// <param name="in_len">The number of samples *PER-CHANNEL* in the input buffer. After this function returns, this
	/// value will be set to the number of input samples actually processed</param>
	/// <param name="output">Output buffer</param>
	/// <param name="out_len">The size of the output buffer in samples-per-channel. After this function returns, this value
	/// will be set to the number of samples per channel actually produced</param>
	void ProcessInterleaved(Span<float> input, ref int in_len, Span<float> output, ref int out_len);

	/// <summary>
	/// Resamples an interleaved int16 array. The stride is automatically determined by the number of channels of the resampler.
	/// </summary>
	/// <param name="input">Input buffer</param>
	/// <param name="in_len">The number of samples *PER-CHANNEL* in the input buffer. After this function returns, this
	/// value will be set to the number of input samples actually processed</param>
	/// <param name="output">Output buffer</param>
	/// <param name="out_len">The size of the output buffer in samples-per-channel. After this function returns, this value
	/// will be set to the number of samples per channel actually produced</param>
	void ProcessInterleaved(Span<short> input, ref int in_len, Span<short> output, ref int out_len);

	/// <summary>
	/// Sets the input/output sampling rates and resampling ration (fractional values in Hz supported)
	/// </summary>
	/// <param name="ratio_num">Numerator of the sampling rate ratio</param>
	/// <param name="ratio_den">Denominator of the sampling rate ratio</param>
	/// <param name="in_rate">Input sampling rate rounded to the nearest integer (in Hz)</param>
	/// <param name="out_rate">Output sampling rate rounded to the nearest integer (in Hz)</param>
	void SetRateFraction(int ratio_num, int ratio_den, int in_rate, int out_rate);

	/// <summary>
	/// Sets the input and output rates
	/// </summary>
	/// <param name="in_rate">Input sampling rate, in hertz</param>
	/// <param name="out_rate">Output sampling rate, in hertz</param>
	void SetRates(int in_rate, int out_rate);
}
