using SharpJaad.AAC.Filterbank;
using SharpJaad.AAC.Sbr;
using SharpJaad.AAC.Tools;
using System;

namespace SharpJaad.AAC.Syntax
{
    public class SyntacticElements
    {
        //global properties
        private DecoderConfig config;
        private bool sbrPresent, psPresent;
        private int bitsRead;
        private int frame = 0;
        //elements
        private PCE pce;
        private Element[] elements; //SCE, LFE and CPE of the current frame, in bitstream order
        private bool[] elementIsLFE;
        //the elements by instance tag: an element keeps its state (prediction, LTP, SBR) for the channels it codes
        private readonly SCE_LFE[] sces = new SCE_LFE[MAX_TAGS], lfes = new SCE_LFE[MAX_TAGS];
        private readonly CPE[] cpes = new CPE[MAX_TAGS];
        private readonly CCE[] cceByTag = new CCE[MAX_TAGS];
        //for an instance tag used twice in a frame (invalid, but decodable)
        private SCE_LFE[] spareSCEs;
        private CPE[] spareCPEs;
        private const int MAX_TAGS = 16; //element_instance_tag is 4 bits
        private CCE[] cces;
        private DSE[] dses;
        private FIL[] fils;
        private int curElem, curCCE, curDSE, curFIL;
        private float[][] data;
        private float[][] dataMono, dataStereo; //the buffers for one and two channels, sharing the first

        public SyntacticElements(DecoderConfig config)
        {
            this.config = config;

            pce = new PCE();
            elements = new Element[4 * Constants.MAX_ELEMENTS];
            elementIsLFE = new bool[elements.Length];
            spareSCEs = new SCE_LFE[elements.Length];
            spareCPEs = new CPE[elements.Length];
            cces = new CCE[Constants.MAX_ELEMENTS];
            dses = new DSE[Constants.MAX_ELEMENTS];
            fils = new FIL[Constants.MAX_ELEMENTS];
            // FIL and DSE elements are cheap: create them up front, so that a frame with more of them than before allocates nothing
            for (int i = 0; i < Constants.MAX_ELEMENTS; i++)
            {
                dses[i] = new DSE();
                fils[i] = new FIL(IsSBRDownSampled());
            }

            StartNewFrame();
        }

        /// <summary>
        /// SBR in downsampled mode: the output keeps the sample rate of the core. Signaled in the decoder specific info
        /// (the SBR sample rate is the core one), and for implicit SBR above a core of 24 kHz (as FAAD2 does).
        /// </summary>
        private bool IsSBRDownSampled()
        {
            return config.IsSBRDownSampled() || (!config.IsSBRPresent() && config.GetSampleFrequency().GetFrequency() > 24000);
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
                    // the SBR data of a FIL belongs to the last SCE or CPE: DSE, PCE and FIL elements in between do not
                    // change it (as in FAAD2 and FFmpeg); SBR for a CCE is not supported and skipped
                    switch (type)
                    {
                        case Constants.ELEMENT_SCE:
                        case Constants.ELEMENT_LFE:
                            //LOGGER.finest("SCE");
                            prev = DecodeSCE_LFE(input, type == Constants.ELEMENT_LFE);
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
                            break;
                        case Constants.ELEMENT_PCE:
                            //LOGGER.finest("PCE");
                            DecodePCE(input);
                            break;
                        case Constants.ELEMENT_FIL:
                            //LOGGER.finest("FIL");
                            DecodeFIL(input, prev);
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
                        DecodeSCE_LFE(input, false);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_STEREO:
                        DecodeCPE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_STEREO_PLUS_CENTER:
                        DecodeSCE_LFE(input, false);
                        DecodeCPE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_STEREO_PLUS_CENTER_PLUS_REAR_MONO:
                        DecodeSCE_LFE(input, false);
                        DecodeCPE(input);
                        DecodeSCE_LFE(input, false);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_FIVE:
                        DecodeSCE_LFE(input, false);
                        DecodeCPE(input);
                        DecodeCPE(input);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_FIVE_PLUS_ONE:
                        DecodeSCE_LFE(input, false);
                        DecodeCPE(input);
                        DecodeCPE(input);
                        DecodeSCE_LFE(input, true);
                        break;
                    case ChannelConfiguration.CHANNEL_CONFIG_SEVEN_PLUS_ONE:
                        DecodeSCE_LFE(input, false);
                        DecodeCPE(input);
                        DecodeCPE(input);
                        DecodeCPE(input);
                        DecodeSCE_LFE(input, true);
                        break;
                    default:
                        throw new AACException("unsupported channel configuration for error resilience: " + config.GetChannelConfiguration());
                }
            }
            input.ByteAlign();

