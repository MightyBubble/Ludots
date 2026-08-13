using System.IO;
using System.Text.Json;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
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

    public static GraphOpsNodeGallerySymbolResolver CreateStandalone(string assetsRoot)
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
        var tagDisplay = new TagDisplayTableRegistry();
        BindSandboxDisplayTable(tagDisplay, assetsRoot);
        RegisterAuthoredCompileSymbols(assetsRoot);
        return new GraphOpsNodeGallerySymbolResolver(templates, types, metrics, flags, reasons, presets, tagDisplay);
    }

    internal static void BindSandboxDisplayTable(TagDisplayTableRegistry tagDisplay, string assetsRoot)
    {
        string path = Path.Combine(assetsRoot, "GAS", "sandbox", "catalog.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Gallery requires assets/GAS/sandbox/catalog.json.", path);
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        GraphOpsNodeGallerySandboxCatalog? catalog = JsonSerializer.Deserialize<GraphOpsNodeGallerySandboxCatalog>(
            File.ReadAllText(path),
            options);
        if (catalog == null || string.IsNullOrWhiteSpace(catalog.DisplayTable))
        {
            throw new InvalidOperationException($"Sandbox catalog '{path}' deserialized to null.");
        }

        if (tagDisplay.TryGetTableId(catalog.DisplayTable, out _))
        {
            return;
        }

        if (tagDisplay.IsFrozen)
        {
            throw new InvalidOperationException(
                $"Tag display registry is frozen without table '{catalog.DisplayTable}'.");
        }

        if (string.IsNullOrWhiteSpace(catalog.BurningTag) ||
            string.IsNullOrWhiteSpace(catalog.MarkedTag) ||
            catalog.BurningTokenId <= 0 ||
            catalog.MarkedTokenId <= 0)
        {
            throw new InvalidOperationException($"Sandbox catalog '{path}' is missing display table fields.");
        }

        int burning = TagRegistry.Register(catalog.BurningTag);
        int marked = TagRegistry.Register(catalog.MarkedTag);
        var mask = new GameplayTagContainer();
        mask.AddTag(burning);
        mask.AddTag(marked);
        tagDisplay.RegisterTable(
            catalog.DisplayTable,
            in mask,
            new (int, int)[]
            {
                (burning, catalog.BurningTokenId),
                (marked, catalog.MarkedTokenId)
            });
    }

    public int ResolveTag(string name)
    {
        int id = TagRegistry.GetId(name);
        if (id <= 0)
        {
            throw new InvalidOperationException(
                $"Graph references unknown tag '{name}'. Register tags before compiling gallery graphs.");
        }

        return id;
    }

    public int ResolveAttribute(string name)
    {
        int id = AttributeRegistry.GetId(name);
        if (id < 0)
        {
            throw new InvalidOperationException(
                $"Graph references unknown attribute '{name}'. Register attributes before compiling gallery graphs.");
        }

        return id;
    }

    public int ResolveEffectTemplate(string name)
    {
        int id = EffectTemplateIdRegistry.GetId(name);
        if (id <= 0)
        {
            throw new InvalidOperationException($"Graph references unknown effect template '{name}'.");
        }

        return id;
    }

    public int ResolveRelationshipType(string name) => _types.GetId(name);
    public int ResolveRelationshipMetric(string name) => _metrics.GetId(name);
    public int ResolveRelationshipFlag(string name) => _flags.GetId(name);

    public int ResolveRelationshipReason(string name)
    {
        if (!_reasons.TryGetId(name, out int id) || id <= 0)
        {
            throw new InvalidOperationException($"Graph references unknown relationship reason '{name}'.");
        }

        return id;
    }

    public int ResolveTargetDispatchPreset(string name) => _dispatchPresets.GetId(name);

    public int ResolveEntityTemplate(string name)
    {
        if (!_templates.TryGetId(name, out int id) || id <= 0)
        {
            throw new InvalidOperationException(
                $"Graph references unknown entity template '{name}'. Register templates before compiling gallery graphs.");
        }

        return id;
    }

    public int ResolveTagDisplayTable(string name)
    {
        if (_tagDisplay == null)
        {
            throw new InvalidOperationException(
                $"Graph references tag display table '{name}', but no TagDisplayTableRegistry is bound.");
        }

        return _tagDisplay.GetTableId(name);
    }

    internal static void RegisterAuthoredCompileSymbols(string assetsRoot)
    {
        _ = AttributeRegistry.Register("Health");
        RegisterTagRules(Path.Combine(assetsRoot, "GAS", "tag_rules.json"));
        RegisterEffectIds(Path.Combine(assetsRoot, "GAS", "effects.json"));
    }

    private static void RegisterTagRules(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Gallery requires assets/GAS/tag_rules.json.", path);
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Tag rules '{path}' must be a JSON array.");
        }

        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            string? id = entry.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"Tag rules '{path}' contains an entry without id.");
            }

            _ = TagRegistry.Register(id);
        }
    }

    private static void RegisterEffectIds(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Gallery requires assets/GAS/effects.json.", path);
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Effects '{path}' must be a JSON array.");
        }

        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            string? id = entry.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"Effects '{path}' contains an entry without id.");
            }

            _ = EffectTemplateIdRegistry.Register(id);
        }
    }
}
