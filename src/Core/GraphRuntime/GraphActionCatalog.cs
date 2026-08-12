using System;
using System.Collections.Generic;

namespace Ludots.Core.GraphRuntime
{
    /// <summary>
    /// ActionLib: yieldable Script actions for L2 / Script slice hosts. Not callable from Effect transactions.
    /// </summary>
    public sealed class GraphActionCatalog
    {
        private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

        public void Clear() => _byName.Clear();

        public void Register(string name, int graphId, GraphKind kind)
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

            string key = name.Trim();
            if (!_byName.TryAdd(key, graphId))
            {
                throw new InvalidOperationException($"Graph action '{key}' is already registered.");
            }
        }

        public bool TryGet(string name, out int graphId) => _byName.TryGetValue(name, out graphId);

        public int Require(string name)
        {
            if (!TryGet(name, out int graphId))
            {
                throw new InvalidOperationException($"Graph action '{name}' is not registered.");
            }

            return graphId;
        }

        public int Count => _byName.Count;
    }
}
