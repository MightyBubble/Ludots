using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsBlackboardMod.Runtime;

internal sealed class GraphOpsBlackboardSymbolResolver : IGraphSymbolResolver
{
    public int ResolveTag(string name) => TagRegistry.Register(name);
    public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
    public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
    public int ResolveRelationshipType(string name) => ConfigKeyRegistry.Register($"relationship.type.{name}");
    public int ResolveRelationshipMetric(string name) => ConfigKeyRegistry.Register($"relationship.metric.{name}");
    public int ResolveRelationshipFlag(string name) => ConfigKeyRegistry.Register($"relationship.flag.{name}");
    public int ResolveRelationshipReason(string name) => ConfigKeyRegistry.Register($"relationship.reason.{name}");
    public int ResolveTargetDispatchPreset(string name) => ConfigKeyRegistry.Register($"targetDispatch.{name}");
    public int ResolveEntityTemplate(string name) => ConfigKeyRegistry.Register($"entityTemplate.{name}");
}

internal static class GraphOpsBlackboardGraphAuthoring
{
    public const string MemoGraphId = "showcase.graph_ops_blackboard.memo";
    public const string LifecycleGraphId = "showcase.graph_ops_blackboard.lifecycle";

    public static GraphControlFlowCompileResult CompileMemoGraph()
        => Compile(MemoGraphJson, MemoGraphId, GraphKind.Effect);

    public static GraphControlFlowCompileResult CompileLifecycleGraph()
        => Compile(LifecycleGraphJson, LifecycleGraphId, GraphKind.Effect);

    private static GraphControlFlowCompileResult Compile(string json, string graphId, GraphKind kind)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = JsonNode.Parse(json)!.AsObject();
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
        GraphProgramSymbolPatcher.Patch(package.Symbols, package.Program, new GraphOpsBlackboardSymbolResolver());

        GraphKindOperationPolicy.RequireAllowed(
            kind,
            compiled.Program,
            GasGraphOpHandlerTable.Instance,
            entrypoint: nameof(GraphOpsBlackboardGraphAuthoring));

