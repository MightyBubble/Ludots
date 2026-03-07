namespace Ludots.UI.Runtime;

public sealed class UiStyleRule
{
    public UiStyleRule(UiSelector selector, UiStyleDeclaration declaration, int order)
    {
        Selector = selector ?? throw new ArgumentNullException(nameof(selector));
        Declaration = declaration ?? throw new ArgumentNullException(nameof(declaration));
        Order = order;
    }

    public UiSelector Selector { get; }

    public UiStyleDeclaration Declaration { get; }

    public int Order { get; }
}
