using SharpJaad.AAC.Syntax;

namespace SharpJaad.AAC
{
    public class DecoderConfig
    {
        private Profile _profile, _extProfile;
        private SampleFrequency _sampleFrequency;
        private ChannelConfiguration _channelConfiguration;
        private int _pceChannelCount; //the channels of a program config element, 0 if there is none
        private PCE _pce;
        internal int _pnsRandomState = 0x1F2E3D4C; //the random generator of PNS, per decoder
        private bool _frameLengthFlag;
        private bool _dependsOnCoreCoder;
        private int _coreCoderDelay;
        private bool _extensionFlag;
        //extension: SBR
        private bool _sbrPresent, _downSampledSBR, _sbrEnabled;
        //extension: error resilience
        private bool _sectionDataResilience, _scalefactorResilience, _spectralDataResilience;

        public DecoderConfig()
        {
            _profile = Profile.AAC_MAIN;
            _extProfile = Profile.UNKNOWN;
            _sampleFrequency = SampleFrequency.SAMPLE_FREQUENCY_NONE;
            _channelConfiguration = ChannelConfiguration.CHANNEL_CONFIG_UNSUPPORTED;
            _frameLengthFlag = false;
            _sbrPresent = false;
            _downSampledSBR = false;
            _sbrEnabled = true;
            _sectionDataResilience = false;
            _scalefactorResilience = false;
            _spectralDataResilience = false;
        }

        public void SetSBRPresent(bool sbr)
        {
            _sbrPresent = sbr;
        }

        public void SetSBRDownsampled(bool sbr)
        {
            _downSampledSBR = sbr;
        }

        /* ========== gets/sets ========== */
        public ChannelConfiguration GetChannelConfiguration()
        {
            return _channelConfiguration;
        }

        public void SetChannelConfiguration(ChannelConfiguration channelConfiguration)
        {
            _pceChannelCount = 0;
            _pce = null;
            _channelConfiguration = channelConfiguration;
        }

        /// <summary>
        /// The number of channels: the configuration is the number, except for 7.1 (7), or the channels of a program config
        /// element, which are also set as the configuration.
        /// </summary>
        internal int GetChannelCount()
        {
            if (_pceChannelCount > 0) return _pceChannelCount;
            return _channelConfiguration == ChannelConfiguration.CHANNEL_CONFIG_SEVEN_PLUS_ONE ? 8 : (int)_channelConfiguration;
        }

        internal void SetChannelCountFromPCE(PCE pce)
        {
            _pce = pce;
            _pceChannelCount = pce.GetChannelCount();
            _channelConfiguration = (ChannelConfiguration)_pceChannelCount;
        }

        /// <summary>
        /// The program config element that set the channels, null if there is none.
        /// </summary>
        internal PCE GetPCE()
        {
            return _pce;
        }

        public int GetCoreCoderDelay()
        {
            return _coreCoderDelay;
        }

        public void SetCoreCoderDelay(int coreCoderDelay)
        {
            _coreCoderDelay = coreCoderDelay;
        }

        public bool IsDependsOnCoreCoder()
        {
            return _dependsOnCoreCoder;
        }

        public void SetDependsOnCoreCoder(bool dependsOnCoreCoder)
        {
            _dependsOnCoreCoder = dependsOnCoreCoder;
        }

        public Profile GetExtObjectType()
        {
            return _extProfile;
        }

        public void SetExtObjectType(Profile extObjectType)
        {
            _extProfile = extObjectType;
        }

        public int GetFrameLength()
        {
            return _frameLengthFlag ? Constants.WINDOW_SMALL_LEN_LONG : Constants.WINDOW_LEN_LONG;
        }

        public bool IsSmallFrameUsed()
        {
            return _frameLengthFlag;
        }

        public void SetSmallFrameUsed(bool shortFrame)
        {
            _frameLengthFlag = shortFrame;
        }

        public Profile GetProfile()
        {
            return _profile;
        }

        public void SetProfile(Profile profile)
        {
            _profile = profile;
        }

        public SampleFrequency GetSampleFrequency()
        {
            return _sampleFrequency;
        }

        public void SetSampleFrequency(SampleFrequency sampleFrequency)
        {
            _sampleFrequency = sampleFrequency;
        }

        //=========== SBR =============
        public bool IsSBRPresent()
        {
            return _sbrPresent;
        }

        public bool IsSBRDownSampled()
        {
            return _downSampledSBR;
        }

        public bool IsSBREnabled()
        {
            return _sbrEnabled;
        }

        public void IetSBREnabled(bool enabled)
        {
            _sbrEnabled = enabled;
        }

