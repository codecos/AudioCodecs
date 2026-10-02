using System;
using System.IO;
using Concentus.Enums;

using Concentus.Structs;

namespace Concentus;

/// <summary>
/// Central factory class for creating Opus encoder / decoder structs.
/// Using these methods allows the runtime to decide the most appropriate
/// implementation for your platform based on what is available (generally,
/// this means using a P/Invoke native adapter if native libopus is present)
/// </summary>
public static class OpusCodecFactory
{
	private static readonly object _mutex = new object();

	private static bool _nativeLibInitialized = false;

	private static bool _isNativeLibAvailable = false;

	private static bool _userAllowNativeLib = true;

	/// <summary>
	/// Gets or sets a global flag that determines whether the codec factory should attempt to
	/// use a native opus.dll or libopus implementation. True by default, but you can override
	/// the value to false if the library probe causes problems or something.
	/// </summary>
	public static bool AttemptToUseNativeLibrary
	{
		get
		{
			return _userAllowNativeLib;
		}
		set
		{
			_userAllowNativeLib = value;
		}
	}








}
