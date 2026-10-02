namespace Concentus.Common.CPlusPlus;

internal class BoxedValueSbyte
{
	internal sbyte Val;

	internal BoxedValueSbyte(sbyte v = 0)
	{
		Val = v;
	}

	public override string ToString()
	{
		return Val.ToString();
	}
}
