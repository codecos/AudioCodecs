using System;
using No_Glass_Player.PlayerHost.Playback;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Aac;

internal sealed class AacReaderWorkspace : IDisposable
{
    private const int ContextCacheCapacity = 8;
    private const int DecodeInfoCacheCapacity = 8;

    private NativeBufferOwner<byte>? _packetBuffer;
    private NativeBufferOwner<float>? _floatOutput;
    private NativeBufferOwner<int>? _pcm32Output;
    private NativeBufferOwner<int>? _stereoPcm32Output;
    private readonly AacContextCacheEntry[] _contexts = new AacContextCacheEntry[ContextCacheCapacity];
    private readonly AacDecodeInfoCacheEntry[] _decodeInfos = new AacDecodeInfoCacheEntry[DecodeInfoCacheCapacity];

    private long _useStamp;


    internal Span<byte> PacketBuffer => GetSpan(_packetBuffer, "AAC packet");
    internal Span<float> FloatOutput => GetSpan(_floatOutput, "AAC Float32 output");
    internal Span<int> Pcm32Output => GetSpan(_pcm32Output, "AAC PCM32 output");
    internal Span<int> StereoPcm32Output => GetSpan(_stereoPcm32Output, "AAC stereo PCM32 output");
    internal nint FloatOutputPointer => GetPointer(_floatOutput, "AAC Float32 output");
    internal nint Pcm32OutputPointer => GetPointer(_pcm32Output, "AAC PCM32 output");

    internal AacMp4Demuxer.AacDecodeInfo AcquireDecodeInfo(string path)
    {
        var stamp = ++_useStamp;
        for (var i = 0; i < _decodeInfos.Length; i++)
        {
            ref var entry = ref _decodeInfos[i];
            if (entry.Info is not null && string.Equals(entry.Path, path, StringComparison.Ordinal))
            {
                entry.LastUseStamp = stamp;
                return entry.Info;
            }
        }

        var replacementIndex = -1;
        var oldestStamp = long.MaxValue;
        for (var i = 0; i < _decodeInfos.Length; i++)
        {
            ref var entry = ref _decodeInfos[i];
            if (entry.Info is null)
            {
                replacementIndex = i;
                break;
            }

            if (entry.LastUseStamp < oldestStamp)
            {
                oldestStamp = entry.LastUseStamp;
                replacementIndex = i;
            }
        }

        ref var replacement = ref _decodeInfos[replacementIndex];
        replacement.Dispose();
        replacement.Path = path;
        replacement.Info = AacMp4Demuxer.ReadDecodeInfo(path);
        replacement.LastUseStamp = stamp;
        return replacement.Info;
    }

    internal void EnsureCapacity(int maxPacketBytes, int frameLength, int channels)
    {
        var sampleCount = checked(Math.Max(1, frameLength) * Math.Max(1, channels));
        EnsureOwner(ref _packetBuffer, Math.Max(1, maxPacketBytes));
        EnsureOwner(ref _floatOutput, sampleCount);
        EnsureOwner(ref _pcm32Output, sampleCount);
        EnsureOwner(ref _stereoPcm32Output, checked(Math.Max(1, frameLength) * 2));
    }

