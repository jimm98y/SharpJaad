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
    /// the PCM with what SharpJaad 0.1.1 produced, listed in TestData/conformance.txt. The streams are not in the
    /// repository: set SHARPJAAD_CONFORMANCE_DIR to a folder holding them (see doc/testing.md). Without it the tests
    /// only report that they did not run.
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
        public void DecodesToTheSamePcmAsTheLastRelease(string name, string expected)
        {
            string path = Locate(name);
            if (path == null)
            {
                _output.WriteLine($"not run: {name} not found, {DirectoryVariable} = '{Environment.GetEnvironmentVariable(DirectoryVariable)}'");
                return;
            }

            // as in a new process, which is how conformance.txt was made
            TestStreams.ResetNoiseGenerator();
            Assert.Equal(expected, Comparable(TestStreams.Summarize(path)));
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
