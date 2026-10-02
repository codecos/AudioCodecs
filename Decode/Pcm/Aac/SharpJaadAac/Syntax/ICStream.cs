using SharpJaad.AAC.Error;
using SharpJaad.AAC.Gain;
using SharpJaad.AAC.Huffman;
using SharpJaad.AAC.Tools;
using System;
using System.Diagnostics;

namespace SharpJaad.AAC.Syntax
{
    public class ICStream
    {
        private const int SF_DELTA = 60;
        private const int SF_OFFSET = 200;
        private static int randomState = 0x1F2E3D4C;
        private int _frameLength;
        //always needed
        private ICSInfo _info;
        private int[] _sfbCB;
        private int[] _sectEnd;
        private float[] _data;
        private float[] _scaleFactors;
        private int _globalGain;
        private bool _pulseDataPresent, _tnsDataPresent, _gainControlPresent;
        //only allocated if needed
        private TNS _tns;
        private GainControl _gainControl;
        private int[] _pulseOffset, _pulseAmp;
        private int _pulseCount;
        private int _pulseStartSWB;
        //error resilience
#pragma warning disable CS0649 // Field 'ICStream._noiseUsed' is never assigned to, and will always have its default value false
        private bool _noiseUsed;
#pragma warning restore CS0649 // Field 'ICStream._noiseUsed' is never assigned to, and will always have its default value false
        private int _reorderedSpectralDataLen, _longestCodewordLen;
        private RVLC _rvlc;

        public ICStream(DecoderConfig config)
        {
            _frameLength = config.GetFrameLength();
            _info = new ICSInfo(config);
            _sfbCB = new int[Constants.MAX_SECTIONS];
            _sectEnd = new int[Constants.MAX_SECTIONS];
            _data = new float[_frameLength];
            _scaleFactors = new float[Constants.MAX_SECTIONS];
            _tns = new TNS();
            _pulseOffset = new int[4];
            _pulseAmp = new int[4];
        }

        /* ========= decoding ========== */
        public void Decode(BitStream input, bool commonWindow, DecoderConfig conf)
        {
            if (conf.IsScalefactorResilienceUsed() && _rvlc == null) _rvlc = new RVLC();
            bool er = conf.GetProfile().IsErrorResilientProfile();
            bool profiling = DecoderProfiling.Enabled;
            long stageStart = 0;

            _globalGain = input.ReadBits(8);

            if (!commonWindow)
            {
                if (profiling) stageStart = Stopwatch.GetTimestamp();
                _info.Decode(input, conf, commonWindow);
                if (profiling) DecoderProfiling.AddIcsInfoTicks(Stopwatch.GetTimestamp() - stageStart);
            }

            if (profiling) stageStart = Stopwatch.GetTimestamp();
            DecodeSectionData(input, conf.IsSectionDataResilienceUsed());
            if (profiling) DecoderProfiling.AddSectionTicks(Stopwatch.GetTimestamp() - stageStart);

            //if(conf.isScalefactorResilienceUsed()) rvlc.decode(in, this, scaleFactors);
            /*else*/
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            DecodeScaleFactors(input);
            if (profiling) DecoderProfiling.AddScaleFactorTicks(Stopwatch.GetTimestamp() - stageStart);

            _pulseDataPresent = input.ReadBool();
            if (_pulseDataPresent)
            {
                if (_info.IsEightShortFrame()) throw new AACException("pulse data not allowed for short frames");
                //LOGGER.log(Level.FINE, "PULSE");
                DecodePulseData(input);
            }

            _tnsDataPresent = input.ReadBool();
            if (_tnsDataPresent && !er)
            {
                if (profiling) stageStart = Stopwatch.GetTimestamp();
                _tns.Decode(input, _info);
                if (profiling) DecoderProfiling.AddTnsTicks(Stopwatch.GetTimestamp() - stageStart);
            }

            _gainControlPresent = input.ReadBool();
            if (_gainControlPresent)
            {
                if (_gainControl == null) _gainControl = new GainControl(_frameLength);
                //LOGGER.log(Level.FINE, "GAIN");
                if (profiling) stageStart = Stopwatch.GetTimestamp();
                _gainControl.Decode(input, _info.GetWindowSequence());
                if (profiling) DecoderProfiling.AddGainControlTicks(Stopwatch.GetTimestamp() - stageStart);
            }

            //RVLC spectral data
            //if(conf.isScalefactorResilienceUsed()) rvlc.decodeScalefactors(this, in, scaleFactors);

            if (conf.IsSpectralDataResilienceUsed())
            {
                int max = conf.GetChannelConfiguration() == ChannelConfiguration.CHANNEL_CONFIG_STEREO ? 6144 : 12288;
                _reorderedSpectralDataLen = Math.Max(input.ReadBits(14), max);
                _longestCodewordLen = Math.Max(input.ReadBits(6), 49);
                //HCR.decodeReorderedSpectralData(this, in, data, conf.isSectionDataResilienceUsed());
            }
            else
            {
                if (profiling) stageStart = Stopwatch.GetTimestamp();
                DecodeSpectralData(input);
                if (profiling) DecoderProfiling.AddSpectralTicks(Stopwatch.GetTimestamp() - stageStart);
            }
        }

