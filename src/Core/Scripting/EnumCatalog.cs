using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Scripting
{
    /// <summary>
    /// Pure-data enum declaration (<c>Enums/enums.json</c>). Member values are the
    /// declaration-order index (0, 1, 2, ...) — explicit values are not authored, so
    /// appending members from later mods never re-numbers earlier ones. Graph sugar
    /// resolves member names to these ints at compile time; the runtime only ever sees ints.
    /// </summary>
    public sealed class EnumSchema
    {
        public EnumSchema(string typeName, IReadOnlyList<string> members)
        {
            if (string.IsNullOrWhiteSpace(typeName)) throw new ArgumentException("Enum type name is required.", nameof(typeName));
            if (members == null || members.Count == 0) throw new ArgumentException("Enum requires at least one member.", nameof(members));

            TypeName = typeName;
            Members = members;
        }

        public string TypeName { get; }
        public IReadOnlyList<string> Members { get; }

        public bool TryGetValue(string memberName, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(memberName)) return false;
            for (int i = 0; i < Members.Count; i++)
            {
                if (string.Equals(Members[i], memberName, StringComparison.Ordinal))
                {
                    value = i;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetName(int value, out string memberName)
        {
            memberName = string.Empty;
            if ((uint)value >= (uint)Members.Count) return false;
            memberName = Members[value];
            return true;
        }
    }

    /// <summary>
    /// Loaded enum vocabulary shared by graph compilation (SwitchOnEnum / SelectByEnum sugar,
    /// event param <c>enumType</c> annotations). Construction goes through <see cref="Builder"/>
    /// or <see cref="EnumCatalogLoader"/>; there is no runtime mutation.
    /// </summary>
    public sealed class EnumCatalog
    {
        public static EnumCatalog Empty { get; } = new(new Dictionary<string, EnumSchema>(StringComparer.Ordinal));

        private readonly Dictionary<string, EnumSchema> _byType;

        private EnumCatalog(Dictionary<string, EnumSchema> byType)
        {
            _byType = byType;
        }

        public IReadOnlyCollection<EnumSchema> All => _byType.Values;

        public bool TryGet(string typeName, out EnumSchema schema)
        {
            schema = null!;
            if (string.IsNullOrWhiteSpace(typeName)) return false;
            return _byType.TryGetValue(typeName.Trim(), out schema!);
        }

        /// <summary>
        /// Accumulates enum entries across mods: first sight of a type name claims the
        /// declaration order; later fragments may only append members they do not already
        /// declare — re-declaring a member is a value-change attempt and fails closed.
        /// </summary>
        public sealed class Builder
        {
            private readonly Dictionary<string, List<string>> _members = new(StringComparer.Ordinal);
            private readonly HashSet<string> _frozen = new(StringComparer.Ordinal);

            public void AddOrAppend(JsonObject entry, string context)
            {
                (string typeName, List<string> members) = EnumEntryParser.Parse(entry, context);
                if (!_members.TryGetValue(typeName, out List<string>? existing))
                {
                    _members[typeName] = members;
                    return;
                }

                for (int i = 0; i < members.Count; i++)
                {
                    if (existing.Contains(members[i], StringComparer.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{context}: enum '{typeName}' already declares member '{members[i]}'; " +
                            "appending cannot change an existing member's value.");
                    }
                }

                existing.AddRange(members);
            }

            public EnumCatalog ToCatalog()
            {
                var byType = new Dictionary<string, EnumSchema>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, List<string>> pair in _members)
                {
                    byType[pair.Key] = new EnumSchema(pair.Key, pair.Value);
                }

                return new EnumCatalog(byType);
            }
        }
    }

    /// <summary>
    /// Strict parser for one <c>Enums/enums.json</c> entry: <c>{ id, description?, members: [name...] }</c>.
    /// Unknown fields, missing id, missing/empty members, duplicate member names, and
    /// invalid name shapes fail closed. Member names are identifiers (ASCII letters,
    /// digits, underscore, first char a letter); type ids additionally allow '.', '-'.
    /// </summary>
    public static class EnumEntryParser
    {
        private static readonly string[] EntryFields = { "id", "description", "members" };

        public static (string TypeName, List<string> Members) Parse(JsonObject entry, string context)
        {
            if (entry == null) throw new InvalidOperationException($"{context} must be an object.");

            foreach (KeyValuePair<string, JsonNode?> field in entry)
            {
                if (!IsKnownEntryField(field.Key))
                {
                    throw new InvalidOperationException(
                        $"{context} has unknown field '{field.Key}'; allowed: {string.Join(", ", EntryFields)}.");
                }
            }

            string? id = entry["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException($"{context} requires a non-empty 'id'.");
            }

            string typeName = id.Trim();
            if (!IsValidTypeId(typeName))
            {
                throw new InvalidOperationException(
                    $"{context} id '{typeName}' is invalid: letters, digits, '.', '_', '-' only, first char a letter.");
            }

            if (entry["members"] is not JsonArray membersArray)
            {
                throw new InvalidOperationException($"{context} requires a 'members' array.");
            }

            if (membersArray.Count == 0)
            {
                throw new InvalidOperationException($"{context} declares no members; enums require at least one.");
            }

            var members = new List<string>(membersArray.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < membersArray.Count; i++)
            {
                string? member = membersArray[i] is JsonValue v && v.TryGetValue<string>(out string? text) ? text : null;
                if (string.IsNullOrWhiteSpace(member))
                {
                    throw new InvalidOperationException($"{context} members[{i}] must be a non-empty string.");
                }

                string name = member.Trim();
                if (!IsValidMemberName(name))
                {
                    throw new InvalidOperationException(
                        $"{context} members[{i}] '{name}' is invalid: member names are ASCII letters, digits, " +
                        "and underscores, first char a letter.");
                }

                if (!seen.Add(name))
                {
                    throw new InvalidOperationException($"{context} declares member '{name}' more than once.");
                }

                members.Add(name);
            }

            return (typeName, members);
        }

        public static bool IsValidMemberName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!char.IsAsciiLetter(name[0])) return false;
            for (int i = 1; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
            }

            return true;
        }

        public static bool IsValidTypeId(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return false;
            if (!char.IsAsciiLetter(typeName[0])) return false;
            for (int i = 1; i < typeName.Length; i++)
            {
                char c = typeName[i];
                if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-') return false;
            }

            return true;
        }

        private static bool IsKnownEntryField(string key)
        {
            for (int i = 0; i < EntryFields.Length; i++)
            {
                if (string.Equals(EntryFields[i], key, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Config-pipeline loader for <c>Enums/enums.json</c>: ArrayById on "id" with
    /// <c>ArrayAppendFields: ["members"]</c> declared in the mod's config_catalog.json,
    /// so later mods append members while earlier declaration order (the values) is frozen.
    /// </summary>
    public sealed class EnumCatalogLoader
    {
        public const string ConfigPath = "Enums/enums.json";

        private readonly ConfigPipeline _configs;

        public EnumCatalogLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public EnumCatalog Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
        {
            if (catalog == null || !catalog.TryGet(ConfigPath, out ConfigCatalogEntry entry))
            {
                return EnumCatalog.Empty;
            }

            var builder = new EnumCatalog.Builder();
            IReadOnlyList<MergedConfigEntry> merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject node)
                {
                    throw new InvalidOperationException($"{ConfigPath} entry #{i} must be an object.");
                }

                builder.AddOrAppend(node, $"{ConfigPath} entry '{merged[i].Id}'");
            }

            return builder.ToCatalog();
        }
    }
}
