using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Navigation.AgentProfiles
{
    public sealed class AgentProfileConfigLoader
    {
        public const string RelativePath = "Navigation/agent_profiles.json";

        private readonly ConfigPipeline _pipeline;

        public AgentProfileConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public AgentProfileRegistry Load(ConfigCatalog catalog = null, ConfigConflictReport report = null)
        {
            ConfigCatalogEntry entry = ConfigPipeline.RequireEntry(
                catalog,
                RelativePath,
                ConfigMergePolicy.ArrayById,
                defaultIdField: "id");
            IReadOnlyList<MergedConfigEntry> entries = _pipeline.MergeArrayByIdFromCatalog(in entry, report);
            if (entries == null || entries.Count == 0)
            {
                throw new InvalidOperationException("Navigation/agent_profiles.json must define at least one profile.");
            }

            var profiles = new List<AgentProfileConfig>(entries.Count);
            JsonSerializerOptions options = StrictJsonOptions.CreateCamelCase();
            for (int i = 0; i < entries.Count; i++)
            {
                ValidateRaw(entries[i].Node, i);
                AgentProfileConfig? profile = entries[i].Node.Deserialize<AgentProfileConfig>(options);
                if (profile == null)
                {
                    throw new InvalidOperationException($"AgentProfile[{i}] failed to deserialize.");
                }

                profiles.Add(profile);
            }

            return new AgentProfileRegistry(profiles);
        }

        private static readonly string[] RequiredProperties =
            { "id", "radiusCm", "heightCm", "clearanceCm", "draftCm", "beamCm", "mass", "layer", "navProfileId" };
        private static readonly string[] OptionalProperties = Array.Empty<string>();

        private static void ValidateRaw(JsonObject obj, int index)
        {
            string path = $"AgentProfile[{index}]";
            foreach (var property in obj)
            {
                bool known = false;
                for (int i = 0; i < RequiredProperties.Length; i++) known |= string.Equals(property.Key, RequiredProperties[i], StringComparison.Ordinal);
                for (int i = 0; i < OptionalProperties.Length; i++) known |= string.Equals(property.Key, OptionalProperties[i], StringComparison.Ordinal);
                if (!known)
                {
                    if (string.Equals(property.Key, "templateId", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{path} 已删除 templateId。模板 id 是实体模板自己的 id；群体速度写在 CrowdSimulationAgent.speedCmPerSecond。");
                    }

                    throw new InvalidOperationException($"{path} contains unknown property '{property.Key}'.");
                }
            }

            for (int i = 0; i < RequiredProperties.Length; i++)
            {
                if (!obj.ContainsKey(RequiredProperties[i]))
                {
                    throw new InvalidOperationException($"{path} must explicitly define '{RequiredProperties[i]}'.");
                }
            }
        }

    }
}
