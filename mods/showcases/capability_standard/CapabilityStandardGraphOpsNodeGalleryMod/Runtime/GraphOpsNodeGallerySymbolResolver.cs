using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

internal sealed class GraphOpsNodeGallerySymbolResolver : IGraphSymbolResolver
{
    internal const string SquadCollectionKey = "squad.members";
    internal const string SnapCollectionKey = "showcase.graph_op.snap";
    internal const string TargetToResolvedPreset = "TargetToResolved";

    internal static readonly EntityTemplateKeyRegistry SimTemplates = CreateSimTemplates();
    internal static readonly EntityCollectionStore Collections = CreateCollections();
    internal static readonly TargetDispatchPresetRegistry DispatchPresets = CreateDispatchPresets();

    public int ResolveTag(string name) => TagRegistry.Register(name);
    public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
    public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
    public int ResolveRelationshipType(string name) => ConfigKeyRegistry.Register($"relationship.type.{name}");
    public int ResolveRelationshipMetric(string name) => ConfigKeyRegistry.Register($"relationship.metric.{name}");
    public int ResolveRelationshipFlag(string name) => ConfigKeyRegistry.Register($"relationship.flag.{name}");
    public int ResolveRelationshipReason(string name) => ConfigKeyRegistry.Register($"relationship.reason.{name}");
    public int ResolveTargetDispatchPreset(string name) => DispatchPresets.GetId(name);
    public int ResolveEntityTemplate(string name) => SimTemplates.Register(name);
    public int ResolveTagDisplayTable(string name) => ConfigKeyRegistry.Register($"tagDisplay.{name}");

    private static EntityTemplateKeyRegistry CreateSimTemplates()
    {
        var templates = new EntityTemplateKeyRegistry();
        _ = templates.Register(GraphOpsVisualTemplates.Soldier);
        _ = templates.Register(GraphOpsVisualTemplates.Scout);
        return templates;
    }

    private static EntityCollectionStore CreateCollections()
    {
        var collections = new EntityCollectionStore(
            new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
        _ = collections.KeyRegistry.Register(SquadCollectionKey);
        _ = collections.KeyRegistry.Register(SnapCollectionKey);
        return collections;
    }

    private static TargetDispatchPresetRegistry CreateDispatchPresets()
    {
        var presets = new TargetDispatchPresetRegistry();
        presets.Register(
            TargetToResolvedPreset,
            new TargetResolverContextMapping
            {
                PayloadSource = ContextSlot.OriginalTarget,
                PayloadTarget = ContextSlot.ResolvedEntity,
                PayloadTargetContext = ContextSlot.OriginalSource
            });
        return presets;
    }
}
