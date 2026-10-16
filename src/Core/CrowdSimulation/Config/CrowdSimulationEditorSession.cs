using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Hosting;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.CrowdSimulation.Config;

public static partial class CrowdSimulationEditorApi
{
    internal sealed partial class EditorSession
    {
        internal sealed class PatchSet
        {
            public Dictionary<string, JsonNode> Files { get; } = new(StringComparer.Ordinal);

            public void Put(string relative, JsonNode node)
            {
                Files[relative] = node.DeepClone()!;
            }

            public void Add(PatchSet other)
            {
                foreach (KeyValuePair<string, JsonNode> pair in other.Files)
                {
                    if (Files.TryGetValue(pair.Key, out JsonNode? existing)
                        && existing is JsonObject left
                        && pair.Value is JsonObject right)
                    {
                        MergeInto(left, right);
                    }
                    else
                    {
                        Files[pair.Key] = pair.Value.DeepClone()!;
                    }
                }
            }

            public void WriteTo(string modRoot)
            {
                foreach (KeyValuePair<string, JsonNode> pair in Files)
                {
                    string path = AssetPath(modRoot, pair.Key);
                    string? directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 镜像用硬链接省掉高度图拷贝。先摘掉链接再写，避免改到源文件。
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }

                    File.WriteAllText(path, pair.Value.ToJsonString(WriteOptions));
                }
            }
        }

        private sealed class Opened
        {
            private Opened(VirtualFileSystem vfs, ModLoader mods, TriggerManager triggers, ConfigPipeline pipeline, ConfigCatalog catalog)
            {
                Vfs = vfs;
                Mods = mods;
                Triggers = triggers;
                Pipeline = pipeline;
                Catalog = catalog;
            }

            public VirtualFileSystem Vfs { get; }
            public ModLoader Mods { get; }
            public TriggerManager Triggers { get; }
            public ConfigPipeline Pipeline { get; }
            public ConfigCatalog Catalog { get; }

