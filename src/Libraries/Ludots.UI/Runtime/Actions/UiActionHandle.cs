namespace Ludots.UI.Runtime.Actions;

public readonly record struct UiActionHandle(int Value)
{
	public bool IsValid => Value > 0;

	public static readonly UiActionHandle Invalid = new UiActionHandle(0);

	public override string ToString()
	{
		return Value.ToString();
	}
}
