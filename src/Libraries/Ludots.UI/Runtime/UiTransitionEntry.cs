namespace Ludots.UI.Runtime;

public sealed record UiTransitionEntry(string PropertyName, float DurationSeconds, float DelaySeconds, UiTransitionEasing Easing);

public sealed record UiTransitionSpec(IReadOnlyList<UiTransitionEntry> Entries)
{
    public bool TryGet(string propertyName, out UiTransitionEntry? entry)
    {
        entry = Entries.FirstOrDefault(item => string.Equals(item.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.PropertyName, "all", StringComparison.OrdinalIgnoreCase));
        return entry != null;
    }
}
