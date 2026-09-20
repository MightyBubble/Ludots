using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Registry;

/// <summary>
/// Ability classification identity (abilities.json <c>categories</c>).
/// Semantic routing / aggregation only — not an entity gameplay tag.
/// Ids stay in [1, 255] so category sets can reuse the fixed 256-bit mask layout.
/// </summary>
public static class AbilityCategoryRegistry
{
    public const int InvalidId = 0;
    public const int MaxCategories = 256;

    private static IdentityTable Table => ModRegistryAmbient.Current.AbilityCategories;

    public static bool IsFrozen => Table.IsFrozen;

    public static void Freeze() => Table.Freeze();

    public static void Clear() => ModRegistryAmbient.Current.ReplaceAbilityCategories();

    public static int Register(string name) => Table.Register(name);

    public static int GetId(string name) => Table.GetId(name);

    public static string GetName(int id) => Table.GetName(id);

    public static int Count => Table.Count;

    public static bool ContainsId(int id) => Table.ContainsId(id);

    public static RegistryMapping[] SnapshotMappings() => Table.SnapshotMappings();
}