    internal AacDecoderContext AcquireContext(ReadOnlySpan<byte> decoderSpecificInfo)
    {
        var stamp = ++_useStamp;
        for (var i = 0; i < _contexts.Length; i++)
        {
            ref var entry = ref _contexts[i];
            if (!entry.InUse && entry.Context is not null && entry.ConfigMatches(decoderSpecificInfo))
            {
                entry.InUse = true;
                entry.LastUseStamp = stamp;
                entry.Context.Reset();
                return entry.Context;
            }
        }

        var replacementIndex = -1;
        var oldestStamp = long.MaxValue;
        for (var i = 0; i < _contexts.Length; i++)
        {
            ref var entry = ref _contexts[i];
            if (entry.InUse)
            {
                continue;
            }

            if (entry.Context is null)
            {
                replacementIndex = i;
                break;
            }

            if (entry.LastUseStamp < oldestStamp)
            {
                oldestStamp = entry.LastUseStamp;
                replacementIndex = i;
            }
        }

        if (replacementIndex < 0)
        {
            throw new InvalidOperationException("AAC decoder context cache has no available slot.");
        }

        ref var replacement = ref _contexts[replacementIndex];
        replacement.Dispose();
        replacement.SetConfig(decoderSpecificInfo);
        replacement.Context = new AacDecoderContext(decoderSpecificInfo);
        replacement.InUse = true;
        replacement.LastUseStamp = stamp;
        return replacement.Context;
    }

    internal void ReleaseContext(AacDecoderContext context)
    {
        context.SampleBuffer.ClearExternalBuffers();
        for (var i = 0; i < _contexts.Length; i++)
        {
            ref var entry = ref _contexts[i];
            if (ReferenceEquals(entry.Context, context))
            {
                entry.InUse = false;
                return;
            }
        }
    }

    public void Dispose()
    {

        for (var i = 0; i < _decodeInfos.Length; i++)
        {
            _decodeInfos[i].Dispose();
        }

        for (var i = 0; i < _contexts.Length; i++)
        {
            _contexts[i].Dispose();
        }

        DisposeOwner(ref _stereoPcm32Output);
        DisposeOwner(ref _pcm32Output);
        DisposeOwner(ref _floatOutput);
        DisposeOwner(ref _packetBuffer);
    }

    private static Span<T> GetSpan<T>(NativeBufferOwner<T>? owner, string name)
        where T : unmanaged
        => (owner ?? throw new InvalidOperationException($"The {name} buffer has not been prepared.")).Span;

    private static nint GetPointer<T>(NativeBufferOwner<T>? owner, string name)
        where T : unmanaged
        => (owner ?? throw new InvalidOperationException($"The {name} buffer has not been prepared.")).Pointer;

    private static void EnsureOwner<T>(ref NativeBufferOwner<T>? owner, int requiredElements)
        where T : unmanaged
    {
        if (owner is not null && owner.CapacityElements >= requiredElements)
        {
            return;
        }

        var replacement = new NativeBufferOwner<T>(requiredElements);
        try
        {
            replacement.PreTouch();
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        var current = owner;
        owner = replacement;
        current?.Dispose();
    }

    private static void DisposeOwner<T>(ref NativeBufferOwner<T>? owner)
        where T : unmanaged
    {
        owner?.Dispose();
        owner = null;
    }

    private struct AacContextCacheEntry
    {
        private NativeBufferOwner<byte>? _config;
        private int _configLength;

        internal AacDecoderContext? Context;
        internal bool InUse;
        internal long LastUseStamp;

        internal bool ConfigMatches(ReadOnlySpan<byte> config)
            => _configLength == config.Length
                && _config is not null
                && _config.Span.Slice(0, _configLength).SequenceEqual(config);

        internal void SetConfig(ReadOnlySpan<byte> config)
        {
            _config = new NativeBufferOwner<byte>(Math.Max(1, config.Length));
            _config.PreTouch();
            config.CopyTo(_config.Span);
            _configLength = config.Length;
        }

        internal void Dispose()
        {
            Context?.SampleBuffer.ClearExternalBuffers();
            Context = null;
            InUse = false;
            LastUseStamp = 0;
            _configLength = 0;
            _config?.Dispose();
            _config = null;
        }
    }

    private struct AacDecodeInfoCacheEntry
    {
        internal string? Path;
        internal AacMp4Demuxer.AacDecodeInfo? Info;
        internal long LastUseStamp;

        internal void Dispose()
        {
            Info?.Dispose();
            Info = null;
            Path = null;
            LastUseStamp = 0;
        }
    }
}
