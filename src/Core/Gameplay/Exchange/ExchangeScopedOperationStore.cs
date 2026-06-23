using System;
using System.Collections.Generic;
using Ludots.Core.Association;

namespace Ludots.Core.Gameplay.Exchange
{
    public sealed class ExchangeScopedOperationStore
    {
        private readonly Dictionary<ScopedOperationKey, ExchangeOperationDefinition> _scopedDefinitions = new();

        public void Clear()
        {
            _scopedDefinitions.Clear();
        }

        public void Set(int operationId, in ScopeKey scope, ExchangeOperationDefinition definition)
        {
            if (operationId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(operationId), "Exchange operation id must be positive.");
            }

            int scopeKey = RequireNamedScope(in scope);
            _scopedDefinitions[new ScopedOperationKey(operationId, scopeKey)] = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public bool Remove(int operationId, in ScopeKey scope)
        {
            return operationId > 0 &&
                   TryGetNamedScopeId(in scope, out int scopeKey) &&
                   _scopedDefinitions.Remove(new ScopedOperationKey(operationId, scopeKey));
        }

        public bool TryGet(int operationId, in ScopeKey scope, out ExchangeOperationDefinition definition)
        {
            if (operationId > 0 &&
                TryGetNamedScopeId(in scope, out int scopeKey) &&
                _scopedDefinitions.TryGetValue(new ScopedOperationKey(operationId, scopeKey), out definition!))
            {
                return true;
            }

            definition = null!;
            return false;
        }

        private static int RequireNamedScope(in ScopeKey scope)
        {
            int scopeKey = scope.Kind == ScopeKind.Named ? scope.ScopeKeyId : 0;
            if (scopeKey <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKey), "Exchange scope key must be positive.");
            }

            return scopeKey;
        }

        private static bool TryGetNamedScopeId(in ScopeKey scope, out int scopeKey)
        {
            if (scope.Kind == ScopeKind.Named && scope.ScopeKeyId > 0)
            {
                scopeKey = scope.ScopeKeyId;
                return true;
            }

            scopeKey = 0;
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
