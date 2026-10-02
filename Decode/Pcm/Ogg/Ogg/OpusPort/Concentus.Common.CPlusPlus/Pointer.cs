using System;
using System.Text;

namespace Concentus.Common.CPlusPlus;

/// <summary>
/// This simulates a C++ style pointer as far as can be implemented in C#. It represents a handle
/// to an array of objects, along with a base offset that represents the address.
/// When you are programming in debug mode, this class also enforces memory boundaries,
/// tracks uninitialized values, and also records all statistics of accesses to its base array.
/// </summary>
/// <typeparam name="T"></typeparam>
internal class Pointer<T>
{
	private const bool CHECK_UNINIT_MEM = false;

	private T[] _array;

	private int _offset;

	internal int Offset => _offset;

	internal T[] Data => _array;

	internal T this[int index]
	{
		get
		{
			return _array[index + _offset];
		}
		set
		{
			_array[index + _offset] = value;
		}
	}

	internal T this[uint index]
	{
		get
		{
			return this[(int)index];
		}
		set
		{
			this[(int)index] = value;
		}
	}

	internal Pointer(int capacity)
	{
		_array = new T[capacity];
		_offset = 0;
	}

	internal Pointer(T[] buffer)
	{
		_array = buffer;
		_offset = 0;
	}

	internal Pointer(T[] buffer, int absoluteOffset)
	{
		_array = buffer;
		_offset = absoluteOffset;
	}

	/// <summary>
	/// Returns the value currently under the pointer, and returns a new pointer with +1 offset.
	/// This method is not very efficient because it creates new pointers; this is because we must preserve
	/// the pass-by-value nature of C++ pointers when they are used as arguments to functions
	/// </summary>
	/// <returns></returns>
	internal Pointer<T> Iterate(out T returnVal)
	{
		returnVal = _array[_offset];
		return Point(1);
	}

	internal Pointer<T> Point(int relativeOffset)
	{
		if (relativeOffset == 0)
		{
			return this;
		}
		return new Pointer<T>(_array, _offset + relativeOffset);
	}

	internal Pointer<T> Point(uint relativeOffset)
	{
		if (relativeOffset == 0)
		{
			return this;
		}
		return new Pointer<T>(_array, _offset + (int)relativeOffset);
	}

	private static string invert_endianness(string hexstring)
	{
		StringBuilder stringBuilder = new StringBuilder(hexstring.Length);
		for (int i = 0; i < hexstring.Length / 2; i++)
		{
			stringBuilder.Append(hexstring.Substring(hexstring.Length - (i + 1) * 2, 2));
		}
		return stringBuilder.ToString();
	}

	private static void PrintMemCopy<E>(E[] source, int sourceOffset, int length)
	{
		if (typeof(E) == typeof(int) || typeof(E) == typeof(uint))
		{
			string text = string.Empty;
			for (int i = 0; i < length; i++)
			{
				text += invert_endianness($"{source[i + sourceOffset]:x8}");
			}
		}
		else if (typeof(E) == typeof(short) || typeof(E) == typeof(ushort))
		{
			string text2 = string.Empty;
			for (int j = 0; j < length; j++)
			{
				text2 += invert_endianness($"{source[j + sourceOffset]:x4}");
			}
		}
		else if (typeof(E) == typeof(byte) || typeof(E) == typeof(sbyte))
		{
			string text3 = string.Empty;
			for (int k = 0; k < length; k++)
			{
				text3 += invert_endianness($"{source[k + sourceOffset]:x2}");
			}
		}
	}

	/// <summary>
	/// Copies the contents of this pointer, starting at its current address, into the space of another pointer.
	/// !!! IMPORTANT !!! REMEMBER THAT C++ memcpy is (DEST, SOURCE, LENGTH) !!!!
	/// IN C# IT IS (SOURCE, DEST, LENGTH). DON'T GET SCOOPED LIKE I DID
	/// </summary>
	/// <param name="destination"></param>
	/// <param name="length"></param>
	internal void MemCopyTo(Pointer<T> destination, int length)
	{
		if (destination != null)
		{
			Array.Copy(_array, _offset, destination._array, destination.Offset, length);
			return;
		}
		for (int i = 0; i < length; i++)
		{
			destination[i] = _array[i + _offset];
		}
	}

	/// <summary>
	/// Copies the contents of this pointer, starting at its current address, into an array.
	/// !!! IMPORTANT !!! REMEMBER THAT C++ memcpy is (DEST, SOURCE, LENGTH) !!!!
	/// </summary>
	/// <param name="destination"></param>
	/// <param name="length"></param>
	internal void MemCopyTo(T[] destination, int offset, int length)
	{
		Array.Copy(_array, _offset, destination, offset, length);
	}

	/// <summary>
	/// Loads N values from a source array into this pointer's space
	/// </summary>
	/// <param name="length"></param>
	internal void MemCopyFrom(T[] source, int sourceOffset, int length)
	{
		Array.Copy(source, sourceOffset, _array, _offset, length);
	}

	/// <summary>
	/// Assigns a certain value to a range of spaces in this array
	/// </summary>
	/// <param name="value">The value to set</param>
	/// <param name="length">The number of values to write</param>
	internal void MemSet(T value, int length)
	{
		MemSet(value, (uint)length);
	}

	/// <summary>
	/// Assigns a certain value to a range of spaces in this array
	/// </summary>
	/// <param name="value">The value to set</param>
	/// <param name="length">The number of values to write</param>
	internal void MemSet(T value, uint length)
	{
		for (int i = _offset; i < _offset + length; i++)
		{
			_array[i] = value;
		}
	}

	internal void MemMoveTo(Pointer<T> other, int length)
	{
		if (_array == other._array)
		{
			MemMove(other.Offset - Offset, length);
		}
		else
		{
			MemCopyTo(other, length);
		}
	}

	/// <summary>
	/// Moves regions of memory within the bounds of this pointer's array.
	/// Extra checks are done to ensure that the data is not corrupted if the copy
	/// regions overlap
	/// </summary>
	/// <param name="move_dist">The offset to send this pointer's data to</param>
	/// <param name="length">The number of values to copy</param>
	internal void MemMove(int move_dist, int length)
	{
		_array.AsSpan(_offset, length).CopyTo(_array.AsSpan(_offset + move_dist, length));
	}

	public override bool Equals(object obj)
	{
		if (obj == null || GetType() != obj.GetType())
		{
			return false;
		}
		Pointer<T> pointer = (Pointer<T>)obj;
		if (pointer._offset == _offset)
		{
			return pointer._array == _array;
		}
		return false;
	}

	public override int GetHashCode()
	{
		return _array.GetHashCode() + _offset.GetHashCode();
	}
}
/// <summary>
/// This is a helper class which contains static methods that involve pointers
/// </summary>
internal static class Pointer
{
	/// <summary>
	/// Allocates a new array and returns a pointer to it
	/// </summary>
	/// <typeparam name="E"></typeparam>
	/// <param name="capacity"></param>
	/// <returns></returns>
	internal static Pointer<E> Malloc<E>(int capacity)
	{
		return new Pointer<E>(capacity);
	}

	/// <summary>
	/// Creates a pointer to an existing array
	/// </summary>
	/// <typeparam name="E"></typeparam>
	/// <param name="memory"></param>
	/// <param name="offset"></param>
	/// <returns></returns>
	internal static Pointer<E> GetPointer<E>(this E[] memory, int offset = 0)
	{
		if (memory == null)
		{
			return null;
		}
		return new Pointer<E>(memory, offset);
	}
}
