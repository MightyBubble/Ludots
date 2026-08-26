using System.IO;
using System.Text.Json;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

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
    private readonly GraphLookupTableRegistry? _lookupTables;
    private readonly PresentationTextCatalog? _presentationTextCatalog;
    private readonly Ludots.Core.Gameplay.Rng.RngPickService? _rngPicks;

    public GraphOpsNodeGallerySymbolResolver(
        EntityTemplateKeyRegistry templates,
        RelationshipTypeRegistry types,
        RelationshipMetricRegistry metrics,
        RelationshipFlagRegistry flags,
        RelationshipReasonRegistry reasons,
        TargetDispatchPresetRegistry dispatchPresets,
        GraphLookupTableRegistry? lookupTables = null,
        Ludots.Core.Gameplay.Rng.RngPickService? rngPicks = null,
        PresentationTextCatalog? presentationTextCatalog = null)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _types = types ?? throw new ArgumentNullException(nameof(types));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _reasons = reasons ?? throw new ArgumentNullException(nameof(reasons));
        _dispatchPresets = dispatchPresets ?? throw new ArgumentNullException(nameof(dispatchPresets));
        _lookupTables = lookupTables;
        _rngPicks = rngPicks;
        _presentationTextCatalog = presentationTextCatalog;
    }

    public int ResolveRngDistribution(string name)
    {
        if (_rngPicks == null)
        {
            throw new InvalidOperationException(
                "GAS.GRAPH.ERR.RngDistributionUnavailable");
        }

        return _rngPicks.ResolveDistributionKey(name);
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
        RegisterAuthoredCompileSymbols(assetsRoot);
        return new GraphOpsNodeGallerySymbolResolver(
            templates,
            types,
            metrics,
            flags,
            reasons,
            presets,
            LoadLookupTables(Path.Combine(assetsRoot, "GraphTables")),
            LoadDistributionPicks(assetsRoot),
            LoadPresentationTextCatalog(assetsRoot));
    }

    private static Ludots.Core.Gameplay.Rng.RngPickService? LoadDistributionPicks(string assetsRoot)
    {
        string path = Path.Combine(assetsRoot, "Rng", "distributions.json");
        if (!File.Exists(path))
        {
            return null;
        }

        var configs = System.Text.Json.JsonSerializer.Deserialize<Ludots.Core.Gameplay.Rng.DistributionConfig[]>(
            File.ReadAllText(path),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (configs == null || configs.Length == 0)
        {
            return null;
        }

        var tables = new List<Ludots.Core.Gameplay.Rng.DistributionTable>(configs.Length);
        foreach (var config in configs)
        {
            tables.Add(new Ludots.Core.Gameplay.Rng.DistributionTable(config.Id, config.Stream, config.Entries));
        }

        return new Ludots.Core.Gameplay.Rng.RngPickService(
            new Ludots.Core.Engine.Randomization.RngStreamService(),
            tables);
    }

    public int ResolveGraphLookupTable(string name)
    {
        if (_lookupTables == null)
        {
            throw new InvalidOperationException(
                $"Graph references lookup table '{name}', but gallery assets ship no GraphTables.");
        }

        return _lookupTables.GetTableId(name);
    }

    public int ResolveGraphLookupField(string name)
    {
        if (_lookupTables == null)
        {
            throw new InvalidOperationException(
                $"Graph references lookup field '{name}', but gallery assets ship no GraphTables.");
        }

        int separator = name.IndexOf('/');
        if (separator <= 0 || separator >= name.Length - 1)
        {
            throw new InvalidOperationException(
                $"Graph lookup field symbol '{name}' must be encoded as '<tableId>/<fieldId>'.");
        }

        return _lookupTables.GetFieldId(name[..separator], name[(separator + 1)..]);
    }

    internal static GraphLookupTableRegistry? LoadLookupTables(string graphTablesDir)
    {
        string tablePath = Path.Combine(graphTablesDir, "lookup_tables.json");
        if (!File.Exists(tablePath))
        {
            return null;
        }

        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", Path.GetDirectoryName(graphTablesDir.TrimEnd(Path.DirectorySeparatorChar)) ?? ".");
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        var pipeline = new ConfigPipeline(vfs, modLoader);
        var catalog = new ConfigCatalog();
        catalog.Add(new ConfigCatalogEntry(
            GraphLookupTableLoader.ConfigPath,
            ConfigMergePolicy.ArrayById,
            "id",
            allowEmpty: true));
        return new GraphLookupTableLoader(pipeline).Load(catalog);
    }

    internal static PresentationTextCatalog? LoadPresentationTextCatalog(string assetsRoot)
    {
        string tokensPath = Path.Combine(assetsRoot, "Presentation", "text_tokens.json");
        if (!File.Exists(tokensPath))
        {
            return null;
        }

        var vfs = new VirtualFileSystem();
        vfs.Mount("Gallery", assetsRoot);
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        modLoader.LoadedModIds.Add("Gallery");
        var pipeline = new ConfigPipeline(vfs, modLoader);
        var catalog = new ConfigCatalog();
        catalog.Add(new ConfigCatalogEntry(
            "Presentation/text_tokens.json",
            ConfigMergePolicy.ArrayById,
            "id",
            allowEmpty: true));
        catalog.Add(new ConfigCatalogEntry(
            "Presentation/text_locales.json",
            ConfigMergePolicy.DeepObject,
            allowEmpty: true));
        return new PresentationTextCatalogLoader(pipeline).Load(catalog);
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

    public int ResolveTextToken(string name)
    {
        if (_presentationTextCatalog == null)
        {
            throw new InvalidOperationException(
                $"Graph references text token '{name}', but gallery assets ship no Presentation/text_tokens.json.");
        }

        int id = _presentationTextCatalog.GetTokenId(name);
        if (id <= 0)
        {
            throw new InvalidOperationException(
                $"Graph references unknown text token '{name}'. Register Presentation/text_tokens.json before compiling gallery graphs.");
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
