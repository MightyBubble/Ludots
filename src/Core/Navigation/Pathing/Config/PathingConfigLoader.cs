using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ludots.Core.Config;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public sealed class PathingConfigLoader
    {
        private readonly ConfigPipeline _pipeline;

        public PathingConfigLoader(ConfigPipeline pipeline)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        }

        public PathingConfig Load(
            ConfigCatalog catalog = null,
            ConfigConflictReport report = null,
            string relativePath = "Navigation/pathing.json")
        {
            var entry = ConfigPipeline.RequireEntry(catalog, relativePath, ConfigMergePolicy.DeepObject);
            var mergedObject = _pipeline.MergeDeepObjectFromCatalog(in entry, report);
            if (mergedObject == null) throw new InvalidOperationException("PathingConfig not found in any source.");

            ValidateRaw(mergedObject);
            var opts = StrictJsonOptions.CreateCamelCase();
            opts.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            var cfg = mergedObject.Deserialize<PathingConfig>(opts);
            if (cfg == null) throw new InvalidOperationException("Failed to deserialize PathingConfig.");
            Validate(cfg);
            return cfg;
        }

        private static void ValidateRaw(JsonObject root)
        {
            RequireOnlyProperties(root, "PathingConfig", "agentTypes");
            if (root["agentTypes"] is not JsonArray agentTypes || agentTypes.Count == 0)
            {
                throw new InvalidOperationException("PathingConfig.agentTypes must be a non-empty explicit array.");
            }

            for (int i = 0; i < agentTypes.Count; i++)
            {
                if (agentTypes[i] is not JsonObject agent)
                {
                    throw new InvalidOperationException($"PathingConfig.agentTypes[{i}] must be an object.");
                }

                string agentPath = $"PathingConfig.agentTypes[{i}]";
                RequireOnlyProperties(agent, agentPath, "id", "profileId", "layer", "selection", "navMesh", "nodeGraph");
                RequireString(agent, "id", agentPath);
                RequireString(agent, "profileId", agentPath);
                RequireNumber(agent, "layer", agentPath);

                if (agent["selection"] is not JsonObject selection)
                {
                    throw new InvalidOperationException($"{agentPath}.selection must be an explicit object.");
                }

                RequireOnlyProperties(selection, $"{agentPath}.selection", "mode", "graphBias", "meshBias", "graphCostWeight", "meshCostWeight");
                string mode = RequireString(selection, "mode", $"{agentPath}.selection");
                if (!string.Equals(mode, nameof(PathSelectionMode.AutoCheapest), StringComparison.Ordinal) &&
                    !string.Equals(mode, nameof(PathSelectionMode.PreferGraph), StringComparison.Ordinal) &&
                    !string.Equals(mode, nameof(PathSelectionMode.PreferMesh), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{agentPath}.selection.mode '{mode}' is not a canonical path selection mode.");
                }

                RequireNumber(selection, "graphBias", $"{agentPath}.selection");
                RequireNumber(selection, "meshBias", $"{agentPath}.selection");
                RequireNumber(selection, "graphCostWeight", $"{agentPath}.selection");
                RequireNumber(selection, "meshCostWeight", $"{agentPath}.selection");

                if (agent["navMesh"] is not JsonObject navMesh)
                {
                    throw new InvalidOperationException($"{agentPath}.navMesh must be an explicit object.");
                }

                RequireOnlyProperties(navMesh, $"{agentPath}.navMesh", "areaCosts");
                RequireAreaCosts(navMesh, $"{agentPath}.navMesh");

                if (agent["nodeGraph"] is not JsonObject nodeGraph)
                {
                    throw new InvalidOperationException($"{agentPath}.nodeGraph must be an explicit object.");
                }

                RequireOnlyProperties(nodeGraph, $"{agentPath}.nodeGraph", "projectionMaxRadiusCm", "requiredTagsAll", "forbiddenTagsAny", "tagCostRules");
                RequireNumber(nodeGraph, "projectionMaxRadiusCm", $"{agentPath}.nodeGraph");
                RequireStringArray(nodeGraph, "requiredTagsAll", $"{agentPath}.nodeGraph");
                RequireStringArray(nodeGraph, "forbiddenTagsAny", $"{agentPath}.nodeGraph");
                RequireTagCostRules(nodeGraph, $"{agentPath}.nodeGraph");
            }
        }

        private static void RequireOnlyProperties(JsonObject obj, string path, params string[] allowed)
        {
            foreach (var property in obj)
            {
                bool known = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(property.Key, allowed[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new InvalidOperationException($"{path} contains unknown property '{property.Key}'.");
                }
            }

            for (int i = 0; i < allowed.Length; i++)
            {
                if (!obj.ContainsKey(allowed[i]))
                {
                    throw new InvalidOperationException($"{path} must explicitly define '{allowed[i]}'.");
                }
            }
        }

        private static string RequireString(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException($"{path}.{key} must be a non-empty string.");
            }

            if (!string.Equals(text.Trim(), text, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{path}.{key} must not contain leading or trailing whitespace.");
            }

            return text;
        }

        private static void RequireNumber(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonValue value ||
                (!value.TryGetValue<int>(out _) &&
                 !value.TryGetValue<float>(out _) &&
                 !value.TryGetValue<double>(out _)))
            {
                throw new InvalidOperationException($"{path}.{key} must be a number.");
            }
        }

        private static void RequireStringArray(JsonObject obj, string key, string path)
        {
            if (obj[key] is not JsonArray array)
            {
                throw new InvalidOperationException($"{path}.{key} must be an explicit string array.");
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonValue value ||
                    !value.TryGetValue<string>(out string? text) ||
                    string.IsNullOrWhiteSpace(text) ||
                    !string.Equals(text.Trim(), text, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.{key}[{i}] must be a canonical non-empty string.");
                }
            }
        }

        private static void RequireAreaCosts(JsonObject obj, string path)
        {
            if (obj["areaCosts"] is not JsonArray areaCosts)
            {
                throw new InvalidOperationException($"{path}.areaCosts must be an explicit array.");
            }

            for (int i = 0; i < areaCosts.Count; i++)
            {
                if (areaCosts[i] is not JsonObject area)
                {
                    throw new InvalidOperationException($"{path}.areaCosts[{i}] must be an object.");
                }

                string areaPath = $"{path}.areaCosts[{i}]";
                RequireOnlyProperties(area, areaPath, "areaId", "cost");
                RequireNumber(area, "areaId", areaPath);
                RequireNumber(area, "cost", areaPath);
            }
        }

        private static void RequireTagCostRules(JsonObject obj, string path)
        {
            if (obj["tagCostRules"] is not JsonArray rules)
            {
                throw new InvalidOperationException($"{path}.tagCostRules must be an explicit array.");
            }

            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] is not JsonObject rule)
                {
                    throw new InvalidOperationException($"{path}.tagCostRules[{i}] must be an object.");
                }

                string rulePath = $"{path}.tagCostRules[{i}]";
                RequireOnlyProperties(rule, rulePath, "tag", "costMul", "costAdd", "block");
                RequireString(rule, "tag", rulePath);
                RequireNumber(rule, "costMul", rulePath);
                RequireNumber(rule, "costAdd", rulePath);
                if (rule["block"] is not JsonValue value || !value.TryGetValue<bool>(out _))
                {
                    throw new InvalidOperationException($"{rulePath}.block must be a boolean.");
                }
            }
        }

        private static void Validate(PathingConfig cfg)
        {
            if (cfg.AgentTypes == null || cfg.AgentTypes.Count == 0) throw new InvalidOperationException("PathingConfig.agentTypes is empty.");
            for (int i = 0; i < cfg.AgentTypes.Count; i++)
            {
                var a = cfg.AgentTypes[i];
                if (a == null) throw new InvalidOperationException("PathingConfig.agentTypes contains null.");
                if (string.IsNullOrWhiteSpace(a.Id)) throw new InvalidOperationException("PathingConfig.agentTypes.id is required.");
                if (string.IsNullOrWhiteSpace(a.ProfileId)) throw new InvalidOperationException($"PathingConfig.agentTypes[{a.Id}].profileId is required.");
                if (a.NavMesh == null) a.NavMesh = new PathingNavMeshConfig();
                if (a.NodeGraph == null) a.NodeGraph = new PathingNodeGraphConfig();
                if (a.Selection == null) a.Selection = new PathingSelectionConfig();
                if (a.NavMesh.AreaCosts == null) a.NavMesh.AreaCosts = new();
                if (a.NodeGraph.RequiredTagsAll == null) a.NodeGraph.RequiredTagsAll = new();
                if (a.NodeGraph.ForbiddenTagsAny == null) a.NodeGraph.ForbiddenTagsAny = new();
                if (a.NodeGraph.TagCostRules == null) a.NodeGraph.TagCostRules = new();
            }
        }
    }
}
