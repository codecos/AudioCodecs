using SharpJaad.AAC;

using System.Runtime.CompilerServices;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SharpJaad.AAC.Filterbank
{
    public class FFT
    {
        private int _length;
        private int[] _bitReverse;
        private float[] _rootRe;
        private float[] _rootImForward;
        private float[] _rootImInverse;

        public FFT(int length)
        {
            _length = length;
            float[,] roots;

            switch (length)
            {
                case 64:
                    roots = FFTables.FFT_TABLE_64;
                    break;
                case 512:
                    roots = FFTables.FFT_TABLE_512;
                    break;
                case 60:
                    roots = FFTables.FFT_TABLE_60;
                    break;
                case 480:
                    roots = FFTables.FFT_TABLE_480;
                    break;
                default:
                    throw new AACException("unexpected FFT length: " + length);
            }

            _rootRe = new float[length];
            _rootImForward = new float[length];
            _rootImInverse = new float[length];
            _bitReverse = new int[length];
            var rootColumns = roots.GetLength(1);
            for (int i = 0; i < length; i++)
            {
                _rootRe[i] = roots[i, 0];
                _rootImInverse[i] = roots[i, 1];
                _rootImForward[i] = rootColumns > 2 ? roots[i, 2] : -roots[i, 1];
            }

            int reversedIndex = 0;
            for (int i = 0; i < length; i++)
            {
                _bitReverse[i] = reversedIndex;
                int k = length >> 1;
                while (reversedIndex >= k && k > 0)
                {
                    reversedIndex -= k;
                    k >>= 1;
                }

                reversedIndex += k;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Process(float[] real, float[] imag, bool forward)
        {
            float[] rootImag = forward ? _rootImForward : _rootImInverse;
            //bit-reversal
            for (int i = 0; i < _length; i++)
            {
                int sourceIndex = _bitReverse[i];
                if (sourceIndex > i)
                {
                    float tmpRe = real[i];
                    real[i] = real[sourceIndex];
                    real[sourceIndex] = tmpRe;

                    float tmpIm = imag[i];
                    imag[i] = imag[sourceIndex];
                    imag[sourceIndex] = tmpIm;
                }
            }

            //bottom base-4 round
            for (int i = 0; i < _length; i += 4)
            {
                float aRe = real[i] + real[i + 1];
                float aIm = imag[i] + imag[i + 1];
                float bRe = real[i + 2] + real[i + 3];
                float bIm = imag[i + 2] + imag[i + 3];
                float cRe = real[i] - real[i + 1];
                float cIm = imag[i] - imag[i + 1];
                float dRe = real[i + 2] - real[i + 3];
                float dIm = imag[i + 2] - imag[i + 3];
                real[i] = aRe + bRe;
                imag[i] = aIm + bIm;
                real[i + 2] = aRe - bRe;
                imag[i + 2] = aIm - bIm;

                float e1Re = cRe - dIm;
                float e1Im = cIm + dRe;
                float e2Re = cRe + dIm;
                float e2Im = cIm - dRe;
                if (forward)
                {
                    real[i + 1] = e2Re;
                    imag[i + 1] = e2Im;
                    real[i + 3] = e1Re;
                    imag[i + 3] = e1Im;
                }
                else
                {
                    real[i + 1] = e1Re;
                    imag[i + 1] = e1Im;
                    real[i + 3] = e2Re;
                    imag[i + 3] = e2Im;
                }
            }

            //iterations from bottom to top
            int shift, m, km;
            float rootRe, rootIm, zRe, zIm;
            for (int i = 4; i < _length; i <<= 1)
            {
                shift = i << 1;
                m = _length / shift;

                // At the final radix-2 stage twiddle indices are contiguous
                // (m == 1). Process several butterflies at once; earlier
                // stages use strided twiddles and remain on the scalar path.
                if (m == 1)
                {
                    int vectorWidth = Vector<float>.Count;
                    for (int j = 0; j < _length; j += shift)
                    {
                        int k = 0;
                        for (; k <= i - vectorWidth; k += vectorWidth)
                        {
                            int lower = j + k;
                            int upper = lower + i;
                            var upperRe = new Vector<float>(real, upper);
                            var upperIm = new Vector<float>(imag, upper);
                            var rootReVector = new Vector<float>(_rootRe, k);
                            var rootImVector = new Vector<float>(rootImag, k);
                            var zReVector = (upperRe * rootReVector) - (upperIm * rootImVector);
                            var zImVector = (upperRe * rootImVector) + (upperIm * rootReVector);
                            var lowerReVector = new Vector<float>(real, lower);
                            var lowerImVector = new Vector<float>(imag, lower);
                            (lowerReVector - zReVector).CopyTo(real, upper);
                            (lowerImVector - zImVector).CopyTo(imag, upper);
                            (lowerReVector + zReVector).CopyTo(real, lower);
                            (lowerImVector + zImVector).CopyTo(imag, lower);
                        }

                        for (; k < i; k++)
                        {
                            int scalarKm = k;
                            int upperIndex = i + j + k;
                            int lowerIndex = j + k;
                            float scalarZRe = real[upperIndex] * _rootRe[scalarKm] - imag[upperIndex] * rootImag[scalarKm];
                            float scalarZIm = real[upperIndex] * rootImag[scalarKm] + imag[upperIndex] * _rootRe[scalarKm];
                            float lowerRe = real[lowerIndex];
                            float lowerIm = imag[lowerIndex];
                            real[upperIndex] = lowerRe - scalarZRe;
                            imag[upperIndex] = lowerIm - scalarZIm;
                            real[lowerIndex] = lowerRe + scalarZRe;
                            imag[lowerIndex] = lowerIm + scalarZIm;
                        }
                    }
                    continue;
                }

                if (m == 2 && Avx2.IsSupported && _length >= 32)
                {
                    ProcessStride2Avx2(real, imag, forward, i, shift);
                    continue;
                }

                for (int j = 0; j < _length; j += shift)
                {
                    for (int k = 0; k < i; k++)
                    {
                        km = k * m;
                        rootRe = _rootRe[km];
                        rootIm = rootImag[km];
                        int upperIndex = i + j + k;
                        int lowerIndex = j + k;
                        zRe = real[upperIndex] * rootRe - imag[upperIndex] * rootIm;
                        zIm = real[upperIndex] * rootIm + imag[upperIndex] * rootRe;
                        float lowerRe = real[lowerIndex];
                        float lowerIm = imag[lowerIndex];

                        real[upperIndex] = lowerRe - zRe;
                        imag[upperIndex] = lowerIm - zIm;
                        real[lowerIndex] = lowerRe + zRe;
                        imag[lowerIndex] = lowerIm + zIm;
                    }
                }
            }
        }

        private unsafe void ProcessStride2Avx2(float[] real, float[] imag, bool forward, int butterflyWidth, int shift)
        {
            var rootImag = forward ? _rootImForward : _rootImInverse;
            var rootIndices = Vector256.Create(0, 2, 4, 6, 8, 10, 12, 14);
            fixed (float* realBase = real)
            fixed (float* imagBase = imag)
            fixed (float* rootReBase = _rootRe)
            fixed (float* rootImBase = rootImag)
            {
                for (int j = 0; j < _length; j += shift)
                {
                    int k = 0;
                    for (; k <= butterflyWidth - 8; k += 8)
                    {
                        int lower = j + k;
                        int upper = lower + butterflyWidth;
                        var upperRe = Avx.LoadVector256(upper + realBase);
                        var upperIm = Avx.LoadVector256(upper + imagBase);
                        var rootRe = Avx2.GatherVector256(rootReBase + (k << 1), rootIndices, 4);
                        var rootIm = Avx2.GatherVector256(rootImBase + (k << 1), rootIndices, 4);
                        var zRe = Avx.Subtract(Avx.Multiply(upperRe, rootRe), Avx.Multiply(upperIm, rootIm));
                        var zIm = Avx.Add(Avx.Multiply(upperRe, rootIm), Avx.Multiply(upperIm, rootRe));
                        var lowerRe = Avx.LoadVector256(lower + realBase);
                        var lowerIm = Avx.LoadVector256(lower + imagBase);
                        Avx.Store(upper + realBase, Avx.Subtract(lowerRe, zRe));
                        Avx.Store(upper + imagBase, Avx.Subtract(lowerIm, zIm));
                        Avx.Store(lower + realBase, Avx.Add(lowerRe, zRe));
                        Avx.Store(lower + imagBase, Avx.Add(lowerIm, zIm));
                    }
                    for (; k < butterflyWidth; k++)
                    {
                        int upperIndex = j + butterflyWidth + k;
                        int lowerIndex = j + k;
                        int rootIndex = k << 1;
                        float rootIm = rootImag[rootIndex];
                        float scalarZRe = real[upperIndex] * _rootRe[rootIndex] - imag[upperIndex] * rootIm;
                        float scalarZIm = real[upperIndex] * rootIm + imag[upperIndex] * _rootRe[rootIndex];
                        float lowerRe = real[lowerIndex];
                        float lowerIm = imag[lowerIndex];
                        real[upperIndex] = lowerRe - scalarZRe;
                        imag[upperIndex] = lowerIm - scalarZIm;
                        real[lowerIndex] = lowerRe + scalarZRe;
                        imag[lowerIndex] = lowerIm + scalarZIm;
                    }
                }
            }
        }
    }
}
