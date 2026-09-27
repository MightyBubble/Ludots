using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Registry;

public static class TagRegistry
{
    public const int InvalidId = 0;
    /// <summary>内嵌镜像宽度（GameplayTagContainer 位图 4×64）。位 [256, <see cref="MaxTagIds"/>) 只存在于世界列存（RFC-0067 P2）。</summary>
    public const int MaxTags = 256;

    /// <summary>可登记标签种类上限 = 容量计划绝对天花板（RFC-0067 §3.1）。</summary>
    public const int MaxTagIds = 4096;

    private static IdentityTable Table => ModRegistryAmbient.Current.Tags;

    public static bool IsFrozen => Table.IsFrozen;

    public static void Freeze() => Table.Freeze();

    public static int Count => Table.Count;

    public static void Clear() => ModRegistryAmbient.Current.ReplaceTags();

    public static int Register(string name) => Table.Register(name);

    public static int GetId(string name) => Table.GetId(name);

    public static string GetName(int id) => Table.GetName(id);

    public static RegistryMapping[] SnapshotMappings() => Table.SnapshotMappings();
}
