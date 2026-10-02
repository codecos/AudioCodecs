# APE decoder

This directory contains the C# APE decoder translated from the official Monkey's Audio source and format materials.

- Official license information: https://www.monkeysaudio.com/license.html
- Official developer SDK: https://www.monkeysaudio.com/developers.html
- The implementation keeps the official frame layout, range/Rice bitstream handling, predictor order, CRC checks, and seek metadata model.
- The reader exposes the project's PCM32 output contract and uses a bounded streaming workspace.

The exact license obligations follow the official Monkey's Audio license and the source files distributed with the decoder. This code is not a third-party decoder DLL. Validate APE versions, compression levels, channel layouts, and damaged frames against the media collection used for deployment.
