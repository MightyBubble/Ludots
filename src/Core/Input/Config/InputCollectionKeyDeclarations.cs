using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Engine;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Config
{
    /// <summary>
    /// Role to collection-key table from Input/collection_keys.json (constitution §08:
    /// the engine holds no builtin key table). Load points resolve authored
    /// <c>role:&lt;name&gt;</c> tokens through this table. A literal collection key has no prefix.
    /// </summary>
    public sealed class InputCollectionKeyDeclarations
    {
        // The prefix is the whole distinction. Anything that does not start with it is a literal collection key.
        public const string RoleTokenPrefix = "role:";

        private readonly Dictionary<string, string> _roles;
        private readonly Dictionary<string, int> _roleKeyIds;

        private InputCollectionKeyDeclarations(
            Dictionary<string, string> roles,
            Dictionary<string, int> roleKeyIds)
        {
            _roles = roles;
            _roleKeyIds = roleKeyIds;
        }

        public IReadOnlyDictionary<string, string> Roles => _roles;

        public bool TryGetRole(string role, out string collectionKey)
        {
            if (role != null && _roles.TryGetValue(role, out collectionKey!))
            {
                return true;
            }

            collectionKey = string.Empty;
            return false;
        }

        public string RequireRole(string role)
        {
            if (TryGetRole(role, out string collectionKey) && !string.IsNullOrWhiteSpace(collectionKey))
            {
                return collectionKey;
            }

            throw new InvalidOperationException(
                $"INPUT.COLLECTION_KEYS.ERR.RoleUnbound: Input/collection_keys.json role '{role}' is not bound.");
        }

        public int RequireRoleKeyId(string role)
        {
            if (role != null && _roleKeyIds.TryGetValue(role, out int keyId))
            {
                return keyId;
            }

            throw new InvalidOperationException(
                $"INPUT.COLLECTION_KEYS.ERR.RoleUnbound: Input/collection_keys.json role '{role}' is not bound.");
        }

        public static InputCollectionKeyDeclarations LoadAndRegister(
            global::Ludots.Core.Config.ConfigPipeline configs,
            StringIntRegistry keyRegistry,
            global::Ludots.Core.Config.ConfigCatalog catalog = null,
            global::Ludots.Core.Config.ConfigConflictReport report = null)
        {
            var entry = global::Ludots.Core.Config.ConfigPipeline.RequireEntry(
                catalog,
                "Input/collection_keys.json",
                global::Ludots.Core.Config.ConfigMergePolicy.DeepObject);
            List<global::Ludots.Core.Config.ConfigFragment> fragments = configs.CollectFragmentsWithSources(in entry);
            if (fragments.Count == 0)
            {
                throw new InvalidOperationException(
                    "INPUT.COLLECTION_KEYS.ERR.FileMissing: Input/collection_keys.json must declare input collection key roles (constitution §08: no builtin key fallback).");
            }

            var roles = new Dictionary<string, string>(StringComparer.Ordinal);
            var firstSource = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Node is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"INPUT.COLLECTION_KEYS.ERR.RoleValueType: '{fragments[i].SourceUri}' must be a JSON object of role to collection key.");
                }

                foreach (KeyValuePair<string, JsonNode?> property in obj)
                {
                    if (property.Value is not JsonValue value || !value.TryGetValue<string>(out string? collectionKey))
                    {
                        throw new InvalidOperationException(
                            $"INPUT.COLLECTION_KEYS.ERR.RoleValueType: '{fragments[i].SourceUri}' role '{property.Key}' must be a string collection key.");
                    }

                    if (string.IsNullOrWhiteSpace(collectionKey))
                    {
                        throw new InvalidOperationException(
                            $"INPUT.COLLECTION_KEYS.ERR.MissingRole: Input/collection_keys.json role '{property.Key}' in '{fragments[i].SourceUri}' is blank.");
                    }

                    if (roles.TryGetValue(property.Key, out string? existing) &&
                        !string.Equals(existing, collectionKey, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"INPUT.COLLECTION_KEYS.ERR.RoleConflict: Input/collection_keys.json role '{property.Key}' is '{existing}' in '{firstSource[property.Key]}' and '{collectionKey}' in '{fragments[i].SourceUri}'.");
                    }

                    if (!roles.ContainsKey(property.Key))
                    {
                        roles[property.Key] = collectionKey;
                        firstSource[property.Key] = fragments[i].SourceUri;
                    }
                }
            }

            _ = configs.MergeDeepObjectFromCatalog(in entry, report);

            var roleKeyIds = new Dictionary<string, int>(roles.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> role in roles)
            {
                roleKeyIds[role.Key] = keyRegistry.Register(role.Value);
            }

            return new InputCollectionKeyDeclarations(roles, roleKeyIds);
        }

        internal static InputCollectionKeyDeclarations ForResolvedRoles(
            StringIntRegistry keyRegistry,
            IReadOnlyDictionary<string, string> roles)
        {
            if (keyRegistry == null)
            {
                throw new ArgumentNullException(nameof(keyRegistry));
            }

            if (roles == null)
            {
                throw new ArgumentNullException(nameof(roles));
            }

            var copy = new Dictionary<string, string>(roles.Count, StringComparer.Ordinal);
            var roleKeyIds = new Dictionary<string, int>(roles.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> role in roles)
            {
                if (string.IsNullOrWhiteSpace(role.Value))
                {
                    throw new InvalidOperationException(
                        $"INPUT.COLLECTION_KEYS.ERR.MissingRole: role '{role.Key}' is blank.");
                }

                copy[role.Key] = role.Value;
                roleKeyIds[role.Key] = keyRegistry.Register(role.Value);
            }

            return new InputCollectionKeyDeclarations(copy, roleKeyIds);
        }

        public static InputCollectionKeyDeclarations Require(GameEngine engine)
        {
            if (engine == null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            return Require(engine.GlobalContext);
        }

        public static InputCollectionKeyDeclarations Require(IReadOnlyDictionary<string, object> globals)
        {
            if (globals != null &&
                globals.TryGetValue(CoreServiceKeys.InputCollectionKeys.Name, out object? raw) &&
                raw is InputCollectionKeyDeclarations declarations)
            {
                return declarations;
            }

            throw new InvalidOperationException(
                "INPUT.COLLECTION_KEYS.ERR.RoleUnbound: Input collection roles are not loaded.");
        }

        public static string RequireRole(GameEngine engine, string role)
        {
            return Require(engine).RequireRole(role);
        }

        public static string RequireRole(IReadOnlyDictionary<string, object> globals, string role)
        {
            return Require(globals).RequireRole(role);
        }

        public static string Resolve(InputCollectionKeyDeclarations? roles, string? authored, string site, string? sourceMod)
        {
            if (string.IsNullOrEmpty(authored) ||
                !authored.StartsWith(RoleTokenPrefix, StringComparison.Ordinal))
            {
                return authored ?? string.Empty;
            }

            string role = authored.Substring(RoleTokenPrefix.Length);
            string mod = string.IsNullOrEmpty(sourceMod) ? "(unknown)" : sourceMod;
            if (roles != null && roles.TryGetRole(role, out string collectionKey) && !string.IsNullOrWhiteSpace(collectionKey))
            {
                return collectionKey;
            }

            throw new InvalidOperationException(
                $"INPUT.COLLECTION_KEYS.ERR.RoleUnbound: {site} uses collection role '{role}', source mod '{mod}'. Input/collection_keys.json did not bind it.");
        }
    }
}
