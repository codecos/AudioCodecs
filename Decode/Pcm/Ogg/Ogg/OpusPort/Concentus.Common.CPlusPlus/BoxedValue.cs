namespace Concentus.Common.CPlusPlus;

/// <summary>
/// For performance reasons, do not use this generic class if possible
/// </summary>
/// <typeparam name="T"></typeparam>
internal class BoxedValue<T>
{
	internal T Val;

	internal BoxedValue(T v = default(T))
	{
		Val = v;
	}

	public override string ToString()
	{
		if (Val != null)
		{
			return Val.ToString();
		}
		return "null";
	}
}
