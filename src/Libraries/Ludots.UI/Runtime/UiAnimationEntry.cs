namespace Ludots.UI.Runtime;

public enum UiAnimationDirection
{
    Normal = 0,
    Reverse = 1,
    Alternate = 2,
    AlternateReverse = 3
}

public enum UiAnimationFillMode
{
    None = 0,
    Forwards = 1,
    Backwards = 2,
    Both = 3
}

public enum UiAnimationPlayState
{
    Running = 0,
    Paused = 1
}

public sealed record UiKeyframeStop(float Offset, UiStyleDeclaration Declaration);

public sealed class UiKeyframeDefinition
{
    public UiKeyframeDefinition(string name, IEnumerable<UiKeyframeStop> stops)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Keyframe name is required.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(stops);

        Name = name.Trim();
        Stops = stops.OrderBy(static stop => stop.Offset).ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<UiKeyframeStop> Stops { get; }
}

public sealed record UiAnimationEntry(
    string Name,
    float DurationSeconds,
    float DelaySeconds,
    UiTransitionEasing Easing,
    float IterationCount,
    UiAnimationDirection Direction,
    UiAnimationFillMode FillMode,
    UiAnimationPlayState PlayState,
    UiKeyframeDefinition? Keyframes = null);

public sealed class UiAnimationSpec : IEquatable<UiAnimationSpec>
{
    public UiAnimationSpec(IReadOnlyList<UiAnimationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries;
    }

    public IReadOnlyList<UiAnimationEntry> Entries { get; }

    public bool Equals(UiAnimationSpec? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other == null || Entries.Count != other.Entries.Count)
        {
            return false;
        }

        for (int i = 0; i < Entries.Count; i++)
        {
            if (!Equals(Entries[i], other.Entries[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is UiAnimationSpec other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        for (int i = 0; i < Entries.Count; i++)
        {
            hash.Add(Entries[i]);
        }

        return hash.ToHashCode();
    }
}
