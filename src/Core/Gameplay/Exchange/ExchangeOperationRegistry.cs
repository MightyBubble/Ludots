using System;
using System.Collections.Generic;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.Exchange
{
    public sealed class ExchangeOperationRegistry
    {
        private readonly StringIntRegistry _ids = new(capacity: 128, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
        private readonly List<ExchangeOperationDefinition?> _definitions = new() { null };

        public void Clear()
        {
            _ids.Clear();
            ClearDefinitions();
        }

        public void ClearDefinitions()
        {
            _definitions.Clear();
            _definitions.Add(null);
        }

        public int Register(string id, ExchangeOperationDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Exchange operation id is required.", nameof(id));
            }

            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            int operationId = _ids.Register(id);
            EnsureCapacity(operationId);
            _definitions[operationId] = definition;
            return operationId;
        }

        public int GetId(string id)
        {
            return _ids.GetId(id);
        }

        public string GetName(int id)
        {
            return _ids.GetName(id);
        }

        public bool TryGet(int id, out ExchangeOperationDefinition definition)
        {
            if ((uint)id < (uint)_definitions.Count && _definitions[id] != null)
            {
                definition = _definitions[id]!;
                return true;
            }

            definition = null!;
            return false;
        }

        private void EnsureCapacity(int id)
        {
            while (_definitions.Count <= id)
            {
                _definitions.Add(null);
            }
        }
    }
}
