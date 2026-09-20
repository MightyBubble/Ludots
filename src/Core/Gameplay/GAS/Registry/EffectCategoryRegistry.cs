using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Registry;

/// <summary>
/// Effect classification identity (effects.json <c>categories</c>).
/// Matching key for response chain / phase listeners — not an entity gameplay tag.
/// </summary>
public static class EffectCategoryRegistry
{
    public const int InvalidId = 0;
    public const int MaxCategories = 4095;

    private static IdentityTable Table => ModRegistryAmbient.Current.EffectCategories;

    public static bool IsFrozen => Table.IsFrozen;

    public static void Freeze() => Table.Freeze();

    public static void Clear() => ModRegistryAmbient.Current.ReplaceEffectCategories();

    public static int Register(string name) => Table.Register(name);

    public static int GetId(string name) => Table.GetId(name);

    public static string GetName(int id) => Table.GetName(id);

    public static int Count => Table.Count;

    public static RegistryMapping[] SnapshotMappings() => Table.SnapshotMappings();
}
