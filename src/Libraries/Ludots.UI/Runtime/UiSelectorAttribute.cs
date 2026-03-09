namespace Ludots.UI.Runtime;

public sealed record UiSelectorAttribute(string Name, string? Value, UiSelectorAttributeOperator Operator = UiSelectorAttributeOperator.Exists);

public enum UiSelectorAttributeOperator : byte
{
    Exists = 0,
    Equals = 1,
    Includes = 2,
    DashMatch = 3,
    Prefix = 4,
    Suffix = 5,
    Substring = 6
}
