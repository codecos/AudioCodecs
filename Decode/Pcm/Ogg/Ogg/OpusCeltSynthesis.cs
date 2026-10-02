using System.Runtime.CompilerServices;

/// <summary>
/// CELT synthesis stage. The decoder supplies one interleaved channel block of
/// de-quantized MDCT coefficients; this stage performs the inverse transform,
/// applies the CELT sine window, overlap-adds the previous block, and writes
/// signed 32-bit PCM directly.
/// </summary>
internal sealed class OpusCeltSynthesis
{
    private readonly int _channels;
    private readonly int _frameSamples;
    private readonly int _transformSize;
    private readonly double[] _window;
    private readonly double[][] _overlap;
    private readonly double[][] _imdct;
    private readonly double[] _time;

    internal OpusCeltSynthesis(int channels, int frameSamples)
    {
        if (channels is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        if (frameSamples is < 120 or > 960 || (frameSamples & (frameSamples - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameSamples));
        }

        _channels = channels;
        _frameSamples = frameSamples;
        _transformSize = frameSamples * 2;
        _window = CreateSineWindow(_transformSize);
        _time = new double[_transformSize];
        _overlap = new double[channels][];
        for (var channel = 0; channel < channels; channel++)
        {
            _overlap[channel] = new double[frameSamples];
        }

        _imdct = CreateImdctMatrix(_transformSize);
    }

    internal int Channels => _channels;

    internal int FrameSamples => _frameSamples;

    internal void Reset()
    {
        for (var channel = 0; channel < _channels; channel++)
        {
            Array.Clear(_overlap[channel]);
        }
    }

    /// <summary>
    /// Synthesizes one frame. Coefficients are laid out as channel-major blocks
    /// of N values, where N is the number of MDCT bins for this frame.
    /// </summary>
    internal void Synthesize(ReadOnlySpan<double> coefficients, Span<int> destination)
    {
        var coefficientCount = _frameSamples * _channels;
        if (coefficients.Length < coefficientCount)
        {
            throw new ArgumentException("The coefficient buffer is too small.", nameof(coefficients));
        }

        if (destination.Length < coefficientCount)
        {
            throw new ArgumentException("The PCM32 destination is too small.", nameof(destination));
        }

        for (var channel = 0; channel < _channels; channel++)
        {
            var channelCoefficients = coefficients.Slice(channel * _frameSamples, _frameSamples);
            InverseMdct(channelCoefficients, _time);

            var previous = _overlap[channel];
            var outputOffset = channel;
            for (var sample = 0; sample < _frameSamples; sample++)
            {
                var value = previous[sample] + _time[sample] * _window[sample];
                destination[outputOffset] = QuantizePcm32(value);
                outputOffset += _channels;
            }

            for (var sample = 0; sample < _frameSamples; sample++)
            {
                previous[sample] = _time[sample + _frameSamples] * _window[sample + _frameSamples];
            }
        }
    }

    private void InverseMdct(ReadOnlySpan<double> coefficients, Span<double> destination)
    {
        var n = _frameSamples;
        var scale = 2d / n;
        for (var sample = 0; sample < destination.Length; sample++)
        {
            var sum = 0d;
            for (var bin = 0; bin < n; bin++)
            {
                sum += coefficients[bin] * _imdct[sample][bin];
            }

            destination[sample] = sum * scale;
        }
    }

    private static double[] CreateSineWindow(int length)
    {
        var window = new double[length];
        for (var i = 0; i < length; i++)
        {
            window[i] = Math.Sin(Math.PI / length * (i + 0.5));
        }

        return window;
    }

    private static double[][] CreateImdctMatrix(int length)
    {
        var matrix = new double[length][];
        var n = length / 2;
        for (var sample = 0; sample < length; sample++)
        {
            var row = new double[n];
            var phase = Math.PI / n * (sample + 0.5 + n / 2d);
            for (var bin = 0; bin < n; bin++)
            {
                row[bin] = Math.Cos(phase * (bin + 0.5));
            }

            matrix[sample] = row;
        }

        return matrix;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int QuantizePcm32(double value)
    {
        var scaled = value * 2147483647d;
        if (scaled >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (scaled <= int.MinValue)
        {
            return int.MinValue;
        }

        return (int)Math.Round(scaled, MidpointRounding.ToEven);
    }
}
