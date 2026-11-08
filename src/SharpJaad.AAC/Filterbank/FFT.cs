using SharpJaad.AAC;

namespace SharpJaad.AAC.Filterbank
{
    public class FFT
    {
        private int _length;
        private float[,] _roots;
        private float[,] _rev;
        private float[] _a, _b, _c, _d, _e1, _e2;

        public FFT(int length)
        {
            _length = length;

            switch (length)
            {
                case 64:
                    _roots = FFTables.FFT_TABLE_64;
                    break;
                case 512:
                    _roots = FFTables.FFT_TABLE_512;
                    break;
                case 60:
                    _roots = FFTables.FFT_TABLE_60;
                    break;
                case 480:
                    _roots = FFTables.FFT_TABLE_480;
                    break;
                default:
                    throw new AACException("unexpected FFT length: " + length);
            }

            // the tables have the root and, for both directions, the imaginary part: [re, im inverse, im forward]
            if (_roots.GetLength(1) == 2)
            {
                var roots = new float[length, 3];
                for (int i = 0; i < length; i++)
                {
                    roots[i, 0] = _roots[i, 0];
                    roots[i, 1] = _roots[i, 1];
                    roots[i, 2] = -_roots[i, 1];
                }
                _roots = roots;
            }

            //processing buffers
            _rev = new float[length, 2];
            if ((length & (length - 1)) != 0) InitMixedRadix();
            _a = new float[2];
            _b = new float[2];
            _c = new float[2];
            _d = new float[2];
            _e1 = new float[2];
            _e2 = new float[2];
        }

        public void Process(float[,] input, bool forward)
        {
            if (_factors != null)
            {
                ProcessMixedRadix(input, forward);
                return;
            }
            int imOff = forward ? 2 : 1;
            int scale = 1;
            //bit-reversal
            int ii = 0;
            for (int i = 0; i < _length; i++)
            {
                _rev[i, 0] = input[ii, 0];
                _rev[i, 1] = input[ii, 1];
                int k = _length >> 1;
                while (ii >= k && k > 0)
                {
                    ii -= k;
                    k >>= 1;
                }
                ii += k;
            }
            for (int i = 0; i < _length; i++)
            {
                input[i, 0] = _rev[i, 0];
                input[i, 1] = _rev[i, 1];
            }

            //bottom base-4 round
            for (int i = 0; i < _length; i += 4)
            {
                _a[0] = input[i, 0] + input[i + 1, 0];
                _a[1] = input[i, 1] + input[i + 1, 1];
                _b[0] = input[i + 2, 0] + input[i + 3, 0];
                _b[1] = input[i + 2, 1] + input[i + 3, 1];
                _c[0] = input[i, 0] - input[i + 1, 0];
                _c[1] = input[i, 1] - input[i + 1, 1];
                _d[0] = input[i + 2, 0] - input[i + 3, 0];
                _d[1] = input[i + 2, 1] - input[i + 3, 1];
                input[i, 0] = _a[0] + _b[0];
                input[i, 1] = _a[1] + _b[1];
                input[i + 2, 0] = _a[0] - _b[0];
                input[i + 2, 1] = _a[1] - _b[1];

                _e1[0] = _c[0] - _d[1];
                _e1[1] = _c[1] + _d[0];
                _e2[0] = _c[0] + _d[1];
                _e2[1] = _c[1] - _d[0];
                if (forward)
                {
                    input[i + 1, 0] = _e2[0];
                    input[i + 1, 1] = _e2[1];
                    input[i + 3, 0] = _e1[0];
                    input[i + 3, 1] = _e1[1];
                }
                else
                {
                    input[i + 1, 0] = _e1[0];
                    input[i + 1, 1] = _e1[1];
                    input[i + 3, 0] = _e2[0];
                    input[i + 3, 1] = _e2[1];
                }
            }

            //iterations from bottom to top
            int shift, m, km;
            float rootRe, rootIm, zRe, zIm;
            for (int i = 4; i < _length; i <<= 1)
            {
                shift = i << 1;
                m = _length / shift;
                for (int j = 0; j < _length; j += shift)
                {
                    for (int k = 0; k < i; k++)
                    {
                        km = k * m;
                        rootRe = _roots[km, 0];
                        rootIm = _roots[km, imOff];
                        zRe = input[i + j + k, 0] * rootRe - input[i + j + k, 1] * rootIm;
                        zIm = input[i + j + k, 0] * rootIm + input[i + j + k, 1] * rootRe;

                        input[i + j + k, 0] = (input[j + k, 0] - zRe) * scale;
                        input[i + j + k, 1] = (input[j + k, 1] - zIm) * scale;
                        input[j + k, 0] = (input[j + k, 0] + zRe) * scale;
                        input[j + k, 1] = (input[j + k, 1] + zIm) * scale;
                    }
                }
            }
        }

        // lengths that are no power of 2 (60 and 480, for frames of 960 samples): a Stockham FFT over the prime
        // factors, with the roots of the table
        private int[] _factors;
        private float[,] _work;
        private float[] _sumRe, _sumIm;

        private void InitMixedRadix()
        {
            int n = _length, count = 0;
            var factors = new int[32];
            foreach (int p in new[] { 4, 2, 3, 5 })
            {
                while (n % p == 0)
                {
                    factors[count++] = p;
                    n /= p;
                }
            }
            if (n != 1) throw new AACException("unexpected FFT length: " + _length);
            _factors = new int[count];
            System.Array.Copy(factors, _factors, count);
            _work = new float[_length, 2];
            _sumRe = new float[5];
            _sumIm = new float[5];
        }

        private void ProcessMixedRadix(float[,] input, bool forward)
        {
            int imOff = forward ? 2 : 1;
            float[,] x = input, y = _work;
            int n = _length, s = 1;
            foreach (int p in _factors)
            {
                int m = n / p;
                int step = _length / n; // the roots of n are every step-th root of the table
                for (int j = 0; j < m; j++)
                {
                    for (int k = 0; k < s; k++)
                    {
                        // a DFT of size p over x[k + s * (j + m * r)], then the twiddle factor of n for output q
                        for (int q = 0; q < p; q++)
                        {
                            float re = 0, im = 0;
                            for (int r = 0; r < p; r++)
                            {
                                int root = (r * q % p) * (_length / p);
                                float wr = _roots[root, 0], wi = _roots[root, imOff];
                                float ar = x[k + s * (j + m * r), 0], ai = x[k + s * (j + m * r), 1];
                                re += ar * wr - ai * wi;
                                im += ar * wi + ai * wr;
                            }
                            _sumRe[q] = re;
                            _sumIm[q] = im;
                        }
                        for (int q = 0; q < p; q++)
                        {
                            int root = (j * q * step) % _length;
                            float wr = _roots[root, 0], wi = _roots[root, imOff];
                            y[k + s * (p * j + q), 0] = _sumRe[q] * wr - _sumIm[q] * wi;
                            y[k + s * (p * j + q), 1] = _sumRe[q] * wi + _sumIm[q] * wr;
                        }
                    }
                }
                float[,] t = x;
                x = y;
                y = t;
                n = m;
                s *= p;
            }
            if (x != input)
            {
                for (int i = 0; i < _length; i++)
                {
                    input[i, 0] = x[i, 0];
                    input[i, 1] = x[i, 1];
                }
            }
        }
    }
}
