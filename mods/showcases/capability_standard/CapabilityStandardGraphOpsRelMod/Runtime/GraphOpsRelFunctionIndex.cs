using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace CapabilityStandardGraphOpsRelMod.Runtime;

public sealed class GraphOpsRelFunctionIndex
{
    private readonly Dictionary<string, GraphFunctionEntry> _byName = new(StringComparer.Ordinal);

    public void Register(string name, int graphId, GraphKind kind)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Function name is required.", nameof(name));
        }

        if (graphId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(graphId));
        }

        if (kind is not (GraphKind.Query or GraphKind.Effect))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Rel showcase func lib accepts Query or Effect only.");
        }

        var entry = new GraphFunctionEntry(name.Trim(), graphId, kind);
        if (!_byName.TryAdd(entry.Name, entry))
        {
            throw new InvalidOperationException($"Graph function '{entry.Name}' is already registered.");
        }
    }

    public GraphFunctionEntry Require(string name)
    {
        if (!_byName.TryGetValue(name, out GraphFunctionEntry entry))
        {
            throw new InvalidOperationException($"Graph function '{name}' is not registered in rel showcase func lib.");
        }

        return entry;
    }
}
