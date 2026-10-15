using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Config;

public static partial class CrowdSimulationEditorApi
{
    public const string PresentersFile = "Presentation/presenters.json";

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
            JsonArray mergedProfiles,
            JsonObject pathing)
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

            var agentTypes = new JsonArray();
            if (pathing["agentTypes"] is JsonObject agents)
            {
                foreach (KeyValuePair<string, JsonNode?> agent in agents)
                {
                    agentTypes.Add(agent.Key);
                }
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
                [CrowdUnitTemplateFacts.AgentTypesReference] = agentTypes,
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
            Dictionary<string, JsonArray> presenters = PresenterLinks(full);
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
                    ["presenters"] = presenters.TryGetValue(id, out JsonArray? links) ? links.DeepClone() : new JsonArray(),
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

        /// <summary>模板 id → 生成时创建的表现。两种写法：rules 里的 EntitySpawned → CreatePresenter，或定义自己的 bindSpawn.template。</summary>
        private static Dictionary<string, JsonArray> PresenterLinks(Opened full)
        {
            var links = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
            JsonArray presenters = full.ReadArray(PresentersFile, new ConfigConflictReport());
            if (presenters.Count == 0)
            {
                return links;
            }

            FieldStamps stamps = FieldStamps.Collect(full, PresentersFile);
            Dictionary<string, JsonObject> defined = IndexById(presenters, "id");
            foreach (JsonNode? node in presenters)
            {
                if (node is not JsonObject presenter || IdOf(presenter, "id") is not { Length: > 0 } owner)
                {
                    continue;
                }

                if (presenter["bindSpawn"] is JsonObject bind
                    && bind["template"] is JsonValue templateValue
                    && templateValue.TryGetValue(out string? boundTemplate))
                {
                    AddLink(links, boundTemplate, owner, owner, defined, stamps.Leaf(owner, "bindSpawn.template"));
                }

                if (presenter["rules"] is not JsonArray rules)
                {
                    continue;
                }

                foreach (JsonNode? ruleNode in rules)
                {
                    if (ruleNode is JsonObject rule
                        && rule["event"] is JsonObject ev
                        && Text(ev["kind"]) == nameof(PresentationEventKind.EntitySpawned)
                        && Text(ev["key"]) is { Length: > 0 } spawnedTemplate
                        && rule["command"] is JsonObject command
                        && Text(command["kind"]) == nameof(PresenterCommandKind.CreatePresenter)
                        && Text(command["definitionId"]) is { Length: > 0 } definitionId)
                    {
                        AddLink(links, spawnedTemplate, definitionId, owner, defined, stamps.Leaf(owner, "rules"));
                    }
                }
            }

            return links;
        }

        private static void AddLink(
            Dictionary<string, JsonArray> links,
            string templateId,
            string definitionId,
            string ruleOwner,
            Dictionary<string, JsonObject> defined,
            string? sourceUri)
        {
            if (!links.TryGetValue(templateId, out JsonArray? list))
            {
                list = new JsonArray();
                links[templateId] = list;
            }

            JsonObject link = sourceUri == null ? new JsonObject() : Source(sourceUri, null);
            link["definitionId"] = definitionId;
            link["ruleOwner"] = ruleOwner;
            link["defined"] = defined.ContainsKey(definitionId);
            list.Add(link);
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
