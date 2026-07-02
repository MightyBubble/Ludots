using System;
using System.Collections.Generic;
using Ludots.Core.Registry;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    /// <summary>
    /// Resolves authored order blackboard key names to stable runtime integer keys.
    /// Mods declare their own keys in GAS/order_types.json before order types are parsed.
    /// </summary>
    public static class OrderBlackboardKeyRegistry
    {
        private const int CustomStartId = 10_000;
        private static readonly object Sync = new();
        private static readonly Dictionary<string, int> BuiltinIdsByKey = CreateBuiltinIdsByKey();
        private static readonly Dictionary<int, string> BuiltinKeysById = CreateBuiltinKeysById(BuiltinIdsByKey);
        private static StringIntRegistry _custom = CreateCustomRegistry();

        public static int Register(string key)
        {
            key = RequireCanonicalKey(key);
            if (BuiltinIdsByKey.TryGetValue(key, out int builtinId))
            {
                return builtinId;
            }

            lock (Sync)
            {
                return _custom.Register(key);
            }
        }

        public static bool TryGetId(string key, out int id)
        {
            if (!TryRequireCanonicalKey(key, out string canonicalKey))
            {
                id = 0;
                return false;
            }

            if (BuiltinIdsByKey.TryGetValue(canonicalKey, out id))
            {
                return true;
            }

            lock (Sync)
            {
                return _custom.TryGetId(canonicalKey, out id);
            }
        }

        public static bool IsBuiltinKey(string key)
        {
            return TryRequireCanonicalKey(key, out string canonicalKey) &&
                   BuiltinIdsByKey.ContainsKey(canonicalKey);
        }

        public static string GetKey(int id)
        {
            if (BuiltinKeysById.TryGetValue(id, out string? builtinKey))
            {
                return builtinKey;
            }

            lock (Sync)
            {
                return _custom.GetName(id);
            }
        }

        public static RegistryMapping[] SnapshotMappings()
        {
            lock (Sync)
            {
                return RegistryMappingSnapshot.Merge(
                    RegistryMappingSnapshot.FromNameToId(BuiltinIdsByKey),
                    _custom.SnapshotMappings());
            }
        }

        public static void ResetToBuiltins()
        {
            lock (Sync)
            {
                _custom = CreateCustomRegistry();
            }
        }

        private static string RequireCanonicalKey(string key)
        {
            if (!TryRequireCanonicalKey(key, out string canonicalKey))
            {
                throw new ArgumentException("Order blackboard key must be non-empty and must not contain leading or trailing whitespace.", nameof(key));
            }

            return canonicalKey;
        }

        private static bool TryRequireCanonicalKey(string key, out string canonicalKey)
        {
            canonicalKey = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string trimmed = key.Trim();
            if (!string.Equals(trimmed, key, StringComparison.Ordinal))
            {
                return false;
            }

            canonicalKey = key;
            return true;
        }

        private static StringIntRegistry CreateCustomRegistry()
        {
            return new StringIntRegistry(
                capacity: CustomStartId + 256,
                startId: CustomStartId,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
        }

        private static Dictionary<string, int> CreateBuiltinIdsByKey()
        {
            var idsByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (OrderBlackboardKeyDefinition definition in OrderBlackboardKeys.BuiltinDefinitions)
            {
                idsByKey.Add(definition.Key, definition.Id);
            }

            return idsByKey;
        }

        private static Dictionary<int, string> CreateBuiltinKeysById(Dictionary<string, int> idsByKey)
        {
            var keysById = new Dictionary<int, string>();
            foreach (var kvp in idsByKey)
            {
                if (keysById.TryGetValue(kvp.Value, out string? existingKey))
                {
                    throw new InvalidOperationException(
                        $"Order blackboard built-in id {kvp.Value} is duplicated by '{existingKey}' and '{kvp.Key}'.");
                }

                keysById.Add(kvp.Value, kvp.Key);
            }

            return keysById;
        }
    }
}