            bitsRead = input.GetPosition() - start;
        }

        private Element DecodeSCE_LFE(BitStream input, bool lfe)
        {
            if (curElem == elements.Length) throw new AACException("too many elements");
            int tag = input.PeekBits(4);
            SCE_LFE[] pool = lfe ? lfes : sces;
            SCE_LFE e = pool[tag];
            if (e == null) e = pool[tag] = new SCE_LFE(config);
            else if (IsInFrame(e)) e = spareSCEs[curElem] ?? (spareSCEs[curElem] = new SCE_LFE(config));
            e.Decode(input, config);
            elements[curElem] = e;
            elementIsLFE[curElem] = lfe;
            curElem++;
            return e;
        }

        private Element DecodeCPE(BitStream input)
        {
            if (curElem == elements.Length) throw new AACException("too many elements");
            int tag = input.PeekBits(4);
            CPE e = cpes[tag];
            if (e == null) e = cpes[tag] = new CPE(config);
            else if (IsInFrame(e)) e = spareCPEs[curElem] ?? (spareCPEs[curElem] = new CPE(config));
            e.Decode(input, config);
            elements[curElem] = e;
            elementIsLFE[curElem] = false;
            curElem++;
            return e;
        }

        private bool IsInFrame(Element e)
        {
            for (int i = 0; i < curElem; i++)
            {
                if (elements[i] == e) return true;
            }
            return false;
        }

        private void DecodeCCE(BitStream input)
        {
            if (curCCE == Constants.MAX_ELEMENTS) throw new AACException("too much CCE elements");
            // by instance tag, as the other elements: a CCE keeps the state of its channel
            int tag = input.PeekBits(4);
            CCE cce = cceByTag[tag];
            if (cce == null) cce = cceByTag[tag] = new CCE(config);
            for (int i = 0; i < curCCE; i++)
            {
                if (cces[i] == cce) throw new AACException("CCE instance tag used twice in a frame: " + tag);
            }
            cce.Decode(input, config);
            cces[curCCE] = cce;
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
            config.SetChannelCountFromPCE(pce);
        }

        private void DecodeFIL(BitStream input, Element prev)
        {
            if (curFIL == Constants.MAX_ELEMENTS) throw new AACException("too much FIL elements");
            // as when the FIL was created on first use: it takes the configuration at that point
            fils[curFIL].InitOnFirstUse(IsSBRDownSampled());
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
            //final ChannelConfiguration channels = config.getChannelConfiguration();

            int chs = config.GetChannelCount();
            if (chs == 1 && psPresent) chs++;
            int mult = sbrPresent && !IsSBRDownSampled() ? 2 : 1;
            int length = mult * config.GetFrameLength();
            //only reallocate if needed
            if (data == null || chs != data.Length || length != data[0].Length)
            {
                if (chs <= 2 && dataStereo != null && dataStereo[0].Length == length)
                {
                    // mono to PS stereo: the buffers are ready (each is overwritten in full by every frame)
                    data = chs == 1 ? dataMono : dataStereo;
                }
                else
                {
                    data = new float[chs][];

                    for (int i = 0; i < chs; i++)
                    {
                        data[i] = new float[length];
                    }
                    // a mono stream turns stereo when PS starts: the buffers for that are allocated along
                    dataMono = chs == 1 ? data : chs == 2 ? new float[][] { data[0] } : null;
                    dataStereo = chs == 1 ? new float[][] { data[0], new float[length] } : chs == 2 ? data : null;
                }
            }

            //the coupling channels first: their output is added to the channels they couple to
            for (int i = 0; i < curCCE; i++) cces[i].Process(profile, sf);

            int channel = 0;
            PCE channelMap = config.GetPCE();
            for (int i = 0; i < curElem; i++)
            {
                Element e = elements[i];
                bool isCPE = e is CPE;
                // with a PCE the channels follow its order of the elements, without one the bitstream order
                if (channelMap != null)
                {
                    int mapped = channelMap.FindChannel(isCPE, elementIsLFE[i], e.GetElementInstanceTag());
                    if (mapped >= 0) channel = mapped;
                }
                if (channel + (isCPE ? 2 : 1) > chs) continue;

                if (isCPE)
                {
                    ProcessPair((CPE)e, filterBank, channel, profile, sf);
                    channel += 2;
                }
                else channel += ProcessSingle((SCE_LFE)e, elementIsLFE[i], filterBank, channel, profile, sf);
            }
        }

