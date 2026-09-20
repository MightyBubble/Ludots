using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;
using Ludots.Core.Scripting;

namespace Ludots.Core.Fields.Config
{
    /// <summary>One authored emission contract for a field region key.</summary>
    public sealed class FieldRegionEmissionEntry
    {
        public string Id { get; set; } = string.Empty;
        public string? Layer { get; set; }
        public JsonObject? Emit { get; set; }
    }

    /// <summary>
    /// Config-pipeline loader for <c>Fields/region_emissions.json</c> (ArrayById,
    /// id = region key): attaches the RegionVolume emission contract to field region
    /// entities at materialization (#1468). Structural parsing via the shared
    /// component authoring, semantic validation via the shared bake-pass validator —
    /// unknown events, schema-divergent payloads, and reserved keys fail closed at
    /// map load naming the region key.
    /// </summary>
    public sealed class FieldRegionEmissionLoader
    {
        public const string ConfigPath = "Fields/region_emissions.json";

        private readonly ConfigPipeline _configs;

        public FieldRegionEmissionLoader(ConfigPipeline configs)
        {
            _configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public IReadOnlyDictionary<string, RegionVolumeEmissionCm> Load(
            ConfigCatalog? catalog,
            ConfigConflictReport? report,
            string mapId,
            CustomEventNameRegistry customEvents,
            EventSchemaRegistry schemas)
        {
            var table = new Dictionary<string, RegionVolumeEmissionCm>(StringComparer.Ordinal);
            if (catalog == null || !catalog.TryGet(ConfigPath, out var entry))
            {
                return table;
            }

            IReadOnlyList<MergedConfigEntry> merged = _configs.MergeArrayByIdFromCatalog(in entry, report);
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i].Node is not JsonObject node)
                {
                    throw new InvalidOperationException(
                        $"{ConfigPath} entry #{i} must be an object.");
                }

                var parsed = new FieldRegionEmissionEntry();
                foreach (var kvp in node)
                {
                    switch (kvp.Key)
                    {
                        case "id":
                            parsed.Id = kvp.Value?.GetValue<string>() ?? string.Empty;
                            break;
                        case "layer":
                            parsed.Layer = kvp.Value?.GetValue<string>();
                            break;
                        case "emit":
                            parsed.Emit = kvp.Value as JsonObject;
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"{ConfigPath} entry #{i} has unknown field '{kvp.Key}'; allowed: id, layer, emit.");
                    }
                }

                if (string.IsNullOrWhiteSpace(parsed.Id) || !string.Equals(parsed.Id, parsed.Id.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{ConfigPath} entry #{i} requires a trimmed non-empty 'id' (the region key).");
                }

                if (parsed.Emit == null)
                {
                    throw new InvalidOperationException(
                        $"{ConfigPath} entry '{parsed.Id}' requires an 'emit' object.");
                }

                if (table.ContainsKey(parsed.Id))
                {
                    throw new InvalidOperationException(
                        $"{ConfigPath} has duplicate region key '{parsed.Id}'.");
                }

                RegionVolumeEmissionCm emission = RegionVolumeComponentAuthoring.ParseEmission(
                    parsed.Emit, $"{ConfigPath} entry '{parsed.Id}'");
                table[parsed.Id] = RegionVolumeBakePass.ValidateAndCanonicalizeEmission(
                    emission, parsed.Id, mapId, customEvents, schemas);
            }

            return table;
        }
    }
}
