using System;

namespace Concentus.Common.CPlusPlus;

internal static class Arrays
{
	internal static T[][] InitTwoDimensionalArray<T>(int x, int y)
	{
		T[][] array = new T[x][];
		for (int i = 0; i < x; i++)
		{
			array[i] = new T[y];
		}
		return array;
	}

	internal static Pointer<Pointer<T>> InitTwoDimensionalArrayPointer<T>(int x, int y)
	{
		Pointer<Pointer<T>> pointer = Pointer.Malloc<Pointer<T>>(x);
		for (int i = 0; i < x; i++)
		{
			pointer[i] = Pointer.Malloc<T>(y);
		}
		return pointer;
	}

	internal static T[][][] InitThreeDimensionalArray<T>(int x, int y, int z)
	{
		T[][][] array = new T[x][][];
		for (int i = 0; i < x; i++)
		{
			array[i] = new T[y][];
			for (int j = 0; j < y; j++)
			{
				array[i][j] = new T[z];
			}
		}
		return array;
	}

	internal static void MemSetByte(byte[] array, byte value)
	{
		array.AsSpan().Fill(value);
	}

	internal static void MemSetInt(int[] array, int value, int length)
	{
		array.AsSpan(0, length).Fill(value);
	}

	internal static void MemSetShort(short[] array, short value, int length)
	{
		array.AsSpan(0, length).Fill(value);
	}

	internal static void MemSetFloat(float[] array, float value, int length)
	{
		array.AsSpan(0, length).Fill(value);
	}

	internal static void MemSetSbyte(sbyte[] array, sbyte value, int length)
	{
		array.AsSpan(0, length).Fill(value);
	}

	internal static void MemSetWithOffset<T>(T[] array, T value, int offset, int length)
	{
		array.AsSpan(offset, length).Fill(value);
	}

	internal static void MemSetWithOffset<T>(Span<T> array, T value, int offset, int length)
	{
		array.Slice(offset, length).Fill(value);
	}

	internal static void MemMoveByte(byte[] array, int src_idx, int dst_idx, int length)
	{
		if (src_idx != dst_idx && length != 0)
		{
			Buffer.BlockCopy(array, src_idx, array, dst_idx, length);
		}
	}

	internal static void MemMoveByte(Span<byte> array, int src_idx, int dst_idx, int length)
	{
		if (src_idx != dst_idx && length != 0)
		{
			array.Slice(src_idx, length).CopyTo(array.Slice(dst_idx, length));
		}
	}

	internal static void MemCopy(int[] src, int src_idx, int[] dst, int dst_idx, int length)
	{
		if (length != 0)
		{
			Buffer.BlockCopy(src, src_idx * 4, dst, dst_idx * 4, length * 4);
		}
	}

	internal static void MemCopy(short[] src, int src_idx, short[] dst, int dst_idx, int length)
	{
		if (length != 0)
		{
			Buffer.BlockCopy(src, src_idx * 2, dst, dst_idx * 2, length * 2);
		}
	}

	internal static void MemCopy(sbyte[] src, int src_idx, sbyte[] dst, int dst_idx, int length)
	{
		if (length != 0)
		{
			Buffer.BlockCopy(src, src_idx, dst, dst_idx, length);
		}
	}

	internal static void MemMoveInt(int[] array, int src_idx, int dst_idx, int length)
	{
		if (src_idx != dst_idx && length != 0)
		{
			Buffer.BlockCopy(array, src_idx * 4, array, dst_idx * 4, length * 4);
		}
	}

	internal static void MemMoveShort(short[] array, int src_idx, int dst_idx, int length)
	{
		if (src_idx != dst_idx && length != 0)
		{
			Buffer.BlockCopy(array, src_idx * 2, array, dst_idx * 2, length * 2);
		}
	}
}
