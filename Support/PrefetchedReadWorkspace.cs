using System;

namespace No_Glass_Player.PlayerHost.Playback;

public sealed class PrefetchedReadWorkspace : IDisposable
{
    private NativeBufferOwner<byte>? _bufferA;
    private NativeBufferOwner<byte>? _bufferB;

    internal NativeBufferOwner<byte> AcquireBuffer(int slot, int requiredBytes)
    {
        return slot switch
        {
            0 => AcquireOwner(ref _bufferA, requiredBytes),
            1 => AcquireOwner(ref _bufferB, requiredBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
    }

    internal (int BufferA, int BufferB) CapacitySnapshot
        => (_bufferA?.CapacityElements ?? 0, _bufferB?.CapacityElements ?? 0);

    public void Dispose()
    {
        _bufferB?.Dispose();
        _bufferB = null;
        _bufferA?.Dispose();
        _bufferA = null;
    }

    private static NativeBufferOwner<byte> AcquireOwner(
        ref NativeBufferOwner<byte>? owner,
        int requiredBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredBytes);
        var current = owner;
        if (current is not null && current.CapacityElements >= requiredBytes)
        {
            return current;
        }

        var replacement = new NativeBufferOwner<byte>(requiredBytes);
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
        return replacement;
    }
}
