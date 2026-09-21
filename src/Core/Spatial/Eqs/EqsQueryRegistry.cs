using System;
using Ludots.Core.Registry;

namespace Ludots.Core.Spatial.Eqs
{
    /// <summary>
    /// Engine-side EQS query catalog: config-declared queries (Spatial/eqs_queries.json,
    /// ArrayById across root assets and mod fragments) registered by key id so graph symbols
    /// (SubmitEngageBatch) and the order kernel resolve a query by int id. Frozen after
    /// engine assembly; lookups are array-indexed, allocation free.
    /// </summary>
    public sealed class EqsQueryRegistry
    {
        private readonly StringIntRegistry _ids;
        private readonly EqsQuery?[] _queries;

        public EqsQueryRegistry(StringIntRegistry ids, int capacity)
        {
            _ids = ids ?? throw new ArgumentNullException(nameof(ids));
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "EQS query registry capacity must be positive.");
            }

            _queries = new EqsQuery?[capacity];
        }

        public StringIntRegistry Ids => _ids;

        /// <summary>Registers one query under its declared key; ids are dense from 1.</summary>
        public void Install(string key, EqsQuery query)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("EQS query key is required.", nameof(key));
            }

            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            int id = _ids.Register(key);
            if (id <= 0 || id > _queries.Length)
            {
                throw new InvalidOperationException(
                    $"EQS.ERR.QueryRegistryCapacity: query '{key}' got id {id}, capacity {_queries.Length}.");
            }

            _queries[id - 1] = query;
        }

        public bool TryGet(int id, out EqsQuery query)
        {
            query = null!;
            if (id <= 0 || id > _queries.Length)
            {
                return false;
            }

            query = _queries[id - 1]!;
            return query != null;
        }
    }
}
