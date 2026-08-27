# WebGL Opus plugin

`build.sh` builds the WebGL static library from Xiph's Opus repository at commit
`788cc89ce4f2c42025d8c70ec1b4457dc89cd50f`. This is the source revision identified by the
existing native plugin's `libopus 1.6.1-11-g788cc89c` version string.

The build requires Unity Editor 6000.5.2f1 and verifies its bundled Emscripten version is
`4.0.20-git`. Pass source and build directories outside the package so generated files do not
enter Unity's asset database:

```sh
./build.sh /path/to/opus-source /path/to/opus-build /Applications/Unity/Hub/Editor/6000.5.2f1
```

The script fetches only `https://github.com/xiph/opus.git`, checks out the exact commit, and writes
`Plugins/webgl/libopus.a`. The local wrapper converts OpusSharp's fixed-signature CTL entry points
to libopus's variadic CTL API.

DRED is disabled because this source revision does not store its generated model headers in the
repository. The DRED symbols remain available through libopus and return `OPUS_UNIMPLEMENTED`;
encoder, decoder, packet, repacketizer, multistream, and CTL functions are fully implemented.
