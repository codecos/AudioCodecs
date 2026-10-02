using System.Buffers;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Ape;

// APE 播放所需的大块缓冲和格式状态集中在一个生命周期对象中。
// 构造阶段完成容量准备，reader 的 Read/Seek 只复用这些资源。
internal sealed class ApeReaderWorkspace : IDisposable
{
    internal ApeReaderWorkspace(ApeInfo info)
    {
        Compressed = ArrayPool<byte>.Shared.Rent(Math.Max(64, info.MaxFrameBytes + 8));
        Pcm = ArrayPool<int>.Shared.Rent(checked(info.BlocksPerFrame * info.Channels));
        if (info.Version >= 3980)
        {
            Modern = new ApeDecoder.ModernFrameWorkspace(info);
            return;
        }

        LegacyResidual0 = ArrayPool<int>.Shared.Rent(info.BlocksPerFrame);
        LegacyResidual1 = ArrayPool<int>.Shared.Rent(info.BlocksPerFrame);
        LegacyDecoded0 = ArrayPool<int>.Shared.Rent(info.BlocksPerFrame);
        LegacyDecoded1 = ArrayPool<int>.Shared.Rent(info.BlocksPerFrame);
        Legacy = new ApeLegacyDecoder.LegacyFrameWorkspace(info);
    }

    internal byte[] Compressed { get; }
    internal int[] Pcm { get; }
    internal ApeDecoder.ModernFrameWorkspace? Modern { get; }
    internal ApeLegacyDecoder.LegacyFrameWorkspace? Legacy { get; }
    internal int[]? LegacyResidual0 { get; }
    internal int[]? LegacyResidual1 { get; }
    internal int[]? LegacyDecoded0 { get; }
    internal int[]? LegacyDecoded1 { get; }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(Compressed);
        ArrayPool<int>.Shared.Return(Pcm);
        if (LegacyResidual0 is not null) ArrayPool<int>.Shared.Return(LegacyResidual0);
        if (LegacyResidual1 is not null) ArrayPool<int>.Shared.Return(LegacyResidual1);
        if (LegacyDecoded0 is not null) ArrayPool<int>.Shared.Return(LegacyDecoded0);
        if (LegacyDecoded1 is not null) ArrayPool<int>.Shared.Return(LegacyDecoded1);
    }
}
