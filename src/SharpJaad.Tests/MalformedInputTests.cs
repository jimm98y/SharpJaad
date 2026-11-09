using SharpJaad.AAC;
using SharpJaad.ADTS;
using SharpJaad.MP4;
using SharpJaad.MP4.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace SharpJaad.Tests
{
    /// <summary>
    /// Malformed and malicious input: the parsers and the decoder must fail with their documented exceptions
    /// (IOException: MP4Exception, AACException, EndOfStreamException), in bounded time, without allocating more than
    /// the input holds and without overflowing the stack.
    /// </summary>
    public class MalformedInputTests
    {
        private const int TimeLimitMs = 10000;

        // runs the action on its own thread: a hang fails the test instead of blocking it
        private static Exception RunBounded(Action action, out long allocated)
        {
            Exception error = null;
            long bytes = 0;
            var thread = new Thread(() =>
            {
#if NET
                long before = GC.GetAllocatedBytesForCurrentThread();
#endif
                try { action(); }
                catch (Exception e) { error = e; }
#if NET
                bytes = GC.GetAllocatedBytesForCurrentThread() - before;
#endif
            }, 16 << 20);
            thread.IsBackground = true;
            thread.Start();
            Assert.True(thread.Join(TimeLimitMs), "the parsing did not finish in time");
            allocated = bytes;
            return error;
        }

        private static void AssertFailsCleanly(Action action, long maxAllocated = 64 << 20)
        {
            Exception error = RunBounded(action, out long allocated);
            if (error != null) Assert.True(error is IOException, $"unexpected {error.GetType().Name}: {error.Message}");
            Assert.True(allocated <= maxAllocated, $"{allocated} bytes allocated");
        }

        private static void ReadAll(Stream input)
        {
            var movie = new MP4Container(input).GetMovie();
            if (movie == null) return;
            foreach (var track in movie.GetTracks())
            {
                for (int i = 0; i < 1000 && track.HasMoreFrames(); i++) track.ReadNextFrame();
            }
        }

        // MP4 boxes
        private static byte[] Box(string type, params byte[][] content)
        {
            byte[] payload = content.SelectMany(c => c).ToArray();
            int size = 8 + payload.Length;
            return new byte[] { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size }
                .Concat(type.Select(c => (byte)c)).Concat(payload).ToArray();
        }

        private static byte[] U32(long v) => new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        private static readonly byte[] Ftyp = Box("ftyp", "M4A ".Select(c => (byte)c).ToArray(), U32(0));

        private static MemoryStream File(params byte[][] boxes) => new MemoryStream(Ftyp.Concat(boxes.SelectMany(b => b)).ToArray());

        [Fact]
        public void TruncatedFilesFailWithoutHanging()
        {
            byte[] sample = System.IO.File.ReadAllBytes(TestStreams.SamplePath);
            for (int length = 0; length < sample.Length; length += Math.Max(1, sample.Length / 97))
            {
                byte[] truncated = sample.Take(length).ToArray();
                AssertFailsCleanly(() => ReadAll(new MemoryStream(truncated)));
            }
        }

        [Fact]
        public void TruncatedFilesFailWithoutHangingAlsoWhenTheStreamCannotSeek()
        {
            byte[] sample = System.IO.File.ReadAllBytes(TestStreams.SamplePath);
            for (int length = 0; length < sample.Length; length += Math.Max(1, sample.Length / 31))
            {
                byte[] truncated = sample.Take(length).ToArray();
                AssertFailsCleanly(() => ReadAll(new ForwardOnlyStream(truncated)));
            }
        }

        [Fact]
        public void DeeplyNestedBoxesAreRejected()
        {
            // 20000 nested container boxes: recursive parsing would overflow the stack, which ends the process
            byte[] nested = new byte[0];
            for (int i = 0; i < 20000; i++) nested = Box("udta", nested);
            Exception error = RunBounded(() => ReadAll(File(nested)), out _);
            Assert.IsType<MP4Exception>(error);
        }

        [Fact]
        public void ASampleCountWithoutSamplesIsRejected()
        {
            // a 20 byte stsz with one size for 2^31-1 samples: 16 GB of sizes from nothing
            byte[] stsz = Box("stsz", U32(0), U32(1), U32(0x7FFFFFFF));
            AssertFailsCleanly(() => ReadAll(File(Box("moov", Box("trak", Box("mdia", Box("minf", Box("stbl", stsz))))))), 1 << 20);
        }

        [Fact]
        public void AnEntryCountLargerThanTheBoxIsRejected()
        {
            byte[] stco = Box("stco", U32(0), U32(0x10000000), U32(0));
            Exception error = RunBounded(() => ReadAll(File(Box("moov", Box("trak", Box("mdia", Box("minf", Box("stbl", stco))))))), out long allocated);
            Assert.IsType<MP4Exception>(error);
            Assert.True(allocated < 1 << 20, $"{allocated} bytes allocated");
        }

        [Fact]
        public void ABoxLargerThanItsParentIsRejected()
        {
            byte[] inner = Box("free", new byte[16]);
            inner[3] = 200; // claims 200 bytes in a parent of 32
            Exception error = RunBounded(() => ReadAll(File(Box("moov", inner))), out _);
            Assert.IsType<MP4Exception>(error);
        }

        [Fact]
        public void DeeplyNestedDescriptorsAreRejected()
        {
            // ES descriptors (tag 3) nested 10000 deep in an esds box
            var descriptor = new List<byte>();
            for (int i = 0; i < 10000; i++) descriptor.AddRange(new byte[] { 3, 0x80, 0x80, 0x80, 0x7F, 0, 0, 0 });
            byte[] esds = Box("esds", U32(0), descriptor.ToArray());
            AssertFailsCleanly(() => ReadAll(File(Box("moov", esds))));
        }

        [Fact]
        public void AHugeDescriptorLengthIsRejected()
        {
            // a decoder specific info descriptor that claims 256 MB (the largest 4 byte length) in a tiny box
            byte[] esds = Box("esds", U32(0), new byte[] { 5, 0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3 });
            AssertFailsCleanly(() => ReadAll(File(Box("moov", esds))), 1 << 20);
        }

        [Fact]
        public void MutatedFilesFailCleanly()
        {
            byte[] sample = System.IO.File.ReadAllBytes(TestStreams.SamplePath);
            var random = new Random(1);
            for (int n = 0; n < 200; n++)
            {
                byte[] mutated = (byte[])sample.Clone();
                int region = n % 2 == 0 ? 4096 : mutated.Length;
                for (int i = 0, count = 1 + random.Next(6); i < count; i++) mutated[random.Next(region)] = (byte)random.Next(256);
                AssertFailsCleanly(() => ReadAll(new MemoryStream(mutated)));
            }
        }

        // ADTS
        private static byte[] Adts(TestStreams.Stream stream, int frames)
        {
            // profile, sample rate index and channel configuration of the AudioSpecificConfig
            int profile = (stream.DecoderSpecificInfo[0] >> 3) - 1;
            int sfi = ((stream.DecoderSpecificInfo[0] & 7) << 1) | (stream.DecoderSpecificInfo[1] >> 7);
            int channels = (stream.DecoderSpecificInfo[1] >> 3) & 15;
            var output = new MemoryStream();
            foreach (byte[] frame in stream.Frames.Take(frames))
            {
                int length = frame.Length + 7;
                output.Write(new byte[] { 0xFF, 0xF1, (byte)((profile << 6) | (sfi << 2) | (channels >> 2)),
                    (byte)(((channels & 3) << 6) | (length >> 11)), (byte)(length >> 3), (byte)(((length & 7) << 5) | 0x1F), 0xFC }, 0, 7);
                output.Write(frame, 0, frame.Length);
            }
            return output.ToArray();
        }

        private static List<byte[]> ReadAdts(Stream input)
        {
            var adts = new ADTSDemultiplexer(input);
            var frames = new List<byte[]>();
            while (true)
            {
                try { frames.Add(adts.ReadNextFrame()); }
                catch (EndOfStreamException) { break; }
            }
            return frames;
        }

        [Fact]
        public void AdtsIsReadFromAStreamThatCannotSeek()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            byte[] adts = Adts(stream, 50);
            List<byte[]> frames = ReadAdts(new ForwardOnlyStream(adts));
            Assert.Equal(50, frames.Count);
            for (int i = 0; i < 50; i++) Assert.Equal(stream.Frames[i], frames[i]);
        }

        [Fact]
        public void TruncatedAdtsEndsCleanly()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            byte[] adts = Adts(stream, 20);
            for (int length = 1; length < adts.Length; length += 97)
            {
                byte[] truncated = adts.Take(length).ToArray();
                AssertFailsCleanly(() => ReadAdts(new MemoryStream(truncated)));
            }
        }

        [Fact]
        public void AnAdtsFrameShorterThanItsHeaderIsRejected()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            byte[] adts = Adts(stream, 2);
            adts[3] &= 0xFC; adts[4] = 0; adts[5] &= 0x1F; // frame length 0
            Exception error = RunBounded(() => ReadAdts(new MemoryStream(adts)), out _);
            Assert.True(error is IOException, error?.GetType().Name ?? "no exception");
        }

        // decoder
        [Fact]
        public void RandomFramesOnlyFailWithAACException()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            var random = new Random(2);
            Exception error = RunBounded(() =>
            {
                var decoder = new Decoder(stream.DecoderSpecificInfo);
                var buffer = new SampleBuffer();
                for (int i = 0; i < 2000; i++)
                {
                    byte[] frame = (byte[])stream.Frames[i % stream.Frames.Count].Clone();
                    if (i % 3 == 0) random.NextBytes(frame);
                    else for (int k = 0, n = 1 + random.Next(8); k < n; k++) frame[random.Next(frame.Length)] ^= (byte)(1 << random.Next(8));
                    try { decoder.DecodeFrame(frame, buffer); }
                    catch (AACException) { }
                }
            }, out _);
            Assert.Null(error);
        }

        [Fact]
        public void RandomDecoderSpecificInfoOnlyFailsWithAACException()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            var random = new Random(3);
            Exception error = RunBounded(() =>
            {
                for (int i = 0; i < 2000; i++)
                {
                    byte[] dsi = new byte[1 + random.Next(16)];
                    random.NextBytes(dsi);
                    Decoder decoder;
                    try { decoder = new Decoder(dsi); }
                    catch (AACException) { continue; }
                    var buffer = new SampleBuffer();
                    for (int f = 0; f < 3; f++)
                    {
                        try { decoder.DecodeFrame(stream.Frames[f], buffer); }
                        catch (AACException) { }
                    }
                }
            }, out _);
            Assert.Null(error);
        }

        /// <summary>
        /// A stream that can only be read forward, as a network stream.
        /// </summary>
        private sealed class ForwardOnlyStream : Stream
        {
            private readonly MemoryStream _data;
            public ForwardOnlyStream(byte[] data) { _data = new MemoryStream(data); }
            public override int Read(byte[] buffer, int offset, int count) => _data.Read(buffer, offset, count);
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
