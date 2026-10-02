namespace Concentus.Common.CPlusPlus;

internal class BoxedValueInt
{
	internal int Val;

	internal BoxedValueInt(int v = 0)
	{
		Val = v;
	}

	public override string ToString()
	{
		return Val.ToString();
	}
}
