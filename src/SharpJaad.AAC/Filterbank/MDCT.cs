using SharpJaad.AAC;
#if NET8_0_OR_GREATER
using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
#endif

namespace SharpJaad.AAC.Filterbank
{
    public class MDCT
    {
        private int _N, _N2, _N4, _N8;
        private float[][] _sincos;
        private FFT _fft;
        private float[,] _buf;
        private float[] _tmp;
#if NET8_0_OR_GREATER
        // of the post-IFFT multiplication, _sincos of each k, each lane twice: the cosines, then the sines
        private float[] _cosSin;
#endif

        public MDCT(int length)
        {
            _N = length;
            _N2 = length >> 1;
            _N4 = length >> 2;
            _N8 = length >> 3;
            switch (length)
            {
                case 2048:
                    _sincos = MDCTTables.MDCT_TABLE_2048;
                    break;
                case 256:
                    _sincos = MDCTTables.MDCT_TABLE_128;
                    break;
                case 1920:
                    _sincos = MDCTTables.MDCT_TABLE_1920;
                    break;
                case 240:
                    _sincos = MDCTTables.MDCT_TABLE_240;
                    break;
                default:
                    throw new AACException("unsupported MDCT length: " + length);
            }
            _fft = new FFT(_N4);
            _buf = new float[_N4, 2];
            _tmp = new float[2];
#if NET8_0_OR_GREATER
            _cosSin = new float[4 * _N4];
            for (int k = 0; k < _N4; k++)
            {
                _cosSin[2 * k] = _cosSin[2 * k + 1] = _sincos[k][0];
                _cosSin[2 * _N4 + 2 * k] = _cosSin[2 * _N4 + 2 * k + 1] = _sincos[k][1];
            }
#endif
        }

        public void Process(float[] input, int inOff, float[] output, int outOff)
        {
            int k;

            //pre-IFFT complex multiplication
#if NET8_0_OR_GREATER
            // the buffer as a span of (re, im), the cosines and sines of their flat table: of the operations of the loops
            Span<float> b = MemoryMarshal.CreateSpan(ref _buf[0, 0], _N4 * 2);
            ReadOnlySpan<float> x = input.AsSpan(inOff, _N2);
            for (k = 0; k < _N4; k++)
            {
                float c = _cosSin[2 * k], s = _cosSin[2 * _N4 + 2 * k];
                float even = x[2 * k], odd = x[_N2 - 1 - 2 * k];
                b[2 * k + 1] = even * c + odd * s;
                b[2 * k] = odd * c - even * s;
            }
#else
            for (k = 0; k < _N4; k++)
            {
                _buf[k, 1] = input[inOff + 2 * k] * _sincos[k][0] + input[inOff + _N2 - 1 - 2 * k] * _sincos[k][1];
                _buf[k, 0] = input[inOff + _N2 - 1 - 2 * k] * _sincos[k][0] - input[inOff + 2 * k] * _sincos[k][1];
            }
#endif

            //complex IFFT, non-scaling
            _fft.Process(_buf, false);

            //post-IFFT complex multiplication
            k = 0;
#if NET8_0_OR_GREATER
            if (Vector128.IsHardwareAccelerated)
            {
                // two of k at a time, of the operations of the loop below on each lane: (t0 c - t1 s, t1 c + t0 s)
                Vector128<int> swap = Vector128.Create(1, 0, 3, 2);
                Vector128<float> sign = Vector128.Create(-1f, 1f, -1f, 1f);
                for (; k + 2 <= _N4; k += 2)
                {
                    Vector128<float> t = Vector128.Create((ReadOnlySpan<float>)b.Slice(2 * k, 4));
                    Vector128<float> c = Vector128.Create((ReadOnlySpan<float>)_cosSin.AsSpan(2 * k, 4));
                    Vector128<float> s = Vector128.Create((ReadOnlySpan<float>)_cosSin.AsSpan(2 * _N4 + 2 * k, 4));
                    (t * c + Vector128.Shuffle(t.AsInt32(), swap).AsSingle() * s * sign).CopyTo(b.Slice(2 * k, 4));
                }
            }
#endif
            for (; k < _N4; k++)
            {
                _tmp[0] = _buf[k, 0];
                _tmp[1] = _buf[k, 1];
                _buf[k, 1] = _tmp[1] * _sincos[k][0] + _tmp[0] * _sincos[k][1];
                _buf[k, 0] = _tmp[0] * _sincos[k][0] - _tmp[1] * _sincos[k][1];
            }

            //reordering
            for (k = 0; k < _N8; k += 2)
            {
                output[outOff + 2 * k] = _buf[_N8 + k, 1];
                output[outOff + 2 + 2 * k] = _buf[_N8 + 1 + k, 1];

                output[outOff + 1 + 2 * k] = -_buf[_N8 - 1 - k, 0];
                output[outOff + 3 + 2 * k] = -_buf[_N8 - 2 - k, 0];

                output[outOff + _N4 + 2 * k] = _buf[k, 0];
                output[outOff + _N4 + 2 + 2 * k] = _buf[1 + k, 0];

                output[outOff + _N4 + 1 + 2 * k] = -_buf[_N4 - 1 - k, 1];
                output[outOff + _N4 + 3 + 2 * k] = -_buf[_N4 - 2 - k, 1];

                output[outOff + _N2 + 2 * k] = _buf[_N8 + k, 0];
                output[outOff + _N2 + 2 + 2 * k] = _buf[_N8 + 1 + k, 0];

                output[outOff + _N2 + 1 + 2 * k] = -_buf[_N8 - 1 - k, 1];
                output[outOff + _N2 + 3 + 2 * k] = -_buf[_N8 - 2 - k, 1];

                output[outOff + _N2 + _N4 + 2 * k] = -_buf[k, 1];
                output[outOff + _N2 + _N4 + 2 + 2 * k] = -_buf[1 + k, 1];

                output[outOff + _N2 + _N4 + 1 + 2 * k] = _buf[_N4 - 1 - k, 0];
                output[outOff + _N2 + _N4 + 3 + 2 * k] = _buf[_N4 - 2 - k, 0];
            }
        }

