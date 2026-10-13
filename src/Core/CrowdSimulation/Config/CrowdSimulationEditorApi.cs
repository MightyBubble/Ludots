using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Hosting;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// 群体配置编辑器的读、校验、写回。数值和引用规则只调用既有装载器。
/// 写回只落在调用方点名的那个 Mod 的资产文件上。
/// </summary>
public static partial class CrowdSimulationEditorApi
{
    public const string ConfigFile = CrowdSimulationConfigLoader.RelativePath;
    public const string NavmeshFile = NavMeshConfigPaths.BakeConfigPath;
    public const string PathingFile = "Navigation/pathing.json";
    public const string AgentProfilesFile = AgentProfileConfigLoader.RelativePath;
    public const string TemplatesFile = ConfigMergeProvenance.TemplatesRelativePath;
    public const string DebugFile = "CrowdSimulationDebug.json";
    public const string DebugViewFile = "CrowdSimulationDebugView.json";

    private static readonly string[] GlobalsKeys =
    [
        "worker", "agents", "navmesh", "hpa", "navtile", "flowfield", "planning", "fog",
        "formation", "formations", "movement", "avoidance", "push", "pairRules", "sim", "spawn", "telemetry",
    ];

    private static readonly string[] ScenarioConfigKeys = ["world", "deploy"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions TemplateOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        IncludeFields = true,
    };

