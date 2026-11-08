using SharpJaad.AAC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpJaad.Tests
{
    /// <summary>
    /// Compares the PCM of the conformance streams with the reference PCM of the FFmpeg test suite (the .s16 files next
    /// to the streams at https://fate-suite.ffmpeg.org/aac/), channel by channel. TestData/reference.txt lists for each
    /// stream its reference, the channels of the reference, for each of them the channel of SharpJaad's output it is
    /// compared with (the references of multichannel streams are in FFmpeg's channel order) and the lowest signal to
    /// noise ratio a channel may have. The thresholds are a little below what the decoder reaches now: a change that
    /// makes a stream worse fails, one that makes it better can raise its threshold.
    /// </summary>
    public class ReferenceTests
    {
        private readonly ITestOutputHelper _output;

        public ReferenceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static IEnumerable<object[]> References()
        {
            string file = Path.Combine(AppContext.BaseDirectory, "TestData", "reference.txt");
            foreach (string line in File.ReadAllLines(file).Where(l => l.Length > 0 && !l.StartsWith("#")))
            {
                string[] f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                yield return new object[] { f[0], f[1], int.Parse(f[2]), f[3], double.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture) };
            }
        }

        [Theory]
        [MemberData(nameof(References))]
        public void MatchesTheFFmpegReference(string name, string reference, int referenceChannels, string channelMap, double minSnr)
        {
            string path = ConformanceTests.Locate(name);
            string referencePath = ConformanceTests.Locate(reference);
            if (path == null || referencePath == null)
            {
                _output.WriteLine($"not run: {name} or {reference} not found (see doc/testing.md)");
                return;
            }

            int[] map = channelMap.Split(',').Select(int.Parse).ToArray();
            short[] ours = Decode(path, out int channels);
            byte[] refBytes = File.ReadAllBytes(referencePath);
            int frames = Math.Min(ours.Length / channels, refBytes.Length / 2 / referenceChannels);

            var failures = new List<string>();
            for (int c = 0; c < referenceChannels; c++)
            {
                double signal = 0, error = 0;
                for (int i = 0; i < frames; i++)
                {
                    int k = 2 * (i * referenceChannels + c);
                    double r = (short)(refBytes[k] | (refBytes[k + 1] << 8));
                    double d = ours[i * channels + map[c]] - r;
                    signal += r * r;
                    error += d * d;
                }
                double snr = error == 0 ? double.PositiveInfinity : 10 * Math.Log10(signal / error);
                _output.WriteLine($"reference channel {c} (ours {map[c]}): {snr:F1} dB");
                if (snr < minSnr) failures.Add($"channel {c}: {snr:F1} dB < {minSnr} dB");
            }
            Assert.True(failures.Count == 0, string.Join(", ", failures));
        }

        private static short[] Decode(string path, out int channels)
        {
            TestStreams.Stream stream = TestStreams.Read(path);
            var decoder = TestStreams.CreateDecoder(stream.DecoderSpecificInfo);
            var buffer = new SampleBuffer();
            buffer.SetBigEndian(false);
            var pcm = new List<short>();
            channels = 0;
            foreach (byte[] frame in stream.Frames)
            {
                decoder.DecodeFrame(frame, buffer);
                channels = Math.Max(channels, buffer.Channels);
                byte[] data = buffer.Data;
                for (int i = 0; i + 1 < data.Length; i += 2) pcm.Add((short)(data[i] | (data[i + 1] << 8)));
            }
            return pcm.ToArray();
        }
    }
}
