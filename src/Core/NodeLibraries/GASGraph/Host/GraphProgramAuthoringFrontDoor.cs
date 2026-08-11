using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    /// <summary>
    /// Single compile front door for GAS L1 authoring: Kind → ControlFlow schema → GraphControlFlowCompiler.
    /// </summary>
    public static class GraphProgramAuthoringFrontDoor
    {
        public static (GraphProgramPackage? Package, GraphOutputSchema OutputSchema, List<GraphDiagnostic> Diagnostics)
            CompileJsonObject(JsonObject obj, string graphId, JsonSerializerOptions options)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrWhiteSpace(graphId)) throw new ArgumentException("graphId is required.", nameof(graphId));
            if (options == null) throw new ArgumentNullException(nameof(options));

            GraphKind kind = RequireKind(obj, graphId);
            RequireControlFlowAuthoringShape(obj, graphId, kind);

            GraphControlFlowDocument? doc;
            try
            {
                doc = obj.Deserialize<GraphControlFlowDocument>(options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Strict JSON rejected ControlFlow graph '{graphId}': {ex.Message}",
                    ex);
            }

            if (doc == null)
            {
                throw new InvalidOperationException($"Failed to deserialize ControlFlow graph '{graphId}'.");
            }

            if (string.IsNullOrWhiteSpace(doc.Id))
            {
                doc.Id = graphId;
            }

            if (!string.Equals(doc.Id, graphId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Graph id mismatch: '{graphId}' vs '{doc.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(doc.Kind))
            {
                doc.Kind = kind.ToString();
            }

            return GraphControlFlowCompiler.CompileWithOutputs(doc);
        }

        public static GraphKind RequireKind(JsonObject obj, string graphId)
        {
            string? kindText = null;
            if (obj.TryGetPropertyValue("kind", out JsonNode? kindNode) && kindNode is JsonValue kindValue)
            {
                kindText = kindValue.GetValue<string>();
            }

            if (!GraphKindParser.TryParse(kindText, out GraphKind kind) || kind == GraphKind.None)
            {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' requires an authored kind ({GraphAuthoringKindPolicy.DescribeSupportedKinds()}).");
            }

            if (!GraphAuthoringKindPolicy.IsControlFlowAuthoringKind(kind))
            {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' kind '{kind}' is not a ControlFlow authoring kind.");
            }

            return kind;
        }

        public static void RequireControlFlowAuthoringShape(JsonObject obj, string graphId, GraphKind kind)
        {
            if (HasLegacyNextChain(obj))
            {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' kind '{kind}' uses nodes[].next. " +
                    "L1 authoring SSOT requires controlEdges/valueEdges only (issue #861).");
            }

            if (!obj.ContainsKey("controlEdges") || !obj.ContainsKey("valueEdges"))
            {
                throw new InvalidOperationException(
                    $"Graph '{graphId}' kind '{kind}' must author controlEdges and valueEdges. " +
                    "Loader no longer selects a compiler from JSON shape.");
            }
        }

        public static bool HasLegacyNextChain(JsonObject obj)
        {
            if (obj["nodes"] is not JsonArray nodes)
            {
                return false;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is JsonObject node && node.ContainsKey("next"))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