        return compiled;
    }

    private const string MemoGraphJson = """
        {
          "kind": "Effect",
          "entry": "loadSrc",
          "nodes": [
            { "id": "loadSrc", "op": "LoadContextSource" },
            { "id": "loadCtx", "op": "LoadContextTargetContext" },
            { "id": "writeSrc", "op": "WriteBlackboardEntity", "blackboardKey": "showcase.bb.memoSource" },
            { "id": "writeCtx", "op": "WriteBlackboardEntity", "blackboardKey": "showcase.bb.memoContext" },
            { "id": "loadCfgFloat", "op": "LoadConfigFloat", "configKey": "showcase.config.power" },
            { "id": "writePower", "op": "WriteBlackboardFloat", "blackboardKey": "showcase.bb.power" },
            { "id": "loadCfgInt", "op": "LoadConfigInt", "configKey": "showcase.config.tier" },
            { "id": "writeTier", "op": "WriteBlackboardInt", "blackboardKey": "showcase.bb.tier" },
            { "id": "loadCfgEffect", "op": "LoadConfigEffectId", "configKey": "showcase.config.chainEffect" },
            { "id": "writeChain", "op": "WriteBlackboardInt", "blackboardKey": "showcase.bb.chainEffect" },
            { "id": "readPower", "op": "ReadBlackboardFloat", "blackboardKey": "showcase.bb.power" },
            { "id": "writePowerEcho", "op": "WriteBlackboardFloat", "blackboardKey": "showcase.bb.powerEcho" },
            { "id": "readTier", "op": "ReadBlackboardInt", "blackboardKey": "showcase.bb.tier" },
            { "id": "writeTierEcho", "op": "WriteBlackboardInt", "blackboardKey": "showcase.bb.tierEcho" },
            { "id": "readSrc", "op": "ReadBlackboardEntity", "blackboardKey": "showcase.bb.memoSource" },
            { "id": "writeSrcEcho", "op": "WriteBlackboardEntity", "blackboardKey": "showcase.bb.sourceEcho" }
          ],
          "controlEdges": [
            { "from": "loadSrc", "fromPort": "next", "to": "loadCtx" },
            { "from": "loadCtx", "fromPort": "next", "to": "writeSrc" },
            { "from": "writeSrc", "fromPort": "next", "to": "writeCtx" },
            { "from": "writeCtx", "fromPort": "next", "to": "loadCfgFloat" },
            { "from": "loadCfgFloat", "fromPort": "next", "to": "writePower" },
            { "from": "writePower", "fromPort": "next", "to": "loadCfgInt" },
            { "from": "loadCfgInt", "fromPort": "next", "to": "writeTier" },
            { "from": "writeTier", "fromPort": "next", "to": "loadCfgEffect" },
            { "from": "loadCfgEffect", "fromPort": "next", "to": "writeChain" },
            { "from": "writeChain", "fromPort": "next", "to": "readPower" },
            { "from": "readPower", "fromPort": "next", "to": "writePowerEcho" },
            { "from": "writePowerEcho", "fromPort": "next", "to": "readTier" },
            { "from": "readTier", "fromPort": "next", "to": "writeTierEcho" },
            { "from": "writeTierEcho", "fromPort": "next", "to": "readSrc" },
            { "from": "readSrc", "fromPort": "next", "to": "writeSrcEcho" }
          ],
          "valueEdges": [
            { "from": "loadSrc", "fromPort": "value", "to": "writeSrc", "toPort": "source" },
            { "from": "loadSrc", "fromPort": "value", "to": "writeSrc", "toPort": "value" },
            { "from": "loadSrc", "fromPort": "value", "to": "writeCtx", "toPort": "source" },
            { "from": "loadCtx", "fromPort": "value", "to": "writeCtx", "toPort": "value" },
            { "from": "loadSrc", "fromPort": "value", "to": "writePower", "toPort": "source" },
            { "from": "loadCfgFloat", "fromPort": "value", "to": "writePower", "toPort": "value" },
            { "from": "loadSrc", "fromPort": "value", "to": "writeTier", "toPort": "source" },
            { "from": "loadCfgInt", "fromPort": "value", "to": "writeTier", "toPort": "value" },
            { "from": "loadSrc", "fromPort": "value", "to": "writeChain", "toPort": "source" },
            { "from": "loadCfgEffect", "fromPort": "value", "to": "writeChain", "toPort": "value" },
            { "from": "loadSrc", "fromPort": "value", "to": "readPower", "toPort": "source" },
            { "from": "loadSrc", "fromPort": "value", "to": "writePowerEcho", "toPort": "source" },
            { "from": "readPower", "fromPort": "value", "to": "writePowerEcho", "toPort": "value" },
            { "from": "loadSrc", "fromPort": "value", "to": "readTier", "toPort": "source" },
            { "from": "loadSrc", "fromPort": "value", "to": "writeTierEcho", "toPort": "source" },
            { "from": "readTier", "fromPort": "value", "to": "writeTierEcho", "toPort": "value" },
            { "from": "loadSrc", "fromPort": "value", "to": "readSrc", "toPort": "source" },
            { "from": "loadSrc", "fromPort": "value", "to": "writeSrcEcho", "toPort": "source" },
            { "from": "readSrc", "fromPort": "value", "to": "writeSrcEcho", "toPort": "value" }
          ]
        }
        """;

    private const string LifecycleGraphJson = """
        {
          "kind": "Effect",
          "entry": "begin",
          "nodes": [
            { "id": "begin", "op": "BeginLifecycleTransaction" },
            { "id": "transfer", "op": "InvokeBuiltin", "builtinHandler": "TransferStableId" },
            { "id": "clearFx", "op": "InvokeBuiltin", "builtinHandler": "ClearActiveEffects" }
          ],
          "controlEdges": [
            { "from": "begin", "fromPort": "next", "to": "transfer" },
            { "from": "transfer", "fromPort": "next", "to": "clearFx" }
          ],
          "valueEdges": []
        }
        """;
}
