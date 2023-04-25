using SharpJaad.AAC;
using SharpJaad.ADTS;
using SharpJaad.MP4;
using SharpJaad.MP4.API;
using SharpJaad.WAV;
using System;
using System.Collections.Generic;
using System.IO;

namespace SharpJaad
{
    /**
    * Command line example, that can decode an AAC file to a WAVE file.
    * @author in-somnia
    */
    public class Program
    {
        private const string USAGE = "usage:\r\nSharpJaadMain [-mp4] <infile> <outfile>\r\n\r\n\t-mp4\tinput file is in MP4 container format";

        public static void Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    PrintUsage();
                }
                else if (args[0].Equals("-mp4"))
                {
                    if (args.Length < 3) PrintUsage();
                    else DecodeMP4(args[1], args[2]);
                }
                else
                {
                    DecodeAAC(args[0], args[1]);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("error while decoding: " + e.ToString());
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine(USAGE);
        }

        private static void DecodeMP4(string input, string output)
        {
            WaveFileWriter wav = null;
            try 
            {
                using (var fileStream = File.OpenRead(input))
                {
                    MP4Container cont = new MP4Container(fileStream);
                    Movie movie = cont.GetMovie();
                    List<Track> tracks = movie.GetTracks(AudioTrack.AudioCodec.AAC);
                    if (tracks.Count == 0) throw new Exception("movie does not contain any AAC track");
                    AudioTrack track = (AudioTrack)tracks[0];

                    wav = new WaveFileWriter(File.OpenWrite(output), track.GetSampleRate(), track.GetChannelCount(), track.GetSampleSize());

                    Decoder dec = new Decoder(track.GetDecoderSpecificInfo());
                    Frame frame;
                    SampleBuffer buf = new SampleBuffer();

                    while (track.HasMoreFrames())
                    {
                        frame = track.ReadNextFrame();
                        dec.DecodeFrame(frame.GetData(), buf);
                        wav.Write(buf.Data);
                    }
                }
            }
            finally 
            {
                if (wav != null)
                    wav.Close();
            }
        }

        private static void DecodeAAC(string input, string output)
        {
            WaveFileWriter wav = null;
		    try 
            {
                using (var fileStream = File.OpenRead(input))
                {
                    ADTSDemultiplexer adts = new ADTSDemultiplexer(fileStream);
                    Decoder dec = new Decoder(adts.GetDecoderSpecificInfo());
                    SampleBuffer buf = new SampleBuffer();
                    byte[] b;

                    while (true)
                    {
                        b = adts.ReadNextFrame();
                        dec.DecodeFrame(b, buf);

                        if (wav == null)
                            wav = new WaveFileWriter(File.OpenWrite(output), buf.SampleRate, buf.Channels, buf.BitsPerSample);

                        wav.Write(buf.Data);
                    }
                }
            }
		    finally 
            {
                if (wav != null) 
                    wav.Close();
            }
        }
    }
}