        private int ProcessSingle(SCE_LFE scelfe, bool lfe, FilterBank filterBank, int channel, Profile profile, SampleFrequency sf)
        {
            ICStream ics = scelfe.GetICStream();
            ICSInfo info = ics.GetInfo();
            LTPrediction ltp = info.GetLTPrediction();
            int elementID = scelfe.GetElementInstanceTag();

            //inverse quantization
            float[] iqData = ics.GetInvQuantData();

            //prediction
            if (profile == Profile.AAC_MAIN && info.GetICPrediction() != null) info.GetICPrediction().Process(ics, iqData, sf);
            if (ltp != null) ltp.Process(ics, iqData, filterBank, sf);

            //dependent coupling
            if (!lfe) processDependentCoupling(false, elementID, CCE.BEFORE_TNS, iqData, null);

            //TNS
            if (ics.IsTNSDataPresent()) ics.GetTNS().Process(ics, iqData, sf, false);

            //dependent coupling
            if (!lfe) processDependentCoupling(false, elementID, CCE.AFTER_TNS, iqData, null);

            //filterbank
            filterBank.Process(info.GetWindowSequence(), info.GetWindowShape(ICSInfo.CURRENT), info.GetWindowShape(ICSInfo.PREVIOUS), iqData, data[channel], channel);

            if (ltp != null) ltp.UpdateState(data[channel], filterBank.GetOverlap(channel), profile);

            //dependent coupling
            if (!lfe) ProcessIndependentCoupling(false, elementID, data[channel], null);

            //gain control
            if (ics.IsGainControlPresent()) ics.GetGainControl().Process(iqData, info.GetWindowShape(ICSInfo.CURRENT), info.GetWindowShape(ICSInfo.PREVIOUS), info.GetWindowSequence());

            //SBR
            int chs = 1;
            if (sbrPresent && config.IsSBREnabled())
            {
                //if(data[channel].Length==config.getFrameLength()) LOGGER.log(Level.WARNING, "SBR data present, but buffer has normal size!");
                SBR sbr = scelfe.GetOrCreateSBR(sf, false, IsSBRDownSampled(), config.IsSmallFrameUsed());
                if (sbr.IsPSUsed() && channel + 1 < data.Length)
                {
                    chs = 2;
                    sbr.ProcessPS(data[channel], data[channel + 1], false);
                }
                else
                    sbr.Process(data[channel], false);
            }
            return chs;
        }

