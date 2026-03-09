namespace Ludots.UI.Runtime;

public sealed record UiSelectorAttribute(string Name, string? Value, UiSelectorAttributeOperator Operator = UiSelectorAttributeOperator.Exists);
