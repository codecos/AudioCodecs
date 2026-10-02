using SharpJaad.AAC.Filterbank;
using SharpJaad.AAC.Sbr;
using SharpJaad.AAC.Tools;
using System;
using System.Diagnostics;

namespace SharpJaad.AAC.Syntax
{
    public class SyntacticElements
    {
        private const float ShortPcmScale = 1f / 32768f;
        //global properties
        private DecoderConfig config;
        private bool sbrPresent, psPresent;
        private int bitsRead;
        private int frame = 0;
        //elements
        private PCE pce;
        private Element[] elements; //SCE, LFE and CPE
        private CCE[] cces;
        private DSE[] dses;
        private FIL[] fils;
        private int curElem, curCCE, curDSE, curFIL;
        private float[][] data;

        public SyntacticElements(DecoderConfig config)
        {
            this.config = config;

            pce = new PCE();
            elements = new Element[4 * Constants.MAX_ELEMENTS];
            cces = new CCE[Constants.MAX_ELEMENTS];
            dses = new DSE[Constants.MAX_ELEMENTS];
            fils = new FIL[Constants.MAX_ELEMENTS];
            for (int i = 0; i < fils.Length; i++)
            {
                fils[i] = new FIL(config.IsSBRDownSampled());
            }

            StartNewFrame();
        }

        public void StartNewFrame()
        {
            curElem = 0;
            curCCE = 0;
            curDSE = 0;
            curFIL = 0;
            sbrPresent = false;
            psPresent = false;
            bitsRead = 0;
        }

        internal void ResetStreamState()
        {
            StartNewFrame();
            frame = 0;
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i]?.ResetStreamState();
                if (elements[i] is SCE_LFE single)
                {
                    single.GetICStream().ResetStreamState();
                }
                else if (elements[i] is CPE pair)
                {
                    pair.GetLeftChannel().ResetStreamState();
                    pair.GetRightChannel().ResetStreamState();
                }
            }

