namespace Ludots.UI.Runtime;

public readonly record struct UiNodeId(int Value)
{
	public bool IsValid => Value > 0;

	public static readonly UiNodeId None = new UiNodeId(0);

	public override string ToString()
	{
		return Value.ToString();
	}
}
