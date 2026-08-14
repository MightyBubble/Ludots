using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;

namespace CapabilityStandardGraphOpsQueryMod.Runtime;

internal sealed class GraphOpsQueryShowcaseBundle
{
    public required GraphProgramRegistry Programs { get; init; }
    public required EntityTemplateKeyRegistry Templates { get; init; }
    public required EntityCollectionStore Collections { get; init; }
    public required int SoldierTemplateId { get; init; }
    public required int ScoutTemplateId { get; init; }
    public required int HealthAttrId { get; init; }
    public required int EnemyTagId { get; init; }
    public required int DeadTagId { get; init; }
}

internal static class GraphOpsQueryCatalogBootstrap
{
    public const string FilterPipelineGraph = "Graph.GraphOpsQuery.FilterPipeline";
    public const string FromCollectionGraph = "Graph.GraphOpsQuery.FromCollection";
    public const string SquadCollectionKey = "squad.members";
    public const string SoldierTemplate = "Unit.Soldier";
    public const string ScoutTemplate = "Unit.Scout";

    private const string ModAssetsRelative =
        "mods/showcases/capability_standard/CapabilityStandardGraphOpsQueryMod/assets";

    public static GraphOpsQueryShowcaseBundle LoadStandalone()
    {
        var programs = new GraphProgramRegistry();
        var templates = new EntityTemplateKeyRegistry();
        int soldierId = templates.Register(SoldierTemplate);
        int scoutId = templates.Register(ScoutTemplate);
        int healthId = AttributeRegistry.GetId("Health");
        if (healthId < 0)
        {
            healthId = GraphOpsMutableRegistry.Attribute("Health");
        }

        int enemyTagId = TagRegistry.GetId("Enemy");
        if (enemyTagId <= 0)
        {
            enemyTagId = GraphOpsMutableRegistry.Tag("Enemy");
        }

        int deadTagId = TagRegistry.GetId("Dead");
        if (deadTagId <= 0)
        {
            deadTagId = GraphOpsMutableRegistry.Tag("Dead");
        }

        var collections = new EntityCollectionStore(
            new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
        _ = collections.KeyRegistry.Register(SquadCollectionKey);

        var types = new RelationshipTypeRegistry();
        var metrics = new RelationshipMetricRegistry();
        var flags = new RelationshipFlagRegistry();
        var reasons = new RelationshipReasonRegistry();
        var resolver = new GasGraphSymbolResolver(
            types,
            metrics,
            flags,
            reasons,
            new TargetDispatchPresetRegistry(),
            templates);

        string graphsPath = Path.Combine(FindRepoRoot(), ModAssetsRelative, "GAS", "graphs.json");
        LoadGraphs(programs, graphsPath, resolver, collections);
        RequireGraph(programs, FilterPipelineGraph);
        RequireGraph(programs, FromCollectionGraph);

        return new GraphOpsQueryShowcaseBundle
        {
            Programs = programs,
            Templates = templates,
            Collections = collections,
            SoldierTemplateId = soldierId,
            ScoutTemplateId = scoutId,
            HealthAttrId = healthId,
            EnemyTagId = enemyTagId,
            DeadTagId = deadTagId
        };
    }

    private static void LoadGraphs(
        GraphProgramRegistry programs,
        string graphsPath,
        GasGraphSymbolResolver resolver,
        EntityCollectionStore collections)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        using var doc = JsonDocument.Parse(File.ReadAllText(graphsPath));
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            string kind = el.GetProperty("kind").GetString() ?? "";
            string id = el.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("Query gallery graph missing id.");
            var obj = JsonNode.Parse(el.GetRawText())!.AsObject();
            GraphControlFlowCompileResult compiled =
                GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, id, options);
            if (!compiled.Succeeded || !compiled.Package.HasValue)
            {
                string message = string.Join("; ", compiled.Diagnostics.ConvertAll(d => $"{d.Code}:{d.Message}"));
                throw new InvalidOperationException($"Gallery graph '{id}' FrontDoor failed: {message}");
            }

            GraphProgramPackage package = compiled.Package.Value;
            GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, resolver, collections);
            GraphKind graphKind = ParseKind(kind);
            GraphKindOperationPolicy.RequireAllowed(
                graphKind,
                package.Program,
                GasGraphOpHandlerTable.Instance,
                entrypoint: nameof(GraphOpsQueryCatalogBootstrap));
            int graphId = GraphIdRegistry.Register(id);
            programs.Register(graphId, package.Program, graphKind, compiled.SourceMap, package.Symbols);
        }
    }

    private static void RequireGraph(GraphProgramRegistry programs, string graphKey)
    {
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (graphId <= 0 || !programs.TryGetProgram(graphId, out _))
        {
            throw new InvalidOperationException($"Required query gallery graph '{graphKey}' is missing.");
        }
    }

    private static GraphKind ParseKind(string kind)
        => kind switch
        {
            "Query" => GraphKind.Query,
            _ => throw new InvalidOperationException($"Query gallery rejects graph kind '{kind}'.")
        };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Repository root not found for Query GraphOps assets.");
    }
}
