namespace SharpJaad.AAC.Syntax
{
    public class FIL
    {
        public class DynamicRangeInfo
        {
            public const int MAX_NBR_BANDS = 7;
            internal const int MAX_DRC_BANDS = 16; //bands_increment is 4 bits + 1
            internal const int MAX_EXCLUDED_CHANNELS = 63; //the mask of further channels is not kept
            public bool[] _excludeMask;
            public bool[] _additionalExcludedChannels;
            public bool _pceTagPresent;
            public int _pceInstanceTag;
            public int _tagReservedBits;
            public bool _excludedChannelsPresent;
            public bool _bandsPresent;
            public int _bandsIncrement, _interpolationScheme;
            public int[] _bandTop;
            public bool _progRefLevelPresent;
            public int _progRefLevel, _progRefLevelReservedBits;
            public bool[] _dynRngSgn;
            public int[] _dynRngCtl;

            public DynamicRangeInfo()
            {
                _excludeMask = new bool[MAX_EXCLUDED_CHANNELS];
                _additionalExcludedChannels = new bool[MAX_NBR_BANDS];
                _bandTop = new int[MAX_DRC_BANDS];
                _dynRngSgn = new bool[MAX_DRC_BANDS];
                _dynRngCtl = new int[MAX_DRC_BANDS];
            }
        }

        private const int TYPE_FILL = 0;
        private const int TYPE_FILL_DATA = 1;
        private const int TYPE_EXT_DATA_ELEMENT = 2;
        private const int TYPE_DYNAMIC_RANGE = 11;
        private const int TYPE_SBR_DATA = 13;
        private const int TYPE_SBR_DATA_CRC = 14;
        private bool _downSampledSBR;
        private DynamicRangeInfo _dri;
        private bool _used;

        public FIL(bool downSampledSBR)
        {
            _downSampledSBR = downSampledSBR;
            _dri = new DynamicRangeInfo();
        }

        internal void InitOnFirstUse(bool downSampledSBR)
        {
            if (_used) return;
            _used = true;
            _downSampledSBR = downSampledSBR;
        }

        public void Decode(BitStream input, Element prev, SampleFrequency sf, bool sbrEnabled, bool smallFrames)
        {
            int count = input.ReadBits(4);
            if (count == 15) count += input.ReadBits(8) - 1;
            count *= 8; //convert to bits

            int cpy = count;
            int pos = input.GetPosition();

            while (count > 0)
            {
                count = DecodeExtensionPayload(input, count, prev, sf, sbrEnabled, smallFrames);
            }

            int pos2 = input.GetPosition() - pos;
            int bitsLeft = cpy - pos2;
            if (bitsLeft > 0) input.SkipBits(bitsLeft);
            else if (bitsLeft < 0) throw new AACException("FIL element overread: " + bitsLeft);
        }

        private int DecodeExtensionPayload(BitStream input, int count, Element prev, SampleFrequency sf, bool sbrEnabled, bool smallFrames)
        {
            int type = input.ReadBits(4);
            int ret = count - 4;
            switch (type)
            {
                case TYPE_DYNAMIC_RANGE:
                    ret = DecodeDynamicRangeInfo(input, ret);
                    break;
                case TYPE_SBR_DATA:
                case TYPE_SBR_DATA_CRC:
                    // without an element to apply it to (SBR before the first SCE or CPE) the data is skipped
                    if (sbrEnabled && (prev is SCE_LFE || prev is CPE))
                    {
                        prev.DecodeSBR(input, sf, ret, prev is CPE, type == TYPE_SBR_DATA_CRC, _downSampledSBR, smallFrames);
                        ret = 0;
                    }
                    else
                    {
                        input.SkipBits(ret);
                        ret = 0;
                    }
                    break;
                case TYPE_FILL:
                case TYPE_FILL_DATA:
                case TYPE_EXT_DATA_ELEMENT:
                default:
                    input.SkipBits(ret);
                    ret = 0;
                    break;
            }
            return ret;
        }

        private int DecodeDynamicRangeInfo(BitStream input, int count)
        {
            //the first byte is the extension type and the four presence flags
            int ret = count - 4;

            int bandCount = 1;

            //pce tag
            if (_dri._pceTagPresent = input.ReadBool())
            {
                _dri._pceInstanceTag = input.ReadBits(4);
                _dri._tagReservedBits = input.ReadBits(4);
                ret -= 8;
            }

            //excluded channels
            if (_dri._excludedChannelsPresent = input.ReadBool())
            {
                ret -= DecodeExcludedChannels(input);
            }

            //bands
            if (_dri._bandsPresent = input.ReadBool())
            {
                _dri._bandsIncrement = input.ReadBits(4);
                _dri._interpolationScheme = input.ReadBits(4);
                ret -= 8;
                bandCount += _dri._bandsIncrement;
                for (int i = 0; i < bandCount; i++)
                {
                    _dri._bandTop[i] = input.ReadBits(8);
                    ret -= 8;
                }
            }

            //prog ref level
            if (_dri._progRefLevelPresent = input.ReadBool())
            {
                _dri._progRefLevel = input.ReadBits(7);
                _dri._progRefLevelReservedBits = input.ReadBits(1);
                ret -= 8;
            }

            for (int i = 0; i < bandCount; i++)
            {
                _dri._dynRngSgn[i] = input.ReadBool();
                _dri._dynRngCtl[i] = input.ReadBits(7);
                ret -= 8;
            }
            return ret;
        }

        private int DecodeExcludedChannels(BitStream input)
        {
            int i;
            int exclChs = 0;

            //groups of 7 channels, each followed by a bit that says if another group follows
            do
            {
                for (i = 0; i < 7; i++)
                {
                    bool excluded = input.ReadBool();
                    if (exclChs < _dri._excludeMask.Length) _dri._excludeMask[exclChs] = excluded;
                    exclChs++;
                }
            }
            while (input.ReadBool());

            return exclChs / 7 * 8;
        }
    }
}
