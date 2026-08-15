using System;
using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// ActionLib: yieldable Script actions for L2 / Script slice hosts. Not callable from Effect transactions.
    /// </summary>
    public sealed class GraphActionCatalog
    {
        private readonly Dictionary<string, GraphActionEntry> _byName = new(StringComparer.Ordinal);

        public void Clear() => _byName.Clear();

        public void Register(string name, int graphId, GraphKind kind, GraphActionHost host)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Action name is required.", nameof(name));
            }

            if (graphId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(graphId));
            }

            if (kind != GraphKind.Script)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "Action catalog accepts Script only.");
            }

            if (host == GraphActionHost.None || !Enum.IsDefined(typeof(GraphActionHost), host))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(host),
                    host,
                    "Action catalog requires an explicit supported host.");
            }

            string key = name.Trim();
            var entry = new GraphActionEntry(key, graphId, kind, host);
            if (!_byName.TryAdd(key, entry))
            {
                throw new InvalidOperationException($"Graph action '{key}' is already registered.");
            }
        }

        public bool TryGet(string name, out int graphId)
        {
            if (_byName.TryGetValue(name, out GraphActionEntry entry))
            {
                graphId = entry.GraphId;
                return true;
            }

            graphId = 0;
            return false;
        }

        public bool TryGetEntry(string name, out GraphActionEntry entry)
            => _byName.TryGetValue(name, out entry);

        public IReadOnlyCollection<string> Names => _byName.Keys;

        public int Require(string name)
        {
            if (!TryGetEntry(name, out GraphActionEntry entry))
            {
                throw new InvalidOperationException($"Graph action '{name}' is not registered.");
            }

            return entry.GraphId;
        }

        public int Require(string name, GraphActionHost expectedHost)
        {
            if (!TryGetEntry(name, out GraphActionEntry entry))
            {
                throw new InvalidOperationException($"Graph action '{name}' is not registered.");
            }

            if (expectedHost == GraphActionHost.None || !Enum.IsDefined(typeof(GraphActionHost), expectedHost))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expectedHost),
                    expectedHost,
                    "Action lookup requires an explicit supported host.");
            }

            if (entry.Host != expectedHost)
            {
                throw new InvalidOperationException(
                    $"Graph action '{name}' is registered for host '{entry.Host}', but '{expectedHost}' is required.");
            }

            return entry.GraphId;
        }

        public int Count => _byName.Count;
    }

    public readonly struct GraphActionEntry
    {
        public GraphActionEntry(string name, int graphId, GraphKind kind, GraphActionHost host)
        {
            Name = name;
            GraphId = graphId;
            Kind = kind;
            Host = host;
        }

        public string Name { get; }
        public int GraphId { get; }
        public GraphKind Kind { get; }
        public GraphActionHost Host { get; }
    }
}
