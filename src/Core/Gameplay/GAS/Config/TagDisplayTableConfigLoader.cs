using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.TagDisplay;

namespace Ludots.Core.Gameplay.GAS.Config
{
    /// <summary>
    /// Loads tag display table entries from GAS/tag_display_tables.json (ArrayById on table id).
    /// Tables must be registered before graph programs are patched so displayTable symbol
    /// references resolve at compile time.
    /// </summary>
    public sealed class TagDisplayTableConfigLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly TagDisplayTableRegistry _registry;

        public TagDisplayTableConfigLoader(ConfigPipeline pipeline, TagDisplayTableRegistry registry)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "GAS/tag_display_tables.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.ArrayById, "id");
            var merged = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            var ordered = merged.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                RegisterTable(_registry, ordered[i].Node);
            }
        }

        public static void LoadFromJson(TagDisplayTableRegistry registry, string json)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var array = JsonNode.Parse(json ?? string.Empty)?.AsArray()
                ?? throw new InvalidOperationException("TagDisplayTableConfigLoader: JSON root must be an array.");
            for (int i = 0; i < array.Count; i++)
            {
                var table = array[i] as JsonObject
                    ?? throw new InvalidOperationException($"TagDisplayTableConfigLoader: entry[{i}] must be an object.");
                RegisterTable(registry, table);
            }
        }

        private static void RegisterTable(TagDisplayTableRegistry registry, JsonObject table)
        {
            string id = RequireString(table, "id");
            if (registry.TryGetTableId(id, out _))
            {
                return;
            }

            var entriesNode = table["entries"] as JsonArray
                ?? throw new InvalidOperationException($"Tag display table '{id}' must declare an entries array.");
            if (entriesNode.Count == 0)
            {
                throw new InvalidOperationException($"Tag display table '{id}' has no entries.");
            }

            var mask = new GameplayTagContainer();
            var entries = new List<(int TagId, int TokenId)>(entriesNode.Count);
            for (int i = 0; i < entriesNode.Count; i++)
            {
                var item = entriesNode[i] as JsonObject
                    ?? throw new InvalidOperationException($"Tag display table '{id}' entry[{i}] must be an object.");
                string tag = RequireString(item, "tag", $"table '{id}' entry[{i}]");
                int token = item["token"]?.GetValue<int>()
                    ?? throw new InvalidOperationException($"Tag display table '{id}' entry[{i}] needs an integer token.");
                if (token <= 0)
                {
                    throw new InvalidOperationException($"Tag display table '{id}' entry[{i}] token must be positive.");
                }

                int tagId = TagRegistry.Register(tag);
                mask.AddTag(tagId);
                entries.Add((tagId, token));
            }

            registry.RegisterTable(id, in mask, entries.ToArray());
        }

        private static string RequireString(JsonObject obj, string key, string context = null)
        {
            string value = obj[key]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Tag display table{(context == null ? string.Empty : " " + context)} requires a non-empty '{key}'.");
            }

            return value;
        }
    }
}
