using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Pathing.Config;
using Ludots.Core.Scripting;

namespace Ludots.Core.CrowdSimulation.Runtime;

public sealed partial class CrowdSimulationRuntime
{
    public JsonObject ApplyDebugFromPipeline()
    {
        try
        {
            GameEngine engine = RequireLiveEngine();
            JsonObject document = ReadMergedObject(engine, CrowdSimulationDebugPath);
            return CrowdSimulationLiveApply.ApplyDebug(document, () => CommitDebug(document)).ToJson();
        }
        catch (Exception ex)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.From(ex)).ToJson();
        }
    }

    public JsonObject ApplyDebugViewFromPipeline()
    {
        try
        {
            GameEngine engine = RequireLiveEngine();
            JsonObject document = ReadMergedObject(engine, CrowdSimulationDebugViewPath);
            return CrowdSimulationLiveApply.ApplyDebugView(document, () => CommitDebugView(document)).ToJson();
        }
        catch (Exception ex)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.From(ex)).ToJson();
        }
    }

    public JsonObject RebuildWorld()
    {
        try
        {
            return CrowdSimulationLiveApply.ApplyRebuild(PreflightRebuild, CommitRebuild).ToJson();
        }
        catch (Exception ex)
        {
            return CrowdSimulationEditorResult.Failed(CrowdSimulationAuthoringIssues.From(ex)).ToJson();
        }
    }

    private GameEngine RequireLiveEngine()
        => _engine ?? throw new InvalidOperationException("没有运行中的群体会话。");

    private static JsonObject ReadMergedObject(GameEngine engine, string relative)
    {
        ConfigCatalog catalog = engine.ConfigCatalog
            ?? throw new InvalidOperationException("配置目录还没有装上。");
        if (!catalog.TryGet(relative, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"加载序没有登记 {relative}。");
        }

        JsonObject? merged = engine.ConfigPipeline.MergeDeepObjectFromCatalog(in entry, engine.ConfigConflictReport);
        if (merged == null)
        {
            throw new InvalidOperationException($"{relative} 存在目录登记但合并为空。");
        }

        return merged;
    }

    private void CommitDebug(JsonObject document)
    {
        CrowdSimulationDebugConfig loaded = CrowdSimulationDebugConfig.Load(document);
        _debug = loaded;
        if (loaded.Presentation != null)
        {
            _presentation = loaded.Presentation;
            if (_engine != null && loaded.Presentation.FogDecal != null)
            {
                _engine.SetService(CoreServiceKeys.FogDecalPalette, loaded.Presentation.FogDecal.ToPalette());
            }
        }

        if (loaded.Tools != null && loaded.Rates != null && _deployDemo != null)
        {
            _deployDemo.UseDebugTools(loaded.Tools, loaded.Rates);
        }
    }

    private void CommitDebugView(JsonObject document)
    {
        CrowdSimulationDebugViewConfig loaded = CrowdSimulationDebugViewConfig.Load(document, CrowdSimulationDebugViewPath);
        _debugView = loaded;
        if (_engine == null
            || _deployViewProjector == null
            || _projectorSlot == null
            || _deployInput is not S4DeployInputSystem input
            || _deployPresentation is not S4DeployDemoPresentationSystem presented)
        {
            return;
        }

        var frame = new CrowdDebugViewFrame(loaded);
        frame.BindEngine(_engine);
        input.UseDebugView(frame);
        presented.UseDebugView(frame);
        _projectorSlot.Current = new S4DeployFrameProjector(_deployViewProjector, frame, () => _session);
    }

    private IReadOnlyList<CrowdSimulationAuthoringIssue> PreflightRebuild()
    {
        try
        {
            GameEngine engine = RequireLiveEngine();
            string mapId = _focusedCrowdMapId ?? _activeMapId
                ?? throw new InvalidOperationException("没有聚焦的群体地图。");
            ConfigCatalog catalog = engine.ConfigCatalog
                ?? throw new InvalidOperationException("配置目录还没有装上。");
            ConfigConflictReport report = engine.ConfigConflictReport;
            CrowdSimulationConfig config = new CrowdSimulationConfigLoader(engine.ConfigPipeline).LoadConfig(catalog, report);
            AgentProfileRegistry profiles = new AgentProfileConfigLoader(engine.ConfigPipeline).Load(catalog, report);
            NavMeshBakeConfig navmesh = new NavMeshBakeConfigLoader(engine.ConfigPipeline, profiles).Load(catalog, report);
            PathingConfig pathing = new PathingConfigLoader(engine.ConfigPipeline).Load(catalog, report);
            MapConfig map = engine.MapManager.LoadMap(mapId)
                ?? throw new InvalidOperationException($"Maps/{mapId}.json 没有装载出来。");
            List<EntityTemplate> templates = ReadTemplates(engine, catalog, report);
            CrowdSimulationAuthoringContract.Validate(templates, config, map, profiles);
            List<CrowdUnitTemplateFacts> facts = CrowdUnitTemplateFacts.FromTemplates(templates);
            var teamRelations = engine.GetService(CoreServiceKeys.TeamRelationQuery)
                ?? throw new InvalidOperationException("CrowdSimulation 会话需要 TeamRelationQuery。");
            var players = engine.GetService(CoreServiceKeys.PlayerEntityLookup)
                ?? throw new InvalidOperationException("CrowdSimulation 会话需要 PlayerEntityLookup。");
            // 投影失败时会话还在。拆掉会话只发生在这份检查通过之后。
            var projected = CrowdPairRulesProjection.Project(config, map, teamRelations, players, engine.World);
            int fixedHz = (int)MathF.Round(1f / Time.FixedDeltaTime);
            CrowdSimulationConfigLoader.Load(config, map, profiles, navmesh, pathing, facts, fixedHz, projected.Rules);
            return Array.Empty<CrowdSimulationAuthoringIssue>();
        }
        catch (Exception ex)
        {
            return CrowdSimulationAuthoringIssues.From(ex);
        }
    }

    private void CommitRebuild()
    {
        GameEngine engine = RequireLiveEngine();
        string mapId = _focusedCrowdMapId ?? _activeMapId
            ?? throw new InvalidOperationException("没有聚焦的群体地图。");
        var id = new MapId(mapId);
        ReleasePathPresentation(engine, id);
        ReleaseDeployPresentation(engine, id);
        Deactivate(engine);
        _configDto = null;
        _debug = null;
        _debugView = null;
        _navmesh = null;
        _planTimeout = null;
        _focusedCrowdMapId = null;
        engine.RemoveService(CoreServiceKeys.NavMeshBakeConfig);
        ConfigCatalog catalog = engine.ConfigCatalog
            ?? throw new InvalidOperationException("配置目录还没有装上。");
        AgentProfileRegistry profiles = new AgentProfileConfigLoader(engine.ConfigPipeline)
            .Load(catalog, engine.ConfigConflictReport);
        engine.SetService(CoreServiceKeys.AgentProfiles, profiles);
        engine.MapLoader.LoadTemplates(catalog, engine.ConfigConflictReport);
        MapConfig map = engine.MapManager.LoadMap(mapId)
            ?? throw new InvalidOperationException($"Maps/{mapId}.json 没有装载出来。");
        if (!HandleMapFocused(engine, id, map))
        {
            throw new InvalidOperationException($"地图 \"{mapId}\" 没有重新进入群体会话。");
        }
    }

    private static List<EntityTemplate> ReadTemplates(GameEngine engine, ConfigCatalog catalog, ConfigConflictReport report)
    {
        var registry = new DataRegistry<EntityTemplate>(engine.ConfigPipeline);
        registry.Load(ConfigMergeProvenance.TemplatesRelativePath, catalog, report);
        var byId = new Dictionary<string, EntityTemplate>(StringComparer.Ordinal);
        foreach (EntityTemplate template in registry.GetAll())
        {
            byId.Add(template.Id, template);
        }

        EntityTemplateInheritance.ExpandAll(byId, report);
        return byId.Values.ToList();
    }
}
