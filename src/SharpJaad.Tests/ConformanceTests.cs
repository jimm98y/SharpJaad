using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SharpJaad.Tests
{
    /// <summary>
    /// Decodes the AAC conformance streams of the FFmpeg test suite (https://fate-suite.ffmpeg.org/aac/) and compares
    /// the PCM with the output recorded in TestData/conformance.txt: any change of the output shows, also where no
    /// reference exists (ReferenceTests compares with FFmpeg). The streams are not in the repository: set
    /// SHARPJAAD_CONFORMANCE_DIR to a folder holding them (see doc/testing.md). Without it the tests only report that
    /// they did not run.
    /// </summary>
    public class ConformanceTests
    {
        private const string DirectoryVariable = "SHARPJAAD_CONFORMANCE_DIR";
        private readonly ITestOutputHelper _output;

        public ConformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static IEnumerable<object[]> Streams()
        {
            string file = Path.Combine(AppContext.BaseDirectory, "TestData", "conformance.txt");
            foreach (string line in File.ReadAllLines(file).Where(l => l.Length > 0 && !l.StartsWith("#")))
            {
                int colon = line.IndexOf(": ", StringComparison.Ordinal);
                yield return new object[] { line.Substring(0, colon), Comparable(line.Substring(colon + 2)) };
            }
        }

        public static string Locate(string name)
        {
            string directory = Environment.GetEnvironmentVariable(DirectoryVariable);
            if (string.IsNullOrEmpty(directory)) return null;
            string path = Path.Combine(directory, name);
            return File.Exists(path) ? path : null;
        }

        [Theory]
        [MemberData(nameof(Streams))]
        public void DecodesToTheRecordedPcm(string name, string expected)
        {
            string path = Locate(name);
            if (path == null)
            {
                _output.WriteLine($"not run: {name} not found, {DirectoryVariable} = '{Environment.GetEnvironmentVariable(DirectoryVariable)}'");
                return;
            }

            string actual = TestStreams.Summarize(path);
            _output.WriteLine($"{name}: {actual}"); // the line for conformance.txt
            Assert.Equal(expected, Comparable(actual));
        }

        [Fact]
        public void TheNoiseOfPnsDoesNotDependOnOtherDecoders()
        {
            // al17 and al18 use PNS: decoding one while another decoder decodes the other must not change its noise
            string pathA = Locate("al17_44.mp4"), pathB = Locate("al18_44.mp4");
            if (pathA == null || pathB == null)
            {
                _output.WriteLine($"not run: al17_44.mp4 or al18_44.mp4 not found, {DirectoryVariable} = '{Environment.GetEnvironmentVariable(DirectoryVariable)}'");
                return;
            }
            TestStreams.Stream a = TestStreams.Read(pathA), b = TestStreams.Read(pathB);

            var alone = new AAC.Decoder(a.DecoderSpecificInfo);
            var aloneBuffer = new AAC.SampleBuffer();
            var expected = new List<byte[]>();
            foreach (byte[] frame in a.Frames)
            {
                alone.DecodeFrame(frame, aloneBuffer);
                expected.Add((byte[])aloneBuffer.Data.Clone());
            }

            var first = new AAC.Decoder(a.DecoderSpecificInfo);
            var second = new AAC.Decoder(b.DecoderSpecificInfo);
            var firstBuffer = new AAC.SampleBuffer();
            var secondBuffer = new AAC.SampleBuffer();
            for (int i = 0; i < a.Frames.Count; i++)
            {
                if (i < b.Frames.Count) second.DecodeFrame(b.Frames[i], secondBuffer);
                first.DecodeFrame(a.Frames[i], firstBuffer);
                Assert.Equal(expected[i], firstBuffer.Data);
            }
        }

        // exception messages come from the runtime and may be localized: compare the exception type only
        private static string Comparable(string summary)
        {
            int first = summary.IndexOf(" first=", StringComparison.Ordinal);
            if (first >= 0) summary = summary.Substring(0, first);
            int colon = summary.StartsWith("SETUP-ERR") ? summary.IndexOf(':') : -1;
            return colon >= 0 ? summary.Substring(0, colon) : summary;
        }
    }
}
