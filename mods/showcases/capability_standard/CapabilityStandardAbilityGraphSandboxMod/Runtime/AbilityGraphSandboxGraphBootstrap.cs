using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.TagDisplay;
using Ludots.Core.Registry;

namespace CapabilityStandardAbilityGraphSandboxMod.Runtime;

public static class AbilityGraphSandboxGraphKeys
{
    public const string Scout = "Graph.AbilityGraphSandbox.Scout";
    public const string Apply = "Graph.AbilityGraphSandbox.Apply";
    public const string Bond = "Graph.AbilityGraphSandbox.Bond";
    public const string MarkEffect = "Effect.AbilityGraphSandbox.Mark";
    public const string BuffEffect = "Effect.AbilityGraphSandbox.Buff";
    public const string InspiredTag = "State.Sandbox.Inspired";
    public const string MarkedTag = "State.Sandbox.Marked";
    public const string StatusDisplayTable = "sandbox.status.display";
    public const string NearbyCountKey = "sandbox.scout.nearbyCount";
    public const string NearestKey = "sandbox.scout.nearest";
    public const string StatusTokenKey = "sandbox.scout.statusToken";
    public const string BuffTemplateKey = "sandbox.apply.buffTemplate";
    public const string LoyaltyKey = "sandbox.bond.loyalty";
    public const int InspiredTokenId = 1;
    public const int MarkedTokenId = 2;
    public const int QueryLimit = 5;
}

public sealed class AbilityGraphSandboxBundle
{
    public required GraphProgramRegistry Programs { get; init; }
    public required TagDisplayTableRegistry TagDisplay { get; init; }
    public required RelationshipTypeRegistry Types { get; init; }
    public required RelationshipMetricRegistry Metrics { get; init; }
    public required RelationshipFlagRegistry Flags { get; init; }
    public required RelationshipReasonRegistry Reasons { get; init; }
    public int InspiredTagId { get; init; }
    public int MarkedTagId { get; init; }
    public int SocialBondTypeId { get; init; }
    public int LoyaltyMetricId { get; init; }
    public int TrustedFlagId { get; init; }
    public int MarkTemplateId { get; init; }
    public int BuffTemplateId { get; init; }
}

internal sealed class AbilityGraphSandboxSymbolResolver : IGraphSymbolResolver
{
    private readonly RelationshipTypeRegistry _types;
    private readonly RelationshipMetricRegistry _metrics;
    private readonly RelationshipFlagRegistry _flags;
    private readonly RelationshipReasonRegistry _reasons;
    private readonly TagDisplayTableRegistry _tables;

    public AbilityGraphSandboxSymbolResolver(
        RelationshipTypeRegistry types,
        RelationshipMetricRegistry metrics,
        RelationshipFlagRegistry flags,
        RelationshipReasonRegistry reasons,
        TagDisplayTableRegistry tables)
    {
        _types = types;
        _metrics = metrics;
        _flags = flags;
        _reasons = reasons;
        _tables = tables;
    }

    public int ResolveTag(string name) => TagRegistry.Register(name);
    public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
    public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
    public int ResolveRelationshipType(string name) => _types.Register(name);
    public int ResolveRelationshipMetric(string name) => _metrics.Register(name, -100, 100, 0);
    public int ResolveRelationshipFlag(string name) => _flags.Register(name);
    public int ResolveRelationshipReason(string name) => _reasons.Register(name);
    public int ResolveTargetDispatchPreset(string name) => ConfigKeyRegistry.Register($"targetDispatch.{name}");
    public int ResolveEntityTemplate(string name) => ConfigKeyRegistry.Register($"entityTemplate.{name}");
    public int ResolveTagDisplayTable(string name) => _tables.GetTableId(name);
}

