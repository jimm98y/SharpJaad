#if NET
using SharpJaad.AAC;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpJaad.Tests
{
    /// <summary>
    /// Decoding a frame allocates nothing once the decoder has decoded its first frame (which allocates the
    /// elements of the stream and the output buffer). GC.GetAllocatedBytesForCurrentThread is not available on
    /// .NET Framework, so these tests run on .NET only.
    /// </summary>
    [Collection(nameof(AllocationTests))]
    public class AllocationTests
    {
        [Fact]
        public void DecodingTheSampleAllocatesNothingAfterTheFirstFrame()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            AssertNoAllocationsAfterFirstFrame(stream, stream.Frames.Count);
        }

        [Theory]
        [MemberData(nameof(ConformanceTests.Streams), MemberType = typeof(ConformanceTests))]
        public void DecodingConformanceStreamsAllocatesNothingAfterTheFirstFrame(string name, string expected)
        {
            string path = ConformanceTests.Locate(name);
            if (path == null || expected.StartsWith("SETUP-ERR")) return; // see ConformanceTests

            TestStreams.Stream stream = TestStreams.Read(path);
            // frames that throw allocate their exception: measure up to the first one
            int valid = CountFramesUntilFirstError(stream);
            if (valid < 2) return;
            AssertNoAllocationsAfterFirstFrame(stream, valid);
        }

        private static int CountFramesUntilFirstError(TestStreams.Stream stream)
        {
            var decoder = TestStreams.CreateDecoder(stream.DecoderSpecificInfo);
            var buffer = new SampleBuffer();
            for (int i = 0; i < stream.Frames.Count; i++)
            {
                try { decoder.DecodeFrame(stream.Frames[i], buffer); }
                catch (AACException) { return i; }
            }
            return stream.Frames.Count;
        }

        [Fact]
        public void ChangingTheChannelsWithPCEsAllocatesNothing()
        {
            // frames of only a PCE, alternating between 2 and 93 channels (the most a PCE can describe): the output
            // changes size in every frame, the buffers of both sizes are reused
            var stream = TestStreams.Read(TestStreams.SamplePath);
            byte[] two = PceFrame(1, 0, 0, 0), many = PceFrame(15, 15, 15, 3);
            var decoder = new Decoder(stream.DecoderSpecificInfo);
            var buffer = new SampleBuffer();
            for (int i = 0; i < 6; i++) decoder.DecodeFrame(i % 2 == 0 ? two : many, buffer);
            Assert.Equal(93, buffer.Channels);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++) decoder.DecodeFrame(i % 2 == 0 ? two : many, buffer);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(93, buffer.Channels);
            Assert.Equal(0, allocated);
        }

        // a raw data block of a PCE with front, side and back CPEs and LFEs, then END
        private static byte[] PceFrame(int front, int side, int back, int lfe)
        {
            var bits = new List<int>();
            void Put(int value, int n) { for (int i = n - 1; i >= 0; i--) bits.Add((value >> i) & 1); }
            Put(5, 3); // ID_PCE
            Put(0, 4); Put(1, 2); Put(3, 4); // tag, object type (LC), sample rate index (48 kHz)
            Put(front, 4); Put(side, 4); Put(back, 4); Put(lfe, 2); Put(0, 3); Put(0, 4);
            Put(0, 1); Put(0, 1); Put(0, 1); // no mixdowns
            for (int i = 0; i < front + side + back; i++) { Put(1, 1); Put(i % 16, 4); } // CPEs
            for (int i = 0; i < lfe; i++) Put(i, 4);
            while (bits.Count % 8 != 0) bits.Add(0); // byte_align
            Put(0, 8); // no comment
            Put(7, 3); // ID_END
            while (bits.Count % 8 != 0) bits.Add(0);
            var frame = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) frame[i / 8] |= (byte)(bits[i] << (7 - i % 8));
            return frame;
        }

        private static void AssertNoAllocationsAfterFirstFrame(TestStreams.Stream stream, int frameCount)

        {
            List<byte[]> frames = stream.Frames;

            // warm up: static tables and JIT compilation are done once per process, not per frame; twice, so that the
            // tiered JIT has compiled the hot methods again before the measurement
            for (int pass = 0; pass < 2; pass++)
            {
                var warmup = TestStreams.CreateDecoder(stream.DecoderSpecificInfo);
                var warmupBuffer = new SampleBuffer();
                for (int i = 0; i < frameCount; i++) warmup.DecodeFrame(frames[i], warmupBuffer);
            }

            var decoder = TestStreams.CreateDecoder(stream.DecoderSpecificInfo);
            var buffer = new SampleBuffer();
            buffer.SetBigEndian(false);
            decoder.DecodeFrame(frames[0], buffer);

            long[] perFrame = new long[frameCount];
            long total = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i < frameCount; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                decoder.DecodeFrame(frames[i], buffer);
                perFrame[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            total = GC.GetAllocatedBytesForCurrentThread() - total;

            if (total != 0)
            {
                int first = Array.FindIndex(perFrame, a => a != 0);
                Assert.Fail($"{total} bytes allocated in frames 1..{frameCount - 1}, the first {perFrame[first]} bytes in frame {first}");
            }
        }
    }
}
#endif

#if NET
namespace SharpJaad.Tests
{
    /// <summary>
    /// The allocation tests run alone: allocations of the runtime on the test thread (the tiered JIT compiling while
    /// other tests run) would otherwise count as allocations of the decoder.
    /// </summary>
    [CollectionDefinition(nameof(AllocationTests), DisableParallelization = true)]
    public class AllocationTestsCollection
    {
    }
}
#endif
