using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.Exchange
{
    public sealed class ExchangeScopedOperationStore
    {
        private readonly Dictionary<ScopedOperationKey, ExchangeOperationDefinition> _scopedDefinitions = new();

        public void Clear()
        {
            _scopedDefinitions.Clear();
        }

        public void Set(int operationId, int scopeKey, ExchangeOperationDefinition definition)
        {
            if (operationId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(operationId), "Exchange operation id must be positive.");
            }

            if (scopeKey <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKey), "Exchange scope key must be positive.");
            }

            _scopedDefinitions[new ScopedOperationKey(operationId, scopeKey)] = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public bool Remove(int operationId, int scopeKey)
        {
            return operationId > 0 &&
                   scopeKey > 0 &&
                   _scopedDefinitions.Remove(new ScopedOperationKey(operationId, scopeKey));
        }

        public bool TryGet(int operationId, int scopeKey, out ExchangeOperationDefinition definition)
        {
            if (operationId > 0 &&
                scopeKey > 0 &&
                _scopedDefinitions.TryGetValue(new ScopedOperationKey(operationId, scopeKey), out definition!))
            {
                return true;
            }

            definition = null!;
            return false;
        }

        private readonly struct ScopedOperationKey : IEquatable<ScopedOperationKey>
        {
            public ScopedOperationKey(int operationId, int scopeKey)
            {
                OperationId = operationId;
                ScopeKey = scopeKey;
            }

            public int OperationId { get; }

            public int ScopeKey { get; }

            public bool Equals(ScopedOperationKey other)
            {
                return OperationId == other.OperationId && ScopeKey == other.ScopeKey;
            }

            public override bool Equals(object? obj)
            {
                return obj is ScopedOperationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(OperationId, ScopeKey);
            }
        }
    }
}