            if (data is not null)
            {
                for (int channel = 0; channel < data.Length; channel++)
                {
                    Array.Clear(data[channel]);
                }
            }
        }

        public void Decode(BitStream input)
        {
            ++frame;
            int start = input.GetPosition(); //should be 0

            int type;
            Element prev = null;
            bool content = true;
            if (!config.GetProfile().IsErrorResilientProfile())
            {
                while (content && (type = input.ReadBits(3)) != Constants.ELEMENT_END)
                {
                    switch (type)
                    {
                        case Constants.ELEMENT_SCE:
                        case Constants.ELEMENT_LFE:
                            //LOGGER.finest("SCE");
                            prev = DecodeSCE_LFE(input);
                            break;
                        case Constants.ELEMENT_CPE:
                            //LOGGER.finest("CPE");
                            prev = DecodeCPE(input);
                            break;
                        case Constants.ELEMENT_CCE:
                            //LOGGER.finest("CCE");
                            DecodeCCE(input);
                            prev = null;
                            break;
                        case Constants.ELEMENT_DSE:
                            //LOGGER.finest("DSE");
                            DecodeDSE(input);
                            prev = null;
                            break;
                        case Constants.ELEMENT_PCE:
                            //LOGGER.finest("PCE");
                            DecodePCE(input);
                            prev = null;
                            break;
                        case Constants.ELEMENT_FIL:
                            //LOGGER.finest("FIL");
                            DecodeFIL(input, prev);
                            prev = null;
                            break;
                    }
                }
                //LOGGER.finest("END");
                content = false;
                prev = null;
            }
            else
            {
                //error resilient raw data block
                switch (config.GetChannelConfiguration())
                {
                    case ChannelConfiguration.CHANNEL_CONFIG_MONO:
                        DecodeSCE_LFE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_STEREO:
                        DecodeCPE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_STEREO_PLUS_CENTER:
                        DecodeSCE_LFE(input);
                        DecodeCPE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_STEREO_PLUS_CENTER_PLUS_REAR_MONO:
                        DecodeSCE_LFE(input);
                        DecodeCPE(input);
                        DecodeSCE_LFE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_FIVE:
                        DecodeSCE_LFE(input);
                        DecodeCPE(input);
                        DecodeCPE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_FIVE_PLUS_ONE:
                        DecodeSCE_LFE(input);
                        DecodeCPE(input);
                        DecodeCPE(input);
                        DecodeSCE_LFE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_SEVEN_PLUS_ONE:
                        DecodeSCE_LFE(input);
                        DecodeCPE(input);
                        DecodeCPE(input);
                        DecodeCPE(input);
                        DecodeSCE_LFE(input);
                        break;
                    default:
                        throw new AACException("unsupported channel configuration for error resilience: " + config.GetChannelConfiguration());
                }
            }
            input.ByteAlign();

            bitsRead = input.GetPosition() - start;
        }

        private Element DecodeSCE_LFE(BitStream input)
        {
            if (elements[curElem] == null) elements[curElem] = new SCE_LFE(config);
            ((SCE_LFE)elements[curElem]).Decode(input, config);
            curElem++;
            return elements[curElem - 1];
        }

        private Element DecodeCPE(BitStream input)
        {
            if (elements[curElem] == null) elements[curElem] = new CPE(config);
            ((CPE)elements[curElem]).Decode(input, config);
            curElem++;
            return elements[curElem - 1];
        }

        private void DecodeCCE(BitStream input)
        {
            if (curCCE == Constants.MAX_ELEMENTS) throw new AACException("too much CCE elements");
            if (cces[curCCE] == null) cces[curCCE] = new CCE(config);
            cces[curCCE].Decode(input, config);
            curCCE++;
        }

        private void DecodeDSE(BitStream input)
        {
            if (curDSE == Constants.MAX_ELEMENTS) throw new AACException("too much CCE elements");
            if (dses[curDSE] == null) dses[curDSE] = new DSE();
            dses[curDSE].Decode(input);
            curDSE++;
        }

        private void DecodePCE(BitStream input)
        {
            pce.Decode(input);
            config.SetProfile(pce.GetProfile());
            config.SetSampleFrequency(pce.GetSampleFrequency());
            config.SetChannelConfiguration((ChannelConfiguration)pce.GetChannelCount());
        }

        private void DecodeFIL(BitStream input, Element prev)
        {
            if (curFIL == Constants.MAX_ELEMENTS) throw new AACException("too much FIL elements");
            fils[curFIL].Decode(input, prev, config.GetSampleFrequency(), config.IsSBREnabled(), config.IsSmallFrameUsed());
            curFIL++;

            if (prev != null && prev.IsSBRPresent())
            {
                sbrPresent = true;
                if (!psPresent && prev.GetSBR().IsPSUsed()) psPresent = true;
            }
        }

        public void Process(FilterBank filterBank)
        {
            Profile profile = config.GetProfile();
            SampleFrequency sf = config.GetSampleFrequency();
            int chs = (int)config.GetChannelConfiguration();
            if (chs == 1 && psPresent) chs++;
            int mult = sbrPresent ? 2 : 1;
            int frameLength = config.GetFrameLength();
            //only reallocate if needed
            if (data == null || chs != data.Length || mult * frameLength != data[0].Length)
            {
                data = new float[chs][];

                for (int i = 0; i < chs; i++)
                {
                    data[i] = new float[mult * frameLength];
                }
            }

            int channel = 0;
            Element e;
            SCE_LFE scelfe;
            CPE cpe;
            for (int i = 0; i < curElem && channel < chs; i++)
            {
                e = elements[i];
                if (e == null) continue;
                if (e is SCE_LFE)
                {
                    scelfe = (SCE_LFE)e;
                    channel += ProcessSingle(scelfe, filterBank, channel, profile, sf);
                }
                else if (e is CPE)
                {
                    cpe = (CPE)e;
                    ProcessPair(cpe, filterBank, channel, profile, sf);
                    channel += 2;
                }
                else if (e is CCE)
                {
                    //applies invquant and save the result in the CCE
                    ((CCE)e).Process();
                    channel++;
                }
            }
        }

        private int ProcessSingle(SCE_LFE scelfe, FilterBank filterBank, int channel, Profile profile, SampleFrequency sf)
        {
            bool profiling = DecoderProfiling.Enabled;
            long stageStart = 0;
            ICStream ics = scelfe.GetICStream();
            ICSInfo info = ics.GetInfo();
            LTPrediction ltp = info.GetLTPrediction();
            int elementID = scelfe.GetElementInstanceTag();
            var windowSequence = info.GetWindowSequence();
            var currentWindowShape = info.GetWindowShape(ICSInfo.CURRENT);
            var previousWindowShape = info.GetWindowShape(ICSInfo.PREVIOUS);

            //inverse quantization
            float[] iqData = ics.GetInvQuantData();

            //prediction
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            if (profile == Profile.AAC_MAIN && info.IsICPredictionPresent()) info.GetICPrediction().Process(ics, iqData, sf);
            if (ltp != null) ltp.Process(ics, iqData, filterBank, sf);
            if (profiling) DecoderProfiling.AddProcessPredictionTicks(Stopwatch.GetTimestamp() - stageStart);

            //dependent coupling
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            processDependentCoupling(false, elementID, CCE.BEFORE_TNS, iqData, null);
            if (profiling) DecoderProfiling.AddProcessCouplingTicks(Stopwatch.GetTimestamp() - stageStart);

            //TNS
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            if (ics.IsTNSDataPresent()) ics.GetTNS().Process(ics, iqData, sf, false);
            if (profiling) DecoderProfiling.AddProcessTnsTicks(Stopwatch.GetTimestamp() - stageStart);

            //dependent coupling
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            processDependentCoupling(false, elementID, CCE.AFTER_TNS, iqData, null);
            if (profiling) DecoderProfiling.AddProcessCouplingTicks(Stopwatch.GetTimestamp() - stageStart);

            //filterbank
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            filterBank.Process(windowSequence, currentWindowShape, previousWindowShape, iqData, data[channel], channel);
            if (profiling) DecoderProfiling.AddProcessFilterbankTicks(Stopwatch.GetTimestamp() - stageStart);

            if (ltp != null) ltp.UpdateState(data[channel], filterBank.GetOverlap(channel), profile);

            //dependent coupling
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            ProcessIndependentCoupling(false, elementID, data[channel], null);
            if (profiling) DecoderProfiling.AddProcessCouplingTicks(Stopwatch.GetTimestamp() - stageStart);

            //gain control
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            if (ics.IsGainControlPresent()) ics.GetGainControl().Process(iqData, currentWindowShape, previousWindowShape, windowSequence);
            if (profiling) DecoderProfiling.AddProcessGainTicks(Stopwatch.GetTimestamp() - stageStart);

            //SBR
            int chs = 1;
            if (sbrPresent && config.IsSBREnabled())
            {
                if (profiling) stageStart = Stopwatch.GetTimestamp();
                //if(data[channel].Length==config.getFrameLength()) LOGGER.log(Level.WARNING, "SBR data present, but buffer has normal size!");
                SBR sbr = scelfe.GetSBR();
                if (sbr.IsPSUsed())
                {
                    chs = 2;
                    scelfe.GetSBR().ProcessPS(data[channel], data[channel + 1], false);
                }
                else
                    scelfe.GetSBR().Process(data[channel], false);
                if (profiling) DecoderProfiling.AddProcessSbrTicks(Stopwatch.GetTimestamp() - stageStart);
            }
            return chs;
        }

        private void ProcessPair(CPE cpe, FilterBank filterBank, int channel, Profile profile, SampleFrequency sf)
        {
            bool profiling = DecoderProfiling.Enabled;
            long stageStart = 0;
            ICStream ics1 = cpe.GetLeftChannel();
            ICStream ics2 = cpe.GetRightChannel();
            ICSInfo info1 = ics1.GetInfo();
            ICSInfo info2 = ics2.GetInfo();
            LTPrediction ltp1 = info1.GetLTPrediction();
            LTPrediction ltp2 = info2.GetLTPrediction();
            int elementID = cpe.GetElementInstanceTag();
            var windowSequence1 = info1.GetWindowSequence();
            var windowSequence2 = info2.GetWindowSequence();
            var currentWindowShape1 = info1.GetWindowShape(ICSInfo.CURRENT);
            var previousWindowShape1 = info1.GetWindowShape(ICSInfo.PREVIOUS);
            var currentWindowShape2 = info2.GetWindowShape(ICSInfo.CURRENT);
            var previousWindowShape2 = info2.GetWindowShape(ICSInfo.PREVIOUS);

            //inverse quantization
            float[] iqData1 = ics1.GetInvQuantData();
            float[] iqData2 = ics2.GetInvQuantData();

            //MS
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            if (cpe.IsCommonWindow() && cpe.IsMSMaskPresent()) SharpJaad.AAC.Tools.MS.Process(cpe, iqData1, iqData2);
            //main prediction
            if (profile == Profile.AAC_MAIN)
            {
                if (info1.IsICPredictionPresent()) info1.GetICPrediction().Process(ics1, iqData1, sf);
                if (info2.IsICPredictionPresent()) info2.GetICPrediction().Process(ics2, iqData2, sf);
            }
            //IS
            IS.Process(cpe, iqData1, iqData2);
            if (profiling) DecoderProfiling.AddProcessStereoTicks(Stopwatch.GetTimestamp() - stageStart);

            //LTP
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            if (ltp1 != null) ltp1.Process(ics1, iqData1, filterBank, sf);
            if (ltp2 != null) ltp2.Process(ics2, iqData2, filterBank, sf);
            if (profiling) DecoderProfiling.AddProcessPredictionTicks(Stopwatch.GetTimestamp() - stageStart);

            //dependent coupling
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            processDependentCoupling(true, elementID, CCE.BEFORE_TNS, iqData1, iqData2);
            if (profiling) DecoderProfiling.AddProcessCouplingTicks(Stopwatch.GetTimestamp() - stageStart);

            //TNS
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            if (ics1.IsTNSDataPresent()) ics1.GetTNS().Process(ics1, iqData1, sf, false);
            if (ics2.IsTNSDataPresent()) ics2.GetTNS().Process(ics2, iqData2, sf, false);
            if (profiling) DecoderProfiling.AddProcessTnsTicks(Stopwatch.GetTimestamp() - stageStart);

            //dependent coupling
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            processDependentCoupling(true, elementID, CCE.AFTER_TNS, iqData1, iqData2);
            if (profiling) DecoderProfiling.AddProcessCouplingTicks(Stopwatch.GetTimestamp() - stageStart);

            //filterbank
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            filterBank.Process(windowSequence1, currentWindowShape1, previousWindowShape1, iqData1, data[channel], channel);
            filterBank.Process(windowSequence2, currentWindowShape2, previousWindowShape2, iqData2, data[channel + 1], channel + 1);
            if (profiling) DecoderProfiling.AddProcessFilterbankTicks(Stopwatch.GetTimestamp() - stageStart);

            if (ltp1 != null) ltp1.UpdateState(data[channel], filterBank.GetOverlap(channel), profile);
            if (ltp2 != null) ltp2.UpdateState(data[channel + 1], filterBank.GetOverlap(channel + 1), profile);

            //independent coupling
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            ProcessIndependentCoupling(true, elementID, data[channel], data[channel + 1]);
            if (profiling) DecoderProfiling.AddProcessCouplingTicks(Stopwatch.GetTimestamp() - stageStart);

            //gain control
            if (profiling) stageStart = Stopwatch.GetTimestamp();
            if (ics1.IsGainControlPresent()) ics1.GetGainControl().Process(iqData1, currentWindowShape1, previousWindowShape1, windowSequence1);
            if (ics2.IsGainControlPresent()) ics2.GetGainControl().Process(iqData2, currentWindowShape2, previousWindowShape2, windowSequence2);
            if (profiling) DecoderProfiling.AddProcessGainTicks(Stopwatch.GetTimestamp() - stageStart);

            //SBR
            if (sbrPresent && config.IsSBREnabled())
            {
                if (profiling) stageStart = Stopwatch.GetTimestamp();
                //if(data[channel].Length==config.getFrameLength()) LOGGER.log(Level.WARNING, "SBR data present, but buffer has normal size!");
                cpe.GetSBR().Process(data[channel], data[channel + 1], false);
                if (profiling) DecoderProfiling.AddProcessSbrTicks(Stopwatch.GetTimestamp() - stageStart);
            }
        }

        private void ProcessIndependentCoupling(bool channelPair, int elementID, float[] data1, float[] data2)
        {
            int index, c, chSelect;
            CCE cce;
            for (int i = 0; i < curCCE; i++)
            {
                cce = cces[i];
                index = 0;
                if (cce != null && cce.GetCouplingPoint() == CCE.AFTER_IMDCT)
                {
                    for (c = 0; c <= cce.GetCoupledCount(); c++)
                    {
                        chSelect = cce.GetCHSelect(c);
                        if (cce.IsChannelPair(c) == channelPair && cce.GetIDSelect(c) == elementID)
                        {
                            if (chSelect != 1)
                            {
                                cce.ApplyIndependentCoupling(index, data1);
                                if (chSelect != 0) index++;
                            }
                            if (chSelect != 2)
                            {
                                cce.ApplyIndependentCoupling(index, data2);
                                index++;
                            }
                        }
                        else index += 1 + (chSelect == 3 ? 1 : 0);
                    }
                }
            }
        }

        private void processDependentCoupling(bool channelPair, int elementID, int couplingPoint, float[] data1, float[] data2)
        {
            int index, c, chSelect;
            CCE cce;
            for (int i = 0; i < curCCE; i++)
            {
                cce = cces[i];
                index = 0;
                if (cce != null && cce.GetCouplingPoint() == couplingPoint)
                {
                    for (c = 0; c <= cce.GetCoupledCount(); c++)
                    {
                        chSelect = cce.GetCHSelect(c);
                        if (cce.IsChannelPair(c) == channelPair && cce.GetIDSelect(c) == elementID)
                        {
                            if (chSelect != 1)
                            {
                                cce.ApplyDependentCoupling(index, data1);
                                if (chSelect != 0) index++;
                            }
                            if (chSelect != 2)
                            {
                                cce.ApplyDependentCoupling(index, data2);
                                index++;
                            }
                        }
                        else
                            index += 1 + (chSelect == 3 ? 1 : 0);
                    }
                }
            }
        }

        public void SendToOutput(SampleBuffer buffer)
        {
            int chs = data.Length;
            int mult = sbrPresent && config.IsSBREnabled() ? 2 : 1;
            int length = mult * config.GetFrameLength();
            int freq = mult * config.GetSampleFrequency().GetFrequency();
            if (buffer.PreferPcm32Output)
            {
                Span<int> pcm32 = buffer.PreparePcm32Data(chs * length);

                if (chs == 1)
                {
                    float[] mono = data[0];
                    for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
                    {
                        pcm32[sampleIndex] = ScaleToPcm32(mono[sampleIndex]);
                    }
                }
                else if (chs == 2)
                {
                    float[] left = data[0];
                    float[] right = data[1];
                    int destinationIndex = 0;
                    for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
                    {
                        pcm32[destinationIndex] = ScaleToPcm32(left[sampleIndex]);
                        pcm32[destinationIndex + 1] = ScaleToPcm32(right[sampleIndex]);
                        destinationIndex += 2;
                    }
                }
                else
                {
                    for (int channel = 0; channel < chs; channel++)
                    {
                        float[] current = data[channel];
                        int destinationIndex = channel;
                        for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
                        {
                            pcm32[destinationIndex] = ScaleToPcm32(current[sampleIndex]);
                            destinationIndex += chs;
                        }
                    }
                }

                buffer.CommitPcm32Data(pcm32.Length, freq, chs, bitsRead);
                return;
            }

            Span<float> pcm = buffer.PrepareFloatData(chs * length);

            if (chs == 1)
            {
                float[] mono = data[0];
                for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
                {
                    pcm[sampleIndex] = mono[sampleIndex] * ShortPcmScale;
                }
            }
            else if (chs == 2)
            {
                float[] left = data[0];
                float[] right = data[1];
                int destinationIndex = 0;
                for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
                {
                    pcm[destinationIndex] = left[sampleIndex] * ShortPcmScale;
                    pcm[destinationIndex + 1] = right[sampleIndex] * ShortPcmScale;
                    destinationIndex += 2;
                }
            }
            else
            {
                for (int channel = 0; channel < chs; channel++)
                {
                    float[] current = data[channel];
                    int destinationIndex = channel;
                    for (int sampleIndex = 0; sampleIndex < length; sampleIndex++)
                    {
                        pcm[destinationIndex] = current[sampleIndex] * ShortPcmScale;
                        destinationIndex += chs;
                    }
                }
            }

            buffer.CommitFloatData(pcm.Length, freq, chs, bitsRead);
        }

        private static int ScaleToPcm32(float value)
        {
            var normalized = value * ShortPcmScale;
            if (normalized >= 1f)
            {
                return int.MaxValue;
            }

            if (normalized <= -1f)
            {
                return int.MinValue;
            }

            return normalized >= 0f
                ? (int)MathF.Round(normalized * 2147483647f)
                : (int)MathF.Round(normalized * 2147483648f);
        }
    }
}
