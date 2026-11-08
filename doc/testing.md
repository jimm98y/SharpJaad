# Testing

```
dotnet test src/SharpJaad.sln
```

runs `src/SharpJaad.Tests` on net481, net8.0 and net10.0:

- **DecoderTests** decode `src/SharpJaadMain/aac-sample.mp4` and compare the PCM with the recorded output, and check
  what does not show in a hash: big endian output, independent decoders, truncated frames, frames of changing size.
- **BitStreamTests** check the bit reader, also when it is given shorter data after longer data.
- **AllocationTests** (.NET only, .NET Framework cannot count allocations) check that decoding allocates nothing once
  the first frame of a stream is decoded: the first frame allocates the elements of the stream and the output buffer,
  every later frame reuses them.
- **ConformanceTests** decode the AAC conformance streams and compare the PCM of each with the output recorded in
  `src/SharpJaad.Tests/TestData/conformance.txt`, errors included: any change of the output shows, also for streams
  without a reference. A change that means to change the output updates the lines (the test writes the actual line to
  its output) and says why. They also check that the noise of PNS does not depend on other decoders.
- **ReferenceTests** compare the PCM of the conformance streams with the reference PCM of the FFmpeg test suite,
  channel by channel: `src/SharpJaad.Tests/TestData/reference.txt` lists the reference of each stream, how its
  channels map to SharpJaad's and the lowest signal to noise ratio each channel may have, a little below what the
  decoder reaches now.

The AllocationTests run on the conformance streams too.

## The conformance streams

They are not part of the repository. Download them and their reference PCM from the FFmpeg test suite into a folder of
their own and point `SHARPJAAD_CONFORMANCE_DIR` at it; without it, the conformance and reference tests pass without
decoding anything and say so in their output.

```
mkdir conformance && cd conformance
for f in Fd_2_c1_Ms_0x01.mp4 Fd_2_c1_Ms_0x04.mp4 aac-sce-in-stereo.mp4 al04_44.mp4 al04sf_48.mp4 al05_44.mp4 \
         al06_44.mp4 al07_96.mp4 al15_44.mp4 al17_44.mp4 al18_44.mp4 al22_chCfg0PCE_44.mp4 al_sbr_cm_48_2.mp4 \
         al_sbr_cm_48_5.1.mp4 al_sbr_ps_04_new.mp4 al_sbr_ps_06_new.mp4 al_sbr_sr_48_2_fsaac48.mp4 am00_88.mp4 \
         am05_44.mp4 ap05_48.mp4 er_ad6000np_44_ep0.mp4 ct_faac-adts.aac \
         al04_44.s16 al05_44.s16 al06_44.s16 al07_96_reorder.s16 al15_44_reorder.s16 al17_44.s16 al18_44.s16 \
         al_sbr_hq_cm_48_2.s16 al_sbr_hq_cm_48_5.1_reorder.s16 al_sbr_ps_04_ur.s16 al_sbr_ps_06_ur.s16 \
         al_sbr_hq_sr_48_2_fsaac48.s16 am00_88.s16 am05_44_reorder.s16 ap05_48.s16; do
  curl -sO https://fate-suite.ffmpeg.org/aac/$f
done
export SHARPJAAD_CONFORMANCE_DIR=$PWD
```

What does not decode: `Fd_*` (USAC), `er_ad6000np_44_ep0` (ER AAC LD) and `al04sf_48` (960 sample frames) are not
supported. Parametric stereo (`al_sbr_ps_*`) still deviates from the reference after PS starts.

## Running the tests against another version

The test project builds the libraries from `$(SharpJaadSrc)`, the `src` folder it is in by default. To see what an
older version does with the same tests, check it out and run them there:

```
git worktree add ../SharpJaad-0.1.1 3b2014c
cp -r src/SharpJaad.Tests ../SharpJaad-0.1.1/src/
dotnet test ../SharpJaad-0.1.1/src/SharpJaad.Tests -f net10.0
```

0.1.1 fails the AllocationTests (it allocates from a few hundred bytes to about 20 KB in every frame), and with the
conformance streams most of the conformance and reference tests: it could not set up streams with a program config
element or explicit SBR signaling in the decoder specific info, did not apply TNS, pulse data, Main profile prediction
or independent coupling, inverted the noise of PNS, and failed SBR after the first header change.
