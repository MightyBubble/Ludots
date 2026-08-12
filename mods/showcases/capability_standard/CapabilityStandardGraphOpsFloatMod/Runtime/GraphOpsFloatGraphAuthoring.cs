using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace CapabilityStandardGraphOpsFloatMod.Runtime;

internal static class GraphOpsFloatGraphAuthoring
{
    public const string EffectGraphId = "showcase.graph_ops_float.damage";
    public const string ValidationGraphId = "showcase.graph_ops_float.range";
    public const string FinalDamageNodeId = "finalDamage";
    public const string RangeValidNodeId = "rangeValid";

    public static GraphControlFlowCompileResult CompileEffectGraph(float distance)
    {
        string json = EffectGraphJsonTemplate.Replace(
            DistancePlaceholder,
            distance.ToString(CultureInfo.InvariantCulture));
        return Compile(json, EffectGraphId);
    }

    public static GraphControlFlowCompileResult CompileValidationGraph(float distance)
    {
        string json = ValidationGraphJsonTemplate.Replace(
            DistancePlaceholder,
            distance.ToString(CultureInfo.InvariantCulture));
        return Compile(json, ValidationGraphId);
    }

    public static byte RequireFloatDest(GraphControlFlowCompileResult compiled, string nodeId, GraphNodeOp op)
    {
        GraphInstruction[] program = compiled.Program;
        GraphInstructionSourceMap map = compiled.SourceMap;
        for (int i = 0; i < program.Length; i++)
        {
            if (!map.TryGetSource(i, out GraphInstructionSource source) ||
                !string.Equals(source.NodeId, nodeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (program[i].Op == (ushort)op)
            {
                return program[i].Dst;
            }
        }

        throw new InvalidOperationException($"Compiled graph missing float node '{nodeId}' ({op}).");
    }

    public static byte RequireBoolDest(GraphControlFlowCompileResult compiled, string nodeId, GraphNodeOp op)
    {
        GraphInstruction[] program = compiled.Program;
        GraphInstructionSourceMap map = compiled.SourceMap;
        for (int i = 0; i < program.Length; i++)
        {
            if (!map.TryGetSource(i, out GraphInstructionSource source) ||
                !string.Equals(source.NodeId, nodeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (program[i].Op == (ushort)op)
            {
                return program[i].Dst;
            }
        }

        throw new InvalidOperationException($"Compiled graph missing bool node '{nodeId}' ({op}).");
    }

    private static GraphControlFlowCompileResult Compile(string json, string graphId)
    {
        JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase(includeFields: true);
        JsonObject obj = JsonNode.Parse(json)!.AsObject();
        GraphControlFlowCompileResult compiled = GraphProgramAuthoringFrontDoor.CompileJsonObjectFull(obj, graphId, options);
        if (!compiled.Succeeded)
        {
            var message = string.Join("; ", compiled.Diagnostics.Select(d => d.Message));
            throw new InvalidOperationException($"FrontDoor compile failed for '{graphId}': {message}");
        }

        GraphKind kind = graphId == ValidationGraphId ? GraphKind.Validation : GraphKind.Effect;
        GraphKindOperationPolicy.RequireAllowed(kind, compiled.Program, GasGraphOpHandlerTable.Instance);

        return compiled;
    }

    private const string DistancePlaceholder = "__DISTANCE__";

    private const string EffectGraphJsonTemplate = """
        {
          "kind": "Effect",
          "entry": "base",
          "nodes": [
            { "id": "base", "op": "ConstFloat", "floatValue": 100 },
            { "id": "dist", "op": "ConstFloat", "floatValue": __DISTANCE__ },
            { "id": "decay", "op": "SubFloat" },
            { "id": "range", "op": "ConstFloat", "floatValue": 50 },
            { "id": "attenuation", "op": "DivFloat" },
            { "id": "multiplier", "op": "ConstFloat", "floatValue": 1.5 },
            { "id": "scaled", "op": "MulFloat" },
            { "id": "variance", "op": "RandomFloat01" },
            { "id": "varianceScale", "op": "ConstFloat", "floatValue": 5 },
            { "id": "varianceAmt", "op": "MulFloat" },
            { "id": "withVariance", "op": "AddFloat" },
            { "id": "modifier", "op": "ConstFloat", "floatValue": -8 },
            { "id": "negMod", "op": "NegFloat" },
            { "id": "absMod", "op": "AbsFloat" },
            { "id": "adjusted", "op": "AddFloat" },
            { "id": "minCap", "op": "ConstFloat", "floatValue": 0 },
            { "id": "maxCap", "op": "ConstFloat", "floatValue": 80 },
            { "id": "clamped", "op": "ClampFloat" },
            { "id": "ceiling", "op": "MaxFloat" },
            { "id": "finalDamage", "op": "MinFloat" },
            { "id": "threshold", "op": "ConstFloat", "floatValue": 15 },
            { "id": "isCritical", "op": "CompareGtFloat" }
          ],
          "controlEdges": [
            { "from": "base", "fromPort": "next", "to": "dist" },
            { "from": "dist", "fromPort": "next", "to": "decay" },
            { "from": "decay", "fromPort": "next", "to": "range" },
            { "from": "range", "fromPort": "next", "to": "attenuation" },
            { "from": "attenuation", "fromPort": "next", "to": "multiplier" },
            { "from": "multiplier", "fromPort": "next", "to": "scaled" },
            { "from": "scaled", "fromPort": "next", "to": "variance" },
            { "from": "variance", "fromPort": "next", "to": "varianceScale" },
            { "from": "varianceScale", "fromPort": "next", "to": "varianceAmt" },
            { "from": "varianceAmt", "fromPort": "next", "to": "withVariance" },
            { "from": "withVariance", "fromPort": "next", "to": "modifier" },
            { "from": "modifier", "fromPort": "next", "to": "negMod" },
            { "from": "negMod", "fromPort": "next", "to": "absMod" },
            { "from": "absMod", "fromPort": "next", "to": "adjusted" },
            { "from": "adjusted", "fromPort": "next", "to": "minCap" },
            { "from": "minCap", "fromPort": "next", "to": "maxCap" },
            { "from": "maxCap", "fromPort": "next", "to": "clamped" },
            { "from": "clamped", "fromPort": "next", "to": "ceiling" },
            { "from": "ceiling", "fromPort": "next", "to": "finalDamage" },
            { "from": "finalDamage", "fromPort": "next", "to": "threshold" },
            { "from": "threshold", "fromPort": "next", "to": "isCritical" }
          ],
          "valueEdges": [
            { "from": "base", "fromPort": "value", "to": "decay", "toPort": "a" },
            { "from": "dist", "fromPort": "value", "to": "decay", "toPort": "b" },
            { "from": "decay", "fromPort": "value", "to": "attenuation", "toPort": "a" },
            { "from": "range", "fromPort": "value", "to": "attenuation", "toPort": "b" },
            { "from": "attenuation", "fromPort": "value", "to": "scaled", "toPort": "a" },
            { "from": "multiplier", "fromPort": "value", "to": "scaled", "toPort": "b" },
            { "from": "variance", "fromPort": "value", "to": "varianceAmt", "toPort": "a" },
            { "from": "varianceScale", "fromPort": "value", "to": "varianceAmt", "toPort": "b" },
            { "from": "scaled", "fromPort": "value", "to": "withVariance", "toPort": "a" },
            { "from": "varianceAmt", "fromPort": "value", "to": "withVariance", "toPort": "b" },
            { "from": "modifier", "fromPort": "value", "to": "negMod", "toPort": "value" },
            { "from": "negMod", "fromPort": "value", "to": "absMod", "toPort": "value" },
            { "from": "withVariance", "fromPort": "value", "to": "adjusted", "toPort": "a" },
            { "from": "absMod", "fromPort": "value", "to": "adjusted", "toPort": "b" },
            { "from": "adjusted", "fromPort": "value", "to": "clamped", "toPort": "value" },
            { "from": "minCap", "fromPort": "value", "to": "clamped", "toPort": "min" },
            { "from": "maxCap", "fromPort": "value", "to": "clamped", "toPort": "max" },
            { "from": "clamped", "fromPort": "value", "to": "ceiling", "toPort": "a" },
            { "from": "minCap", "fromPort": "value", "to": "ceiling", "toPort": "b" },
            { "from": "ceiling", "fromPort": "value", "to": "finalDamage", "toPort": "a" },
            { "from": "maxCap", "fromPort": "value", "to": "finalDamage", "toPort": "b" },
            { "from": "finalDamage", "fromPort": "value", "to": "isCritical", "toPort": "a" },
            { "from": "threshold", "fromPort": "value", "to": "isCritical", "toPort": "b" }
          ]
        }
        """;

    private const string ValidationGraphJsonTemplate = """
        {
          "kind": "Validation",
          "entry": "caster",
          "nodes": [
            { "id": "caster", "op": "LoadCaster" },
            { "id": "dist", "op": "ConstFloat", "floatValue": __DISTANCE__ },
            { "id": "minRange", "op": "ConstFloat", "floatValue": 5 },
            { "id": "maxRange", "op": "ConstFloat", "floatValue": 45 },
            { "id": "snapped", "op": "ClampFloat" },
            { "id": "rangeValid", "op": "CompareGtFloat" }
          ],
          "controlEdges": [
            { "from": "caster", "fromPort": "next", "to": "dist" },
            { "from": "dist", "fromPort": "next", "to": "minRange" },
            { "from": "minRange", "fromPort": "next", "to": "maxRange" },
            { "from": "maxRange", "fromPort": "next", "to": "snapped" },
            { "from": "snapped", "fromPort": "next", "to": "rangeValid" }
          ],
          "valueEdges": [
            { "from": "dist", "fromPort": "value", "to": "snapped", "toPort": "value" },
            { "from": "minRange", "fromPort": "value", "to": "snapped", "toPort": "min" },
            { "from": "maxRange", "fromPort": "value", "to": "snapped", "toPort": "max" },
            { "from": "snapped", "fromPort": "value", "to": "rangeValid", "toPort": "a" },
            { "from": "minRange", "fromPort": "value", "to": "rangeValid", "toPort": "b" }
          ]
        }
        """;
}
