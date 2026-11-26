using SharpJaad.AAC;
#if NET8_0_OR_GREATER
using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
#endif

namespace SharpJaad.AAC.Filterbank
{
    public class FFT
    {
        private int _length;
        private float[,] _roots;
        private int[] _swaps;

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
            var swaps = new System.Collections.Generic.List<int>();
            for (int i = 0, ii = 0; i < length; i++)
            {
                if (i < ii)
                {
                    swaps.Add(i);
                    swaps.Add(ii);
                }
                int k = length >> 1;
                while (ii >= k && k > 0)
                {
                    ii -= k;
                    k >>= 1;
                }
                ii += k;
            }
            _swaps = swaps.ToArray();
            if ((length & (length - 1)) != 0) InitMixedRadix();
#if NET8_0_OR_GREATER
            else
            {
                // the roots of every stage and direction made here, not of the first frame of each: a frame allocates nothing
                _stageRoots = new float[64][];
                int stage = 0;
                for (int i = 4; i < length; i <<= 1, stage++)
                {
                    StageRoots(stage, i, length / (i << 1), false);
                    StageRoots(stage, i, length / (i << 1), true);
                }
            }
#endif
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
            //bit-reversal: its own inverse, so of the pairs it swaps, made once
            for (int n = 0; n < _swaps.Length; n += 2)
            {
                int i = _swaps[n], j = _swaps[n + 1];
                float re = input[i, 0], im = input[i, 1];
                input[i, 0] = input[j, 0];
                input[i, 1] = input[j, 1];
                input[j, 0] = re;
                input[j, 1] = im;
            }

            //bottom base-4 round
            for (int i = 0; i < _length; i += 4)
            {
                float aRe = input[i, 0] + input[i + 1, 0];
                float aIm = input[i, 1] + input[i + 1, 1];
                float bRe = input[i + 2, 0] + input[i + 3, 0];
                float bIm = input[i + 2, 1] + input[i + 3, 1];
                float cRe = input[i, 0] - input[i + 1, 0];
                float cIm = input[i, 1] - input[i + 1, 1];
                float dRe = input[i + 2, 0] - input[i + 3, 0];
                float dIm = input[i + 2, 1] - input[i + 3, 1];
                input[i, 0] = aRe + bRe;
                input[i, 1] = aIm + bIm;
                input[i + 2, 0] = aRe - bRe;
                input[i + 2, 1] = aIm - bIm;

                float e1Re = cRe - dIm;
                float e1Im = cIm + dRe;
                float e2Re = cRe + dIm;
                float e2Im = cIm - dRe;
                if (forward)
                {
                    input[i + 1, 0] = e2Re;
                    input[i + 1, 1] = e2Im;
                    input[i + 3, 0] = e1Re;
                    input[i + 3, 1] = e1Im;
                }
                else
                {
                    input[i + 1, 0] = e1Re;
                    input[i + 1, 1] = e1Im;
                    input[i + 3, 0] = e2Re;
                    input[i + 3, 1] = e2Im;
                }
            }

            //iterations from bottom to top
            int shift, m, km;
            float rootRe, rootIm, zRe, zIm;
#if NET8_0_OR_GREATER
            if (Vector128.IsHardwareAccelerated && scale == 1)
            {
                ProcessStages(input, forward);
                return;
            }
#endif
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

#if NET8_0_OR_GREATER
        // Of each stage of the iterations from bottom to top and of each direction, its roots in the order the butterflies
        // take them, each lane twice (of a complex value's two): of the real parts, then of the imaginary.
        private float[][] _stageRoots;

        private float[] StageRoots(int stage, int i, int m, bool forward)
        {
            int index = 2 * stage + (forward ? 1 : 0);
            float[] t = _stageRoots[index];
            if (t != null)
                return t;
            int imOff = forward ? 2 : 1;
            t = new float[4 * i];
            for (int k = 0; k < i; k++)
            {
                t[2 * k] = t[2 * k + 1] = _roots[k * m, 0];
                t[2 * i + 2 * k] = t[2 * i + 2 * k + 1] = _roots[k * m, imOff];
            }
            return _stageRoots[index] = t;
        }

        // the iterations from bottom to top, two butterflies at a time: of each lane the operations of the loop of Process,
        // so of the same results - z of input * root as there, its sum and difference of input of j + k
        private void ProcessStages(float[,] input, bool forward)
        {
            // loaded and stored of the references, not sliced and bounds checked of each: the loops keep within
            ref float x = ref input[0, 0];
            Vector128<int> swap = Vector128.Create(1, 0, 3, 2);
            Vector128<float> sign = Vector128.Create(-1f, 1f, -1f, 1f);
            int stage = 0;
            for (int i = 4; i < _length; i <<= 1, stage++)
            {
                int shift = i << 1;
                int m = _length / shift;
                ref float roots = ref StageRoots(stage, i, m, forward)[0];
                for (int j = 0; j < _length; j += shift)
                {
                    for (int k = 0; k < i; k += 2)
                    {
                        Vector128<float> a = Vector128.LoadUnsafe(ref x, (nuint)(2 * (i + j + k)));
                        Vector128<float> rr = Vector128.LoadUnsafe(ref roots, (nuint)(2 * k));
                        Vector128<float> ri = Vector128.LoadUnsafe(ref roots, (nuint)(2 * i + 2 * k));
                        // (re * rootRe - im * rootIm, re * rootIm + im * rootRe): of the swapped, (im * rootIm, re * rootIm)
                        Vector128<float> z = a * rr + Vector128.Shuffle(a.AsInt32(), swap).AsSingle() * ri * sign;
                        Vector128<float> y = Vector128.LoadUnsafe(ref x, (nuint)(2 * (j + k)));
                        (y - z).StoreUnsafe(ref x, (nuint)(2 * (i + j + k)));
                        (y + z).StoreUnsafe(ref x, (nuint)(2 * (j + k)));
                    }
                }
            }
        }
#endif

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
