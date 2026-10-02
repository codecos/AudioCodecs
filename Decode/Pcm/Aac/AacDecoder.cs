using No_Glass_Player.AudioDecodeCore.Decode.Pcm;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Aac;

internal static partial class AacDecoder
{
    private const int MaximumFramePcmSamples = 2048;

    internal static AacMp4Demuxer.AacContainerInfo ReadInfo(string path)
    {
        return AacMp4Demuxer.ReadInfo(path);
    }

    private static void ValidateSupportedConfig(AacMp4Demuxer.AacDecodeInfo decodeInfo)
    {
        var config = decodeInfo.Esds.Config;
        if (config.EffectiveAudioObjectType != 2)
        {
            throw new NotSupportedException($"JAAD AAC path supports AAC-LC and HE-AAC with an AAC-LC core. effectiveAot={config.EffectiveAudioObjectType}");
        }

        if (decodeInfo.Container.Channels is < 1 or > 2)
        {
            throw new NotSupportedException($"SharpJaad AAC path currently supports mono/stereo only. channels={decodeInfo.Container.Channels}");
        }
    }


}

internal sealed class AacDecoderContext
{
    private readonly byte[] _decoderSpecificInfo;

    internal AacDecoderContext(ReadOnlySpan<byte> decoderSpecificInfo)
    {
        _decoderSpecificInfo = decoderSpecificInfo.ToArray();
        Decoder = new SharpJaad.AAC.Decoder(_decoderSpecificInfo);
        SampleBuffer = new SharpJaad.AAC.SampleBuffer();
        SampleBuffer.SetBigEndian(false);
    }

    internal SharpJaad.AAC.Decoder Decoder { get; private set; }
    internal SharpJaad.AAC.SampleBuffer SampleBuffer { get; }

    internal void Reset()
    {
        // SBR/HE-AAC 的解码状态不能只清空 PCM 缓冲区后复用。
        // 这里保留 Decoder Specific Info，在 SBR 流重置时重建 JAAD 状态机。
        // 普通 AAC-LC 仍使用原有 Reset，避免改变稳定路径的开销。
        SampleBuffer.ClearExternalBuffers();
        if (Decoder.GetConfig().IsSBRPresent())
        {
            Decoder = new SharpJaad.AAC.Decoder(_decoderSpecificInfo);
        }
        else
        {
            Decoder.Reset();
        }
    }
}
