using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

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
        JsonObject obj = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        string graphId = GraphOpsNodeIds.GraphId(opName);
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
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
        IGraphSymbolResolver resolver = symbolResolver ?? GraphOpsNodeGallerySymbolResolver.CreateStandalone();
        GraphProgramSymbolPatcher.Patch(
            package.Symbols,
            package.Program,
            resolver,
            collections);

        GraphKindOperationPolicy.RequireAllowed(kind, compiled.Program, GasGraphOpHandlerTable.Instance);

        GraphKindOperationPolicy.RequireAllowed(kind, compiled.Program, GasGraphOpHandlerTable.Instance);
        _ = RequireFeaturedDest(compiled, vignette);
        return compiled;
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
}
