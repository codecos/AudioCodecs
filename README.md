# AudioCodecs

AudioCodecs is a source-level C# audio decoder project for MP3, AAC, Ogg/Vorbis, and Opus. It does not load decoder DLLs and is intended to be usable from Native AOT applications.

## MP3

The code in [`Decode/Pcm/Mp3/Minimp3`](Decode/Pcm/Mp3/Minimp3) is a C# translation based primarily on [lieff/minimp3](https://github.com/lieff/minimp3), with some C# structure and adapter references from [TxN/minimal-internet-radio](https://github.com/TxN/minimal-internet-radio).

The upstream minimp3 source is dedicated under CC0 1.0. The project keeps the upstream source record in [`README.upstream.md`](Decode/Pcm/Mp3/Minimp3/README.upstream.md) and [`LICENSE.minimp3.txt`](Decode/Pcm/Mp3/Minimp3/LICENSE.minimp3.txt).

The MP3 implementation includes MPEG-1/2/2.5 Layer I/II/III decoding, frame scanning, bit reservoir, Huffman decoding, IMDCT, synthesis, seek, VBR metadata, SSE/AVX2 paths, and PCM32 output. PCM32 is the only output path validated in the actual playback environment. Float32 is present as an interface but has not been validated in the actual environment.

## Other codecs

- SharpJaad AAC: MIT; see [`Decode/Pcm/Aac/SharpJaadAac/LICENSE`](Decode/Pcm/Aac/SharpJaadAac/LICENSE).
- Ogg/Vorbis: Ogg page and packet parsing with project PCM adapters. The format references are RFC 3533, RFC 5334, and the Vorbis I specification.
- Opus/Concentus: BSD-3-Clause style terms and patent notices; see [`LICENSE.libopus.txt`](Decode/Pcm/Ogg/Ogg/OpusPort/LICENSE.libopus.txt).
- PCM32 adapters: project-owned MIT code; see [`LICENSE.PCM32-MIT.txt`](LICENSE.PCM32-MIT.txt).

The complete source, license, modification, and validation notes are in [`LICENSES.zh-CN.md`](LICENSES.zh-CN.md). The machine-readable component index is [`THIRD-PARTY-LICENSES.xml`](THIRD-PARTY-LICENSES.xml).

This source release does not promise bug-free behavior or complete coverage of every encoder, container, damaged stream, or hardware path. Validate it against the media collection and deployment environment that matter to you.
