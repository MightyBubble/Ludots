using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Presentation.Events;

/// <summary>
/// Opaque string→int keys for presentation event filtering (aim / path / overlay / hud / spline / world facts).
/// Not gameplay tags — must not share <see cref="Ludots.Core.Gameplay.GAS.Registry.TagRegistry"/>.
/// </summary>
public static class PresentationEventKeyRegistry
{
    public const int InvalidId = 0;

    private static StringIntRegistry _ids = CreateRegistry();

    public static int Register(string name) => _ids.Register(RequireCanonical(name));

    public static int GetId(string name)
        => string.IsNullOrWhiteSpace(name) ? InvalidId : _ids.GetId(name.Trim());

    public static string GetName(int id) => _ids.GetName(id);

    public static int Count => _ids.Count;

    public static void Clear() => _ids = CreateRegistry();

    private static StringIntRegistry CreateRegistry()
        => new(capacity: 4096, startId: 1, invalidId: InvalidId, comparer: StringComparer.Ordinal);

    private static string RequireCanonical(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Presentation event key must not be null or whitespace.", nameof(name));
        }

        string trimmed = name.Trim();
        if (!string.Equals(name, trimmed, StringComparison.Ordinal))
        {
            throw new ArgumentException("Presentation event key must not include leading or trailing whitespace.", nameof(name));
        }

        return name;
    }
}
