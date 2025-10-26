# SharpJaad
JAAD is an AAC decoder and MP4 demultiplexer library written completely in Java. It uses no native libraries, is platform-independent and portable. It can read MP4 container from almost every input-stream (files, network sockets etc.) and decode AAC-LC (Low Complexity) and HE-AAC (High Efficiency/AAC+).

SharpJaad is a netstandard2.0 port of the original Java code base with a few small fixes. It has no third-party dependencies and it's portable as it does not use any platform-specific APIs.

[![NuGet version](https://img.shields.io/nuget/v/SharpJaad.svg?style=flat-square)](https://www.nuget.org/packages/SharpJaad)

## SharpJaad.AAC
SharpJaad.AAC is just the AAC decoder without any MP4 code. It's well suited for applications in RTP where the MP4 container is not needed.

[![NuGet version](https://img.shields.io/nuget/v/SharpJaad.AAC.svg?style=flat-square)](https://www.nuget.org/packages/SharpJaad.AAC)

### Example
To decode stereo AAC Low Complexity (AAC-LC) with 44.1kHz, start with creating the configuration:
```cs
var decoderConfig = new DecoderConfig();
decoderConfig.SetProfile(Profile.AAC_LC);
decoderConfig.SetSampleFrequency(SampleFrequency.SAMPLE_FREQUENCY_44100);
decoderConfig.SetChannelConfiguration((ChannelConfiguration)2);
```

Create the decoder:
```cs
var aacDecoder = new Decoder(decoderConfig);
```

Create an output buffer for the result:
```cs
SampleBuffer buffer = new SampleBuffer();
buffer.SetBigEndian(false);
```

Decode the AAC frame:
```cs
byte[] aacFrame = { ... } // AAC frame to decode
aacDecoder.DecodeFrame(aacFrame, buffer);
```

Convert it to PCM:
```cs
short[] pcm = new short[buffer.Data.Length / sizeof(short)];
Buffer.BlockCopy(buffer.Data, 0, pcm, 0, buffer.Data.Length);
```

## SharpJaadMain
A sample application that can extract AAC audio from a (MP4) file and transcode it into a WAV file.

## Credits
This is a port of the https://sourceforge.net/projects/jaadec/ into C#. 