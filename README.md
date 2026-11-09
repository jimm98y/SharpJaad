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

### Allocations
After the first frame, `DecodeFrame` allocates nothing for AAC-LC, AAC Main, AAC LTP, HE-AAC (SBR) and HE-AACv2 (PS) streams: the decoder reuses its buffers, and the `SampleBuffer` keeps its `Data` array as long as the frame size stays the same. Reuse one `SampleBuffer` for all frames to keep it that way, and copy `Data` if you need it after the next call. Only a frame that fails to decode allocates (its exception). For this, a mono HE-AAC decoder creates its parametric stereo state (about 130 KB) up front, as PS can start in any frame.

### Untrusted input
Files and streams from untrusted sources are safe to parse and decode: malformed or malicious input makes the MP4 and ADTS readers and the decoder throw an `IOException` (`MP4Exception`, `AACException` or `EndOfStreamException`), in bounded time and without allocating more than the input holds; nothing recurses without a depth limit. `DecodeFrame` throws only `AACException`. Each `Decoder` has its own state: don't share one `DecoderConfig` object between decoders, as decoding updates it.

## SharpJaadMain
A sample application that can extract AAC audio from a (MP4) file and transcode it into a WAV file.

## Credits
This is a port of the https://sourceforge.net/projects/jaadec/ into C#. 

## Development
- [doc/testing.md](doc/testing.md): the tests, the AAC conformance streams they decode, and how to run them.
- [doc/public-api.md](doc/public-api.md): how the public API is guarded against unintended changes.
