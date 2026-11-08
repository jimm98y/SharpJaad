using SharpJaad.AAC;
using SharpJaad.AAC.Syntax;
using SharpJaad.ADTS;
using SharpJaad.MP4;
using SharpJaad.MP4.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

// decoding shares static state (the PNS noise generator of ICStream), so the tests must not run at the same time
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

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

        /// <summary>
        /// Creates the decoder for a stream. DecoderConfig cannot parse explicit SBR/PS signaling (object type 5 or 29)
        /// nor a program config element in the decoder specific info, so for those the core is configured directly
        /// and SBR/PS are decoded as implicitly signaled.
        /// </summary>
        public static Decoder CreateDecoder(byte[] dsi)
        {
            try
            {
                return new Decoder(dsi);
            }
            catch (AACException) when ((dsi[0] >> 3) == 5 || (dsi[0] >> 3) == 29 || ((dsi[1] >> 3) & 15) == 0)
            {
                return new Decoder(FallbackConfig(dsi));
            }
        }

        private static DecoderConfig FallbackConfig(byte[] d)
        {
            int bits = 0;
            int Read(int n)
            {
                int v = 0;
                for (int i = 0; i < n; i++, bits++) v = (v << 1) | ((d[bits >> 3] >> (7 - (bits & 7))) & 1);
                return v;
            }

            int aot = Read(5);
            int sfi = Read(4);
            if (sfi == 15) Read(24);
            int ch = Read(4);
            if (aot == 5 || aot == 29)
            {
                int esf = Read(4);
                if (esf == 15) Read(24);
                aot = Read(5);
            }
            if (ch == 0)
            {
                // GASpecificConfig, then program_config_element: count its channels
                Read(3); Read(4); Read(2); Read(4);
                int front = Read(4), side = Read(4), back = Read(4), lfe = Read(2);
                Read(3); Read(4);
                if (Read(1) == 1) Read(4);
                if (Read(1) == 1) Read(4);
                if (Read(1) == 1) Read(3);
                int n = lfe;
                for (int i = 0; i < front + side + back; i++) { n += 1 + Read(1); Read(4); }
                ch = n == 8 ? 7 : n;
            }

            var config = new DecoderConfig();
            config.SetProfile((Profile)aot);
            config.SetSampleFrequency((SampleFrequency)sfi);
            config.SetChannelConfiguration((ChannelConfiguration)ch);
            return config;
        }

        /// <summary>
        /// Puts the noise generator shared by all decoders back to its initial state, as in a new process.
        /// </summary>
        public static void ResetNoiseGenerator()
        {
            typeof(ICStream).GetField("randomState", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0x1F2E3D4C);
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
