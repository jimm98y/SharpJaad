using SharpJaad.AAC;
using System;
using Xunit;

namespace SharpJaad.Tests
{
    public class DecoderTests
    {
        // the PCM of aac-sample.mp4 as decoded by this version: 0.1.1 decoded it to 0x7BE064D41BC5D10F, without TNS, which
        // 22 of its frames use; the output of streams with references is checked against FFmpeg by ReferenceTests
        private const ulong SampleHash = 0x74C42A95F48A1164UL;
        private const long SamplePcmBytes = 3084288;

        [Fact]
        public void SampleDecodesToTheRecordedPcm()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            var decoder = new Decoder(stream.DecoderSpecificInfo);
            var buffer = new SampleBuffer();
            buffer.SetBigEndian(false);

            ulong hash = TestStreams.FnvOffset;
            long bytes = 0;
            foreach (byte[] frame in stream.Frames)
            {
                decoder.DecodeFrame(frame, buffer);
                hash = TestStreams.Fnv(hash, buffer.Data);
                bytes += buffer.Data.Length;
            }

            Assert.Equal(753, stream.Frames.Count);
            Assert.Equal(SamplePcmBytes, bytes);
            Assert.Equal(SampleHash.ToString("X16"), hash.ToString("X16"));
            Assert.Equal(48000, buffer.SampleRate);
            Assert.Equal(2, buffer.Channels);
            Assert.Equal(16, buffer.BitsPerSample);
        }

        [Fact]
        public void BigEndianOutputIsTheByteSwappedLittleEndianOutput()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            var little = new Decoder(stream.DecoderSpecificInfo);
            var big = new Decoder(stream.DecoderSpecificInfo);
            var littleBuffer = new SampleBuffer();
            littleBuffer.SetBigEndian(false);
            var bigBuffer = new SampleBuffer(); // big endian is the default

            foreach (byte[] frame in stream.Frames)
            {
                little.DecodeFrame(frame, littleBuffer);
                big.DecodeFrame(frame, bigBuffer);

                Assert.Equal(littleBuffer.Data.Length, bigBuffer.Data.Length);
                for (int i = 0; i < littleBuffer.Data.Length; i += 2)
                {
                    Assert.Equal(littleBuffer.Data[i], bigBuffer.Data[i + 1]);
                    Assert.Equal(littleBuffer.Data[i + 1], bigBuffer.Data[i]);
                }
            }
        }

        [Fact]
        public void DecodersOfTheSameStreamAreIndependent()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            var first = new Decoder(stream.DecoderSpecificInfo);
            var second = new Decoder(stream.DecoderSpecificInfo);
            var firstBuffer = new SampleBuffer();
            var secondBuffer = new SampleBuffer();

            // the second decoder starts later: it must not see any state of the first one
            for (int i = 0; i < 100; i++) first.DecodeFrame(stream.Frames[i], firstBuffer);
            var reference = new Decoder(stream.DecoderSpecificInfo);
            var referenceBuffer = new SampleBuffer();
            for (int i = 0; i < 200; i++)
            {
                first.DecodeFrame(stream.Frames[100 + i], firstBuffer);
                second.DecodeFrame(stream.Frames[i], secondBuffer);
                reference.DecodeFrame(stream.Frames[i], referenceBuffer);
                Assert.Equal(referenceBuffer.Data, secondBuffer.Data);
            }
        }

        [Fact]
        public void TruncatedFrameYieldsNoSamplesAndDecodingContinues()
        {
            var stream = TestStreams.Read(TestStreams.SamplePath);
            var decoder = new Decoder(stream.DecoderSpecificInfo);
            var buffer = new SampleBuffer();
            for (int i = 0; i < 10; i++) decoder.DecodeFrame(stream.Frames[i], buffer);

            byte[] truncated = new byte[20];
            Array.Copy(stream.Frames[10], truncated, truncated.Length);
            decoder.DecodeFrame(truncated, buffer); // the end of the frame is reached early: no exception

            Assert.Empty(buffer.Data);
            Assert.Equal(0, buffer.SampleRate);

            decoder.DecodeFrame(stream.Frames[11], buffer);
            Assert.Equal(4096, buffer.Data.Length);
            Assert.Equal(48000, buffer.SampleRate);
        }

        [Fact]
        public void FrameTooShortForTheHeaderCheckYieldsNoSamples()
        {
            // the end of a frame of less than 4 bytes is reached while checking for an ADIF header: like any truncated
            // frame it yields no samples (up to 0.2.0 the buffer kept the previous frame) and decoding continues
            var stream = TestStreams.Read(TestStreams.SamplePath);
            var decoder = new Decoder(stream.DecoderSpecificInfo);
            var buffer = new SampleBuffer();
            decoder.DecodeFrame(stream.Frames[0], buffer);

            decoder.DecodeFrame(new byte[] { stream.Frames[1][0], stream.Frames[1][1], stream.Frames[1][2] }, buffer);
            Assert.Empty(buffer.Data);

            decoder.DecodeFrame(stream.Frames[2], buffer);
            Assert.Equal(4096, buffer.Data.Length);
        }

        [Fact]
        public void FramesOfAnySizeDecodeLikeInAFreshDecoder()
        {
            // frames are decoded from small to large and back: the reused bit stream buffer must never leak
            // bytes of an earlier, longer frame into a shorter one
            var stream = TestStreams.Read(TestStreams.SamplePath);
            var decoder = new Decoder(stream.DecoderSpecificInfo);
            var buffer = new SampleBuffer();
            var reference = new Decoder(stream.DecoderSpecificInfo);
            var referenceBuffer = new SampleBuffer();

            int largest = 0;
            for (int i = 1; i < stream.Frames.Count; i++)
            {
                if (stream.Frames[i].Length > stream.Frames[largest].Length) largest = i;
            }

            // the largest frame first, then the stream from the start: both decoders see the same frames after that
            decoder.DecodeFrame(stream.Frames[largest], buffer);
            reference.DecodeFrame(stream.Frames[largest], referenceBuffer);
            for (int i = 0; i < stream.Frames.Count; i++)
            {
                decoder.DecodeFrame(stream.Frames[i], buffer);
                reference.DecodeFrame((byte[])stream.Frames[i].Clone(), referenceBuffer);
                Assert.Equal(referenceBuffer.Data, buffer.Data);
            }
        }
    }
}