        public void ProcessForward(float[] input, float[] output)
        {
            int n, k;
            //pre-FFT complex multiplication
            for (k = 0; k < _N8; k++)
            {
                n = k << 1;
                _tmp[0] = input[_N - _N4 - 1 - n] + input[_N - _N4 + n];
                _tmp[1] = input[_N4 + n] - input[_N4 - 1 - n];

                _buf[k, 0] = _tmp[0] * _sincos[k][0] + _tmp[1] * _sincos[k][1];
                _buf[k, 1] = _tmp[1] * _sincos[k][0] - _tmp[0] * _sincos[k][1];

                _buf[k, 0] *= _N;
                _buf[k, 1] *= _N;

                _tmp[0] = input[_N2 - 1 - n] - input[n];
                _tmp[1] = input[_N2 + n] + input[_N - 1 - n];

                _buf[k + _N8, 0] = _tmp[0] * _sincos[k + _N8][0] + _tmp[1] * _sincos[k + _N8][1];
                _buf[k + _N8, 1] = _tmp[1] * _sincos[k + _N8][0] - _tmp[0] * _sincos[k + _N8][1];

                _buf[k + _N8, 0] *= _N;
                _buf[k + _N8, 1] *= _N;
            }

            //complex FFT, non-scaling
            _fft.Process(_buf, true);

            //post-FFT complex multiplication
            for (k = 0; k < _N4; k++)
            {
                n = k << 1;

                _tmp[0] = _buf[k, 0] * _sincos[k][0] + _buf[k, 1] * _sincos[k][1];
                _tmp[1] = _buf[k, 1] * _sincos[k][0] - _buf[k, 0] * _sincos[k][1];

                output[n] = -_tmp[0];
                output[_N2 - 1 - n] = _tmp[1];
                output[_N2 + n] = -_tmp[1];
                output[_N - 1 - n] = _tmp[0];
            }
        }
    }
}
