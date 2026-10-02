using System;

namespace Concentus;

/// <summary>
/// An exception type which wraps a raw Opus error code.
/// </summary>
public class OpusException : Exception
{
	/// <summary>
	/// Gets the raw Opus error code as defined in the C spec. These codes can
	/// be found in the <see cref="T:Concentus.Enums.OpusError" /> enumeration.
	/// </summary>
	public int OpusErrorCode { get; private set; }

	/// <summary>
	/// Creates a new empty <see cref="T:Concentus.OpusException" />.
	/// This constructor is discouraged as it does not set the raw error code.
	/// </summary>
	internal OpusException()
		: base("Unknown error")
	{
		OpusErrorCode = -100;
	}

	/// <summary>
	/// Creates a new <see cref="T:Concentus.OpusException" /> with a custom error message.
	/// This constructor is discouraged as it does not set the raw error code.
	/// so it reports 
	/// </summary>
	internal OpusException(string message)
		: base(message)
	{
		OpusErrorCode = -100;
	}

	/// <summary>
	/// Creates a new <see cref="T:Concentus.OpusException" /> with a custom error message.
	/// This constructor is discouraged as it does not set the raw error code.
	/// so it reports 
	/// </summary>
	internal OpusException(int opusError)
		: base(CodecHelpers.opus_strerror(opusError))
	{
		OpusErrorCode = opusError;
	}

	/// <summary>
	/// Creates a new <see cref="T:Concentus.OpusException" /> with a custom error message and matching Opus error code.
	/// </summary>
	/// <param name="message">The entire error message string.</param>
	/// <param name="opusError">The raw error code that can be passed to other C-style error handlers
	/// if necessary (it is not used to format the error string).</param>
	internal OpusException(string message, int opusError)
		: base(message)
	{
		OpusErrorCode = opusError;
	}
}
