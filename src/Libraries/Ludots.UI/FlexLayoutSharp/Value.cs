namespace FlexLayoutSharp;

public class Value
{
	public float value;

	public Unit unit;

	public static Value UndefinedValue => new Value(float.NaN, Unit.Undefined);

	public Value(float v, Unit u)
	{
		value = v;
		unit = u;
	}

	public static void CopyValue(Value[] dest, Value[] src)
	{
		for (int i = 0; i < src.Length; i++)
		{
			dest[i].value = src[i].value;
			dest[i].unit = src[i].unit;
		}
	}
}
