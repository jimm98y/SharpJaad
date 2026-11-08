using SharpJaad.AAC;
using SharpJaad.ADTS;
using SharpJaad.MP4;
using SharpJaad.MP4.API;
using System;
using System.Collections.Generic;
using System.IO;

namespace SharpJaad.Tests
{
    /// <summary>
    /// Loads AAC streams and decodes them the same way for every test.
    /// </summary>
    internal static class TestStreams
    {
        public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "TestData", "aac-sample.mp4");

        public sealed class Stream
        {
            public byte[] DecoderSpecificInfo;
            public List<byte[]> Frames = new List<byte[]>();
        }

        public static Stream Read(string path)
        {
            var stream = new Stream();
            using (var fs = File.OpenRead(path))
            {
                if (path.EndsWith(".aac", StringComparison.OrdinalIgnoreCase))
                {
                    var adts = new ADTSDemultiplexer(fs);
                    stream.DecoderSpecificInfo = adts.GetDecoderSpecificInfo();
                    while (true)
                    {
                        try { stream.Frames.Add(adts.ReadNextFrame()); }
                        catch (EndOfStreamException) { break; }
                    }
                }
                else
                {
                    var track = (AudioTrack)new MP4Container(fs).GetMovie().GetTracks(AudioTrack.AudioCodec.AAC)[0];
                    stream.DecoderSpecificInfo = track.GetDecoderSpecificInfo();
                    while (track.HasMoreFrames()) stream.Frames.Add(track.ReadNextFrame().GetData());
                }
            }
            return stream;
        }

        public static Decoder CreateDecoder(byte[] dsi)
        {
            return new Decoder(dsi);
        }

        public const ulong FnvOffset = 1469598103934665603UL;

        public static ulong Fnv(ulong hash, byte[] data)
        {
            foreach (byte b in data)
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }
            return hash;
        }

        /// <summary>
        /// Decodes all frames into little-endian PCM and summarizes the result in the format of TestData/conformance.txt.
        /// </summary>
        public static string Summarize(string path)
        {
            Stream stream;
            Decoder decoder;
            try
            {
                stream = Read(path);
                decoder = CreateDecoder(stream.DecoderSpecificInfo);
            }
            catch (Exception e)
            {
                return $"SETUP-ERR {e.GetType().Name}";
            }

            var buffer = new SampleBuffer();
            buffer.SetBigEndian(false);
            ulong hash = FnvOffset;
            long bytes = 0;
            int errors = 0;
            foreach (byte[] frame in stream.Frames)
            {
                try { decoder.DecodeFrame(frame, buffer); }
                catch (Exception) { errors++; }
                hash = Fnv(hash, buffer.Data);
                bytes += buffer.Data.Length;
            }
            return $"hash={hash:X16} pcmBytes={bytes} sr={buffer.SampleRate} ch={buffer.Channels} errors={errors}";
        }
    }
}