            public static Opened Create(string repoRoot, IReadOnlyList<ResolvedModLoadEntry> plan)
            {
                string assetsRoot = Path.Combine(repoRoot, "assets");
                if (!Directory.Exists(assetsRoot))
                {
                    throw new DirectoryNotFoundException($"Repo root is missing assets/: {repoRoot}");
                }

                var vfs = new VirtualFileSystem();
                vfs.Mount("Core", assetsRoot);
                var triggers = new TriggerManager();
                var mods = new ModLoader(vfs, new FunctionRegistry(), triggers);
                for (int i = 0; i < plan.Count; i++)
                {
                    ResolvedModLoadEntry entry = plan[i];
                    string manifestPath = Path.Combine(entry.RootPath, "mod.json");
                    var manifest = ModManifestJson.ParseStrict(File.ReadAllText(manifestPath), manifestPath)
                        ?? throw new InvalidOperationException($"Failed to parse mod manifest from '{manifestPath}'.");
                    if (!string.Equals(manifest.Name, entry.Id, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Resolved mod plan mismatch: entry id '{entry.Id}' does not match manifest '{manifest.Name}'.");
                    }

                    vfs.Mount(entry.Id, entry.RootPath);
                    mods.LoadedModIds.Add(entry.Id);
                }

                var pipeline = new ConfigPipeline(vfs, mods);
                ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
                return new Opened(vfs, mods, triggers, pipeline, catalog);
            }

            public JsonObject ReadObject(string relative, ConfigConflictReport report)
            {
                if (!Catalog.TryGet(relative, out ConfigCatalogEntry entry))
                {
                    throw new InvalidOperationException($"config_catalog.json 没有登记 {relative}。");
                }

                JsonObject? merged = Pipeline.MergeDeepObjectFromCatalog(in entry, report);
                if (merged == null)
                {
                    return new JsonObject();
                }

                return (JsonObject)merged.DeepClone()!;
            }

            public JsonObject? ReadOptionalObject(string relative, ConfigConflictReport report)
            {
                if (!Catalog.TryGet(relative, out ConfigCatalogEntry entry))
                {
                    return null;
                }

                JsonObject? merged = Pipeline.MergeDeepObjectFromCatalog(in entry, report);
                return merged == null ? null : (JsonObject)merged.DeepClone()!;
            }

            public JsonArray ReadArray(string relative, ConfigConflictReport report)
            {
                if (!Catalog.TryGet(relative, out ConfigCatalogEntry entry))
                {
                    return new JsonArray();
                }

                IReadOnlyList<MergedConfigEntry> merged = Pipeline.MergeArrayByIdFromCatalog(in entry, report);
                var array = new JsonArray();
                for (int i = 0; i < merged.Count; i++)
                {
                    array.Add(merged[i].Node.DeepClone());
                }

                return array;
            }

            public MapConfig LoadMap(string mapId)
            {
                var maps = new MapManager(Vfs, Triggers, Mods, Pipeline);
                return maps.LoadMap(mapId);
            }

            public List<EntityTemplate> LoadTemplates(ConfigConflictReport report)
            {
                if (!Catalog.TryGet(TemplatesFile, out ConfigCatalogEntry entry))
                {
                    throw new InvalidOperationException($"config_catalog.json 没有登记 {TemplatesFile}。");
                }

                IReadOnlyList<MergedConfigEntry> merged = Pipeline.MergeArrayByIdFromCatalog(in entry, report);
                var byId = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
                for (int i = 0; i < merged.Count; i++)
                {
                    EntityTemplate? template = merged[i].Node.Deserialize<EntityTemplate>(TemplateOptions);
                    if (template == null || string.IsNullOrWhiteSpace(template.Id))
                    {
                        throw new InvalidOperationException($"{TemplatesFile}: 条目 '{merged[i].Id}' 没有 id。");
                    }

                    byId[template.Id] = template;
                }

                EntityTemplateInheritance.ExpandAll(byId, report);
                return byId.Values.ToList();
            }

            public JsonArray Owners(ConfigConflictReport report)
            {
                var rows = new JsonArray();
                StampObject(ConfigFile, rows);
                StampObject(NavmeshFile, rows);
                StampObject(TraversalFile, rows);
                StampObject(DebugFile, rows);
                StampObject(DebugViewFile, rows);
                StampIds(TemplatesFile, report, rows);
                StampIds(AgentProfilesFile, report, rows);
                return rows;
            }

            private void StampObject(string relative, JsonArray rows)
            {
                if (!Catalog.TryGet(relative, out ConfigCatalogEntry entry))
                {
                    return;
                }

                var winners = new Dictionary<string, string>(StringComparer.Ordinal);
                List<ConfigFragment> fragments = Pipeline.CollectFragmentsWithSources(in entry);
                for (int i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i].Node is JsonObject obj)
                    {
                        StampLeaves(string.Empty, obj, fragments[i].SourceUri, winners);
                    }
                }

                foreach (KeyValuePair<string, string> pair in winners)
                {
                    rows.Add(OwnerRow(relative, pair.Key, pair.Value));
                }
            }

