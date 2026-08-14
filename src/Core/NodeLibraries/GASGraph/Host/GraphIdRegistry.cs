using Ludots.Core.Registry;

namespace Ludots.Core.NodeLibraries.GASGraph.Host;

public static class GraphIdRegistry
{
    public const int InvalidId = 0;
    public const int MaxGraphs = 4095;

    private static IdentityTable Table => ModRegistryAmbient.Current.GraphIds;

    public static bool IsFrozen => Table.IsFrozen;

    public static void Freeze() => Table.Freeze();

    public static void Clear() => ModRegistryAmbient.Current.ReplaceGraphIds();

    public static int Register(string name) => Table.Register(name);

    public static int GetId(string name) => Table.GetId(name);

    public static string GetName(int id) => Table.GetName(id);

    public static RegistryMapping[] SnapshotMappings() => Table.SnapshotMappings();
}
