using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace No_Glass_Player.PlayerHost.Playback;

public enum NativeAllocationKind
{
    NativeMemory,
    Aligned
}

internal readonly record struct NativeMemoryStatisticsSnapshot(
    long CurrentBlocks,
    long CurrentBytes,
    long PeakBlocks,
    long PeakBytes,
    long AllocationCount,
    long FreeCount);

internal static class NativeMemoryStatistics
{
    private static long _currentBlocks;
    private static long _currentBytes;
    private static long _peakBlocks;
    private static long _peakBytes;
    private static long _allocationCount;
    private static long _freeCount;

    public static NativeMemoryStatisticsSnapshot Snapshot => new(
        Volatile.Read(ref _currentBlocks),
        Volatile.Read(ref _currentBytes),
        Volatile.Read(ref _peakBlocks),
        Volatile.Read(ref _peakBytes),
        Volatile.Read(ref _allocationCount),
        Volatile.Read(ref _freeCount));

    internal static void RecordAllocation(long bytes)
    {
        var blocks = Interlocked.Increment(ref _currentBlocks);
        var currentBytes = Interlocked.Add(ref _currentBytes, bytes);
        Interlocked.Increment(ref _allocationCount);
        UpdatePeak(ref _peakBlocks, blocks);
        UpdatePeak(ref _peakBytes, currentBytes);
    }

    internal static void RecordFree(long bytes)
    {
        Interlocked.Decrement(ref _currentBlocks);
        Interlocked.Add(ref _currentBytes, -bytes);
        Interlocked.Increment(ref _freeCount);
    }

    private static void UpdatePeak(ref long peak, long value)
    {
        var observed = Volatile.Read(ref peak);
        while (value > observed)
        {
            var prior = Interlocked.CompareExchange(ref peak, value, observed);
            if (prior == observed)
            {
                return;
            }

            observed = prior;
        }
    }
}

public sealed unsafe class NativeBufferOwner<T> : IDisposable
    where T : unmanaged
{
    private nint _pointer;

    public NativeBufferOwner(int capacityElements, nuint alignment = 0)
    {
        if (capacityElements <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityElements));
        }

        CapacityElements = capacityElements;
        CapacityBytes = checked((long)capacityElements * sizeof(T));
        if (alignment == 0)
        {
            AllocationKind = NativeAllocationKind.NativeMemory;
            _pointer = (nint)NativeMemory.Alloc((nuint)CapacityBytes);
        }
        else
        {
            if ((alignment & (alignment - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be a power of two.");
            }

            AllocationKind = NativeAllocationKind.Aligned;
            _pointer = (nint)NativeMemory.AlignedAlloc((nuint)CapacityBytes, alignment);
        }

        if (_pointer == 0)
        {
            throw new OutOfMemoryException($"Unable to allocate {CapacityBytes} bytes of native memory.");
        }

        NativeMemoryStatistics.RecordAllocation(CapacityBytes);
    }

    public int CapacityElements { get; private set; }

    public long CapacityBytes { get; private set; }

    public NativeAllocationKind AllocationKind { get; }

    public nint Pointer => Volatile.Read(ref _pointer);

    public bool IsDisposed => Pointer == 0;

    public Span<T> Span
    {
        get
        {
            var pointer = Pointer;
            if (pointer == 0)
            {
                throw new ObjectDisposedException(nameof(NativeBufferOwner<T>));
            }

            return new Span<T>((void*)pointer, CapacityElements);
        }
    }

    public ref T this[int index] => ref Span[index];

    public Span<T> AsSpan()
        => Span;

    public Span<T> AsSpan(int start, int length)
        => Span.Slice(start, length);

    public void PreTouch(int pageSizeBytes = 4096)
    {
        if (pageSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSizeBytes));
        }

        var pointer = (byte*)Pointer;
        if (pointer == null)
        {
            throw new ObjectDisposedException(nameof(NativeBufferOwner<T>));
        }

        for (long offset = 0; offset < CapacityBytes; offset += pageSizeBytes)
        {
            pointer[offset] = 0;
        }

        pointer[CapacityBytes - 1] = 0;
    }

    public void Dispose()
    {
        var pointer = Interlocked.Exchange(ref _pointer, 0);
        if (pointer == 0)
        {
            return;
        }

        var capacityBytes = CapacityBytes;
        CapacityElements = 0;
        CapacityBytes = 0;

        if (AllocationKind == NativeAllocationKind.Aligned)
        {
            NativeMemory.AlignedFree((void*)pointer);
        }
        else
        {
            NativeMemory.Free((void*)pointer);
        }

        NativeMemoryStatistics.RecordFree(capacityBytes);
    }
}
