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

	public void Set(float v, Unit u)
	{
		value = v;
		unit = u;
	}

	public void SetUndefined()
	{
		value = float.NaN;
		unit = Unit.Undefined;
	}

	public void SetAuto()
	{
		value = float.NaN;
		unit = Unit.Auto;
	}

	public static void CopyValue(Value[] dest, Value[] src)
	{
		for (int i = 0; i < src.Length; i++)
		{
			dest[i].value = src[i].value;
			dest[i].unit = src[i].unit;
		}
	}

	public static void ResetEdgeValues(Value[] values)
	{
		for (int i = 0; i < values.Length; i++)
		{
			values[i].SetUndefined();
		}
	}
}
