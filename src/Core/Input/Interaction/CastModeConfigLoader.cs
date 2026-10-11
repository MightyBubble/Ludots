using System;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.Interaction
{
    /// <summary>
    /// Loader for <c>Input/cast_modes.json</c>: an ArrayById catalog of cast-mode ids.
    /// Core stores the ids and never interprets them. Entries are id-only; any other
    /// field fails fast so behaviour cannot hide in the catalog.
    /// </summary>
    public sealed class CastModeConfigLoader
    {
        public const string ConfigPath = "Input/cast_modes.json";

        private readonly ConfigPipeline _pipeline;

        public CastModeConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        /// <summary>Register every merged cast-mode id into <paramref name="registry"/>.</summary>
        public void Install(
            StringIntRegistry registry,
            ConfigCatalog catalog,
            ConfigConflictReport report,
            string relativePath = ConfigPath)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById);
            System.Collections.Generic.IReadOnlyList<MergedConfigEntry> merged =
                _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                RegisterEntry(registry, merged[i].Node, relativePath, i);
            }
        }

        /// <summary>
        /// Register one catalog object. Fails when the object is not id-only, when
        /// <c>id</c> is missing or padded, or when the id is already registered.
        /// </summary>
        public static int RegisterEntry(StringIntRegistry registry, JsonObject node, string source, int index)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (node == null)
            {
                throw new InvalidOperationException($"{source} entry #{index} must be an object with an 'id'.");
            }

            foreach (System.Collections.Generic.KeyValuePair<string, JsonNode?> prop in node)
            {
                if (!string.Equals(prop.Key, "id", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{source} entry #{index} declares field '{prop.Key}', but cast mode entries are id-only.");
                }
            }

            if (node["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out string? rawId) || string.IsNullOrWhiteSpace(rawId))
            {
                throw new InvalidOperationException($"{source} entry #{index} field 'id' must be a non-empty string.");
            }

            string id = rawId;

            if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{source} entry #{index} field 'id' must not contain leading or trailing whitespace.");
            }

            if (registry.Contains(id))
            {
                throw new InvalidOperationException($"{source} entry #{index} duplicates cast mode id '{id}'.");
            }

            return registry.Register(id);
        }
    }
}
