namespace Ludots.UI.Runtime.Actions;

public readonly record struct UiActionHandle(int Value)
{
    public static readonly UiActionHandle Invalid = new(0);

    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString();
}
