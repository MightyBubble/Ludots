using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Presenters;

namespace Ludots.Core.CrowdSimulation.Config;

public static partial class CrowdSimulationEditorApi
{
    public const string PresentersFile = PresenterDefinitionConfigLoader.PresentersFile;

    private static readonly string[] AgentProfileBodyFields =
    [
        JsonNamingPolicy.CamelCase.ConvertName(nameof(AgentProfileConfig.RadiusCm)),
        JsonNamingPolicy.CamelCase.ConvertName(nameof(AgentProfileConfig.HeightCm)),
    ];

    internal sealed partial class EditorSession
    {
        /// <summary>
        /// 按合并顺序给 ArrayById 文件的每个条目、每个叶子记下最后写它的片段。
        /// 数组是一个叶子：ArrayById 的同 id 合并整段替换数组。
        /// </summary>
        private sealed class FieldStamps
        {
            private readonly Dictionary<string, Dictionary<string, string>> _leaves = new(StringComparer.Ordinal);
            private readonly Dictionary<string, HashSet<string>> _writers = new(StringComparer.Ordinal);

            public static FieldStamps Collect(Opened opened, string relative)
            {
                var stamps = new FieldStamps();
                if (!opened.Catalog.TryGet(relative, out ConfigCatalogEntry entry))
                {
                    return stamps;
                }

                List<ConfigFragment> fragments = opened.Pipeline.CollectFragmentsWithSources(in entry);
                for (int i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i].Node is not JsonArray items)
                    {
                        continue;
                    }

                    foreach (JsonNode? item in items)
                    {
                        if (item is not JsonObject obj)
                        {
                            continue;
                        }

                        string id = IdOf(obj, entry.IdField);
                        if (id.Length == 0)
                        {
                            continue;
                        }

                        if (IsDeletion(obj))
                        {
                            stamps._leaves.Remove(id);
                            stamps._writers.Remove(id);
                            continue;
                        }

                        if (!stamps._leaves.TryGetValue(id, out Dictionary<string, string>? leaves))
                        {
                            leaves = new Dictionary<string, string>(StringComparer.Ordinal);
                            stamps._leaves[id] = leaves;
                            stamps._writers[id] = new HashSet<string>(StringComparer.Ordinal);
                        }

                        StampLeaves(string.Empty, obj, fragments[i].SourceUri, leaves);
                        stamps._writers[id].Add(fragments[i].SourceUri);
                    }
                }

                return stamps;
            }

            public string? Leaf(string id, string path)
                => _leaves.TryGetValue(id, out Dictionary<string, string>? leaves) && leaves.TryGetValue(path, out string? source)
                    ? source
                    : null;