public static class AbilityGraphSandboxGraphBootstrap
{
    public static AbilityGraphSandboxBundle LoadModGraphs(string modAssetsRoot)
    {
        if (string.IsNullOrWhiteSpace(modAssetsRoot))
        {
            throw new ArgumentException("Mod assets root is required.", nameof(modAssetsRoot));
        }

        // Not assets/GAS/graphs.json: ConfigPipeline merges that path with GetId-only
        // GasGraphSymbolResolver, which cannot Register sandbox tags/display tables.
        string graphsPath = Path.Combine(modAssetsRoot, "GAS", "sandbox_graphs.json");
        if (!File.Exists(graphsPath))
        {
            throw new FileNotFoundException($"Missing ability graph sandbox graphs: {graphsPath}");
        }

        int inspiredTagId = TagRegistry.Register(AbilityGraphSandboxGraphKeys.InspiredTag);
        int markedTagId = TagRegistry.Register(AbilityGraphSandboxGraphKeys.MarkedTag);
        var mask = new GameplayTagContainer();
        mask.AddTag(inspiredTagId);
        mask.AddTag(markedTagId);
        var tables = new TagDisplayTableRegistry();
        tables.RegisterTable(
            AbilityGraphSandboxGraphKeys.StatusDisplayTable,
            in mask,
            new (int, int)[]
            {
                (inspiredTagId, AbilityGraphSandboxGraphKeys.InspiredTokenId),
                (markedTagId, AbilityGraphSandboxGraphKeys.MarkedTokenId)
            });
        tables.Freeze();

        int markTemplateId = EffectTemplateIdRegistry.Register(AbilityGraphSandboxGraphKeys.MarkEffect);
        int buffTemplateId = EffectTemplateIdRegistry.Register(AbilityGraphSandboxGraphKeys.BuffEffect);

        var types = new RelationshipTypeRegistry();
        var metrics = new RelationshipMetricRegistry();
        var flags = new RelationshipFlagRegistry();
        var reasons = new RelationshipReasonRegistry();
        int socialBondTypeId = types.Register("SocialBond");
        int loyaltyMetricId = metrics.Register("Loyalty", -100, 100, 0);
        int trustedFlagId = flags.Register("Trusted");
        reasons.Register("Scenario.Setup");

        var programs = new GraphProgramRegistry();
        var resolver = new AbilityGraphSandboxSymbolResolver(types, metrics, flags, reasons, tables);
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(graphsPath));

        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string id = element.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Graph entry missing id.");
            JsonObject obj = JsonNode.Parse(element.GetRawText())!.AsObject();
            GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, id, options);
            foreach (GraphDiagnostic diagnostic in compiled.Diagnostics)
            {
                if (diagnostic.Severity == GraphDiagnosticSeverity.Error)
                {
                    throw new InvalidOperationException($"Compile {id}: {diagnostic.Message}");
                }
            }

            if (!compiled.Succeeded || !compiled.Package.HasValue)
            {
                throw new InvalidOperationException($"Compile {id} produced no package.");
            }

            string kindText = element.GetProperty("kind").GetString() ?? "Effect";
            if (!GraphKindParser.TryParse(kindText, out GraphKind kind))
            {
                throw new InvalidOperationException($"Graph '{id}' has invalid kind '{kindText}'.");
            }

            GraphProgramPackage package = compiled.Package.Value;
            GraphKindOperationPolicy.RequireAllowed(
                kind,
                package.Program,
                GasGraphOpHandlerTable.Instance,
                entrypoint: nameof(AbilityGraphSandboxGraphBootstrap));
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver);
            int graphId = GraphIdRegistry.Register(id);
            programs.Register(graphId, package.Program, kind, GraphInstructionSourceMap.Empty, package.Symbols);
        }

        return new AbilityGraphSandboxBundle
        {
            Programs = programs,
            TagDisplay = tables,
            Types = types,
            Metrics = metrics,
            Flags = flags,
            Reasons = reasons,
            InspiredTagId = inspiredTagId,
            MarkedTagId = markedTagId,
            SocialBondTypeId = socialBondTypeId,
            LoyaltyMetricId = loyaltyMetricId,
            TrustedFlagId = trustedFlagId,
            MarkTemplateId = markTemplateId,
            BuffTemplateId = buffTemplateId
        };
    }

    public static string FindModAssetsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "mods",
                "showcases",
                "capability_standard",
                "CapabilityStandardAbilityGraphSandboxMod",
                "assets");
            if (File.Exists(Path.Combine(candidate, "GAS", "sandbox_graphs.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("CapabilityStandardAbilityGraphSandboxMod assets root not found.");
    }
}
