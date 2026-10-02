namespace Concentus.Common.CPlusPlus;

internal class BoxedValueShort
{
	internal short Val;

	internal BoxedValueShort(short v = 0)
	{
		Val = v;
	}

	public override string ToString()
	{
		return Val.ToString();
	}
}
