using SharpJaad.AAC.Syntax;
using System;

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

        //the highest band TNS filters: per sample frequency index, for long and short windows (ISO/IEC 14496-3 4.6.9.4)
        private static readonly int[] TNS_MAX_BANDS_1024 = { 31, 31, 34, 40, 42, 51, 46, 46, 42, 42, 42, 39, 39 };
        private static readonly int[] TNS_MAX_BANDS_128 = { 9, 9, 10, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14 };
        private readonly float[] _lpc = new float[TNS_MAX_ORDER];

        /// <summary>
        /// Applies the TNS filters to the spectrum of a channel.
        /// </summary>
        /// <param name="decode">false to decode: the all-pole filter on the decoded spectrum. true for the
        /// prediction of LTP: the inverse, all-zero filter on the predicted spectrum.</param>
        public void Process(ICStream ics, float[] spec, SampleFrequency sf, bool decode)
        {
            ICSInfo info = ics.GetInfo();
            int sfi = (int)sf;
            if (sfi < 0 || sfi >= TNS_MAX_BANDS_1024.Length) return;
            bool shortWindows = info.IsEightShortFrame();
            int maxBands = Math.Min(shortWindows ? TNS_MAX_BANDS_128[sfi] : TNS_MAX_BANDS_1024[sfi], info.GetMaxSFB());
            int swbCount = info.GetSWBCount();
            int[] offsets = info.GetSWBOffsets();
            int windowCount = info.GetWindowCount();
            int windowLength = shortWindows ? info.GetShortWindowLength() : info.GetFrameLength();

            for (int w = 0; w < windowCount; w++)
            {
                int bottom = swbCount;
                for (int filt = 0; filt < _nFilt[w]; filt++)
                {
                    int top = bottom;
                    bottom = Math.Max(0, top - _length[w, filt]);
                    int order = _order[w, filt];
                    if (order == 0) continue;

                    ToLPC(w, filt, order);

                    int start = offsets[Math.Min(bottom, maxBands)];
                    int end = offsets[Math.Min(top, maxBands)];
                    int size = end - start;
                    if (size <= 0) continue;

                    int inc = 1;
                    if (_direction[w, filt])
                    {
                        inc = -1;
                        start = end - 1;
                    }
                    start += w * windowLength;

                    if (!decode)
                    {
                        //all-pole filter: in place, each output depends on the previous outputs
                        for (int m = 0, k = start; m < size; m++, k += inc)
                        {
                            float y = spec[k];
                            int n = Math.Min(m, order);
                            for (int i = 1; i <= n; i++) y -= spec[k - i * inc] * _lpc[i - 1];
                            spec[k] = y;
                        }
                    }
                    else
                    {
                        //all-zero filter: from the end, each output depends on the previous inputs
                        int k = start + (size - 1) * inc;
                        for (int m = size - 1; m >= 0; m--, k -= inc)
                        {
                            float y = spec[k];
                            int n = Math.Min(m, order);
                            for (int i = 1; i <= n; i++) y += spec[k - i * inc] * _lpc[i - 1];
                            spec[k] = y;
                        }
                    }
                }
            }
        }

        //the reflection coefficients of a filter to the direct form (Levinson recursion)
        private void ToLPC(int w, int filt, int order)
        {
            for (int i = 0; i < order; i++)
            {
                float r = -_coef[w, filt, i];
                _lpc[i] = r;
                for (int j = 0; j < (i + 1) >> 1; j++)
                {
                    float f = _lpc[j];
                    float b = _lpc[i - 1 - j];
                    _lpc[j] = f + r * b;
                    _lpc[i - 1 - j] = b + r * f;
                }
            }
        }
    }
}
