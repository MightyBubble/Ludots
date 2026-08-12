using System;
using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// Named reusable L1 Script/Validation/Score entries ("func lib").
    /// Macros are not supported — reuse via name → graph id + InvokeScript/Call.
    /// </summary>
    public sealed class GraphFunctionCatalog
    {
        private readonly Dictionary<string, GraphFunctionEntry> _byName =
            new(StringComparer.Ordinal);
        private readonly Dictionary<int, GraphFunctionEntry> _byGraphId = new();

        public void Clear()
        {
            _byName.Clear();
            _byGraphId.Clear();
        }

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

            if (kind == GraphKind.None || !Enum.IsDefined(typeof(GraphKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (kind is not (GraphKind.Script or GraphKind.Validation or GraphKind.Score))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Function catalog accepts Script, Validation, or Score only.");
            }

            var entry = new GraphFunctionEntry(name.Trim(), graphId, kind);
            if (!_byName.TryAdd(entry.Name, entry))
            {
                throw new InvalidOperationException(
                    $"Graph function '{entry.Name}' is already registered.");
            }

            _byGraphId.TryAdd(entry.GraphId, entry);
        }

        public bool TryGet(string name, out GraphFunctionEntry entry)
            => _byName.TryGetValue(name, out entry);

        public bool TryGetByGraphId(int graphId, out GraphFunctionEntry entry)
            => _byGraphId.TryGetValue(graphId, out entry);

        public GraphFunctionEntry Require(string name)
        {
            if (!TryGet(name, out GraphFunctionEntry entry))
            {
                throw new InvalidOperationException($"Graph function '{name}' is not registered.");
            }

            return entry;
        }

        public int Count => _byName.Count;
    }

    public readonly struct GraphFunctionEntry
    {
        public GraphFunctionEntry(string name, int graphId, GraphKind kind)
        {
            Name = name;
            GraphId = graphId;
            Kind = kind;
        }

        public string Name { get; }
        public int GraphId { get; }
        public GraphKind Kind { get; }
    }
}