    public static JsonObject Load(string repoRoot, string modId, string? targetRootOverride = null)
    {
        try
        {
            EditorSession session = EditorSession.Open(repoRoot, modId, targetRootOverride);
            return session.ToSnapshot();
        }
        catch (Exception ex)
        {
            var failed = CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.From(ex)).ToJson();
            failed["modId"] = modId;
            return failed;
        }
    }

    public static JsonObject Validate(string repoRoot, string modId, string layer, JsonNode draft, string? targetRootOverride = null)
        => Mutate(repoRoot, modId, layer, draft, write: false, targetRootOverride);

    public static JsonObject Apply(string repoRoot, string modId, string layer, JsonNode draft, string? targetRootOverride = null)
        => Mutate(repoRoot, modId, layer, draft, write: true, targetRootOverride);

    public static JsonObject ExportBundle(string repoRoot, string modId, string? targetRootOverride = null)
    {
        JsonObject snapshot = Load(repoRoot, modId, targetRootOverride);
        if (snapshot["ok"] is JsonValue ok && ok.TryGetValue(out bool fine) && !fine)
        {
            return snapshot;
        }

        return new JsonObject
        {
            [CrowdSimulationAuthoringBundle.ScenarioKey] = snapshot["scenario"]?.DeepClone(),
            [CrowdSimulationAuthoringBundle.GlobalsKey] = snapshot["globals"]?.DeepClone(),
            [CrowdSimulationAuthoringBundle.TemplatesKey] = snapshot["templates"]?.DeepClone(),
            [CrowdSimulationAuthoringBundle.VersionKey] = CrowdSimulationAuthoringBundle.SchemaVersion,
        };
    }

    public static JsonObject ImportBundle(string repoRoot, string modId, JsonObject bundle, bool write, string? targetRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!TryReadVersion(bundle, out int version, out string? versionError))
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.One("version", versionError!)).ToJson();
        }

        if (version != CrowdSimulationAuthoringBundle.SchemaVersion)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.One(
                "version",
                $"完整包 version = {version}，必须等于 {CrowdSimulationAuthoringBundle.SchemaVersion}。没有迁移。")).ToJson();
        }

        if (bundle[CrowdSimulationAuthoringBundle.ScenarioKey] is not JsonObject scenario
            || bundle[CrowdSimulationAuthoringBundle.GlobalsKey] is not JsonObject globals
            || bundle[CrowdSimulationAuthoringBundle.TemplatesKey] is not JsonObject templates)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.One(
                "bundle",
                "完整包必须是 { scenario, globals, templates, version }。")).ToJson();
        }

        try
        {
            EditorSession session = EditorSession.Open(repoRoot, modId, targetRootOverride);
            var patches = new EditorSession.PatchSet();
            var issues = new List<CrowdSimulationAuthoringIssue>();
            patches.Add(session.PatchesFor(CrowdSimulationAuthoringBundle.ScenarioLayer, scenario, issues));
            patches.Add(session.PatchesFor(CrowdSimulationAuthoringBundle.GlobalsLayer, globals, issues));
            patches.Add(session.PatchesFor(CrowdSimulationAuthoringBundle.TemplatesLayer, templates, issues));
            if (issues.Count > 0)
            {
                return CrowdSimulationEditorResult.Failed(issues).ToJson();
            }

            return session.Commit(patches, "production", write).ToJson();
        }
        catch (Exception ex)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.From(ex)).ToJson();
        }
    }

    private static JsonObject Mutate(string repoRoot, string modId, string layer, JsonNode draft, bool write, string? targetRootOverride)
    {
        ArgumentNullException.ThrowIfNull(draft);
        try
        {
            EditorSession session = EditorSession.Open(repoRoot, modId, targetRootOverride);
            var issues = new List<CrowdSimulationAuthoringIssue>();
            EditorSession.PatchSet patches = session.PatchesFor(layer, draft, issues);
            if (issues.Count > 0)
            {
                return CrowdSimulationEditorResult.Failed(issues).ToJson();
            }

            string chain = layer is CrowdSimulationAuthoringBundle.DebugLayer or CrowdSimulationAuthoringBundle.DebugViewLayer
                ? layer
                : "production";
            return session.Commit(patches, chain, write).ToJson();
        }
        catch (Exception ex)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.From(ex)).ToJson();
        }
    }

    private static bool TryReadVersion(JsonObject bundle, out int version, out string? error)
    {
        version = 0;
        error = null;
        if (!bundle.ContainsKey(CrowdSimulationAuthoringBundle.VersionKey) || bundle[CrowdSimulationAuthoringBundle.VersionKey] is null)
        {
            error = "完整包缺少 version。";
            return false;
        }

        if (bundle[CrowdSimulationAuthoringBundle.VersionKey] is not JsonValue value || !value.TryGetValue(out version))
        {
            error = "完整包的 version 必须是整数。";
            return false;
        }

        return true;
    }

    internal sealed partial class EditorSession
    {
        public required string RepoRoot { get; init; }
        public required string ModId { get; init; }
        public required string TargetRoot { get; init; }
        public required IReadOnlyList<ResolvedModLoadEntry> Plan { get; init; }
        public required string MapId { get; init; }
        public required string? MapOwner { get; init; }
        public required bool SelectedOwnsMap { get; init; }
        public required int FixedHz { get; init; }
        public required JsonObject Config { get; init; }
        public required JsonObject BaselineConfig { get; init; }
        public required JsonObject Navmesh { get; init; }
        public required JsonObject BaselineNavmesh { get; init; }
        public required JsonObject Pathing { get; init; }
        public required JsonObject BaselinePathing { get; init; }
        public required JsonArray AgentProfiles { get; init; }
        public required JsonArray BaselineAgentProfiles { get; init; }
        public required JsonArray Templates { get; init; }
        public required JsonArray BaselineTemplates { get; init; }
        public required JsonObject? MapJson { get; init; }
        public required JsonObject? Debug { get; init; }
        public required JsonObject? BaselineDebug { get; init; }
        public required JsonObject? DebugView { get; init; }
        public required JsonObject? BaselineDebugView { get; init; }
        public required JsonArray Owners { get; init; }
        public required JsonObject DisplayDefaults { get; init; }

        public static EditorSession Open(string repoRoot, string modId, string? targetRootOverride)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                throw new ArgumentException("repoRoot is required.", nameof(repoRoot));
            }

            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("modId is required.", nameof(modId));
            }

            repoRoot = Path.GetFullPath(repoRoot);
            IReadOnlyList<ResolvedModLoadEntry> discovered = NavMeshBakeConfigLoader.ResolveModLoadPlan(repoRoot, modId);
            ResolvedModLoadEntry target = discovered.First(entry => string.Equals(entry.Id, modId, StringComparison.Ordinal));
            string targetRoot = string.IsNullOrWhiteSpace(targetRootOverride) ? target.RootPath : Path.GetFullPath(targetRootOverride);
            if (!File.Exists(Path.Combine(targetRoot, "mod.json")))
            {
                throw new InvalidOperationException($"Mod 根目录缺少 mod.json: {targetRoot}");
            }

            var plan = SwapRoot(discovered, modId, targetRoot);
            Opened full = Opened.Create(repoRoot, plan);
            IReadOnlyList<ResolvedModLoadEntry> lowerPlan = plan.Where(entry => !string.Equals(entry.Id, modId, StringComparison.Ordinal)).ToArray();
            Opened? lower = lowerPlan.Count == 0 ? null : Opened.Create(repoRoot, lowerPlan);

            string? mapId = ReadStartupMapId(repoRoot, plan);
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new InvalidOperationException($"Mod \"{modId}\" 的加载序里没有 game.json 的 startupMapId。");
            }

            int fixedHz = ReadFixedHz(repoRoot, plan);
            string? mapOwner = FindMapOwner(plan, mapId);
            bool selectedOwnsMap = File.Exists(MapPath(targetRoot, mapId));
            JsonObject? mapJson = selectedOwnsMap
                ? ReadObjectFile(MapPath(targetRoot, mapId))
                : mapOwner == null
                    ? null
                    : ReadObjectFile(MapPath(plan.First(entry => entry.Id == mapOwner).RootPath, mapId));

            var report = new ConfigConflictReport();
            JsonObject config = full.ReadObject(ConfigFile, report);
            JsonObject navmesh = full.ReadObject(NavmeshFile, report);
            JsonObject pathing = full.ReadObject(PathingFile, report);
            JsonArray profiles = full.ReadArray(AgentProfilesFile, report);
            JsonArray templates = full.ReadArray(TemplatesFile, report);
            JsonObject? debug = full.ReadOptionalObject(DebugFile, report);
            JsonObject? debugView = full.ReadOptionalObject(DebugViewFile, report);

            JsonObject baselineConfig = lower?.ReadObject(ConfigFile, new ConfigConflictReport()) ?? new JsonObject();
            JsonObject baselineNav = lower?.ReadObject(NavmeshFile, new ConfigConflictReport()) ?? new JsonObject();
            JsonObject baselinePathing = lower?.ReadObject(PathingFile, new ConfigConflictReport()) ?? new JsonObject();
            JsonArray baselineProfiles = lower?.ReadArray(AgentProfilesFile, new ConfigConflictReport()) ?? new JsonArray();
            JsonArray baselineTemplates = lower?.ReadArray(TemplatesFile, new ConfigConflictReport()) ?? new JsonArray();
            JsonObject? baselineDebug = lower?.ReadOptionalObject(DebugFile, new ConfigConflictReport());
            JsonObject? baselineDebugView = lower?.ReadOptionalObject(DebugViewFile, new ConfigConflictReport());

            return new EditorSession
            {
                RepoRoot = repoRoot,
                ModId = modId,
                TargetRoot = targetRoot,
                Plan = plan,
                MapId = mapId,
                MapOwner = mapOwner,
                SelectedOwnsMap = selectedOwnsMap,
                FixedHz = fixedHz,
                Config = config,
                BaselineConfig = baselineConfig,
                Navmesh = navmesh,
                BaselineNavmesh = baselineNav,
                Pathing = pathing,
                BaselinePathing = baselinePathing,
                AgentProfiles = profiles,
                BaselineAgentProfiles = baselineProfiles,
                Templates = templates,
                BaselineTemplates = baselineTemplates,
                MapJson = mapJson,
                Debug = debug,
                BaselineDebug = baselineDebug,
                DebugView = debugView,
                BaselineDebugView = baselineDebugView,
                Owners = full.Owners(report),
                DisplayDefaults = new JsonObject(),
            };
        }

        public JsonObject ToSnapshot()
        {
            JsonObject scenario = ScenarioDocument(Config, Navmesh, MapJson, MapId);
            JsonObject globals = Pick(Config, GlobalsKeys);
            JsonObject templates = TemplatesDocument(Templates, Pathing, Navmesh, AgentProfiles, Config);
            JsonObject scenarioDefaults = ScenarioDocument(BaselineConfig, BaselineNavmesh, SelectedOwnsMap ? null : MapJson, MapId);
            if (SelectedOwnsMap)
            {
                scenarioDefaults["map"] = MapJson?.DeepClone() ?? new JsonObject();
            }

            JsonObject globalsDefaults = BaselineConfig.Count == 0 ? Pick(Config, GlobalsKeys) : Pick(BaselineConfig, GlobalsKeys);
            JsonObject templatesDefaults = BaselineTemplates.Count == 0 && BaselinePathing.Count == 0
                ? templates.DeepClone().AsObject()
                : TemplatesDocument(BaselineTemplates, BaselinePathing, BaselineNavmesh, BaselineAgentProfiles, BaselineConfig);

            var node = new JsonObject
            {
                ["ok"] = true,
                ["modId"] = ModId,
                ["mapId"] = MapId,
                ["mapOwner"] = MapOwner,
                ["mapOwnedBySelection"] = SelectedOwnsMap,
                ["fixedHz"] = FixedHz,
                ["bundleVersion"] = CrowdSimulationAuthoringBundle.SchemaVersion,
                ["scenario"] = scenario,
                ["globals"] = globals,
                ["templates"] = templates,
                ["debug"] = Debug?.DeepClone(),
                ["debugView"] = DebugView?.DeepClone(),
                ["defaults"] = new JsonObject
                {
                    ["scenario"] = scenarioDefaults,
                    ["globals"] = globalsDefaults,
                    ["templates"] = templatesDefaults,
                    ["debug"] = (BaselineDebug ?? Debug)?.DeepClone(),
                    ["debugView"] = (BaselineDebugView ?? DebugView)?.DeepClone(),
                    ["costMatrix"] = CostMatrix(
                        BaselinePathing.Count == 0 ? Pathing : BaselinePathing,
                        BaselineNavmesh.Count == 0 ? Navmesh : BaselineNavmesh,
                        MapId),
                },
                ["owners"] = Owners.DeepClone(),
                ["costMatrix"] = CostMatrix(Pathing, Navmesh, MapId),
                ["help"] = Help(),
                ["issues"] = new JsonArray(),
            };
            return node;
        }

        internal PatchSet PatchesFor(string layer, JsonNode draft, List<CrowdSimulationAuthoringIssue> issues)
        {
            var patches = new PatchSet();
            switch (layer)
            {
                case CrowdSimulationAuthoringBundle.ScenarioLayer:
                    PatchScenario(RequireObject(draft, "scenario", issues), patches, issues);
                    break;
                case CrowdSimulationAuthoringBundle.GlobalsLayer:
                    PatchGlobals(RequireObject(draft, "globals", issues), patches, issues);
                    break;
                case CrowdSimulationAuthoringBundle.TemplatesLayer:
                    PatchTemplates(RequireObject(draft, "templates", issues), patches, issues);
                    break;
                case CrowdSimulationAuthoringBundle.CostMatrixLayer:
                    PatchCostMatrix(RequireObject(draft, "costMatrix", issues), patches, issues);
                    break;
                case CrowdSimulationAuthoringBundle.DebugLayer:
                    PatchWholeFile(DebugFile, RequireObject(draft, "debug", issues), BaselineDebug ?? new JsonObject(), Debug == null, patches, issues);
                    break;
                case CrowdSimulationAuthoringBundle.DebugViewLayer:
                    PatchWholeFile(DebugViewFile, RequireObject(draft, "debugView", issues), BaselineDebugView ?? new JsonObject(), DebugView == null, patches, issues);
                    break;
                default:
                    issues.Add(Issue("", $"未知编辑层 \"{layer}\""));
                    break;
            }

            return patches;
        }

        internal CrowdSimulationEditorResult Commit(PatchSet patches, string chain, bool write)
        {
            string temp = MirrorMod(TargetRoot);
            try
            {
                patches.WriteTo(temp);
                var swapped = SwapRoot(Plan, ModId, temp);
                RunChain(swapped, chain);
            }
            catch (Exception ex)
            {
                return CrowdSimulationEditorResult.Failed(Explain(ex));
            }
            finally
            {
                try
                {
                    Directory.Delete(temp, recursive: true);
                }
                catch (IOException)
                {
                }
            }

            if (!write || patches.Files.Count == 0)
            {
                return CrowdSimulationEditorResult.Succeeded();
            }

            var written = new List<string>();
            patches.WriteTo(TargetRoot);
            foreach (string relative in patches.Files.Keys.OrderBy(path => path, StringComparer.Ordinal))
            {
                written.Add(ModId + "/assets/" + relative);
            }

            return CrowdSimulationEditorResult.Succeeded(written);
        }

        private void RunChain(IReadOnlyList<ResolvedModLoadEntry> plan, string chain)
        {
            Opened opened = Opened.Create(RepoRoot, plan);
            var report = new ConfigConflictReport();
            if (chain == CrowdSimulationAuthoringBundle.DebugLayer)
            {
                JsonObject? debug = opened.ReadOptionalObject(DebugFile, report);
                if (debug == null)
                {
                    throw new InvalidOperationException($"{DebugFile} 存在目录登记但合并为空，或加载序没有这份文件。");
                }

                CrowdSimulationDebugConfig.Load(debug);
                return;
            }

            if (chain == CrowdSimulationAuthoringBundle.DebugViewLayer)
            {
                JsonObject? view = opened.ReadOptionalObject(DebugViewFile, report);
                if (view == null)
                {
                    throw new InvalidOperationException($"{DebugViewFile} 存在目录登记但合并为空，或加载序没有这份文件。");
                }

                CrowdSimulationDebugViewConfig.Load(view, DebugViewFile);
                return;
            }

            if (FixedHz < 1)
            {
                throw new InvalidOperationException("Engine/clock.json 的 FixedHz 缺失或小于 1。步进频率只由这份文件规定。");
            }

            var pipeline = opened.Pipeline;
            var catalog = opened.Catalog;
            CrowdSimulationConfig config = new CrowdSimulationConfigLoader(pipeline).LoadConfig(catalog, report);
            AgentProfileRegistry profiles = new AgentProfileConfigLoader(pipeline).Load(catalog, report);
            NavMeshBakeConfig navmesh = new NavMeshBakeConfigLoader(pipeline, profiles).Load(catalog, report);
            PathingConfig pathing = new PathingConfigLoader(pipeline).Load(catalog, report);
            MapConfig map = opened.LoadMap(MapId) ?? throw new InvalidOperationException($"Maps/{MapId}.json 没有装载出来。");
            List<EntityTemplate> templates = opened.LoadTemplates(report);
            CrowdSimulationAuthoringContract.Validate(templates, config, map, profiles);
            List<CrowdUnitTemplateFacts> facts = CrowdUnitTemplateFacts.FromTemplates(templates);
            int players = Math.Max(1, map.Players.Count);
            var rules = CrowdPairRules.Matrices(players, new CrowdPushResponse[players * players], new bool[players * players]);
            for (int i = 0; i < rules.ResponseByPair.Length; i++)
            {
                rules.ResponseByPair[i] = CrowdPushResponse.Hold;
            }

            // 编辑器没有活着的代表实体。关系类型登记在 Mod 代码里，会话重建前由运行中的投影再查一次。
            try
            {
                CrowdSimulationConfigLoader.Load(config, map, profiles, navmesh, pathing, facts, FixedHz, rules);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("配置路径:", StringComparison.Ordinal))
            {
                // 关系检查发生在装载器的 Wrap 之外。补上同一份报告里的路径和胜出，编辑器才能标来源。
                string relative = GuessProvenanceFile(ex.Message);
                throw new InvalidOperationException(ex.Message + ProvenanceSuffix(relative, report), ex);
            }
        }

        private static string ProvenanceSuffix(string relative, ConfigConflictReport report)
        {
            var text = new System.Text.StringBuilder();
            text.Append("\n配置路径: ");
            text.Append(relative);
            text.Append("\n合并顺序: ");
            IReadOnlyList<string> fragments = report.GetFragments(relative);
            if (fragments.Count == 0)
            {
                text.Append("（无）");
            }
            else
            {
                for (int i = 0; i < fragments.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(" | ");
                    }

                    text.Append(fragments[i]);
                }
            }

            text.Append("\n胜出: ");
            if (report.TryGetFileWinner(relative, out string winner, out _) && !string.IsNullOrEmpty(winner))
            {
                text.Append(winner);
            }
            else
            {
                text.Append("（无）");
            }

            return text.ToString();
        }

        private static string GuessProvenanceFile(string message)
        {
            if (message.Contains("pathing.json", StringComparison.Ordinal) || message.Contains("areaCosts", StringComparison.Ordinal))
            {
                return PathingFile;
            }

            if (message.Contains("navmesh.json", StringComparison.Ordinal))
            {
                return NavmeshFile;
            }

            if (message.Contains("templates.json", StringComparison.Ordinal) || message.Contains("CrowdSimulationAgent", StringComparison.Ordinal))
            {
                return TemplatesFile;
            }

            if (message.Contains("agent_profiles.json", StringComparison.Ordinal))
            {
                return AgentProfilesFile;
            }

            return ConfigFile;
        }

        private void PatchScenario(JsonObject draft, PatchSet patches, List<CrowdSimulationAuthoringIssue> issues)
        {
            RejectForeign(draft, ["map", "world", "deploy", "navMap"], "scenario", issues);
            if (draft["map"] is JsonObject mapDraft && !Same(mapDraft, MapJson))
            {
                if (!SelectedOwnsMap)
                {
                    issues.Add(Issue(
                        "map",
                        $"地图 Maps/{MapId}.json 属于 {MapOwner ?? "（没有文件）"}。当前选择的是 {ModId}，地图不会写进别的 Mod。要改地图，改选拥有这份文件的 Mod。"));
                }
                else
                {
                    patches.Put(MapRelative(MapId), mapDraft.DeepClone().AsObject());
                }
            }

            PatchConfigKeys(ScenarioConfigKeys, draft, BaselineConfig, patches, issues);

            if (draft["navMap"] is JsonNode navDraft)
            {
                JsonNode? baselineNav = BaselineNavmesh["maps"] is JsonObject maps && maps[MapId] != null ? maps[MapId] : null;
                RejectOmissions(navDraft, baselineNav, "navMap", issues);
                JsonObject navmeshFile = ReadObjectFile(AssetPath(TargetRoot, NavmeshFile)) ?? new JsonObject();
                JsonObject mapsNode = navmeshFile["maps"] as JsonObject ?? new JsonObject();
                if (Same(navDraft, baselineNav))
                {
                    mapsNode.Remove(MapId);
                }
                else
                {
                    JsonNode? diff = DeepDiff(navDraft, baselineNav);
                    if (diff == null)
                    {
                        mapsNode.Remove(MapId);
                    }
                    else
                    {
                        mapsNode[MapId] = diff;
                    }
                }

                if (mapsNode.Count == 0)
                {
                    navmeshFile.Remove("maps");
                }
                else
                {
                    navmeshFile["maps"] = mapsNode;
                }

                PutIfPresent(patches, NavmeshFile, navmeshFile);
            }
        }

        private void PatchGlobals(JsonObject draft, PatchSet patches, List<CrowdSimulationAuthoringIssue> issues)
        {
            RejectForeign(draft, GlobalsKeys, "globals", issues);
            PatchConfigKeys(GlobalsKeys, draft, BaselineConfig, patches, issues);
        }

        private void PatchTemplates(JsonObject draft, PatchSet patches, List<CrowdSimulationAuthoringIssue> issues)
        {
            RejectForeign(draft, ["structures", "agentTypes", "navProfiles", "unitTypes", "agentProfiles"], "templates", issues);
            if (draft["structures"] is JsonNode structures)
            {
                var carrier = new JsonObject { ["structures"] = structures.DeepClone() };
                PatchConfigKeys(["structures"], carrier, BaselineConfig, patches, issues);
            }

            if (draft["agentTypes"] is JsonObject agentTypes)
            {
                RejectOmissions(agentTypes, BaselinePathing["agentTypes"], "agentTypes", issues);
                PatchObjectFile(PathingFile, "agentTypes", agentTypes, BaselinePathing["agentTypes"], patches);
            }

            if (draft["navProfiles"] is JsonObject profiles)
            {
                RejectOmissions(profiles, BaselineNavmesh["profiles"], "navProfiles", issues);
                PatchObjectFile(NavmeshFile, "profiles", profiles, BaselineNavmesh["profiles"], patches);
            }

            if (draft["unitTypes"] is JsonArray units)
            {
                JsonNode? unitFile = UnitOverlay(units, BaselineTemplates, TargetRoot);
                if (unitFile != null)
                {
                    patches.Put(TemplatesFile, unitFile);
                }
            }

            if (draft["agentProfiles"] is JsonArray profilesArray)
            {
                JsonNode? overlay = ArrayByIdOverlay(profilesArray, BaselineAgentProfiles, AgentProfilesFile, TargetRoot);
                if (overlay != null)
                {
                    patches.Put(AgentProfilesFile, overlay);
                }
            }
        }

        private void PatchCostMatrix(JsonObject draft, PatchSet patches, List<CrowdSimulationAuthoringIssue> issues)
        {
            if (draft["cells"] is not JsonObject cells)
            {
                issues.Add(Issue("cells", "代价矩阵缺少 cells。"));
                return;
            }

            var agentTypes = new JsonObject();
            foreach (var agent in cells)
            {
                if (agent.Value is not JsonObject row)
                {
                    continue;
                }

                var costs = new JsonArray();
                foreach (var cell in row)
                {
                    if (!int.TryParse(cell.Key, out int areaId))
                    {
                        issues.Add(Issue($"cells.{agent.Key}", $"区域 id \"{cell.Key}\" 不是整数。"));
                        continue;
                    }

                    var entry = new JsonObject { ["areaId"] = areaId };
                    JsonObject? previous = PreviousCost(Pathing, agent.Key, areaId);
                    if (cell.Value is null)
                    {
                        entry["passable"] = false;
                        entry["cost"] = PreviousNumber(previous, "cost");
                    }
                    else
                    {
                        entry["cost"] = cell.Value.DeepClone();
                    }

                    if (previous?["speedDivisor"] is JsonNode divisor)
                    {
                        entry["speedDivisor"] = divisor.DeepClone();
                    }

                    costs.Add(entry);
                }

                agentTypes[agent.Key] = new JsonObject
                {
                    ["navMesh"] = new JsonObject { ["areaCosts"] = costs },
                };
            }

            var mergedAgents = DeepMergeObject(BaselinePathing["agentTypes"] as JsonObject, agentTypes);
            PatchObjectFile(PathingFile, "agentTypes", mergedAgents, BaselinePathing["agentTypes"], patches);
        }

        private void PatchWholeFile(string relative, JsonObject draft, JsonObject baseline, bool missingCatalog, PatchSet patches, List<CrowdSimulationAuthoringIssue> issues)
        {
            RejectOmissions(draft, baseline, relative, issues);
            if (missingCatalog && !CatalogHas(relative))
            {
                issues.Add(Issue(relative, $"加载序没有登记 {relative}。"));
                return;
            }

            JsonNode? diff = DeepDiff(draft, baseline);
            if (diff is JsonObject overlay && overlay.Count > 0)
            {
                JsonObject file = ReadObjectFile(AssetPath(TargetRoot, relative)) ?? new JsonObject();
                foreach (var kv in overlay)
                {
                    file[kv.Key] = kv.Value?.DeepClone();
                }

                foreach (var kv in file.ToArray())
                {
                    if (!draft.ContainsKey(kv.Key))
                    {
                        file.Remove(kv.Key);
                    }
                    else if (baseline.ContainsKey(kv.Key) && Same(draft[kv.Key], baseline[kv.Key]))
                    {
                        file.Remove(kv.Key);
                    }
                }

                PutIfPresent(patches, relative, file);
            }
            else
            {
                JsonObject file = ReadObjectFile(AssetPath(TargetRoot, relative)) ?? new JsonObject();
                bool changed = false;
                foreach (var kv in file.ToArray())
                {
                    if (baseline.ContainsKey(kv.Key) && Same(draft[kv.Key], baseline[kv.Key]))
                    {
                        file.Remove(kv.Key);
                        changed = true;
                    }
                }

                if (changed)
                {
                    PutIfPresent(patches, relative, file);
                }
            }
        }

        private bool CatalogHas(string relative)
        {
            Opened opened = Opened.Create(RepoRoot, Plan);
            return opened.Catalog.TryGet(relative, out _);
        }

        private void PatchConfigKeys(string[] keys, JsonObject draft, JsonObject baseline, PatchSet patches, List<CrowdSimulationAuthoringIssue>? issues = null)
        {
            JsonObject file = ReadObjectFile(AssetPath(TargetRoot, ConfigFile)) ?? new JsonObject();
            foreach (string key in keys)
            {
                if (!draft.ContainsKey(key))
                {
                    continue;
                }

                baseline.TryGetPropertyValue(key, out JsonNode? baseNode);
                if (issues != null)
                {
                    RejectOmissions(draft[key], baseNode, key, issues);
                }
                if (Same(draft[key], baseNode))
                {
                    file.Remove(key);
                    continue;
                }

                JsonNode? diff = DeepDiff(draft[key], baseNode);
                if (diff == null)
                {
                    file.Remove(key);
                }
                else
                {
                    file[key] = diff;
                }
            }

            PutIfPresent(patches, ConfigFile, file);
        }

        private void PatchObjectFile(string relative, string key, JsonNode draftValue, JsonNode? baselineValue, PatchSet patches)
        {
            JsonObject file = ReadObjectFile(AssetPath(TargetRoot, relative)) ?? new JsonObject();
            if (Same(draftValue, baselineValue))
            {
                file.Remove(key);
            }
            else
            {
                JsonNode? diff = DeepDiff(draftValue, baselineValue);
                if (diff == null)
                {
                    file.Remove(key);
                }
                else
                {
                    file[key] = diff;
                }
            }

            PutIfPresent(patches, relative, file);
        }
    }
}
