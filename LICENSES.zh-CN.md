# AudioCodecs 许可证和来源说明

本文件记录源码来源、许可证、格式标准和本项目的修改范围。格式标准不是实现代码许可证；每个源码目录仍须保留其原始版权和许可文本。

## 组件总表

| 组件 | 目录 | 代码许可证 | 格式或协议 | 发布要求 |
|---|---|---|---|---|
| minimp3 C# 移植 | `Decode/Pcm/Mp3/Minimp3` | `lieff/minimp3`：CC0 1.0；项目自有适配按 MIT 文件发布 | MPEG Audio Layer I/II/III | 保留上游来源、CC0 说明和本项目修改记录 |
| PCM32 适配层 | `Decode/Pcm/Mp3`、`Decode/Pcm/Aac`、`Decode/Pcm/Ogg` 中的原创适配 | MIT，`LICENSE.PCM32-MIT.txt` | 项目内部 PCM32/Float32 契约 | 保留 MIT 文本 |
| SharpJaad AAC | `Decode/Pcm/Aac/SharpJaadAac` | MIT | ISO/IEC 14496-3 | 保留目录中的 MIT 文本 |
| APE | `Decode/Pcm/Ape` | 依据 Monkey's Audio 官方许可证和源码说明 | Monkey's Audio APE | 保留官方许可证、来源和 SDK 说明 |
| Ogg/Vorbis | `Decode/Pcm/Ogg/Ogg` | 当前适配代码按项目源码维护；外部复制代码另行标注 | RFC 3533、RFC 5334、Vorbis I | 不能把规范当作代码许可证 |
| Opus/Concentus | `Decode/Pcm/Ogg/Ogg/OpusPort` | BSD-3-Clause 风格，见 `LICENSE.libopus.txt` | RFC 6716、RFC 7845 | 保留版权、条件和免责声明 |

## Minimp3 来源与修改

主要来源：[lieff/minimp3](https://github.com/lieff/minimp3)，上游以 CC0 1.0 发布。部分 C# 结构和适配参考：[TxN/minimal-internet-radio](https://github.com/TxN/minimal-internet-radio)。参考项目的源码许可证、版权和免责声明按其对应文件执行。

`Minimp3` 目录是逐函数、逐数据结构对应的 C# 翻译，不依赖 DLL、P/Invoke 或外部运行库。本项目改动如下：

1. 将 decoder、frame info、bit reader、Huffman、reservoir、反量化、IMDCT 和 synthesis 状态翻译为 C#。
2. 保留 MPEG-1/2/2.5 和 Layer I/II/III 的比特流处理逻辑。
3. 增加 C# 的 SSE/AVX2 运算封装，并保留诊断用标量路径。
4. 增加 `mp3dec_ex` 风格的文件、回调、seek 和 VBR 元数据接口。
5. 增加 PCM32 输出适配和复用工作区。
6. 为 AOT 编译清理动态加载和第三方 DLL 依赖。

只有 PCM32 输出经过实际环境验证；Float32 接口尚未在实际环境使用或完成等价验证，不能作为已验证功能发布说明。

## SharpJaad AAC

来源：[SharpJaad](https://github.com/jimm98y/SharpJaad)，源码许可证为 MIT，原始文本位于 `Decode/Pcm/Aac/SharpJaadAac/LICENSE`。AAC 格式由 ISO/IEC 14496-3 定义，格式规范与实现代码许可证分开处理。

项目中的改进和适配包括：

1. 提供 ADTS 和 MP4/fMP4 解复用入口。
2. 连接 AAC 解码器与 PCM32 输出工作区。
3. 补充流状态重置、边界检查和错误传播。
4. 保留 SharpJaad 原始版权和 MIT 条款。

AAC 代码没有经过本项目所有编码器和封装变体的完整验证。

## APE / Monkey's Audio

APE C# 解码器位于 `Decode/Pcm/Ape`，根据 Monkey's Audio 官方源码和格式资料翻译。官方许可证说明：[monkeysaudio.com/license.html](https://www.monkeysaudio.com/license.html)；官方开发者 SDK：[monkeysaudio.com/developers.html](https://www.monkeysaudio.com/developers.html)。

实现保留官方 APE 帧布局、Range/Rice 位流、预测器、CRC、旧版格式分支和 seek 元数据模型，并接入项目的 PCM32 流式读取工作区。具体发布条件以官方许可证和随附源码说明为准。

## Ogg/Vorbis

Ogg 是容器格式，依据 RFC 3533 和 RFC 5334；Vorbis 依据 Xiph.Org Vorbis I specification。当前目录中的 Ogg/Vorbis 解析和 PCM 适配代码没有引入外部解码 DLL；若未来复制 libvorbis 或其他第三方实现，必须在对应文件旁补充来源和许可证。

当前适配包括：

1. Ogg page、segment 和 packet 读取。
2. Vorbis 头信息、注释和音频 packet 处理。
3. seek、流状态重置和 PCM32 输出适配。
4. 有界工作区和顺序读取路径。

协议 RFC 和格式规范不自动授予任何第三方源码的版权许可。

## Opus/Concentus

Opus 代码位于 `Decode/Pcm/Ogg/Ogg/OpusPort`，许可证和免责声明见 `LICENSE.libopus.txt`。该目录是 libopus/Concentus 风格实现的 C# 移植和适配，采用 BSD-3-Clause 风格条件；Opus 算法依据 RFC 6716，Ogg Opus 封装依据 RFC 7845。

项目改进包括：

1. 将解码核心和 CELT/SILK 相关代码接入 C# 项目结构。
2. 增加 Ogg Opus page/packet 读取和 pre-skip 处理。
3. 增加 seek 预卷、流状态重置和 PCM32 输出。
4. 使用复用工作区减少播放路径分配。

发布时必须保留 BSD 条件、免责声明和文件中的专利许可说明；专利许可不等同于代码许可证。Opus 的实际验证范围以项目测试样本为准。

## 免责声明

源码公开用于共享、验证和履行上游许可要求，不保证无 Bug，也不保证覆盖所有编码器、封装、标签、损坏输入或硬件路径。维护者对 MP3、AAC、Opus、Ogg 的实际听音覆盖有限，发布前应使用自己的媒体样本完成测试。
