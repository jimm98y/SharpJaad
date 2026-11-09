using SharpJaad.AAC.Filterbank;
using System;
using Xunit;

namespace SharpJaad.Tests
{
    /// <summary>
    /// The FFT of the filterbank against a direct DFT: the power of 2 lengths of frames of 1024 samples, and 60 and 480
    /// (mixed radix) of frames of 960 samples, in both directions.
    /// </summary>
    public class FftTests
    {
        [Theory]
        [InlineData(64, true)]
        [InlineData(64, false)]
        [InlineData(512, true)]
        [InlineData(512, false)]
        [InlineData(60, true)]
        [InlineData(60, false)]
        [InlineData(480, true)]
        [InlineData(480, false)]
        public void MatchesADirectDft(int length, bool forward)
        {
            var random = new Random(length);
            var data = new float[length, 2];
            var input = new double[length, 2];
            for (int i = 0; i < length; i++)
            {
                input[i, 0] = data[i, 0] = (float)(random.NextDouble() * 2 - 1);
                input[i, 1] = data[i, 1] = (float)(random.NextDouble() * 2 - 1);
            }

            new FFT(length).Process(data, forward);

            // the forward transform uses exp(-i), the inverse exp(+i), neither is scaled
            double sign = forward ? -1 : 1, maxError = 0;
            for (int k = 0; k < length; k++)
            {
                double re = 0, im = 0;
                for (int n = 0; n < length; n++)
                {
                    double a = sign * 2 * Math.PI * k * n / length;
                    re += input[n, 0] * Math.Cos(a) - input[n, 1] * Math.Sin(a);
                    im += input[n, 0] * Math.Sin(a) + input[n, 1] * Math.Cos(a);
                }
                maxError = Math.Max(maxError, Math.Max(Math.Abs(re - data[k, 0]), Math.Abs(im - data[k, 1])));
            }
            // the outputs are of the order of sqrt(length): float precision relative to that
            Assert.True(maxError < 1e-4 * Math.Sqrt(length), $"max error {maxError}");
        }
    }
}
