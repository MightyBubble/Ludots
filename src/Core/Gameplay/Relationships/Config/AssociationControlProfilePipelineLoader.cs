using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Gameplay.Relationships.Config
{
    /// <summary>
    /// Loads and merges <c>Relationships/control_profiles.json</c> fragments across Core and mods
    /// (same ConfigPipeline pattern as <see cref="RelationshipCatalogPipelineLoader"/>).
    /// A missing file yields an empty profile list, which makes the runtime a no-op.
    /// </summary>
    public sealed class AssociationControlProfilePipelineLoader
    {
        private readonly ConfigPipeline _pipeline;
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public AssociationControlProfilePipelineLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public AssociationControlProfileCatalogConfig Load(
            ConfigCatalog? catalog = null,
            ConfigConflictReport? report = null,
            string relativePath = "Relationships/control_profiles.json")
        {
            var fragments = _pipeline.CollectFragmentsWithSources(relativePath);
            if (report != null)
            {
                for (int i = 0; i < fragments.Count; i++)
                {
                    report.RecordFragment(relativePath, fragments[i].SourceUri);
                }
            }

            var byId = new Dictionary<string, AssociationControlProfileConfig>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            for (int i = 0; i < fragments.Count; i++)
            {
                AssociationControlProfileCatalogConfig? fragment = fragments[i].Node.Deserialize<AssociationControlProfileCatalogConfig>(_options);
                if (fragment?.Profiles == null)
                {
                    continue;
                }

                for (int p = 0; p < fragment.Profiles.Count; p++)
                {
                    AssociationControlProfileConfig? profile = fragment.Profiles[p];
                    if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
                    {
                        throw new InvalidOperationException(
                            $"Control profile fragment '{fragments[i].SourceUri}' contains a profile without a non-empty id.");
                    }

                    if (!byId.ContainsKey(profile.Id))
                    {
                        order.Add(profile.Id);
                    }

                    byId[profile.Id] = profile;
                }
            }

            var result = new AssociationControlProfileCatalogConfig();
            for (int i = 0; i < order.Count; i++)
            {
                result.Profiles.Add(byId[order[i]]);
            }

            return result;
        }
    }
}
