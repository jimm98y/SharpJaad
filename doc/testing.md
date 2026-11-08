# Testing

```
dotnet test src/SharpJaad.sln
```

runs `src/SharpJaad.Tests` on net481, net8.0 and net10.0:

- **DecoderTests** decode `src/SharpJaadMain/aac-sample.mp4` and compare the PCM with what SharpJaad 0.1.1 produced,
  and check what does not show in a hash: big endian output, independent decoders, truncated frames, frames of
  changing size.
- **BitStreamTests** check the bit reader, also when it is given shorter data after longer data.
- **AllocationTests** (.NET only, .NET Framework cannot count allocations) check that decoding allocates nothing once
  the first frame of a stream is decoded: the first frame allocates the elements of the stream and the output buffer,
  every later frame reuses them.
- **ConformanceTests** decode the AAC conformance streams and compare the PCM of each with what SharpJaad 0.1.1
  produced, listed in `src/SharpJaad.Tests/TestData/conformance.txt`. The AllocationTests run on them too.

The listed output is what 0.1.1 did, errors included: a stream that failed then must fail the same way now, unless a
change means to fix it. A change that does fix one updates its line in `conformance.txt` and says so.

## The conformance streams

They are not part of the repository. Download them from the FFmpeg test suite into a folder of their own and point
`SHARPJAAD_CONFORMANCE_DIR` at it; without it, the conformance tests pass without decoding anything and say so in
their output.

```
mkdir conformance && cd conformance
for f in Fd_2_c1_Ms_0x01.mp4 Fd_2_c1_Ms_0x04.mp4 aac-sce-in-stereo.mp4 al04_44.mp4 al04sf_48.mp4 al05_44.mp4 \
         al06_44.mp4 al07_96.mp4 al15_44.mp4 al17_44.mp4 al18_44.mp4 al22_chCfg0PCE_44.mp4 al_sbr_cm_48_2.mp4 \
         al_sbr_cm_48_5.1.mp4 al_sbr_ps_04_new.mp4 al_sbr_ps_06_new.mp4 al_sbr_sr_48_2_fsaac48.mp4 am00_88.mp4 \
         am05_44.mp4 ap05_48.mp4 er_ad6000np_44_ep0.mp4 ct_faac-adts.aac; do
  curl -sO https://fate-suite.ffmpeg.org/aac/$f
done
export SHARPJAAD_CONFORMANCE_DIR=$PWD
```

## Running the tests against another version

The test project builds the libraries from `$(SharpJaadSrc)`, the `src` folder it is in by default. To see what an
older version does with the same tests, check it out and run them there:

```
git worktree add ../SharpJaad-0.1.1 3b2014c
cp -r src/SharpJaad.Tests ../SharpJaad-0.1.1/src/
dotnet test ../SharpJaad-0.1.1/src/SharpJaad.Tests -f net10.0
```

On 0.1.1 only the AllocationTests fail: it allocates from a few hundred bytes to about 20 KB in every frame.
