# AudioCodecs

AudioCodecs 是独立的 C# 音频解码源码项目，提供 MP3、AAC、Ogg/Vorbis 和 Opus 的解码及 PCM32 适配代码。项目不加载第三方解码 DLL；源码可直接合并到使用方的 AOT 项目中。

## MP3

`Decode/Pcm/Mp3/Minimp3` 主要逐步翻译自 [lieff/minimp3](https://github.com/lieff/minimp3)，部分 C# 结构和适配参考 [TxN/minimal-internet-radio](https://github.com/TxN/minimal-internet-radio)。上游 minimp3 使用 CC0 1.0 公共领域奉献；参考项目的原始许可证和版权声明仍按其源码要求处理。目录中保留来源说明；项目自有 PCM32 适配代码按 `LICENSE.PCM32-MIT.txt` 发布。

当前 Minimp3 代码包含：

1. MPEG-1、MPEG-2、MPEG-2.5 Layer I/II/III 解码。
2. C# 状态机、帧扫描、bit reservoir、Huffman、反量化、IMDCT 和 synthesis filterbank。
3. SSE/AVX2 计算路径和标量诊断路径。
4. PCM32 输出适配、seek、VBR 信息和流式读取接口；Float32 接口尚未在实际播放环境验证。
5. 复用 decoder workspace，减少播放热路径分配。

目前只有 PCM32 输出经过实际环境验证，Float32 输出不能视为已验证功能。该实现不保证覆盖所有 MP3 编码器、损坏帧和非标准变体。源码公开用于共享和履行上游许可要求，不保证无 Bug。

## 其他组件

- SharpJaad AAC：MIT，许可证位于 `Decode/Pcm/Aac/SharpJaadAac/LICENSE`。
- Ogg/Vorbis：项目内容器和解码适配代码；格式依据 RFC 3533、RFC 5334 和 Vorbis I specification。
- Opus/Concentus：BSD-3-Clause 风格许可，完整条件见 `Decode/Pcm/Ogg/Ogg/OpusPort/LICENSE.libopus.txt`。
- PCM32 适配层：项目自有 MIT 代码，见 `LICENSE.PCM32-MIT.txt`。

SharpJaad AAC 保留 MIT 版权和许可文本，提供 ADTS/MP4 解复用及 PCM32 适配。Ogg/Vorbis 代码处理 page、packet、流状态和 PCM 输出，格式依据 RFC 3533、RFC 5334 与 Vorbis I specification。Opus/Concentus 代码采用 BSD-3-Clause 风格许可，包含 Ogg Opus packet、pre-skip、seek 预卷和 PCM32 适配；完整条款见 `Decode/Pcm/Ogg/Ogg/OpusPort/LICENSE.libopus.txt`。

完整来源、修改范围和发布义务见 [LICENSES.zh-CN.md](LICENSES.zh-CN.md) 与 [THIRD-PARTY-LICENSES.xml](THIRD-PARTY-LICENSES.xml)。
