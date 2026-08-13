using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.TagDisplay;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

internal sealed class GraphOpsNodeGallerySymbolResolver : IGraphSymbolResolver
{
    internal const string SquadCollectionKey = GraphOpsNodeGalleryHost.SquadCollectionKey;
    internal const string SnapCollectionKey = GraphOpsNodeGalleryHost.SnapCollectionKey;
    internal const string TargetToResolvedPreset = GraphOpsNodeGalleryHost.TargetToResolvedPreset;

    private readonly EntityTemplateKeyRegistry _templates;
    private readonly RelationshipTypeRegistry _types;
    private readonly RelationshipMetricRegistry _metrics;
    private readonly RelationshipFlagRegistry _flags;
    private readonly RelationshipReasonRegistry _reasons;
    private readonly TargetDispatchPresetRegistry _dispatchPresets;
    private readonly TagDisplayTableRegistry? _tagDisplay;

    public GraphOpsNodeGallerySymbolResolver(
        EntityTemplateKeyRegistry templates,
        RelationshipTypeRegistry types,
        RelationshipMetricRegistry metrics,
        RelationshipFlagRegistry flags,
        RelationshipReasonRegistry reasons,
        TargetDispatchPresetRegistry dispatchPresets,
        TagDisplayTableRegistry? tagDisplay = null)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _reasons = reasons ?? throw new ArgumentNullException(nameof(reasons));
        _dispatchPresets = dispatchPresets ?? throw new ArgumentNullException(nameof(dispatchPresets));
        _tagDisplay = tagDisplay;
    }

    public static GraphOpsNodeGallerySymbolResolver CreateStandalone()
    {
        var templates = new EntityTemplateKeyRegistry();
        _ = templates.Register(GraphOpsVisualTemplates.Soldier);
        _ = templates.Register(GraphOpsVisualTemplates.Scout);
        _ = templates.Register(GraphOpsVisualTemplates.Caster);
        _ = templates.Register(GraphOpsVisualTemplates.Ally);
        _ = templates.Register(GraphOpsVisualTemplates.Target);
        var types = new RelationshipTypeRegistry();
        types.Register("SocialBond");
        types.Register("Owns");
        types.Register("Controls");
        types.Register("MemberOf");
        var metrics = new RelationshipMetricRegistry();
        metrics.Register("Loyalty", -100, 100, 0);
        var flags = new RelationshipFlagRegistry();
        flags.Register("Trusted");
        flags.Register("Estranged");
        var reasons = new RelationshipReasonRegistry();
        reasons.Register("Scenario.Setup");
        var presets = new TargetDispatchPresetRegistry();
        presets.Register(
            TargetToResolvedPreset,
            new TargetResolverContextMapping
            {
                PayloadSource = ContextSlot.OriginalTarget,
                PayloadTarget = ContextSlot.ResolvedEntity,
                PayloadTargetContext = ContextSlot.OriginalSource
            });
        return new GraphOpsNodeGallerySymbolResolver(templates, types, metrics, flags, reasons, presets);
    }

    public int ResolveTag(string name) => TagRegistry.Register(name);
    public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
    public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
    public int ResolveRelationshipType(string name) => _types.Register(name);
    public int ResolveRelationshipMetric(string name) => _metrics.Register(name, -100, 100, 0);
    public int ResolveRelationshipFlag(string name) => _flags.Register(name);
    public int ResolveRelationshipReason(string name) => _reasons.Register(name);
    public int ResolveTargetDispatchPreset(string name) => _dispatchPresets.GetId(name);
    public int ResolveEntityTemplate(string name) => _templates.Register(name);

    public int ResolveTagDisplayTable(string name)
    {
        if (_tagDisplay == null)
        {
            throw new InvalidOperationException(
                $"Graph references tag display table '{name}', but no TagDisplayTableRegistry is bound.");
        }

        return _tagDisplay.GetTableId(name);
    }
}
