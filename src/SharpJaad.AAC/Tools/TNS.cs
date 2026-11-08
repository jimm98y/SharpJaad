using SharpJaad.AAC.Syntax;

namespace SharpJaad.AAC.Tools
{
    public class TNS
    {
        private static int TNS_MAX_ORDER = 20;
        private static int[] SHORT_BITS = { 1, 4, 3 }, LONG_BITS = { 2, 6, 5 };
        //bitstream
        private int[] _nFilt;
        private int[,] _length, _order;
        private bool[,] _direction;
        private float[,,] _coef;

        public TNS()
        {
            _nFilt = new int[8];
            _length = new int[8, 4];
            _direction = new bool[8, 4];
            _order = new int[8, 4];
            _coef = new float[8, 4, TNS_MAX_ORDER];
        }

        public void Decode(BitStream input, ICSInfo info)
        {
            int windowCount = info.GetWindowCount();
            int[] bits = info.IsEightShortFrame() ? SHORT_BITS : LONG_BITS;

            int w, i, filt, coefLen, coefRes, coefCompress, tmp;
            for (w = 0; w < windowCount; w++)
            {
                if ((_nFilt[w] = input.ReadBits(bits[0])) != 0)
                {
                    coefRes = input.ReadBit();

                    for (filt = 0; filt < _nFilt[w]; filt++)
                    {
                        _length[w, filt] = input.ReadBits(bits[1]);

                        if ((_order[w, filt] = input.ReadBits(bits[2])) > 20) throw new AACException("TNS filter out of range: " + _order[w, filt]);
                        else if (_order[w, filt] != 0)
                        {
                            _direction[w, filt] = input.ReadBool();
                            coefCompress = input.ReadBit();
                            coefLen = coefRes + 3 - coefCompress;
                            tmp = 2 * coefCompress + coefRes;

                            for (i = 0; i < _order[w, filt]; i++)
                            {
                                _coef[w, filt, i] = TNSTables.TNS_TABLES[tmp][input.ReadBits(coefLen)];
                            }
                        }
                    }
                }
            }
        }

        // Called with ltp=false on the decoded spectrum (all-pole filter) and with
        // ltp=true from long-term prediction (all-zero filter), as in FAAD2.
        public void Process(ICStream ics, float[] spec, SampleFrequency sf, bool ltp)
        {
            ICSInfo info = ics.GetInfo();
            bool shortWin = info.IsEightShortFrame();
            int windowCount = info.GetWindowCount();
            int winLen = shortWin ? 128 : 1024;
            int[] swbOffsets = info.GetSWBOffsets();
            int swbOffsetMax = info.GetSWBOffsetMax();
            int maxBand = System.Math.Min(sf.GetMaximalTNS_SFB(shortWin), info.GetMaxSFB());
            float[] lpc = new float[TNS_MAX_ORDER + 1];
            float[] state = new float[TNS_MAX_ORDER];

            for (int w = 0; w < windowCount; w++)
            {
                int bottom = info.GetSWBCount();
                for (int filt = 0; filt < _nFilt[w]; filt++)
                {
                    int top = bottom;
                    bottom = System.Math.Max(top - _length[w, filt], 0);
                    int order = System.Math.Min(_order[w, filt], TNS_MAX_ORDER);
                    if (order == 0) continue;

                    // reflection (PARCOR) coefficients to direct-form LPC
                    lpc[0] = 1f;
                    for (int m = 1; m <= order; m++)
                    {
                        float k = -_coef[w, filt, m - 1]; // tables use ffmpeg's sign
                        for (int i = 1; i <= m / 2; i++)
                        {
                            float a = lpc[i], b = lpc[m - i];
                            lpc[i] = a + k * b;
                            lpc[m - i] = b + k * a;
                        }
                        lpc[m] = k;
                    }

                    int start = System.Math.Min(swbOffsets[System.Math.Min(bottom, maxBand)], swbOffsetMax);
                    int end = System.Math.Min(swbOffsets[System.Math.Min(top, maxBand)], swbOffsetMax);
                    int size = end - start;
                    if (size <= 0) continue;
                    int inc = 1;
                    if (_direction[w, filt]) { inc = -1; start = end - 1; }
                    int pos = w * winLen + start;

                    System.Array.Clear(state, 0, order);
                    for (int n = 0; n < size; n++, pos += inc)
                    {
                        float x = spec[pos], y = x;
                        for (int j = 0; j < order; j++)
                            y += (ltp ? 1f : -1f) * state[j] * lpc[j + 1];
                        for (int j = order - 1; j > 0; j--) state[j] = state[j - 1];
                        state[0] = ltp ? x : y;
                        spec[pos] = y;
                    }
                }
            }
        }
    }
}
