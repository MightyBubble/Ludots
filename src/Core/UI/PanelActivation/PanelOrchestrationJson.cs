using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// Loads visibility orchestration graphs from JSON (#1014): one panelType per
    /// entry, a flat Script instruction list, optional symbol strings. This is the
    /// authoring mouth so orchestration stays data-driven end to end.
    /// </summary>
    public static class PanelOrchestrationJson
    {
        public static IReadOnlyList<PanelOrchestrationEntry> Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Panel orchestration JSON is empty.");
            }

            JsonNode root;
            try
            {
                root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Panel orchestration JSON parsed to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Panel orchestration JSON is malformed: {ex.Message}");
            }

            if (root is not JsonArray entriesNode)
            {
                throw new InvalidOperationException("Panel orchestration root must be a JSON array of entries.");
            }

            var entries = new List<PanelOrchestrationEntry>(entriesNode.Count);
            foreach (JsonNode? entryNode in entriesNode)
            {
                if (entryNode is not JsonObject entryObject)
                {
                    throw new InvalidOperationException("Panel orchestration entries must be objects.");
                }

                RejectUnknownFields(entryObject, "panelType", "instructions", "symbols", "panel orchestration entry");
                string panelType = RequireString(entryObject, "panelType", "panel orchestration entry");
                if (entryObject["instructions"] is not JsonArray instructionsNode || instructionsNode.Count == 0)
                {
                    throw new InvalidOperationException($"Orchestration entry '{panelType}' requires a non-empty 'instructions' array.");
                }

                var instructions = new GraphInstruction[instructionsNode.Count];
                for (int i = 0; i < instructionsNode.Count; i++)
                {
                    if (instructionsNode[i] is not JsonObject instructionObject)
                    {
                        throw new InvalidOperationException($"Orchestration entry '{panelType}' instructions must be objects.");
                    }

                    RejectUnknownFields(instructionObject, "op", "dst", "a", "b", "imm", $"orchestration entry '{panelType}' instruction");
                    string opName = RequireString(instructionObject, "op", $"orchestration entry '{panelType}' instruction");
                    if (!Enum.TryParse<GraphNodeOp>(opName, ignoreCase: false, out GraphNodeOp op) ||
                        !Enum.IsDefined(typeof(GraphNodeOp), op))
                    {
                        throw new InvalidOperationException($"Orchestration entry '{panelType}' has unknown op '{opName}'.");
                    }

                    instructions[i] = new GraphInstruction
                    {
                        Op = (ushort)op,
                        Dst = ReadByte(instructionObject, "dst"),
                        A = ReadByte(instructionObject, "a"),
                        B = ReadByte(instructionObject, "b"),
                        Imm = ReadInt(instructionObject, "imm"),
                    };
                }

                var symbols = new List<string>();
                if (entryObject["symbols"] is JsonArray symbolsNode)
                {
                    foreach (JsonNode? symbolNode in symbolsNode)
                    {
                        string? symbol = symbolNode?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(symbol))
                        {
                            throw new InvalidOperationException($"Orchestration entry '{panelType}' symbols must be non-empty strings.");
                        }

                        symbols.Add(symbol);
                    }
                }

                entries.Add(new PanelOrchestrationEntry(panelType, instructions, symbols.ToArray()));
            }

            return entries;
        }

        private static string RequireString(JsonObject obj, string field, string context)
        {
            string? value = obj[field]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{context} is missing required '{field}'.");
            }

            return value;
        }

        private static byte ReadByte(JsonObject obj, string field)
        {
            return obj[field] is JsonNode node && node.GetValue<int>() is int value && value >= 0 && value <= 255
                ? (byte)value
                : (byte)0;
        }

        private static int ReadInt(JsonObject obj, string field)
        {
            return obj[field] is JsonNode node ? node.GetValue<int>() : 0;
        }

        private static void RejectUnknownFields(JsonObject obj, string f1, string f2, string f3, string context)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal) { f1, f2, f3 };
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (!allowed.Contains(property.Key))
                {
                    throw new InvalidOperationException($"{context} has unknown field '{property.Key}' (allowed: {f1}, {f2}, {f3}).");
                }
            }
        }

        private static void RejectUnknownFields(JsonObject obj, string f1, string f2, string f3, string f4, string f5, string context)
        {
            var allowed = new HashSet<string>(StringComparer.Ordinal) { f1, f2, f3, f4, f5 };
            foreach (KeyValuePair<string, JsonNode?> property in obj)
            {
                if (!allowed.Contains(property.Key))
                {
                    throw new InvalidOperationException($"{context} has unknown field '{property.Key}' (allowed: {f1}, {f2}, {f3}, {f4}, {f5}).");
                }
            }
        }
    }
}
