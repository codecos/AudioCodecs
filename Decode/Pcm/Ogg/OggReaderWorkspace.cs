using System;
using No_Glass_Player.PlayerHost.Playback;

namespace No_Glass_Player.AudioDecodeCore.Decode.Pcm.Ogg;

internal sealed class OggReaderWorkspace : IDisposable
{
    private const int MaximumPagePayloadBytes = 255 * 255;
    private const int InitialPacketBytes = 256 * 1024;
    private const int SetupCacheCapacity = 8;

    private NativeBufferOwner<byte>? _segmentTable;
    private NativeBufferOwner<byte>? _pagePayload;
    private NativeBufferOwner<byte>? _packetBuffer;
    private NativeBufferOwner<float>? _floatOutput;
    private NativeBufferOwner<int>? _pcm32SourceOutput;
    private NativeBufferOwner<int>? _stereoPcm32Output;
    private readonly SetupCacheEntry[] _setupCache = new SetupCacheEntry[SetupCacheCapacity];
    private int _activeSetupIndex = -1;
    private int _setupUseClock;

    internal Span<byte> SegmentTable => GetSpan(_segmentTable, "OGG segment table");
    internal Span<byte> PagePayload => GetSpan(_pagePayload, "OGG page payload");
    internal Span<byte> PacketBuffer => GetSpan(_packetBuffer, "OGG packet");
    internal Span<float> FloatOutput => GetSpan(_floatOutput, "OGG Float32 output");
    internal Span<int> Pcm32SourceOutput => GetSpan(_pcm32SourceOutput, "OGG PCM32 source output");
    internal Span<int> StereoPcm32Output => GetSpan(_stereoPcm32Output, "OGG stereo PCM32 output");

    internal void PreparePacketWorkspace()
        => EnsureCommonCapacity();

    internal void EnsureFloat32Capacity(int blockSizeLarge, int channels)
    {
        EnsureCommonCapacity();
        EnsureOwner(ref _floatOutput, checked(Math.Max(1, blockSizeLarge) * Math.Max(1, channels)));
    }

    internal void EnsurePcm32Capacity(int blockSizeLarge, int channels)
    {
        EnsureCommonCapacity();
        EnsureOwner(ref _pcm32SourceOutput, checked(Math.Max(1, blockSizeLarge) * Math.Max(1, channels)));
        EnsureOwner(ref _stereoPcm32Output, checked(Math.Max(1, blockSizeLarge) * 2));
    }

    internal void EnsurePacketCapacity(int requiredBytes)
        => EnsureOwner(ref _packetBuffer, Math.Max(InitialPacketBytes, requiredBytes));

    internal bool TryGetCachedSetup(
        ReadOnlySpan<byte> setupPacket,
        int channels,
        out global::OggVorbisSetupSummary setup)
    {
        for (var i = 0; i < _setupCache.Length; i++)
        {
            ref var entry = ref _setupCache[i];
            if (!entry.IsOccupied
                || entry.Channels != channels
                || entry.PacketLength != setupPacket.Length
                || entry.Packet is null
                || !setupPacket.SequenceEqual(entry.Packet.Span[..entry.PacketLength]))
            {
                continue;
            }

            entry.LastUse = NextSetupUse();
            _activeSetupIndex = i;
            setup = entry.Setup;
            return true;
        }

        setup = default;
        return false;
    }

    internal void CacheSetup(
        ReadOnlySpan<byte> setupPacket,
        int channels,
        global::OggVorbisSetupSummary setup)
    {
        var index = FindSetupCacheSlot();
        ref var entry = ref _setupCache[index];
        EnsureOwner(ref entry.Packet, Math.Max(1, setupPacket.Length));
        setupPacket.CopyTo(entry.Packet!.Span);
        entry.PacketLength = setupPacket.Length;
        entry.Channels = channels;
        entry.Setup = setup;
        entry.Context = null;
        entry.LastUse = NextSetupUse();
        entry.IsOccupied = true;
        _activeSetupIndex = index;
    }

    internal global::OggVorbisManualDecoder.OggVorbisFloat32Context AcquireDecoderContext(
        int channels,
        int blockSizeSmall,
        int blockSizeLarge,
        global::OggVorbisSetupSummary setup,
        global::OggVorbisManualDecoder.OggVorbisOutputTarget outputTarget)
    {
        if ((uint)_activeSetupIndex >= (uint)_setupCache.Length)
        {
            throw new InvalidOperationException("No OGG setup is active for decoder context acquisition.");
        }

        ref var entry = ref _setupCache[_activeSetupIndex];
        var context = entry.Context;
        if (context is null || !context.IsCompatible(channels, blockSizeSmall, blockSizeLarge, setup, outputTarget))
        {
            context = new global::OggVorbisManualDecoder.OggVorbisFloat32Context(
                channels,
                blockSizeSmall,
                blockSizeLarge,
                setup,
                outputTarget,
                captureSampleLimit: 0);
            entry.Context = context;
        }

        entry.LastUse = NextSetupUse();
        context.ResetState();
        context.BindCapturedOutput(this);
        return context;
    }

    public void Dispose()
    {
        for (var i = 0; i < _setupCache.Length; i++)
        {
            ref var entry = ref _setupCache[i];
            entry.Context = null;
            entry.Setup = default;
            entry.PacketLength = 0;
            entry.Channels = 0;
            entry.LastUse = 0;
            entry.IsOccupied = false;
            DisposeOwner(ref entry.Packet);
        }

        _activeSetupIndex = -1;
        _setupUseClock = 0;
        DisposeOwner(ref _stereoPcm32Output);
        DisposeOwner(ref _pcm32SourceOutput);
        DisposeOwner(ref _floatOutput);
        DisposeOwner(ref _packetBuffer);
        DisposeOwner(ref _pagePayload);
        DisposeOwner(ref _segmentTable);
    }

    private int FindSetupCacheSlot()
    {
        var oldestIndex = 0;
        var oldestUse = int.MaxValue;
        for (var i = 0; i < _setupCache.Length; i++)
        {
            ref var entry = ref _setupCache[i];
            if (!entry.IsOccupied)
            {
                return i;
            }

            if (entry.LastUse < oldestUse)
            {
                oldestUse = entry.LastUse;
                oldestIndex = i;
            }
        }

        ref var evicted = ref _setupCache[oldestIndex];
        evicted.Context = null;
        evicted.Setup = default;
        evicted.IsOccupied = false;
        return oldestIndex;
    }

    private int NextSetupUse()
    {
        if (_setupUseClock == int.MaxValue)
        {
            for (var i = 0; i < _setupCache.Length; i++)
            {
                _setupCache[i].LastUse = 0;
            }

            _setupUseClock = 0;
        }

        return ++_setupUseClock;
    }

    private void EnsureCommonCapacity()
    {
        EnsureOwner(ref _segmentTable, 255);
        EnsureOwner(ref _pagePayload, MaximumPagePayloadBytes);
        EnsurePacketCapacity(InitialPacketBytes);
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

        owner = replacement;
        current?.Dispose();
    }

    private static void DisposeOwner<T>(ref NativeBufferOwner<T>? owner)
        where T : unmanaged
    {
        owner?.Dispose();
        owner = null;
    }

    private struct SetupCacheEntry
    {
        internal NativeBufferOwner<byte>? Packet;
        internal global::OggVorbisSetupSummary Setup;
        internal global::OggVorbisManualDecoder.OggVorbisFloat32Context? Context;
        internal int PacketLength;
        internal int Channels;
        internal int LastUse;
        internal bool IsOccupied;
    }
}
