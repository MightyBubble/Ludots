using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.NodeLibraries.GASGraph.Host
{
    public sealed class GraphLookupTableLoader
    {
        public const string ConfigPath = "GraphTables/lookup_tables.json";

        private readonly ConfigPipeline _configs;
        private readonly Func<string, int>? _resolveTextToken;

        public GraphLookupTableLoader(ConfigPipeline configs, PresentationTextCatalog? textCatalog = null)
            : this(configs, textCatalog == null ? null : textCatalog.GetTokenId)
        {
        }

        public GraphLookupTableLoader(ConfigPipeline configs, Func<string, int>? resolveTextToken)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
            _resolveTextToken = resolveTextToken;
        }

        public GraphLookupTableRegistry Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            var registry = new GraphLookupTableRegistry();
            var entry = ConfigPipeline.RequireEntry(catalog, ConfigPath, ConfigMergePolicy.ArrayById, "id");
            IReadOnlyList<MergedConfigEntry> nodes = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < nodes.Count; i++)
            {
                RegisterTable(registry, nodes[i].Node);
            }

            registry.Freeze();
            return registry;
        }

        private void RegisterTable(GraphLookupTableRegistry registry, JsonNode node)
        {
            string tableId = node["id"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(tableId))
            {
                throw new InvalidOperationException("Lookup table is missing required 'id'.");
            }

            string keyKind = node["keyKind"]?.GetValue<string>() ?? "Int";
            if (!string.Equals(keyKind, "Int", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Lookup table '{tableId}' keyKind '{keyKind}' is unsupported; P0 only allows Int.");
            }

            if (node["columns"] is not JsonArray columnsNode || columnsNode.Count == 0)
            {
                throw new InvalidOperationException($"Lookup table '{tableId}' must declare non-empty 'columns'.");
            }

            if (node["rows"] is not JsonArray rowsNode || rowsNode.Count == 0)
            {
                throw new InvalidOperationException($"Lookup table '{tableId}' must declare non-empty 'rows'.");
            }

            var columns = new (string FieldId, GraphLookupColumnKind Kind)[columnsNode.Count];
            int intColumnCount = 0;
            int floatColumnCount = 0;
            for (int c = 0; c < columnsNode.Count; c++)
            {
                JsonNode? columnNode = columnsNode[c]
                    ?? throw new InvalidOperationException($"Lookup table '{tableId}' has a null column.");
                string fieldId = columnNode["id"]?.GetValue<string>() ?? string.Empty;
                string kindText = columnNode["kind"]?.GetValue<string>() ?? string.Empty;
                GraphLookupColumnKind kind = ParseKind(kindText, tableId, fieldId);
                columns[c] = (fieldId, kind);
                if (kind == GraphLookupColumnKind.Float)
                {
                    floatColumnCount++;
                }
                else
                {
                    intColumnCount++;
                }
            }

            int rowCount = rowsNode.Count;
            var keys = new int[rowCount];
            var intValues = new int[rowCount * intColumnCount];
            var floatValues = new float[rowCount * floatColumnCount];

            for (int r = 0; r < rowCount; r++)
            {
                JsonNode? rowNode = rowsNode[r]
                    ?? throw new InvalidOperationException($"Lookup table '{tableId}' has a null row.");
                if (rowNode["key"] == null)
                {
                    throw new InvalidOperationException($"Lookup table '{tableId}' row {r} is missing 'key'.");
                }

                keys[r] = rowNode["key"]!.GetValue<int>();
                int intWrite = 0;
                int floatWrite = 0;
                for (int c = 0; c < columns.Length; c++)
                {
                    string fieldId = columns[c].FieldId;
                    JsonNode? valueNode = rowNode[fieldId];
                    if (valueNode == null)
                    {
                        throw new InvalidOperationException(
                            $"Lookup table '{tableId}' row key={keys[r]} is missing field '{fieldId}'.");
                    }

                    switch (columns[c].Kind)
                    {
                        case GraphLookupColumnKind.Int:
                            intValues[r * intColumnCount + intWrite++] = valueNode.GetValue<int>();
                            break;
                        case GraphLookupColumnKind.TextToken:
                            string tokenKey = valueNode.GetValue<string>() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(tokenKey))
                            {
                                throw new InvalidOperationException(
                                    $"Lookup table '{tableId}' row key={keys[r]} field '{fieldId}' has empty TextToken.");
                            }

                            if (_resolveTextToken == null)
                            {
                                throw new InvalidOperationException(
                                    $"Lookup table '{tableId}' field '{fieldId}' is TextToken, but no PresentationTextCatalog resolver was provided.");
                            }

                            int tokenId = _resolveTextToken(tokenKey);
                            if (tokenId <= 0)
                            {
                                throw new InvalidOperationException(
                                    $"Lookup table '{tableId}' field '{fieldId}' references unknown text token '{tokenKey}'.");
                            }

                            intValues[r * intColumnCount + intWrite++] = tokenId;
                            break;
                        case GraphLookupColumnKind.Float:
                            floatValues[r * floatColumnCount + floatWrite++] = valueNode.GetValue<float>();
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Lookup table '{tableId}' field '{fieldId}' has unsupported kind '{columns[c].Kind}'.");
                    }
                }
            }

            registry.RegisterTable(tableId, columns, keys, intValues, floatValues);
        }

        private static GraphLookupColumnKind ParseKind(string kindText, string tableId, string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
            {
                throw new InvalidOperationException($"Lookup table '{tableId}' has a column without 'id'.");
            }

            if (string.Equals(kindText, "Int", StringComparison.Ordinal))
            {
                return GraphLookupColumnKind.Int;
            }

            if (string.Equals(kindText, "Float", StringComparison.Ordinal))
            {
                return GraphLookupColumnKind.Float;
            }

            if (string.Equals(kindText, "TextToken", StringComparison.Ordinal))
            {
                return GraphLookupColumnKind.TextToken;
            }

            throw new InvalidOperationException(
                $"Lookup table '{tableId}' field '{fieldId}' has unsupported kind '{kindText}'.");
        }
    }
}
