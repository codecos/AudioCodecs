namespace Concentus.Enums;

/// <summary>
/// Note that since most API-level errors are detected and thrown as
/// OpusExceptions, direct use of this class is not usually needed
/// unless you need to interop with existing C-style error handlers.
/// </summary>
public static class OpusError
{
	/// <summary>
	/// No error
	/// </summary>
	public const int OPUS_OK = 0;

	/// <summary>
	/// -1: One or more invalid/out of range arguments
	/// </summary>
	public const int OPUS_BAD_ARG = -1;

	/// <summary>
	/// -2: Not enough bytes allocated in the buffer
	/// </summary>
	public const int OPUS_BUFFER_TOO_SMALL = -2;

	/// <summary>
	/// -3: An public error was detected
	/// </summary>
	public const int OPUS_INTERNAL_ERROR = -3;

	/// <summary>
	/// -4: The compressed data passed is corrupted
	/// </summary>
	public const int OPUS_INVALID_PACKET = -4;

	/// <summary>
	/// -5: Invalid/unsupported request number
	/// </summary>
	public const int OPUS_UNIMPLEMENTED = -5;

	/// <summary>
	/// -6: An encoder or decoder structure is invalid or already freed
	/// </summary>
	public const int OPUS_INVALID_STATE = -6;

	/// <summary>
	/// -7: Memory allocation has failed (This is typically not possible in the C# implementation).
	/// </summary>
	public const int OPUS_ALLOC_FAIL = -7;

	/// <summary>
	/// -8: Used in rare cases where Concentus throws an error that is not covered by the
	/// original Opus spec.
	/// </summary>
	public const int CONCENTUS_UNKNOWN_ERROR = -100;
}
