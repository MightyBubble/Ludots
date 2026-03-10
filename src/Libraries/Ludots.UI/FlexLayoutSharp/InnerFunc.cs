namespace FlexLayoutSharp;

internal class InnerFunc
{
	internal static float fmaxf(float a, float b)
	{
		if (float.IsNaN(a))
		{
			return b;
		}
		if (float.IsNaN(b))
		{
			return a;
		}
		if (a > b)
		{
			return a;
		}
		return b;
	}

	internal static float fminf(float a, float b)
	{
		if (float.IsNaN(a))
		{
			return b;
		}
		if (float.IsNaN(b))
		{
			return a;
		}
		if (a < b)
		{
			return a;
		}
		return b;
	}
}
