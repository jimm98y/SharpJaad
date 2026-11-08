using SharpJaad.AAC.Sbr;

namespace SharpJaad.AAC.Syntax
{
    public abstract class Element
    {
        private int _elementInstanceTag;
        private SBR _sbr;

        protected void ReadElementInstanceTag(BitStream input)
        {
            _elementInstanceTag = input.ReadBits(4);
        }

        public int GetElementInstanceTag()
        {
            return _elementInstanceTag;
        }

        public void DecodeSBR(BitStream input, SampleFrequency sf, int count, bool stereo, bool crc, bool downSampled, bool smallFrames)
        {
            GetOrCreateSBR(sf, stereo, downSampled, smallFrames).Decode(input, count, crc);
        }

        /// <summary>
        /// The SBR of the element, created if the element has had no SBR data: in a stream with SBR such an element
        /// (an LFE for example) is still upsampled by the SBR filterbanks, which keeps it in time with the others.
        /// </summary>
        internal SBR GetOrCreateSBR(SampleFrequency sf, bool stereo, bool downSampled, bool smallFrames)
        {
            if (_sbr == null)
            {
                // SBR works at twice the core sample rate, also in downsampled mode (as in FAAD2), where only its
                // synthesis filterbank is halved; the tables of SBR depend on this rate
                SampleFrequency doubled = SampleFrequencyExtensions.FromFrequency(2 * sf.GetFrequency());
                if (doubled != SampleFrequency.SAMPLE_FREQUENCY_NONE) sf = doubled;
                _sbr = new SBR(smallFrames, stereo, sf, downSampled);
            }
            return _sbr;
        }

        public bool IsSBRPresent()
        {
            return _sbr != null;
        }

        public SBR GetSBR()
        {
            return _sbr;
        }
    }
}