            private static void StampIds(string relative, ConfigConflictReport report, JsonArray rows)
            {
                IReadOnlyList<(string Id, string SourceUri)> winners = report.GetWinners(relative);
                for (int i = 0; i < winners.Count; i++)
                {
                    rows.Add(OwnerRow(relative, winners[i].Id, winners[i].SourceUri));
                }
            }
        }

        private static IReadOnlyList<ResolvedModLoadEntry> SwapRoot(
            IReadOnlyList<ResolvedModLoadEntry> plan,
            string modId,
            string root)
        {
            var next = new ResolvedModLoadEntry[plan.Count];
            for (int i = 0; i < plan.Count; i++)
            {
                next[i] = string.Equals(plan[i].Id, modId, StringComparison.Ordinal)
                    ? plan[i] with { RootPath = root }
                    : plan[i];
            }

            return next;
        }

        private static string? FindMapOwner(IReadOnlyList<ResolvedModLoadEntry> plan, string mapId)
        {
            string? owner = null;
            for (int i = 0; i < plan.Count; i++)
            {
                if (File.Exists(MapPath(plan[i].RootPath, mapId)))
                {
                    owner = plan[i].Id;
                }
            }

            return owner;
        }

        private static string MapPath(string modRoot, string mapId)
            => Path.Combine(modRoot, "assets", "Maps", mapId + ".json");

        private static string MapRelative(string mapId) => "Maps/" + mapId + ".json";

        private static string AssetPath(string modRoot, string relative)
            => Path.Combine(modRoot, "assets", relative.Replace('/', Path.DirectorySeparatorChar));

        private static JsonObject? ReadObjectFile(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidOperationException($"{path} 的根必须是对象。");
        }

        private static JsonArray ReadArrayFile(string path)
        {
            if (!File.Exists(path))
            {
                return new JsonArray();
            }

            return JsonNode.Parse(File.ReadAllText(path)) as JsonArray
                ?? throw new InvalidOperationException($"{path} 的根必须是数组。");
        }

        private static JsonObject ScenarioDocument(JsonObject config, JsonObject navmesh, JsonObject? map, string mapId)
        {
            var document = new JsonObject();
            if (config["world"] != null)
            {
                document["world"] = config["world"]!.DeepClone();
            }

            if (config["deploy"] != null)
            {
                document["deploy"] = config["deploy"]!.DeepClone();
            }

            if (navmesh["maps"] is JsonObject maps && maps[mapId] != null)
            {
                document["navMap"] = maps[mapId]!.DeepClone();
            }

            if (map != null)
            {
                document["map"] = map.DeepClone();
            }

            return document;
        }

        private static JsonObject Pick(JsonObject source, string[] keys)
        {
            var picked = new JsonObject();
            for (int i = 0; i < keys.Length; i++)
            {
                if (source.ContainsKey(keys[i]) && source[keys[i]] != null)
                {
                    picked[keys[i]] = source[keys[i]]!.DeepClone();
                }
            }

            return picked;
        }

        private static JsonObject TemplatesDocument(
            JsonArray templates,
            JsonArray profiles,
            JsonObject config)
        {
            return new JsonObject
            {
                ["unitTypes"] = templates.DeepClone(),
                ["agentProfiles"] = profiles.DeepClone(),
                ["structures"] = config["structures"]?.DeepClone() ?? new JsonObject(),
            };
        }

        /// <summary>traversal 是合并后的全文，列是全部区域。mapAreas 是这张图的地形经 terrainAreas 落到的区域，只用来高亮。</summary>
        private static JsonObject CostMatrix(JsonObject traversal, JsonObject navmesh, string mapId)
        {
            var mapAreas = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (navmesh["maps"] is JsonObject maps
                && maps[mapId] is JsonObject map
                && map["terrainTypes"] is JsonArray terrainTypes
                && traversal["terrainAreas"] is JsonObject terrainAreas)
            {
                foreach (JsonNode? terrain in terrainTypes)
                {
                    if (terrain is JsonValue terrainValue
                        && terrainValue.TryGetValue(out string? terrainId)
                        && terrainAreas[terrainId] is JsonValue areaValue
                        && areaValue.TryGetValue(out string? areaId)
                        && seen.Add(areaId))
                    {
                        mapAreas.Add(areaId);
                    }
                }
            }

            return new JsonObject
            {
                ["traversal"] = traversal.DeepClone(),
                ["mapAreas"] = mapAreas,
            };
        }

        private static JsonArray Help()
        {
            return new JsonArray
            {
                HelpItem("规模", "sim.maxUnits 是单位上限。Navigation/navmesh.json 的 maps.<地图 id>.navCellSizeCm 和地图板宽决定格子数。deploy.initialUnits 是开局人数。"),
                HelpItem("手感", "单位速度在模板的 speedCmPerSec。formation.leaderSpeedRatio、catchUpSpeedRatio、settleTimeSec 管队形。movement.unitTurnRateRadPerSec 管转向。"),
                HelpItem("拥挤", "推挤优先级在模板的 pushPriority。半径在 Navigation/agent_profiles.json。push、avoidance、agents.avoidanceRadiusScale 管挤开。"),
                HelpItem("地图", "地图文件是 Maps/<startupMapId>.json。导航事实在 Navigation/navmesh.json 的 maps.<地图 id>。地形生成器不进运行时。"),
                HelpItem("通行", "区域、地形到区域、链接种类和移动类型 × 区域的通行矩阵都在 CrowdSimulation/traversal.json。不可通行的格写 false。"),
                HelpItem("外交", "玩家的 TeamId 在地图 Players。代表实体上的 Relations 决定关系。pairRules.yieldsTo 与 sharesVisionWith 是关系类型名。"),
            };
        }

        private static JsonObject HelpItem(string topic, string text)
            => new() { ["topic"] = topic, ["text"] = text };

        private static JsonObject RequireObject(JsonNode draft, string name, List<CrowdSimulationAuthoringIssue> issues)
        {
            if (draft is JsonObject obj)
            {
                return obj;
            }

            issues.Add(Issue(name, $"{name} 必须是 JSON 对象。"));
            return new JsonObject();
        }

        private static string MirrorMod(string sourceRoot)
        {
            string temp = Path.Combine(Path.GetTempPath(), "ludots-crowd-edit-" + Guid.NewGuid().ToString("N"));
            CopyTree(sourceRoot, temp);
            return temp;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int link(string oldpath, string newpath);

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                string target = Path.Combine(destination, Path.GetFileName(file));
                if (link(file, target) != 0)
                {
                    File.Copy(file, target);
                }
            }

            foreach (string directory in Directory.EnumerateDirectories(source))
            {
                CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        private static IReadOnlyList<CrowdSimulationAuthoringIssue> Explain(Exception ex)
            => CrowdSimulationAuthoringIssues.From(ex);

        private static CrowdSimulationAuthoringIssue Issue(string path, string message)
            => new() { Path = path, Message = message };

        private static bool Same(JsonNode? left, JsonNode? right)
        {
            if (left is null && right is null)
            {
                return true;
            }

            if (left is null || right is null)
            {
                return false;
            }

            if (left is JsonValue && right is JsonValue)
            {
                // 浏览器 JSON.parse / stringify 会把 30.0 写回成 30。数值按值比，不按字面。
                if (left.GetValueKind() == JsonValueKind.Number && right.GetValueKind() == JsonValueKind.Number)
                {
                    return double.Parse(left.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture)
                        == double.Parse(right.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
                }

                return left.ToJsonString() == right.ToJsonString();
            }

            if (left is JsonArray leftArray && right is JsonArray rightArray)
            {
                if (leftArray.Count != rightArray.Count)
                {
                    return false;
                }

                for (int i = 0; i < leftArray.Count; i++)
                {
                    if (!Same(leftArray[i], rightArray[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (left is JsonObject leftObject && right is JsonObject rightObject)
            {
                if (leftObject.Count != rightObject.Count)
                {
                    return false;
                }

                foreach (KeyValuePair<string, JsonNode?> pair in leftObject)
                {
                    if (!rightObject.TryGetPropertyValue(pair.Key, out JsonNode? other) || !Same(pair.Value, other))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        private static JsonNode? DeepDiff(JsonNode? draft, JsonNode? baseline)
        {
            if (Same(draft, baseline) || draft == null)
            {
                return null;
            }

            if (draft is JsonObject draftObject && baseline is JsonObject baselineObject)
            {
                var diff = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> pair in draftObject)
                {
                    baselineObject.TryGetPropertyValue(pair.Key, out JsonNode? baseNode);
                    if (Same(pair.Value, baseNode) || pair.Value == null)
                    {
                        continue;
                    }

                    if (pair.Value is JsonObject && baseNode is JsonObject)
                    {
                        JsonNode? child = DeepDiff(pair.Value, baseNode);
                        if (child != null)
                        {
                            diff[pair.Key] = child;
                        }
                    }
                    else
                    {
                        diff[pair.Key] = pair.Value.DeepClone();
                    }
                }

                return diff.Count == 0 ? null : diff;
            }

            return draft.DeepClone();
        }

        /// <summary>
        /// 当前 Mod 要写的覆盖片段。和 <see cref="DeepDiff"/> 的差别：草稿里的 null 和下层不同时照写成 null，
        /// 因为 traversal.json 用 null 表示“不限坡度”，DeepObject 合并时 null 覆盖数。
        /// </summary>
        private static JsonNode? OverrideOf(JsonNode? draft, JsonNode? baseline)
        {
            if (Same(draft, baseline))
            {
                return null;
            }

            if (draft is JsonObject draftObject && baseline is JsonObject baselineObject)
            {
                var diff = new JsonObject();
                foreach (KeyValuePair<string, JsonNode?> pair in draftObject)
                {
                    bool present = baselineObject.TryGetPropertyValue(pair.Key, out JsonNode? baseNode);
                    if (present && Same(pair.Value, baseNode))
                    {
                        continue;
                    }

                    diff[pair.Key] = present && pair.Value is JsonObject && baseNode is JsonObject
                        ? OverrideOf(pair.Value, baseNode)
                        : pair.Value?.DeepClone();
                }

                return diff.Count == 0 ? null : diff;
            }

            return draft?.DeepClone();
        }

        private static JsonObject DeepMergeObject(JsonObject? baseline, JsonObject overlay)
        {
            var merged = baseline == null ? new JsonObject() : (JsonObject)baseline.DeepClone()!;
            MergeInto(merged, overlay);
            return merged;
        }

        private static void MergeInto(JsonObject target, JsonObject source)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in source)
            {
                if (pair.Value is JsonObject sourceObject && target[pair.Key] is JsonObject targetObject)
                {
                    MergeInto(targetObject, sourceObject);
                }
                else if (pair.Value != null)
                {
                    target[pair.Key] = pair.Value.DeepClone();
                }
            }
        }

        private static void RejectForeign(JsonObject draft, string[] allowed, string layer, List<CrowdSimulationAuthoringIssue> issues)
        {
            var known = new HashSet<string>(allowed, StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode?> pair in draft)
            {
                if (!known.Contains(pair.Key))
                {
                    issues.Add(Issue(layer + "." + pair.Key, $"未知键 \"{pair.Key}\"。"));
                }
            }
        }

        private static void RejectOmissions(JsonNode? draft, JsonNode? baseline, string path, List<CrowdSimulationAuthoringIssue> issues)
        {
            if (baseline is not JsonObject baselineObject || draft is not JsonObject draftObject)
            {
                return;
            }

            foreach (KeyValuePair<string, JsonNode?> pair in baselineObject)
            {
                string child = path.Length == 0 ? pair.Key : path + "." + pair.Key;
                if (!draftObject.ContainsKey(pair.Key))
                {
                    issues.Add(Issue(child, "下层已经给出这个键。当前 Mod 的覆盖删不掉它。"));
                    continue;
                }

                RejectOmissions(draftObject[pair.Key], pair.Value, child, issues);
            }
        }

        private static JsonNode? UnitOverlay(JsonArray draft, JsonArray baseline, string targetRoot)
            => RewriteArrayFile(draft, baseline, TemplatesFile, targetRoot, "id");

        private static JsonNode? ArrayByIdOverlay(JsonArray draft, JsonArray baseline, string relative, string targetRoot)
            => RewriteArrayFile(draft, baseline, relative, targetRoot, "id");

        private static JsonNode? RewriteArrayFile(JsonArray draft, JsonArray baseline, string relative, string targetRoot, string idField)
        {
            Dictionary<string, JsonObject> baselineById = IndexById(baseline, idField);
            var drop = new HashSet<string>(StringComparer.Ordinal);
            var changed = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonNode? node in draft)
            {
                if (node is not JsonObject obj)
                {
                    continue;
                }

                string id = IdOf(obj, idField);
                if (id.Length == 0)
                {
                    continue;
                }

                baselineById.TryGetValue(id, out JsonObject? baseNode);
                if (Same(obj, baseNode))
                {
                    drop.Add(id);
                }
                else
                {
                    changed[id] = OverrideEntry(obj, baseNode, idField, id);
                }
            }

            string path = AssetPath(targetRoot, relative);
            JsonArray existing = ReadArrayFile(path);
            var result = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode? node in existing)
            {
                if (node is not JsonObject obj)
                {
                    continue;
                }

                string id = IdOf(obj, idField);
                if (drop.Contains(id))
                {
                    continue;
                }

                if (changed.TryGetValue(id, out JsonObject? replacement))
                {
                    result.Add(replacement.DeepClone());
                    seen.Add(id);
                    continue;
                }

                result.Add(obj.DeepClone());
                if (id.Length > 0)
                {
                    seen.Add(id);
                }
            }

            foreach (KeyValuePair<string, JsonObject> pair in changed)
            {
                if (!seen.Contains(pair.Key))
                {
                    result.Add(pair.Value.DeepClone());
                }
            }

            if (Same(result, existing))
            {
                return null;
            }

            if (result.Count == 0 && existing.Count == 0)
            {
                return null;
            }

            return result;
        }

        /// <summary>下层已有同 id 条目时，ArrayById 按 id 深合并，当前 Mod 只写与下层不同的键。</summary>
        private static JsonObject OverrideEntry(JsonObject draft, JsonObject? baseline, string idField, string id)
        {
            if (baseline == null)
            {
                return draft;
            }

            var entry = new JsonObject { [idField] = id };
            if (DeepDiff(draft, baseline) is JsonObject diff)
            {
                foreach (KeyValuePair<string, JsonNode?> pair in diff)
                {
                    if (!string.Equals(pair.Key, idField, StringComparison.Ordinal))
                    {
                        entry[pair.Key] = pair.Value?.DeepClone();
                    }
                }
            }

            return entry;
        }

        private static void RejectEntryOmissions(JsonArray draft, JsonArray baseline, string path, List<CrowdSimulationAuthoringIssue> issues)
        {
            Dictionary<string, JsonObject> baselineById = IndexById(baseline, "id");
            foreach (JsonNode? node in draft)
            {
                if (node is JsonObject obj
                    && IdOf(obj, "id") is { Length: > 0 } id
                    && baselineById.TryGetValue(id, out JsonObject? baseNode))
                {
                    RejectOmissions(obj, baseNode, path + "." + id, issues);
                }
            }
        }

        private static Dictionary<string, JsonObject> IndexById(JsonArray array, string idField)
        {
            var index = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonNode? node in array)
            {
                if (node is JsonObject obj)
                {
                    string id = IdOf(obj, idField);
                    if (id.Length > 0)
                    {
                        index[id] = obj;
                    }
                }
            }

            return index;
        }

        private static string IdOf(JsonObject obj, string idField)
        {
            if (obj[idField] is JsonValue value && value.TryGetValue(out string? id) && !string.IsNullOrWhiteSpace(id))
            {
                return id;
            }

            return string.Empty;
        }

        private void PutIfPresent(PatchSet patches, string relative, JsonObject file)
        {
            string path = AssetPath(TargetRoot, relative);
            bool existed = File.Exists(path);
            if (!existed && file.Count == 0)
            {
                return;
            }

            JsonNode? existing = existed ? JsonNode.Parse(File.ReadAllText(path)) : null;
            if (Same(file, existing))
            {
                return;
            }

            patches.Put(relative, file);
        }

        private static JsonObject OwnerRow(string file, string path, string sourceFile)
        {
            return new JsonObject
            {
                ["file"] = file,
                ["path"] = path,
                ["sourceMod"] = CrowdSimulationAuthoringIssues.ModIdOf(sourceFile),
                ["sourceFile"] = sourceFile,
            };
        }

        private static void StampLeaves(string path, JsonObject obj, string source, Dictionary<string, string> winners)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in obj)
            {
                string child = path.Length == 0 ? pair.Key : path + "." + pair.Key;
                if (pair.Value is JsonObject nested)
                {
                    StampLeaves(child, nested, source, winners);
                }
                else
                {
                    winners[child] = source;
                }
            }
        }
    }
}
