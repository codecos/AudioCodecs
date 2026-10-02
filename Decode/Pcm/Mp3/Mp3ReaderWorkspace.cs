using System;
using No_Glass_Player.PlayerHost.Playback;
using Minimp3;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Mp3;

internal sealed class Mp3ReaderWorkspace : IDisposable
{
    private const int MaxDecodedSamples = 4096;
    private const int InitialFrameOffsetCapacity = 4096;

    private NativeBufferOwner<float>? _floatStage;
    private NativeBufferOwner<float>? _floatPending;
    private NativeBufferOwner<int>? _pcm32Stage;
    private NativeBufferOwner<int>? _pcm32Pending;
    private NativeBufferOwner<long>? _frameOffsets;
    private mp3dec_ex_t? _decoder;
    private int _frameOffsetCount;
    private readonly PrefetchedReadWorkspace _prefetch = new();

    internal PrefetchedReadWorkspace Prefetch => _prefetch;
    internal Span<float> FloatStage => GetSpan(_floatStage, "MP3 Float32 stage");
    internal Span<float> FloatPending => GetSpan(_floatPending, "MP3 Float32 pending");
    internal Span<int> Pcm32Stage => GetSpan(_pcm32Stage, "MP3 PCM32 stage");
    internal Span<int> Pcm32Pending => GetSpan(_pcm32Pending, "MP3 PCM32 pending");
    internal ReadOnlySpan<long> FrameOffsets
        => _frameOffsets is null ? ReadOnlySpan<long>.Empty : _frameOffsets.Span[.._frameOffsetCount];
    internal int FrameOffsetCount => _frameOffsetCount;

    internal mp3dec_ex_t AcquireDecoder()
        => _decoder ??= new mp3dec_ex_t();
    internal void ResetFrameOffsets()
    {
        _frameOffsetCount = 0;
    }

    internal void AddFrameOffset(long offset)
    {
        EnsureFrameOffsetCapacity(_frameOffsetCount + 1);
        _frameOffsets!.Span[_frameOffsetCount++] = offset;
    }

    internal void EnsureFloat32Capacity(int channels, int encoderPadding)
    {
        EnsureOwner(ref _floatStage, MaxDecodedSamples);
        EnsureOwner(
            ref _floatPending,
            checked(MaxDecodedSamples + Math.Max(0, encoderPadding) * Math.Max(1, channels) + 16));
    }

    internal void EnsurePcm32Capacity(int encoderPadding)
    {
        EnsureOwner(ref _pcm32Stage, MaxDecodedSamples * 2);
        EnsureOwner(
            ref _pcm32Pending,
            checked((MaxDecodedSamples + Math.Max(0, encoderPadding) * 2) * 2 + 16));
    }

    public void Dispose()
    {
        _frameOffsetCount = 0;
        if (_decoder is not null)
        {
            minimp3_ex.mp3dec_ex_close(_decoder);
            _decoder = null;
        }
        DisposeOwner(ref _frameOffsets);
        _prefetch.Dispose();
        DisposeOwner(ref _pcm32Pending);
        DisposeOwner(ref _pcm32Stage);
        DisposeOwner(ref _floatPending);
        DisposeOwner(ref _floatStage);
    }

    private void EnsureFrameOffsetCapacity(int requiredElements)
    {
        var current = _frameOffsets;
        if (current is not null && current.CapacityElements >= requiredElements)
        {
            return;
        }

        var capacity = current is null
            ? InitialFrameOffsetCapacity
            : current.CapacityElements;
        while (capacity < requiredElements)
        {
            capacity = checked(capacity * 2);
        }

        var replacement = new NativeBufferOwner<long>(capacity);
        try
        {
            replacement.PreTouch();
            if (current is not null && _frameOffsetCount > 0)
            {
                current.Span[.._frameOffsetCount].CopyTo(replacement.Span);
            }
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        _frameOffsets = replacement;
        current?.Dispose();
    }

    private static Span<T> GetSpan<T>(NativeBufferOwner<T>? owner, string bufferName)
        where T : unmanaged
        => (owner ?? throw new InvalidOperationException($"The {bufferName} buffer has not been prepared.")).Span;

    private static void EnsureOwner<T>(ref NativeBufferOwner<T>? owner, int requiredElements)
        where T : unmanaged
    {
        var current = owner;
        if (current is not null && current.CapacityElements >= requiredElements)
        {
            return;
        }

        var replacement = new NativeBufferOwner<T>(Math.Max(1, requiredElements));
        try
        {
            replacement.PreTouch();
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        owner = replacement;
        current?.Dispose();
    }

    private static void DisposeOwner<T>(ref NativeBufferOwner<T>? owner)
        where T : unmanaged
    {
        owner?.Dispose();
        owner = null;
    }
}