        private void ProcessPair(CPE cpe, FilterBank filterBank, int channel, Profile profile, SampleFrequency sf)
        {
            ICStream ics1 = cpe.GetLeftChannel();
            ICStream ics2 = cpe.GetRightChannel();
            ICSInfo info1 = ics1.GetInfo();
            ICSInfo info2 = ics2.GetInfo();
            LTPrediction ltp1 = info1.GetLTPrediction();
            LTPrediction ltp2 = info2.GetLTPrediction();
            int elementID = cpe.GetElementInstanceTag();

            //inverse quantization
            float[] iqData1 = ics1.GetInvQuantData();
            float[] iqData2 = ics2.GetInvQuantData();

            //MS
            if (cpe.IsCommonWindow() && cpe.IsMSMaskPresent()) SharpJaad.AAC.Tools.MS.Process(cpe, iqData1, iqData2);
            //main prediction
            if (profile == Profile.AAC_MAIN)
            {
                if (info1.GetICPrediction() != null) info1.GetICPrediction().Process(ics1, iqData1, sf);
                if (info2.GetICPrediction() != null) info2.GetICPrediction().Process(ics2, iqData2, sf);
            }
            //IS
            IS.Process(cpe, iqData1, iqData2);

            //LTP
            if (ltp1 != null) ltp1.Process(ics1, iqData1, filterBank, sf);
            if (ltp2 != null) ltp2.Process(ics2, iqData2, filterBank, sf);

            //dependent coupling
            processDependentCoupling(true, elementID, CCE.BEFORE_TNS, iqData1, iqData2);

            //TNS
            if (ics1.IsTNSDataPresent()) ics1.GetTNS().Process(ics1, iqData1, sf, false);
            if (ics2.IsTNSDataPresent()) ics2.GetTNS().Process(ics2, iqData2, sf, false);

            //dependent coupling
            processDependentCoupling(true, elementID, CCE.AFTER_TNS, iqData1, iqData2);

            //filterbank
            filterBank.Process(info1.GetWindowSequence(), info1.GetWindowShape(ICSInfo.CURRENT), info1.GetWindowShape(ICSInfo.PREVIOUS), iqData1, data[channel], channel);
            filterBank.Process(info2.GetWindowSequence(), info2.GetWindowShape(ICSInfo.CURRENT), info2.GetWindowShape(ICSInfo.PREVIOUS), iqData2, data[channel + 1], channel + 1);

            if (ltp1 != null) ltp1.UpdateState(data[channel], filterBank.GetOverlap(channel), profile);
            if (ltp2 != null) ltp2.UpdateState(data[channel + 1], filterBank.GetOverlap(channel + 1), profile);

            //independent coupling
            ProcessIndependentCoupling(true, elementID, data[channel], data[channel + 1]);

            //gain control
            if (ics1.IsGainControlPresent()) ics1.GetGainControl().Process(iqData1, info1.GetWindowShape(ICSInfo.CURRENT), info1.GetWindowShape(ICSInfo.PREVIOUS), info1.GetWindowSequence());
            if (ics2.IsGainControlPresent()) ics2.GetGainControl().Process(iqData2, info2.GetWindowShape(ICSInfo.CURRENT), info2.GetWindowShape(ICSInfo.PREVIOUS), info2.GetWindowSequence());

            //SBR
            if (sbrPresent && config.IsSBREnabled())
            {
                //if(data[channel].Length==config.getFrameLength()) LOGGER.log(Level.WARNING, "SBR data present, but buffer has normal size!");
                cpe.GetOrCreateSBR(sf, true, IsSBRDownSampled(), config.IsSmallFrameUsed()).Process(data[channel], data[channel + 1], false);
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
            bool be = buffer.BigEndian;

            // always allocate at least two channels
            // mono can't be upgraded after implicit PS occures
            int chs = Math.Max(data.Length, 2);

            int mult = sbrPresent && config.IsSBREnabled() && !IsSBRDownSampled() ? 2 : 1;
            int length = mult * config.GetFrameLength();
            int freq = mult * config.GetSampleFrequency().GetFrequency();

            byte[] b = buffer.Data;
            if (b.Length != chs * length * 2) b = new byte[chs * length * 2];

            float[] cur;
            int i, j, off;
            short s;
            for (i = 0; i < chs; i++)
            {
                // duplicate possible mono channel
                cur = data[i < data.Length ? i : 0];
                for (j = 0; j < length; j++)
                {
                    s = (short)Math.Max(Math.Min(Math.Round(cur[j]), short.MaxValue), short.MinValue);
                    off = (j * chs + i) * 2;
                    if (be)
                    {
                        b[off] = (byte)(s >> 8 & Constants.BYTE_MASK);
                        b[off + 1] = (byte)(s & Constants.BYTE_MASK);
                    }
                    else
                    {
                        b[off + 1] = (byte)(s >> 8 & Constants.BYTE_MASK);
                        b[off] = (byte)(s & Constants.BYTE_MASK);
                    }
                }
            }

            buffer.SetData(b, freq, chs, 16, bitsRead);
        }
    }
}
