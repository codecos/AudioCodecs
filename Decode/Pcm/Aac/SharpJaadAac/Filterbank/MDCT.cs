using SharpJaad.AAC;

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SharpJaad.AAC.Filterbank
{
    public class MDCT
    {
        private int _N, _N2, _N4, _N8;
        private float[] _sincosRe;
        private float[] _sincosIm;
        private FFT _fft;
        private float[] _bufRe;
        private float[] _bufIm;

        public MDCT(int length)
        {
            _N = length;
            _N2 = length >> 1;
            _N4 = length >> 2;
            _N8 = length >> 3;
            float[][] sincos;
            switch (length)
            {
                case 2048:
                    sincos = MDCTTables.MDCT_TABLE_2048;
                    break;
                case 256:
                    sincos = MDCTTables.MDCT_TABLE_128;
                    break;
                case 1920:
                    sincos = MDCTTables.MDCT_TABLE_1920;
                    break;
                case 240:
                    sincos = MDCTTables.MDCT_TABLE_240;
                    break;
                default:
                    throw new AACException("unsupported MDCT length: " + length);
            }

            _sincosRe = new float[_N4];
            _sincosIm = new float[_N4];
            for (int i = 0; i < _N4; i++)
            {
                _sincosRe[i] = sincos[i][0];
                _sincosIm[i] = sincos[i][1];
            }

            _fft = new FFT(_N4);
            _bufRe = new float[_N4];
            _bufIm = new float[_N4];
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void Process(float[] input, int inOff, float[] output, int outOff)
        {
            int k;
            int n2 = _N2;
            int n4 = _N4;
            int n8 = _N8;
            float[] sincosReTable = _sincosRe;
            float[] sincosImTable = _sincosIm;
            float[] bufRe = _bufRe;
            float[] bufIm = _bufIm;

            //pre-IFFT complex multiplication
            if (Avx2.IsSupported && n4 >= 8)
            {
                ProcessPreMultiplyAvx2(input, inOff, bufRe, bufIm, n2, n4);
                k = n4;
            }
            else
            {
                for (k = 0; k < n4; k++)
                {
                    float even = input[inOff + 2 * k];
                    float odd = input[inOff + n2 - 1 - 2 * k];
                    float sincosRe = sincosReTable[k];
                    float sincosIm = sincosImTable[k];
                    bufIm[k] = even * sincosRe + odd * sincosIm;
                    bufRe[k] = odd * sincosRe - even * sincosIm;
                }
            }

            //complex IFFT, non-scaling
            _fft.Process(bufRe, bufIm, false);

            // Fuse post-IFFT complex multiplication with output reordering. The
            // four source regions are contiguous for a block of eight k positions
            // (four iterations of the original k += 2 loop);
            // process those regions with AVX2 and retain the exact scalar mapping
            // for the tail (and for non-AVX2 targets).
            if (Avx2.IsSupported && n8 >= 8)
            {
                ProcessPostMultiplyAvx2(output, outOff, bufRe, bufIm, sincosReTable, sincosImTable, n2, n4, n8);
                k = n8 & ~7;
            }
            else
            {
                k = 0;
            }

            for (; k < n8; k += 2)
            {
                int idx0 = k;
                int idx1 = k + 1;
                int idx2 = n8 + k;
                int idx3 = idx2 + 1;
                int idx4 = n8 - 1 - k;
                int idx5 = idx4 - 1;
                int idx6 = n4 - 1 - k;
                int idx7 = idx6 - 1;

                float sin0Re = sincosReTable[idx0];
                float sin0Im = sincosImTable[idx0];
                float re0 = bufRe[idx0];
                float im0 = bufIm[idx0];
                float post0Re = re0 * sin0Re - im0 * sin0Im;
                float post0Im = im0 * sin0Re + re0 * sin0Im;

                float sin1Re = sincosReTable[idx1];
                float sin1Im = sincosImTable[idx1];
                float re1 = bufRe[idx1];
                float im1 = bufIm[idx1];
                float post1Re = re1 * sin1Re - im1 * sin1Im;
                float post1Im = im1 * sin1Re + re1 * sin1Im;

                float sin2Re = sincosReTable[idx2];
                float sin2Im = sincosImTable[idx2];
                float re2 = bufRe[idx2];
                float im2 = bufIm[idx2];
                float post2Re = re2 * sin2Re - im2 * sin2Im;
                float post2Im = im2 * sin2Re + re2 * sin2Im;

                float sin3Re = sincosReTable[idx3];
                float sin3Im = sincosImTable[idx3];
                float re3 = bufRe[idx3];
                float im3 = bufIm[idx3];
                float post3Re = re3 * sin3Re - im3 * sin3Im;
                float post3Im = im3 * sin3Re + re3 * sin3Im;

                float sin4Re = sincosReTable[idx4];
                float sin4Im = sincosImTable[idx4];
                float re4 = bufRe[idx4];
                float im4 = bufIm[idx4];
                float post4Re = re4 * sin4Re - im4 * sin4Im;
                float post4Im = im4 * sin4Re + re4 * sin4Im;

                float sin5Re = sincosReTable[idx5];
                float sin5Im = sincosImTable[idx5];
                float re5 = bufRe[idx5];
                float im5 = bufIm[idx5];
                float post5Re = re5 * sin5Re - im5 * sin5Im;
                float post5Im = im5 * sin5Re + re5 * sin5Im;

                float sin6Re = sincosReTable[idx6];
                float sin6Im = sincosImTable[idx6];
                float re6 = bufRe[idx6];
                float im6 = bufIm[idx6];
                float post6Re = re6 * sin6Re - im6 * sin6Im;
                float post6Im = im6 * sin6Re + re6 * sin6Im;

                float sin7Re = sincosReTable[idx7];
                float sin7Im = sincosImTable[idx7];
                float re7 = bufRe[idx7];
                float im7 = bufIm[idx7];
                float post7Re = re7 * sin7Re - im7 * sin7Im;
                float post7Im = im7 * sin7Re + re7 * sin7Im;

                int out0 = outOff + (k << 1);
                int out1 = outOff + n4 + (k << 1);
                int out2 = outOff + n2 + (k << 1);
                int out3 = outOff + n2 + n4 + (k << 1);

                output[out0] = post2Im;
                output[out0 + 1] = -post4Re;
                output[out0 + 2] = post3Im;
                output[out0 + 3] = -post5Re;

                output[out1] = post0Re;
                output[out1 + 1] = -post6Im;
                output[out1 + 2] = post1Re;
                output[out1 + 3] = -post7Im;

                output[out2] = post2Re;
                output[out2 + 1] = -post4Im;
                output[out2 + 2] = post3Re;
                output[out2 + 3] = -post5Im;

                output[out3] = -post0Im;
                output[out3 + 1] = post6Re;
                output[out3 + 2] = -post1Im;
                output[out3 + 3] = post7Re;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void PostMultiplyBlockAvx2(
            float* reBase, float* imBase, float* sinReBase, float* sinImBase,
            int offset, bool reverse, float* postRe, float* postIm)
        {
            var valuesRe = Avx.LoadVector256(reBase + offset);
            var valuesIm = Avx.LoadVector256(imBase + offset);
            var sinRe = Avx.LoadVector256(sinReBase + offset);
            var sinIm = Avx.LoadVector256(sinImBase + offset);
            if (reverse)
            {
                var reverseLanes = Vector256.Create(7, 6, 5, 4, 3, 2, 1, 0);
                valuesRe = Avx2.PermuteVar8x32(valuesRe, reverseLanes);
                valuesIm = Avx2.PermuteVar8x32(valuesIm, reverseLanes);
                sinRe = Avx2.PermuteVar8x32(sinRe, reverseLanes);
                sinIm = Avx2.PermuteVar8x32(sinIm, reverseLanes);
            }

            var productRe = Avx.Subtract(Avx.Multiply(valuesRe, sinRe), Avx.Multiply(valuesIm, sinIm));
            var productIm = Avx.Add(Avx.Multiply(valuesIm, sinRe), Avx.Multiply(valuesRe, sinIm));
            Avx.Store(postRe, productRe);
            Avx.Store(postIm, productIm);
        }

        private static unsafe void ProcessPostMultiplyAvx2(
            float[] output, int outOff, float[] bufRe, float[] bufIm,
            float[] sincosReTable, float[] sincosImTable, int n2, int n4, int n8)
        {
            Span<float> post0Re = stackalloc float[8];
            Span<float> post0Im = stackalloc float[8];
            Span<float> post2Re = stackalloc float[8];
            Span<float> post2Im = stackalloc float[8];
            Span<float> post4Re = stackalloc float[8];
            Span<float> post4Im = stackalloc float[8];
            Span<float> post6Re = stackalloc float[8];
            Span<float> post6Im = stackalloc float[8];

            fixed (float* reBase = bufRe)
            fixed (float* imBase = bufIm)
            fixed (float* sinReBase = sincosReTable)
            fixed (float* sinImBase = sincosImTable)
            fixed (float* post0ReBase = post0Re)
            fixed (float* post0ImBase = post0Im)
            fixed (float* post2ReBase = post2Re)
            fixed (float* post2ImBase = post2Im)
            fixed (float* post4ReBase = post4Re)
            fixed (float* post4ImBase = post4Im)
            fixed (float* post6ReBase = post6Re)
            fixed (float* post6ImBase = post6Im)
            {
                for (int k = 0; k <= n8 - 8; k += 8)
                {
                    PostMultiplyBlockAvx2(reBase, imBase, sinReBase, sinImBase, k, false, post0ReBase, post0ImBase);
                    PostMultiplyBlockAvx2(reBase, imBase, sinReBase, sinImBase, n8 + k, false, post2ReBase, post2ImBase);
                    PostMultiplyBlockAvx2(reBase, imBase, sinReBase, sinImBase, n8 - 8 - k, true, post4ReBase, post4ImBase);
                    PostMultiplyBlockAvx2(reBase, imBase, sinReBase, sinImBase, n4 - 8 - k, true, post6ReBase, post6ImBase);

                    for (int j = 0; j < 8; j += 2)
                    {
                        int outK = k + j;
                        int lane = j;
                        int out0 = outOff + (outK << 1);
                        int out1 = outOff + n4 + (outK << 1);
                        int out2 = outOff + n2 + (outK << 1);
                        int out3 = outOff + n2 + n4 + (outK << 1);

                        output[out0] = post2Im[lane];
                        output[out0 + 1] = -post4Re[lane];
                        output[out0 + 2] = post2Im[lane + 1];
                        output[out0 + 3] = -post4Re[lane + 1];

                        output[out1] = post0Re[lane];
                        output[out1 + 1] = -post6Im[lane];
                        output[out1 + 2] = post0Re[lane + 1];
                        output[out1 + 3] = -post6Im[lane + 1];

                        output[out2] = post2Re[lane];
                        output[out2 + 1] = -post4Im[lane];
                        output[out2 + 2] = post2Re[lane + 1];
                        output[out2 + 3] = -post4Im[lane + 1];

                        output[out3] = -post0Im[lane];
                        output[out3 + 1] = post6Re[lane];
                        output[out3 + 2] = -post0Im[lane + 1];
                        output[out3 + 3] = post6Re[lane + 1];
                    }
                }
            }
        }

        private unsafe void ProcessPreMultiplyAvx2(float[] input, int inputOffset, float[] bufRe, float[] bufIm, int n2, int n4)
        {
            var evenIndices = Vector256.Create(0, 2, 4, 6, 8, 10, 12, 14);
            var oddIndices = Vector256.Create(n2 - 1, n2 - 3, n2 - 5, n2 - 7, n2 - 9, n2 - 11, n2 - 13, n2 - 15);
            fixed (float* inputBase = input)
            fixed (float* reBase = _sincosRe)
            fixed (float* imBase = _sincosIm)
            fixed (float* outputRe = bufRe)
            fixed (float* outputIm = bufIm)
            {
                int k = 0;
                for (; k <= n4 - 8; k += 8)
                {
                    var even = Avx2.GatherVector256(inputBase + inputOffset, evenIndices, 4);
                    var odd = Avx2.GatherVector256(inputBase + inputOffset, oddIndices, 4);
                    var sincosRe = Avx.LoadVector256(reBase + k);
                    var sincosIm = Avx.LoadVector256(imBase + k);
                    Avx.Store(outputIm + k, Avx.Add(Avx.Multiply(even, sincosRe), Avx.Multiply(odd, sincosIm)));
                    Avx.Store(outputRe + k, Avx.Subtract(Avx.Multiply(odd, sincosRe), Avx.Multiply(even, sincosIm)));
                    evenIndices = Avx2.Add(evenIndices, Vector256.Create(16));
                    oddIndices = Avx2.Subtract(oddIndices, Vector256.Create(16));
                }
                for (; k < n4; k++)
                {
                    float even = input[inputOffset + 2 * k];
                    float odd = input[inputOffset + n2 - 1 - 2 * k];
                    float sincosRe = _sincosRe[k];
                    float sincosIm = _sincosIm[k];
                    bufIm[k] = even * sincosRe + odd * sincosIm;
                    bufRe[k] = odd * sincosRe - even * sincosIm;
                }
            }
        }

        public void ProcessForward(float[] input, float[] output)
        {
            int n, k;
            //pre-FFT complex multiplication
            for (k = 0; k < _N8; k++)
            {
                n = k << 1;
                float tmpRe = input[_N - _N4 - 1 - n] + input[_N - _N4 + n];
                float tmpIm = input[_N4 + n] - input[_N4 - 1 - n];
                float sincosRe = _sincosRe[k];
                float sincosIm = _sincosIm[k];

                _bufRe[k] = (tmpRe * sincosRe + tmpIm * sincosIm) * _N;
                _bufIm[k] = (tmpIm * sincosRe - tmpRe * sincosIm) * _N;

                tmpRe = input[_N2 - 1 - n] - input[n];
                tmpIm = input[_N2 + n] + input[_N - 1 - n];
                sincosRe = _sincosRe[k + _N8];
                sincosIm = _sincosIm[k + _N8];

                _bufRe[k + _N8] = (tmpRe * sincosRe + tmpIm * sincosIm) * _N;
                _bufIm[k + _N8] = (tmpIm * sincosRe - tmpRe * sincosIm) * _N;
            }

            //complex FFT, non-scaling
            _fft.Process(_bufRe, _bufIm, true);

            //post-FFT complex multiplication
            for (k = 0; k < _N4; k++)
            {
                n = k << 1;
                float real = _bufRe[k];
                float imag = _bufIm[k];
                float sincosRe = _sincosRe[k];
                float sincosIm = _sincosIm[k];
                float tmpRe = real * sincosRe + imag * sincosIm;
                float tmpIm = imag * sincosRe - real * sincosIm;

                output[n] = -tmpRe;
                output[_N2 - 1 - n] = tmpIm;
                output[_N2 + n] = -tmpIm;
                output[_N - 1 - n] = tmpRe;
            }
        }
    }
}