            public bool WrittenBy(string id, string modId)
            {
                if (!_writers.TryGetValue(id, out HashSet<string>? writers))
                {
                    return false;
                }

                foreach (string writer in writers)
                {
                    if (string.Equals(CrowdSimulationAuthoringIssues.ModIdOf(writer), modId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool IsDeletion(JsonObject obj)
            {
                foreach (string key in new[] { "__delete", "Disabled" })
                {
                    if (obj[key] is JsonValue value && value.TryGetValue(out bool flag) && flag)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private static JsonObject UnitTemplatesView(
            Opened full,
            string modId,
            JsonArray mergedTemplates,
            JsonArray mergedProfiles)
        {
            var schema = new JsonArray();
            foreach (CrowdUnitTemplateField field in CrowdUnitTemplateFacts.AuthoredFields)
            {
                schema.Add(new JsonObject
                {
                    ["name"] = field.Name,
                    ["kind"] = field.Kind,
                    ["reference"] = field.Reference,
                });
            }

            var profileSchema = new JsonArray();
            foreach (string name in AgentProfileBodyFields)
            {
                profileSchema.Add(new JsonObject { ["name"] = name, ["kind"] = "number" });
            }

            FieldStamps profileStamps = FieldStamps.Collect(full, AgentProfilesFile);
            Dictionary<string, JsonObject> profilesById = IndexById(mergedProfiles, "id");
            var profileList = new JsonArray();
            foreach (JsonNode? node in mergedProfiles)
            {
                if (node is JsonObject profile && IdOf(profile, "id") is { Length: > 0 } profileId)
                {
                    profileList.Add(new JsonObject
                    {
                        ["id"] = profileId,
                        ["radiusCm"] = profile["radiusCm"]?.DeepClone(),
                        ["navProfileId"] = profile["navProfileId"]?.DeepClone(),
                    });
                }
            }

            var view = new JsonObject
            {
                ["component"] = CrowdUnitTemplateFacts.ComponentName,
                ["fields"] = schema,
                ["profileFields"] = profileSchema,
                [CrowdUnitTemplateFacts.AgentProfilesReference] = profileList,
                ["items"] = new JsonArray(),
                ["issues"] = new JsonArray(),
            };

            List<EntityTemplate> expanded;
            try
            {
                expanded = full.LoadTemplates(new ConfigConflictReport());
            }
            catch (Exception ex)
            {
                var issues = (JsonArray)view["issues"]!;
                foreach (CrowdSimulationAuthoringIssue issue in CrowdSimulationAuthoringIssues.From(ex))
                {
                    issues.Add(issue.ToJson());
                }

                return view;
            }

            var expandedById = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
            foreach (EntityTemplate template in expanded)
            {
                expandedById[template.Id] = template;
            }

            Dictionary<string, JsonObject> rawById = IndexById(mergedTemplates, "id");
            FieldStamps templateStamps = FieldStamps.Collect(full, TemplatesFile);
            PresenterSpawnLinks? presenters = SpawnLinksOrIssue(full, expanded, (JsonArray)view["issues"]!);
            var crowd = new List<(string Id, JsonObject Raw, JsonObject Agent)>();
            var usedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (JsonNode? node in mergedTemplates)
            {
                if (node is not JsonObject raw
                    || IdOf(raw, "id") is not { Length: > 0 } id
                    || !expandedById.TryGetValue(id, out EntityTemplate? template)
                    || !template.Components.TryGetValue(CrowdUnitTemplateFacts.ComponentName, out JsonNode? component)
                    || component is not JsonObject agent)
                {
                    continue;
                }

                crowd.Add((id, raw, agent));
                if (Text(agent[CrowdUnitTemplateFacts.ProfileIdField]) is { Length: > 0 } usedProfile)
                {
                    if (!usedBy.TryGetValue(usedProfile, out List<string>? users))
                    {
                        users = new List<string>();
                        usedBy[usedProfile] = users;
                    }

                    users.Add(id);
                }
            }

            var items = (JsonArray)view["items"]!;
            foreach ((string id, JsonObject raw, JsonObject agent) in crowd)
            {
                var fieldSources = new JsonObject();
                foreach (CrowdUnitTemplateField field in CrowdUnitTemplateFacts.AuthoredFields)
                {
                    string path = "components." + CrowdUnitTemplateFacts.ComponentName + "." + field.Name;
                    JsonObject? source = InheritedFieldSource(id, path, templateStamps, rawById, new HashSet<string>(StringComparer.Ordinal));
                    if (source != null)
                    {
                        fieldSources[field.Name] = source;
                    }
                }

                var inherits = new JsonArray();
                foreach (string parent in Parents(raw))
                {
                    inherits.Add(parent);
                }

                JsonObject? profileView = null;
                if (Text(agent[CrowdUnitTemplateFacts.ProfileIdField]) is { Length: > 0 } profileId
                    && profilesById.TryGetValue(profileId, out JsonObject? profile))
                {
                    var users = new JsonArray();
                    foreach (string user in usedBy[profileId])
                    {
                        users.Add(user);
                    }

                    var values = new JsonObject();
                    var sources = new JsonObject();
                    foreach (string name in AgentProfileBodyFields)
                    {
                        values[name] = profile[name]?.DeepClone();
                        if (profileStamps.Leaf(profileId, name) is string uri)
                        {
                            sources[name] = Source(uri, null);
                        }
                    }

                    profileView = new JsonObject
                    {
                        ["id"] = profileId,
                        ["navProfileId"] = profile["navProfileId"]?.DeepClone(),
                        ["values"] = values,
                        ["fields"] = sources,
                        ["ownedBySelection"] = profileStamps.WrittenBy(profileId, modId),
                        ["usedBy"] = users,
                    };
                }

                items.Add(new JsonObject
                {
                    ["id"] = id,
                    ["ownedBySelection"] = templateStamps.WrittenBy(id, modId),
                    ["inherits"] = inherits,
                    ["agent"] = agent.DeepClone(),
                    ["fields"] = fieldSources,
                    ["profile"] = profileView,
                    ["presenters"] = PresenterLinksOf(presenters, id),
                });
            }

            return view;
        }

        /// <summary>
        /// 字段先看模板自己的片段；没有就按 extends / uses 的折叠优先级（后声明者胜，extends 最低）往上找。
        /// </summary>
        private static JsonObject? InheritedFieldSource(
            string id,
            string path,
            FieldStamps stamps,
            Dictionary<string, JsonObject> rawById,
            HashSet<string> visited)
        {
            if (stamps.Leaf(id, path) is string uri)
            {
                return Source(uri, id);
            }

            if (!visited.Add(id) || !rawById.TryGetValue(id, out JsonObject? raw))
            {
                return null;
            }

            List<string> parents = Parents(raw);
            for (int i = parents.Count - 1; i >= 0; i--)
            {
                JsonObject? found = InheritedFieldSource(parents[i], path, stamps, rawById, visited);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static List<string> Parents(JsonObject raw)
        {
            var parents = new List<string>();
            if (raw["extends"] is JsonValue extends && extends.TryGetValue(out string? parent) && !string.IsNullOrWhiteSpace(parent))
            {
                parents.Add(parent.Trim());
            }

            if (raw["uses"] is JsonArray uses)
            {
                foreach (JsonNode? use in uses)
                {
                    if (use is JsonValue value && value.TryGetValue(out string? block) && !string.IsNullOrWhiteSpace(block))
                    {
                        parents.Add(block.Trim());
                    }
                }
            }

            return parents;
        }

        /// <summary>和表现装载同一份解析。链接本身有错时检视器不列表现，报错进 issues。</summary>
        private static PresenterSpawnLinks? SpawnLinksOrIssue(Opened full, List<EntityTemplate> templates, JsonArray issues)
        {
            try
            {
                return LoadSpawnLinks(full, templates, new ConfigConflictReport());
            }
            catch (InvalidOperationException ex)
            {
                foreach (CrowdSimulationAuthoringIssue issue in CrowdSimulationAuthoringIssues.From(ex))
                {
                    issues.Add(issue.ToJson());
                }

                return null;
            }
        }

        private static JsonArray PresenterLinksOf(PresenterSpawnLinks? links, string templateId)
        {
            var list = new JsonArray();
            if (links == null)
            {
                return list;
            }

            foreach (PresenterSpawnLink link in links.For(templateId))
            {
                JsonObject node = Source(link.Source, null);
                node["definitionId"] = link.DefinitionId;
                node["ruleOwner"] = link.OwnerId;
                node["rule"] = link.Rule;
                node["templateKey"] = link.TemplateId;
                list.Add(node);
            }

            return list;
        }

        private static string? Text(JsonNode? node)
            => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

        private static JsonObject Source(string sourceUri, string? templateId)
        {
            var source = new JsonObject
            {
                ["sourceMod"] = CrowdSimulationAuthoringIssues.ModIdOf(sourceUri),
                ["sourceFile"] = sourceUri,
            };
            if (templateId != null)
            {
                source["template"] = templateId;
            }

            return source;
        }
    }
}
