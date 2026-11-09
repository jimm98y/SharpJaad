using SharpJaad.Tools;

namespace SharpJaad.MP4.Boxes.Impl
{
    public class SampleSizeBox : FullBox
    {
        private long _sampleCount;
        private long[] _sampleSizes;

        public SampleSizeBox() : base("Sample Size Box")
        {  }

        public override void Decode(MP4InputStream input)
        {
            base.Decode(input);

            bool compact = _type == BoxTypes.COMPACT_SAMPLE_SIZE_BOX;
            const long MAX_CONSTANT_SIZE_SAMPLES = 1 << 24; // when the stream length is not known: about 100 hours of AAC


            int sampleSize;
            if (compact) {
                input.SkipBytes(3);
                sampleSize = input.Read();
            }
            else sampleSize = (int)input.ReadBytes(4);

            _sampleCount = input.ReadBytes(4);
            // the sizes must be in the box; with one size for all samples, the samples must fit in the stream
            long tableBytes = !compact ? (sampleSize == 0 ? _sampleCount * 4 : 0) : sampleSize == 4 ? (_sampleCount + 1) / 2 : _sampleCount * (sampleSize / 8);
            if (compact && sampleSize != 4 && sampleSize != 8 && sampleSize != 16) throw new MP4Exception("invalid compact sample size: " + sampleSize);
            if (tableBytes > GetLeft(input) || _sampleCount > int.MaxValue
                || (!compact && sampleSize != 0 && (_sampleCount * sampleSize > input.GetLength() || _sampleCount > MAX_CONSTANT_SIZE_SAMPLES)))
                throw new MP4Exception("invalid sample count: " + _sampleCount);
            _sampleSizes = new long[(int)_sampleCount];

            if (compact)
            {
                //compact: sampleSize can be 4, 8 or 16 bits
                if (sampleSize == 4)
                {
                    int x;
                    for (int i = 0; i < _sampleCount; i += 2)
                    {
                        x = input.Read();
                        _sampleSizes[i] = (x >> 4) & 0xF;
                        if (i + 1 < _sampleCount) _sampleSizes[i + 1] = x & 0xF;
                    }
                }
                else
                {
                    ReadSizes(input, sampleSize / 8);
                }
            }
            else if (sampleSize == 0)
            {
                ReadSizes(input, 4);
            }
            else
            {
                Arrays.Fill(_sampleSizes, sampleSize);
            }
        }

        private void ReadSizes(MP4InputStream input, int len)
        {
            for (int i = 0; i < _sampleCount; i++) 
            {
                _sampleSizes[i] = input.ReadBytes(len);
            }
        }

        public int GetSampleCount()
        {
            return (int)_sampleCount;
        }

        public long[] GetSampleSizes()
        {
            return _sampleSizes;
        }
    }
}