        internal void ResetStreamState()
        {
            _info.ResetStreamState();
            Array.Clear(_data);
            _pulseDataPresent = false;
            _tnsDataPresent = false;
            _gainControlPresent = false;
            _pulseCount = 0;
            _pulseStartSWB = 0;
        }

        public void DecodeSectionData(BitStream input, bool sectionDataResilienceUsed)
        {
            Arrays.Fill(_sfbCB, 0);
            Arrays.Fill(_sectEnd, 0);
            int bits = _info.IsEightShortFrame() ? 3 : 5;
            int escVal = (1 << bits) - 1;

            int windowGroupCount = _info.GetWindowGroupCount();
            int maxSFB = _info.GetMaxSFB();

            int end, cb, incr;
            int idx = 0;

            for (int g = 0; g < windowGroupCount; g++)
            {
                int k = 0;
                while (k < maxSFB)
                {
                    end = k;
                    cb = input.ReadBits(4);
                    if (cb == 12) throw new AACException("invalid huffman codebook: 12");
                    while ((incr = input.ReadBits(bits)) == escVal)
                    {
                        end += incr;
                    }
                    end += incr;
                    if (end > maxSFB) throw new AACException("too many bands: " + end + ", allowed: " + maxSFB);
                    for (; k < end; k++)
                    {
                        _sfbCB[idx] = cb;
                        _sectEnd[idx++] = end;
                    }
                }
            }
        }

        private void DecodePulseData(BitStream input)
        {
            _pulseCount = input.ReadBits(2) + 1;
            _pulseStartSWB = input.ReadBits(6);
            if (_pulseStartSWB >= _info.GetSWBCount()) throw new AACException("pulse SWB out of range: " + _pulseStartSWB + " > " + _info.GetSWBCount());

            _pulseOffset[0] = _info.GetSWBOffsets()[_pulseStartSWB];
            _pulseOffset[0] += input.ReadBits(5);
            _pulseAmp[0] = input.ReadBits(4);
            for (int i = 1; i < _pulseCount; i++)
            {
                _pulseOffset[i] = input.ReadBits(5) + _pulseOffset[i - 1];
                if (_pulseOffset[i] > 1023) throw new AACException("pulse offset out of range: " + _pulseOffset[0]);
                _pulseAmp[i] = input.ReadBits(4);
            }
        }

        public void DecodeScaleFactors(BitStream input)
        {
            int windowGroups = _info.GetWindowGroupCount();
            int maxSFB = _info.GetMaxSFB();
            int spectrumOffset = _globalGain;
            int noiseOffset = _globalGain - 90;
            int intensityOffset = 0;

            int tmp;
            bool noiseFlag = true;

            int sfb, idx = 0;
            for (int g = 0; g < windowGroups; g++)
            {
                for (sfb = 0; sfb < maxSFB;)
                {
                    int end = _sectEnd[idx];
                    switch (_sfbCB[idx])
                    {
                        case HCB.ZERO_HCB:
                            for (; sfb < end; sfb++, idx++)
                            {
                                _scaleFactors[idx] = 0;
                            }
                            break;
                        case HCB.INTENSITY_HCB:
                        case HCB.INTENSITY_HCB2:
                            for (; sfb < end; sfb++, idx++)
                            {
                                intensityOffset += HuffmanDec.DecodeScaleFactor(input) - SF_DELTA;
                                tmp = Math.Min(Math.Max(intensityOffset, -155), 100);
                                _scaleFactors[idx] = ScaleFactorTable.SCALEFACTOR_TABLE[-tmp + SF_OFFSET];
                            }
                            break;
                        case HCB.NOISE_HCB:
                            for (; sfb < end; sfb++, idx++)
                            {
                                if (noiseFlag)
                                {
                                    noiseOffset += input.ReadBits(9) - 256;
                                    noiseFlag = false;
                                }
                                else noiseOffset += HuffmanDec.DecodeScaleFactor(input) - SF_DELTA;
                                tmp = Math.Min(Math.Max(noiseOffset, -100), 155);
                                _scaleFactors[idx] = -ScaleFactorTable.SCALEFACTOR_TABLE[tmp + SF_OFFSET];
                            }
                            break;
                        default:
                            for (; sfb < end; sfb++, idx++)
                            {
                                spectrumOffset += HuffmanDec.DecodeScaleFactor(input) - SF_DELTA;
                                if (spectrumOffset > 255) throw new AACException("scalefactor out of range: " + spectrumOffset);
                                _scaleFactors[idx] = ScaleFactorTable.SCALEFACTOR_TABLE[spectrumOffset - 100 + SF_OFFSET];
                            }
                            break;
                    }
                }
            }
        }

