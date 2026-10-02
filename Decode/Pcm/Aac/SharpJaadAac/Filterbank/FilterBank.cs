using SharpJaad.AAC.Syntax;
using static SharpJaad.AAC.Syntax.ICSInfo;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SharpJaad.AAC.Filterbank
{
    public class FilterBank
    {
        private float[][] _LONG_WINDOWS;// = {SINE_LONG, KBD_LONG};
        private float[][] _LONG_WINDOWS_REVERSED;
        private float[][] _SHORT_WINDOWS;// = {SINE_SHORT, KBD_SHORT};
        private float[][] _SHORT_WINDOWS_REVERSED;
        private int _length;
        private int _shortLen;
        private int _mid;
        private int _trans;
        private MDCT _mdctShort, _mdctLong;
        private float[] _buf;
        private float[][] _overlaps;

        public FilterBank(bool smallFrames, int channels)
        {
            if (smallFrames)
            {
                _length = Constants.WINDOW_SMALL_LEN_LONG;
                _shortLen = Constants.WINDOW_SMALL_LEN_SHORT;
                _LONG_WINDOWS = new float[][] { SineWindows.SINE_960, KBDWindows.KBD_960 };
                _SHORT_WINDOWS = new float[][] { SineWindows.SINE_120, KBDWindows.KBD_120 };
            }
            else
            {
                _length = Constants.WINDOW_LEN_LONG;
                _shortLen = Constants.WINDOW_LEN_SHORT;
                _LONG_WINDOWS = new float[][] { SineWindows.SINE_1024, KBDWindows.KBD_1024 };
                _SHORT_WINDOWS = new float[][] { SineWindows.SINE_128, KBDWindows.KBD_128 };
            }
            _LONG_WINDOWS_REVERSED = new float[][]
            {
                CreateReversedWindow(_LONG_WINDOWS[0]),
                CreateReversedWindow(_LONG_WINDOWS[1])
            };
            _SHORT_WINDOWS_REVERSED = new float[][]
            {
                CreateReversedWindow(_SHORT_WINDOWS[0]),
                CreateReversedWindow(_SHORT_WINDOWS[1])
            };
            _mid = (_length - _shortLen) / 2;
            _trans = _shortLen / 2;

            _mdctShort = new MDCT(_shortLen * 2);
            _mdctLong = new MDCT(_length * 2);

            _overlaps = new float[channels][];
            for (int i = 0; i < channels; i++)
            {
                _overlaps[i] = new float[_length];
            }

            _buf = new float[2 * _length];
        }

        private static float[] CreateReversedWindow(float[] window)
        {
            float[] reversed = new float[window.Length];
            for (int i = 0, j = window.Length - 1; i < window.Length; i++, j--)
            {
                reversed[i] = window[j];
            }

            return reversed;
        }

        public void Reset()
        {
            for (int channel = 0; channel < _overlaps.Length; channel++)
            {
                Array.Clear(_overlaps[channel]);
            }

            Array.Clear(_buf);
        }

        public void Process(WindowSequence windowSequence, int windowShape, int windowShapePrev, float[] input, float[] output, int channel)
        {
            int i;
            bool profiling = DecoderProfiling.Enabled;
            long stageStart = 0;
            float[] overlap = _overlaps[channel];
            float[] longWindowRev = _LONG_WINDOWS_REVERSED[windowShape];
            float[] longWindowPrev = _LONG_WINDOWS[windowShapePrev];
            float[] shortWindow = _SHORT_WINDOWS[windowShape];
            float[] shortWindowRev = _SHORT_WINDOWS_REVERSED[windowShape];
            float[] shortWindowPrev = _SHORT_WINDOWS[windowShapePrev];
            switch (windowSequence)
            {
                case WindowSequence.ONLY_LONG_SEQUENCE:
                    if (profiling) stageStart = Stopwatch.GetTimestamp();
                    _mdctLong.Process(input, 0, _buf, 0);
                    if (profiling) DecoderProfiling.AddProcessFilterbankMdctTicks(Stopwatch.GetTimestamp() - stageStart);
                    //add second half output of previous frame to windowed output of current frame
                    if (profiling) stageStart = Stopwatch.GetTimestamp();
                    MultiplyAdd(overlap, 0, _buf, 0, longWindowPrev, output, 0, _length);

                    //window the second half and save as overlap for next frame
                    Multiply(_buf, _length, longWindowRev, overlap, 0, _length);
                    if (profiling) DecoderProfiling.AddProcessFilterbankWindowTicks(Stopwatch.GetTimestamp() - stageStart);
                    break;
                case WindowSequence.LONG_START_SEQUENCE:
                    if (profiling) stageStart = Stopwatch.GetTimestamp();
                    _mdctLong.Process(input, 0, _buf, 0);
                    if (profiling) DecoderProfiling.AddProcessFilterbankMdctTicks(Stopwatch.GetTimestamp() - stageStart);
                    //add second half output of previous frame to windowed output of current frame
                    if (profiling) stageStart = Stopwatch.GetTimestamp();
                    MultiplyAdd(overlap, 0, _buf, 0, longWindowPrev, output, 0, _length);

                    //window the second half and save as overlap for next frame
                    for (i = 0; i < _mid; i++)
                    {
                        overlap[i] = _buf[_length + i];
                    }
                    for (i = 0; i < _shortLen; i++)
                    {
                        overlap[_mid + i] = _buf[_length + _mid + i] * shortWindowRev[i];
                    }
                    Array.Clear(overlap, _mid + _shortLen, _mid);
                    if (profiling) DecoderProfiling.AddProcessFilterbankWindowTicks(Stopwatch.GetTimestamp() - stageStart);
                    break;
                case WindowSequence.EIGHT_SHORT_SEQUENCE:
                    if (profiling) stageStart = Stopwatch.GetTimestamp();
                    for (i = 0; i < 8; i++)
                    {
                        _mdctShort.Process(input, i * _shortLen, _buf, 2 * i * _shortLen);
                    }
                    if (profiling) DecoderProfiling.AddProcessFilterbankMdctTicks(Stopwatch.GetTimestamp() - stageStart);

                    //add second half output of previous frame to windowed output of current frame
                    if (profiling) stageStart = Stopwatch.GetTimestamp();
                    Array.Copy(overlap, 0, output, 0, _mid);
                    for (i = 0; i < _shortLen; i++)
                    {
                        float shortWindowValue = shortWindow[i];
                        float shortWindowRevValue = shortWindowRev[i];
                        output[_mid + i] = overlap[_mid + i] + _buf[i] * shortWindowPrev[i];
                        output[_mid + 1 * _shortLen + i] = overlap[_mid + _shortLen * 1 + i] + _buf[_shortLen * 1 + i] * shortWindowRevValue + _buf[_shortLen * 2 + i] * shortWindowValue;
                        output[_mid + 2 * _shortLen + i] = overlap[_mid + _shortLen * 2 + i] + _buf[_shortLen * 3 + i] * shortWindowRevValue + _buf[_shortLen * 4 + i] * shortWindowValue;
                        output[_mid + 3 * _shortLen + i] = overlap[_mid + _shortLen * 3 + i] + _buf[_shortLen * 5 + i] * shortWindowRevValue + _buf[_shortLen * 6 + i] * shortWindowValue;
                        if (i < _trans) output[_mid + 4 * _shortLen + i] = overlap[_mid + _shortLen * 4 + i] + _buf[_shortLen * 7 + i] * shortWindowRevValue + _buf[_shortLen * 8 + i] * shortWindowValue;
                    }

                    //window the second half and save as overlap for next frame
                    for (i = 0; i < _shortLen; i++)
                    {
                        float shortWindowValue = shortWindow[i];
                        float shortWindowRevValue = shortWindowRev[i];
                        if (i >= _trans) overlap[_mid + 4 * _shortLen + i - _length] = _buf[_shortLen * 7 + i] * shortWindowRevValue + _buf[_shortLen * 8 + i] * shortWindowValue;
                        overlap[_mid + 5 * _shortLen + i - _length] = _buf[_shortLen * 9 + i] * shortWindowRevValue + _buf[_shortLen * 10 + i] * shortWindowValue;
                        overlap[_mid + 6 * _shortLen + i - _length] = _buf[_shortLen * 11 + i] * shortWindowRevValue + _buf[_shortLen * 12 + i] * shortWindowValue;
                        overlap[_mid + 7 * _shortLen + i - _length] = _buf[_shortLen * 13 + i] * shortWindowRevValue + _buf[_shortLen * 14 + i] * shortWindowValue;
                        overlap[_mid + 8 * _shortLen + i - _length] = _buf[_shortLen * 15 + i] * shortWindowRevValue;
                    }
                    Array.Clear(overlap, _mid + _shortLen, _mid);
                    if (profiling) DecoderProfiling.AddProcessFilterbankWindowTicks(Stopwatch.GetTimestamp() - stageStart);
                    break;
                case WindowSequence.LONG_STOP_SEQUENCE:
                    if (profiling) stageStart = Stopwatch.GetTimestamp();
                    _mdctLong.Process(input, 0, _buf, 0);
                    if (profiling) DecoderProfiling.AddProcessFilterbankMdctTicks(Stopwatch.GetTimestamp() - stageStart);
                    //add second half output of previous frame to windowed output of current frame
                    //construct first half window using padding with 1's and 0's
                    if (profiling) stageStart = Stopwatch.GetTimestamp();
                    Array.Copy(overlap, 0, output, 0, _mid);
                    for (i = 0; i < _shortLen; i++)
                    {
                        output[_mid + i] = overlap[_mid + i] + _buf[_mid + i] * shortWindowPrev[i];
                    }
                    for (i = 0; i < _mid; i++)
                    {
                        output[_mid + _shortLen + i] = overlap[_mid + _shortLen + i] + _buf[_mid + _shortLen + i];
                    }
                    //window the second half and save as overlap for next frame
                    Multiply(_buf, _length, longWindowRev, overlap, 0, _length);
                    if (profiling) DecoderProfiling.AddProcessFilterbankWindowTicks(Stopwatch.GetTimestamp() - stageStart);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MultiplyAdd(float[] add, int addOffset, float[] source, int sourceOffset, float[] window, float[] destination, int destinationOffset, int count)
        {
            int width = Vector<float>.Count;
            int i = 0;
            for (; i <= count - width; i += width)
            {
                var result = new Vector<float>(add, addOffset + i)
                    + new Vector<float>(source, sourceOffset + i) * new Vector<float>(window, i);
                result.CopyTo(destination, destinationOffset + i);
            }
            for (; i < count; i++)
                destination[destinationOffset + i] = add[addOffset + i] + source[sourceOffset + i] * window[i];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Multiply(float[] source, int sourceOffset, float[] window, float[] destination, int destinationOffset, int count)
        {
            int width = Vector<float>.Count;
            int i = 0;
            for (; i <= count - width; i += width)
            {
                var result = new Vector<float>(source, sourceOffset + i) * new Vector<float>(window, i);
                result.CopyTo(destination, destinationOffset + i);
            }
            for (; i < count; i++)
                destination[destinationOffset + i] = source[sourceOffset + i] * window[i];
        }

        //only for LTP: no overlapping, no short blocks
        public void ProcessLTP(WindowSequence windowSequence, int windowShape, int windowShapePrev, float[] input, float[] output)
        {
            int i;
            float[] longWindowRev = _LONG_WINDOWS_REVERSED[windowShape];
            float[] longWindowPrev = _LONG_WINDOWS[windowShapePrev];
            float[] shortWindowRev = _SHORT_WINDOWS_REVERSED[windowShape];
            float[] shortWindowPrev = _SHORT_WINDOWS[windowShapePrev];

            switch (windowSequence)
            {
                case WindowSequence.ONLY_LONG_SEQUENCE:
                    for (i = 0; i < _length; i++)
                    {
                        _buf[i] = input[i] * longWindowPrev[i];
                        _buf[i + _length] = input[i + _length] * longWindowRev[i];
                    }
                    break;

                case WindowSequence.LONG_START_SEQUENCE:
                    for (i = 0; i < _length; i++)
                    {
                        _buf[i] = input[i] * longWindowPrev[i];
                    }
                    for (i = 0; i < _mid; i++)
                    {
                        _buf[i + _length] = input[i + _length];
                    }
                    for (i = 0; i < _shortLen; i++)
                    {
                        _buf[i + _length + _mid] = input[i + _length + _mid] * shortWindowRev[i];
                    }
                    Array.Clear(_buf, _length + _mid + _shortLen, _mid);
                    break;

                case WindowSequence.LONG_STOP_SEQUENCE:
                    Array.Clear(_buf, 0, _mid);
                    for (i = 0; i < _shortLen; i++)
                    {
                        _buf[i + _mid] = input[i + _mid] * shortWindowPrev[i];
                    }
                    for (i = 0; i < _mid; i++)
                    {
                        _buf[i + _mid + _shortLen] = input[i + _mid + _shortLen];
                    }
                    for (i = 0; i < _length; i++)
                    {
                        _buf[i + _length] = input[i + _length] * longWindowRev[i];
                    }
                    break;
            }
            _mdctLong.ProcessForward(_buf, output);
        }

        public float[] GetOverlap(int channel)
        {
            return _overlaps[channel];
        }
    }
}
