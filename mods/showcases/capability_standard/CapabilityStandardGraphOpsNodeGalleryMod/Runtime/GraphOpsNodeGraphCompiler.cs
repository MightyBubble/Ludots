using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime;

public static class GraphOpsNodeGraphCompiler
{
    public static GraphControlFlowCompileResult Compile(
        string assetsRoot,
        GraphOpsNodeVignette vignette,
        IGraphSymbolResolver? symbolResolver = null,
        EntityCollectionStore? collections = null)
    {
        string opName = GraphOpsNodeIds.RequireOpName(vignette.Op);
        string path = Path.Combine(assetsRoot, "GAS", "graphs", opName + ".json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Missing per-op FrontDoor graph for {opName}. Each GraphNodeOp must have assets/GAS/graphs/{opName}.json.",
                path);
        }

        if (!GraphKindParser.TryParse(vignette.GraphKind, out GraphKind kind))
        {
            throw new InvalidOperationException(
                $"Vignette for {opName} has unsupported graphKind '{vignette.GraphKind}'.");
        }

        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = ParseSingleGraphShard(path);
        string graphId = GraphOpsNodeIds.GraphId(opName);
        // Builtin event schemas cover the DispatchMapEvent vignette (MapHeartbeat);
        // every other op never consults the registry.
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(
            obj,
            graphId,
            options,
            new Ludots.Core.Scripting.EventSchemaRegistry());
        if (!compiled.Succeeded)
        {
            string message = string.Join("; ", compiled.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"FrontDoor compile failed for '{graphId}': {message}");
        }

        if (!compiled.Package.HasValue)
        {
            throw new InvalidOperationException($"FrontDoor compile for '{graphId}' produced no package.");
        }

        GraphProgramPackage package = compiled.Package.Value;
        IGraphSymbolResolver resolver = symbolResolver ?? GraphOpsNodeGallerySymbolResolver.CreateStandalone(assetsRoot);
        var builtinHandlers = new Ludots.Core.Gameplay.GAS.BuiltinHandlerRegistry();
        Ludots.Core.Gameplay.GAS.BuiltinHandlers.RegisterAll(builtinHandlers);
        GraphProgramSymbolPatcher.Patch(
            package.Symbols,
            package.Program,
            resolver,
            collections ?? CreateCompileTimeCollections(),
            builtinHandlers);

        GraphKindOperationPolicy.RequireAllowed(kind, compiled.Program, GasGraphOpHandlerTable.Instance);
        _ = RequireFeaturedDest(compiled, vignette);
        return compiled;
    }

    /// <summary>
    /// GAS/graphs shard files are ArrayById fragments: each file is a one-element array
    /// so the ConfigPipeline shard merge accepts it. Direct loaders unwrap the single graph.
    /// </summary>
    public static JsonObject ParseSingleGraphShard(string path)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"Graph shard is null JSON: {path}");
        if (root is not JsonArray array || array.Count != 1 || array[0] is not JsonObject graph)
        {
            throw new InvalidOperationException(
                $"Graph shard {path} must be a JSON array with exactly one graph object (ArrayById shard contract).");
        }

        return graph;
    }

    public static byte RequireFeaturedDest(GraphControlFlowCompileResult compiled, GraphOpsNodeVignette vignette)
    {
        if (!GraphNodeOpParser.TryParse(vignette.Op, out GraphNodeOp featuredOp))
        {
            throw new InvalidOperationException($"Unknown featured op '{vignette.Op}'.");
        }

        GraphInstruction[] program = compiled.Program;
        GraphInstructionSourceMap map = compiled.SourceMap;
        for (int i = 0; i < program.Length; i++)
        {
            if (!map.TryGetSource(i, out GraphInstructionSource source) ||
                !string.Equals(source.NodeId, vignette.FeaturedNodeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (program[i].Op == (ushort)featuredOp)
            {
                return program[i].Dst;
            }
        }

        throw new InvalidOperationException(
            $"Compiled graph for {vignette.Op} is missing featured node '{vignette.FeaturedNodeId}' emitting {featuredOp}.");
    }

    private static EntityCollectionStore CreateCompileTimeCollections()
    {
        var keys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        var store = new EntityCollectionStore(keys);
        _ = store.KeyRegistry.Register(GraphOpsNodeGalleryHost.SquadCollectionKey);
        _ = store.KeyRegistry.Register(GraphOpsNodeGalleryHost.SnapCollectionKey);
        return store;
    }
}