        //=========== ER =============
        public bool IsScalefactorResilienceUsed()
        {
            return _scalefactorResilience;
        }

        public bool IsSectionDataResilienceUsed()
        {
            return _sectionDataResilience;
        }

        public bool IsSpectralDataResilienceUsed()
        {
            return _spectralDataResilience;
        }

        /* ======== static builder ========= */

        /// <summary>
        /// Parses the input arrays as a DecoderSpecificInfo, as used in MP4 containers.
        /// </summary>
        /// <param name="data">Data.</param>
        /// <returns>a DecoderConfig</returns>
        /// <exception cref="AACException"></exception>
        public static DecoderConfig ParseMP4DecoderSpecificInfo(byte[] data)
        {
            if (data == null) throw new System.ArgumentNullException(nameof(data));
            BitStream input = new BitStream(data);
            DecoderConfig config = new DecoderConfig();

            try
            {
                config._profile = ReadProfile(input);
                config._sampleFrequency = ReadSampleFrequency(input);
                config._channelConfiguration = (ChannelConfiguration)input.ReadBits(4);

                // explicit SBR (5) or SBR+PS (29) signaling: the extension sample rate, then the object type of the core
                if (config._profile == Profile.AAC_SBR || (int)config._profile == AOT_PS)
                {
                    config._extProfile = Profile.AAC_SBR;
                    config._sbrPresent = true;
                    SampleFrequency extensionFrequency = ReadSampleFrequency(input);
                    //if sample frequencies are the same: downsample SBR
                    config._downSampledSBR = config._sampleFrequency == extensionFrequency;
                    config._profile = ReadProfile(input);
                }

                switch (config._profile)
                {
                    case Profile.AAC_MAIN:
                    case Profile.AAC_LC:
                    case Profile.AAC_SSR:
                    case Profile.AAC_LTP:
                    case Profile.ER_AAC_LC:
                    case Profile.ER_AAC_LTP:
                    case Profile.ER_AAC_LD:
                        //ga-specific info:
                        config._frameLengthFlag = input.ReadBool();
                        config._dependsOnCoreCoder = input.ReadBool();
                        if (config._dependsOnCoreCoder) config._coreCoderDelay = input.ReadBits(14);
                        else config._coreCoderDelay = 0;
                        config._extensionFlag = input.ReadBool();

                        if (config._channelConfiguration == ChannelConfiguration.CHANNEL_CONFIG_NONE)
                        {
                            PCE pce = new PCE();
                            pce.Decode(input);
                            config.SetChannelCountFromPCE(pce);
                        }

                        if (config._extensionFlag)
                        {
                            if (config._profile.IsErrorResilientProfile())
                            {
                                config._sectionDataResilience = input.ReadBool();
                                config._scalefactorResilience = input.ReadBool();
                                config._spectralDataResilience = input.ReadBool();
                            }
                            //extensionFlag3
                            input.SkipBit();
                        }

                        if (config._extProfile != Profile.AAC_SBR && input.GetBitsLeft() >= 16) ReadSyncExtension(input, config);
                        break;
                    default:
                        throw new AACException("profile not supported: " + (int)config._profile);
                }
                return config;
            }
            finally
            {
                input.Destroy();
            }
        }

        private static Profile ReadProfile(BitStream input)
        {
            int i = input.ReadBits(5);
            if (i == 31) i = 32 + input.ReadBits(6);
            return (Profile)i;
        }

        private const int AOT_PS = 29;

        private static SampleFrequency ReadSampleFrequency(BitStream input)
        {
            int sf = input.ReadBits(4);
            return sf == 0xF ? SampleFrequencyExtensions.FromFrequency(input.ReadBits(24)) : (SampleFrequency)sf;
        }

        // backward compatible signaling of SBR (and PS) after the GASpecificConfig, ISO/IEC 14496-3 1.6.5
        private static void ReadSyncExtension(BitStream input, DecoderConfig config)
        {
            int type = input.ReadBits(11);
            if (type != 0x2B7) return;

            Profile profile = ReadProfile(input);
            if (profile == Profile.AAC_SBR)
            {
                config._sbrPresent = input.ReadBool();
                if (config._sbrPresent)
                {
                    // the profile stays the one of the core: the core decoding depends on it
                    config._extProfile = profile;
                    SampleFrequency extensionFrequency = ReadSampleFrequency(input);
                    config._downSampledSBR = config._sampleFrequency == extensionFrequency;
                    // a following PS sync extension (0x548) needs no handling: PS is detected in the stream
                }
            }
        }
    }
}
