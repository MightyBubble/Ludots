using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Graph.Codegen
{
    public sealed record GraphCodegenCoverageEntry(
        string Op,
        string CodegenStatus,
        string Family);

    public sealed record GraphCodegenCoverageSummary(
        int Total,
        int Covered,
        int Pending,
        int Exempt,
        IReadOnlyList<GraphCodegenCoverageEntry> Entries);

    public static class GraphCodegenCoverageProjection
    {
        public const string RegistryRelativePath = "assets/GAS/graph_node_op_coverage.registry.json";

        public static GraphCodegenCoverageSummary FromRegistryFile(string registryPath)
        {
            if (!File.Exists(registryPath))
            {
                throw new FileNotFoundException("Graph op coverage registry not found.", registryPath);
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(registryPath));
            var entries = new List<GraphCodegenCoverageEntry>();
            int covered = 0, pending = 0, exempt = 0;
            foreach (JsonElement entry in doc.RootElement.GetProperty("entries").EnumerateArray())
            {
                string op = entry.GetProperty("op").GetString() ?? string.Empty;
                string status = entry.TryGetProperty("codegenStatus", out JsonElement statusEl)
                    ? statusEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(status))
                {
                    throw new InvalidOperationException(
                        $"Coverage registry entry '{op}' is missing required codegenStatus (pending|covered|exempt).");
                }

                string family = "unknown";
                if (Enum.TryParse(op, ignoreCase: false, out GraphNodeOp parsed) &&
                    GraphCodegenStrategyCatalog.TryGet(parsed, out GraphCodegenStrategy strategy))
                {
                    family = strategy.Family.ToString();
                }

                entries.Add(new GraphCodegenCoverageEntry(op, status, family));
                switch (status)
                {
                    case "covered":
                        covered++;
                        break;
                    case "pending":
                        pending++;
                        break;
                    case "exempt":
                        exempt++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Coverage registry entry '{op}' has invalid codegenStatus '{status}'.");
                }
            }

            return new GraphCodegenCoverageSummary(
                Total: entries.Count,
                Covered: covered,
                Pending: pending,
                Exempt: exempt,
                Entries: entries.OrderBy(e => e.Op, StringComparer.Ordinal).ToArray());
        }

        public static GraphCodegenCoverageSummary FromCatalogStrategies()
        {
            var entries = new List<GraphCodegenCoverageEntry>();
            int covered = 0, exempt = 0;
            foreach (KeyValuePair<GraphNodeOp, GraphCodegenStrategy> pair in GraphCodegenStrategyCatalog.All
                         .OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
            {
                if (pair.Key == GraphNodeOp.None)
                {
                    entries.Add(new GraphCodegenCoverageEntry(pair.Key.ToString(), "exempt", pair.Value.Family.ToString()));
                    exempt++;
                    continue;
                }

                entries.Add(new GraphCodegenCoverageEntry(pair.Key.ToString(), "covered", pair.Value.Family.ToString()));
                covered++;
            }

            return new GraphCodegenCoverageSummary(
                Total: entries.Count,
                Covered: covered,
                Pending: 0,
                Exempt: exempt,
                Entries: entries);
        }
    }
}
