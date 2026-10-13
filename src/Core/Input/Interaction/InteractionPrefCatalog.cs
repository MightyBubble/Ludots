using System;
using System.Collections.Generic;
using Ludots.Core.Registry;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Mod-declared preference keys. Core stores the ids and never interprets the names.
    /// A context-valued key's allowed values must each be an installed interaction context
    /// profile id; that check happens at install and names the key and the value.
    /// Value symbols are one shared registry: the same word registered by two keys is one id.
    /// </summary>
    public sealed class InteractionPrefCatalog
    {
        private readonly StringIntRegistry _keys;
        private readonly StringIntRegistry _values;
        private readonly Dictionary<int, int[]> _allowedByKey;
        private readonly HashSet<int> _contextValuedKeys;

        public InteractionPrefCatalog()
        {
            _keys = new StringIntRegistry(capacity: 32, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _values = new StringIntRegistry(capacity: 64, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            _allowedByKey = new Dictionary<int, int[]>();
            _contextValuedKeys = new HashSet<int>();
        }

        public StringIntRegistry Keys => _keys;

        public StringIntRegistry Values => _values;

        public void DefineKey(
            string keyName,
            bool contextValued,
            IReadOnlyList<string> valueNames,
            InteractionContextProfileRegistry contextProfiles,
            string source)
        {
            if (string.IsNullOrWhiteSpace(keyName))
            {
                throw new InvalidOperationException($"{source} preference key id must be a non-empty string.");
            }

            if (_keys.Contains(keyName))
            {
                throw new InvalidOperationException($"{source} duplicates preference key '{keyName}'.");
            }

            if (valueNames == null || valueNames.Count == 0)
            {
                throw new InvalidOperationException($"{source} preference key '{keyName}' must declare at least one value.");
            }

            if (contextValued && contextProfiles == null)
            {
                throw new ArgumentNullException(nameof(contextProfiles));
            }

            int keyId = _keys.Register(keyName);
            var allowed = new int[valueNames.Count];
            var seen = new HashSet<string>(valueNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < valueNames.Count; i++)
            {
                string valueName = valueNames[i];
                if (string.IsNullOrWhiteSpace(valueName))
                {
                    throw new InvalidOperationException($"{source} preference key '{keyName}' values[{i}] must be a non-empty string.");
                }

                if (!seen.Add(valueName))
                {
                    throw new InvalidOperationException($"{source} preference key '{keyName}' duplicates value '{valueName}'.");
                }

                if (contextValued && !IsInstalledContextProfile(contextProfiles!, valueName))
                {
                    throw new InvalidOperationException(
                        $"{source} preference key '{keyName}' value '{valueName}' is not an installed interaction context profile.");
                }

                allowed[i] = _values.Register(valueName);
            }

            _allowedByKey[keyId] = allowed;
            if (contextValued)
            {
                _contextValuedKeys.Add(keyId);
            }
        }

        public bool IsContextValued(int keyId) => _contextValuedKeys.Contains(keyId);

        public bool Allows(int keyId, int valueId)
        {
            if (!_allowedByKey.TryGetValue(keyId, out int[]? allowed))
            {
                return false;
            }

            for (int i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] == valueId)
                {
                    return true;
                }
            }

            return false;
        }

        public string KeyName(int keyId) => _keys.GetName(keyId);

        public string ValueName(int valueId) => _values.GetName(valueId);

        private static bool IsInstalledContextProfile(InteractionContextProfileRegistry profiles, string profileName)
        {
            if (!profiles.ProfileIdRegistry.TryGetId(profileName, out int profileId))
            {
                return false;
            }

            return profiles.IsInstalled(profileId);
        }
    }
}
