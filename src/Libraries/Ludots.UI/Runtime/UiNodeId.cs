namespace Ludots.UI.Runtime;

public readonly record struct UiNodeId(int Value)
{
    public static readonly UiNodeId None = new(0);

    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString();
}