        private void DecodeSpectralData(BitStream input)
        {
            Array.Clear(_data, 0, _data.Length);
            int maxSFB = _info.GetMaxSFB();
            int windowGroups = _info.GetWindowGroupCount();
            int[] offsets = _info.GetSWBOffsets();
            float[] data = _data;
            float[] scaleFactors = _scaleFactors;

            int sfb, k, w, hcb, off, width, num;
            int groupOff = 0, idx = 0;
            for (int g = 0; g < windowGroups; g++)
            {
                int groupLen = _info.GetWindowGroupLength(g);

                for (sfb = 0; sfb < maxSFB; sfb++, idx++)
                {
                    hcb = _sfbCB[idx];
                    off = groupOff + offsets[sfb];
                    width = offsets[sfb + 1] - offsets[sfb];
                    if (hcb == HCB.ZERO_HCB || hcb == HCB.INTENSITY_HCB || hcb == HCB.INTENSITY_HCB2)
                    {
                        for (w = 0; w < groupLen; w++, off += 128)
                        {
                            data.AsSpan(off, width).Clear();
                        }
                    }
                    else if (hcb == HCB.NOISE_HCB)
                    {
                        //apply PNS: fill with random values
                        for (w = 0; w < groupLen; w++, off += 128)
                        {
                            float energy = 0;

                            for (k = 0; k < width; k++)
                            {
                                randomState = 1664525 * randomState + 1013904223;
                                data[off + k] = randomState;
                                energy += data[off + k] * data[off + k];
                            }

                            float scale = (float)(scaleFactors[idx] / Math.Sqrt(energy));
                            for (k = 0; k < width; k++)
                            {
                                data[off + k] *= scale;
                            }
                        }
                    }
                    else
                    {
                        float scale = scaleFactors[idx];
                        for (w = 0; w < groupLen; w++, off += 128)
                        {
                            num = hcb >= HCB.FIRST_PAIR_HCB ? 2 : 4;
                            for (k = 0; k < width; k += num)
                            {
                                HuffmanDec.DecodeSpectralDataToFloat(input, hcb, data, off + k, scale);
                            }
                        }
                    }
                }
                groupOff += groupLen << 7;
            }
        }

        /* =========== gets ============ */
        /**
		 * Does inverse quantization and applies the scale factors on the decoded
		 * data. After this the noiseless decoding is finished and the decoded data
		 * is returned.
		 * @return the inverse quantized and scaled data
		 */
        public float[] GetInvQuantData()
        {
            return _data;
        }

        public ICSInfo GetInfo()
        {
            return _info;
        }

        public int[] GetSectEnd()
        {
            return _sectEnd;
        }

        public int[] getSfbCB()
        {
            return _sfbCB;
        }

        public float[] GetScaleFactors()
        {
            return _scaleFactors;
        }

        public bool IsTNSDataPresent()
        {
            return _tnsDataPresent;
        }

        public TNS GetTNS()
        {
            return _tns;
        }

        public int GetGlobalGain()
        {
            return _globalGain;
        }

        public bool IsNoiseUsed()
        {
            return _noiseUsed;
        }

        public int GetLongestCodewordLength()
        {
            return _longestCodewordLen;
        }

        public int GetReorderedSpectralDataLength()
        {
            return _reorderedSpectralDataLen;
        }

        public bool IsGainControlPresent()
        {
            return _gainControlPresent;
        }

        public GainControl GetGainControl()
        {
            return _gainControl;
        }
    }
}
