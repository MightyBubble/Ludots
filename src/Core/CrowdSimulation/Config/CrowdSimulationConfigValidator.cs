using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// CrowdSimulationConfig.json 的数值范围与关系校验（LU-19；数值规则与参考实现
/// config/schema.js 的 RULES 同源，路径已按 Ludots 文件归属换算）。
/// 所有错误消息带文件 / 字段路径 / 规则，不写管理痕迹。
/// </summary>
public static class CrowdSimulationConfigValidator
{
    public const string FileName = "CrowdSimulationConfig.json";

    /// <summary>重烘焙分帧上限 = 反应、重规划两个后续阶段 + 命令内阶段。</summary>
    public const int MaxRebakeSlices = 3;

    public static void Validate(CrowdSimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // ---- 声明式数值规则（RULES 移植；world.navResolution / movement.tickRate 等在组装阶段按映射校验）----
        Rule("hpa.clusterSize", config.Hpa.ClusterSize, intRequired: true, min: 4, max: 128);
        Rule("hpa.maxEntranceWidth", config.Hpa.MaxEntranceWidth, intRequired: true, min: 1);
        Rule("navtile.cacheCapacity", config.Navtile.CacheCapacity, intRequired: true, min: 1);
        Rule("navmesh.maxVertsPerPoly", config.Navmesh.MaxVertsPerPoly, intRequired: true, min: 3, max: 12);
        Rule("navmesh.portalInsetCells", config.Navmesh.PortalInsetCells, min: 0, lt: 1);
        Rule("flowfield.cacheCapacity", config.Flowfield.CacheCapacity, intRequired: true, min: 1);
        Rule("flowfield.poolCapacity", config.Flowfield.PoolCapacity, intRequired: true, min: 0);
        Rule("flowfield.corridorPadding", config.Flowfield.CorridorPadding, intRequired: true, min: 0);
        Rule("flowfield.maxRebuildsPerTick", config.Flowfield.MaxRebuildsPerTick, intRequired: true, min: 1);
        Rule("planning.latencyTicks", config.Planning.LatencyTicks, intRequired: true, min: 1, max: 60);
        Rule("planning.rebakeSlices", config.Planning.RebakeSlices, intRequired: true, min: 1, max: MaxRebakeSlices);
        Rule("planning.refreshLatencyTicks", config.Planning.RefreshLatencyTicks, intRequired: true, min: 1);
        Rule("planning.maxRefreshesPerTick", config.Planning.MaxRefreshesPerTick, intRequired: true, min: 1);
        Rule("push.dominantShare", config.Push.DominantShare, min: 0, max: 0.5);
        Rule("push.movingBonus", config.Push.MovingBonus, min: 0);
        Rule("agents.avoidanceRadiusScale", config.Agents.AvoidanceRadiusScale, gt: 0);
        Rule("spawn.clusterSpacing", config.Spawn.ClusterSpacing, gt: 0);
        Rule("spawn.placementTries", config.Spawn.PlacementTries, intRequired: true, min: 1);
        Rule("spawn.targetSearchTries", config.Spawn.TargetSearchTries, intRequired: true, min: 1);
        Rule("spawn.cellInset", config.Spawn.CellInset, min: 0, lt: 0.5);
        Rule("movement.sightHysteresis", config.Movement.SightHysteresis, min: 1);
        Rule("movement.wakeDistanceCells", config.Movement.WakeDistanceCells, min: 0);
        Rule("movement.stallDecay", config.Movement.StallDecay, min: 0);
        Rule("movement.maxSubSteps", config.Movement.MaxSubSteps, intRequired: true, min: 1, max: 64);
        Rule("movement.restAccelScale", config.Movement.RestAccelScale, gt: 0);
        Rule("movement.restSeparationScale", config.Movement.RestSeparationScale, min: 0);
        // 墙面测试假设单个子步位移小于一格
        Rule("movement.maxStepCells", config.Movement.MaxStepCells, gt: 0, lt: 1);
        Rule("movement.slotTimeConstant", config.Movement.SlotTimeConstant, gt: 0);
        Rule("movement.unitTurnRate", config.Movement.UnitTurnRate, gt: 0);
        Rule("movement.jumpSpeedRatio", config.Movement.JumpSpeedRatio, gt: 0);
        Rule("movement.blendRate", config.Movement.BlendRate, gt: 0);
        Rule("movement.speedCapRatio", config.Movement.SpeedCapRatio, gt: 0);
        Rule("movement.stopSpeedRatio", config.Movement.StopSpeedRatio, gt: 0);
        Rule("movement.stallSpeedRatio", config.Movement.StallSpeedRatio, gt: 0);
        Rule("movement.slotCheckInterval", config.Movement.SlotCheckInterval, intRequired: true, min: 1);
        Rule("movement.laneSpread", config.Movement.LaneSpread, min: 0, max: 1);
        Rule("movement.blendCommit", config.Movement.BlendCommit, gt: 0, lt: 1);
        Rule("avoidance.smoothing", config.Avoidance.Smoothing, min: 0, lt: 1);
        // 关系种类上限 256(P×P Uint8 矩阵的编号域)。
        if (config.Relations.Kinds.Count < 1 || config.Relations.Kinds.Count > 256)
        {
            throw Invalid("relations.kinds", $"需要 1~256 项（当前 {config.Relations.Kinds.Count} 项）");
        }

        foreach (var (name, kind) in config.Relations.Kinds)
        {
            if (kind.Push is not ("priority" or "rigid"))
            {
                throw Invalid($"relations.kinds.{name}.push", "需为 priority / rigid");
            }
        }

        foreach (var (path, kind) in new[]
        {
            (path: "relations.self", kind: config.Relations.Self),
            (path: "relations.sameTeam", kind: config.Relations.SameTeam),
            (path: "relations.default", kind: config.Relations.Default),
        })
        {
            if (!config.Relations.Kinds.ContainsKey(kind))
            {
                throw Invalid(path, $"引用未定义的关系 \"{kind}\"");
            }
        }

        foreach (var o in config.Relations.Overrides)
        {
            if (!config.Relations.Kinds.ContainsKey(o.Kind))
            {
                throw Invalid("relations.overrides", $"引用未定义的关系 \"{o.Kind}\"");
            }
        }

        var radiusSeen = new HashSet<int>();
        foreach (var rc in config.Agents.RadiusClasses)
        {
            if (rc.RadiusCm <= 0)
            {
                throw Invalid("agents.radiusClasses.radiusCm", "需为 > 0 的整数");
            }

            if (!radiusSeen.Add(rc.RadiusCm))
            {
                throw Invalid("agents.radiusClasses", $"半径 {rc.RadiusCm} 重复");
            }

            Rule($"agents.radiusClasses[{rc.RadiusCm}].pushPriority", rc.PushPriority, min: 0);
        }
        Rule("avoidance.rateHz", config.Avoidance.RateHz, gt: 0);
        Rule("avoidance.acceleration", config.Avoidance.Acceleration, gt: 0);
        Rule("avoidance.maxPush", config.Avoidance.MaxPush, gt: 0);
        Rule("avoidance.separationWeight", config.Avoidance.SeparationWeight, min: 0);
        Rule("avoidance.restDeadband", config.Avoidance.RestDeadband, min: 0);
        Rule("avoidance.hashRings", config.Avoidance.HashRings, intRequired: true, min: 1, max: 3);
        Rule("avoidance.maxNeighbors", config.Avoidance.MaxNeighbors, intRequired: true, min: 1);
        Rule("avoidance.maxScan", config.Avoidance.MaxScan, intRequired: true, min: 1);
        Rule("formation.spacingScale", config.Formation.SpacingScale, gt: 0);
        Rule("formation.catchUp", config.Formation.CatchUp, gt: 0);
        Rule("formation.slotSightCells", config.Formation.SlotSightCells, gt: 0);
        Rule("formation.magicBoxMaxSpreadCm", config.Formation.MagicBoxMaxSpreadCm, gt: 0);
        Rule("formation.clumpSlack", config.Formation.ClumpSlack, gt: 0);
        Rule("formation.minClumpCells", config.Formation.MinClumpCells, gt: 0);
        Rule("formation.settleTime", config.Formation.SettleTime, min: 0);
        Rule("formation.magicBoxPadCells", config.Formation.MagicBoxPadCells, min: 0);
        Rule("formation.leaderSpeed", config.Formation.LeaderSpeed, gt: 0);
        Rule("formation.leaderTurnRate", config.Formation.LeaderTurnRate, gt: 0);
        Rule("formation.faceTurnScale", config.Formation.FaceTurnScale, gt: 0);
        Rule("formation.slotArriveCells", config.Formation.SlotArriveCells, gt: 0);
        Rule("formation.goalArriveCells", config.Formation.GoalArriveCells, gt: 0);
        Rule("formation.settleRadiusCells", config.Formation.SettleRadiusCells, gt: 0);
        Rule("formation.leaderLookAhead", config.Formation.LeaderLookAhead, gt: 0);
        Rule("formation.headingInheritDot", config.Formation.HeadingInheritDot, min: 0, max: 1);
        Rule("formation.mirrorFlipDot", config.Formation.MirrorFlipDot, min: -1, max: 0);
        Rule("fog.rateHz", config.Fog.RateHz, gt: 0, max: 30);
        Rule("fog.cellCells", config.Fog.CellCells, intRequired: true, min: 1, max: 32);
        Rule("fog.visionCm", config.Fog.VisionCm, gt: 0);
        Rule("fog.revealTicks", config.Fog.RevealTicks, intRequired: true, min: 1);
        Rule("fog.slotCacheCapacity", config.Fog.SlotCacheCapacity, intRequired: true, min: 0, max: 256);
        Rule("fog.variantCapacity", config.Fog.VariantCapacity, intRequired: true, min: 1);
        Rule("fog.eyeCm", config.Fog.EyeCm, intRequired: true, min: 0);
        Rule("sim.maxUnits", config.Sim.MaxUnits, intRequired: true, min: 1, max: 200000);
        Rule("sim.timeScale", config.Sim.TimeScale, gt: 0);
        Rule("structures.blockCoverage", config.Structures.BlockCoverage, gt: 0, max: 1);
        Rule("structures.portalCells", config.Structures.PortalCells, intRequired: true, min: 1);
        ValidateStructureTemplates(config);
        Rule("deploy.initialUnits", config.Deploy.InitialUnits, intRequired: true, min: 0);
        Rule("deploy.centersPerGroup", config.Deploy.CentersPerGroup, intRequired: true, min: 1);
        Rule("deploy.spreadCells", config.Deploy.SpreadCells, min: 0);
        Rule("deploy.baseJitterCells", config.Deploy.BaseJitterCells, min: 0);

        // ---- 结构检查（LU-19 / 参考实现 validateConfig 的关系型规则）----
        if (string.IsNullOrWhiteSpace(config.MapId))
        {
            throw Invalid("mapId", "需为非空字符串");
        }

        if (string.IsNullOrWhiteSpace(config.World.SurfaceAsset))
        {
            throw Invalid("world.surfaceAsset", "需为非空字符串（.navsurface 资产路径）");
        }

        var terrainIds = CheckIds("world.terrainTypes", TerrainIds(config.World.TerrainTypes), max: 256);
        var areaIds = CheckIds("navAreas", AreaIds(config.NavAreas), max: 256);
        var profileIds = CheckIds("navProfiles", NavProfileIds(config.NavProfiles), max: 256);
        var agentIds = CheckIds("agentTypes", AgentTypeIds(config.AgentTypes), max: 255);
        CheckIds("unitTypes", UnitTypeIds(config.UnitTypes), max: 255);

        foreach (var kvp in config.TerrainAreas)
        {
            if (!terrainIds.Contains(kvp.Key))
            {
                throw Invalid($"terrainAreas.{kvp.Key}", "未知地形类型");
            }

            if (!areaIds.Contains(kvp.Value))
            {
                throw Invalid($"terrainAreas.{kvp.Key}", $"需映射到已定义的导航区域（当前 \"{kvp.Value}\"）");
            }
        }

        foreach (var id in terrainIds)
        {
            if (!config.TerrainAreas.ContainsKey(id))
            {
                throw Invalid("terrainAreas", $"缺少地形类型 \"{id}\" 的映射（每种地形类型恰好映射一次）");
            }
        }

        foreach (var p in config.NavProfiles)
        {
            if (p.MaxSlopeDeg is double slope && !(slope > 0 && slope < 90))
            {
                throw Invalid($"navProfiles.{p.Id}.maxSlopeDeg", "需为 > 0、< 90");
            }

            if (p.Jump is { } jump && !(jump.DownCm >= jump.UpCm && jump.RangeCells > 0 && jump.Cost > 0))
            {
                throw Invalid($"navProfiles.{p.Id}.jump", "参数无效（downCm ≥ upCm，rangeCells / cost > 0）");
            }
        }

        var layerSeen = new HashSet<int>();
        foreach (var a in config.AgentTypes)
        {
            if (!profileIds.Contains(a.Profile))
            {
                throw Invalid($"agentTypes.{a.Id}.profile", $"引用了未定义的 navProfiles \"{a.Profile}\"");
            }

            if (a.Layer < 0 || a.Layer > 255)
            {
                throw Invalid($"agentTypes.{a.Id}.layer", "需为 0~255（导航上下文键 = layer × 256 + clearance）");
            }

            if (!layerSeen.Add(a.Layer))
            {
                throw Invalid($"agentTypes.{a.Id}.layer", $"layer {a.Layer} 重复");
            }

            if (a.AreaCost.Count == 0)
            {
                throw Invalid($"agentTypes.{a.Id}.areaCost", "至少一项；全部不写 = 任何区域都不可通行");
            }

            double minCost = double.PositiveInfinity;
            foreach (var cost in a.AreaCost)
            {
                if (!areaIds.Contains(cost.Key))
                {
                    throw Invalid($"agentTypes.{a.Id}.areaCost.{cost.Key}", "未知导航区域");
                }

                if (!IsFinite(cost.Value) || cost.Value <= 0)
                {
                    throw Invalid($"agentTypes.{a.Id}.areaCost.{cost.Key}", "需为 > 0 的数值（不可通行 = 不写该项）");
                }

                minCost = Math.Min(minCost, cost.Value);
            }

            // A* 启发式 = 八向距离 × 该行最低代价：跳跃代价不得低于它（启发式需可采纳）
            var referenced = Array.Find(config.NavProfiles, p => p.Id == a.Profile);
            if (referenced?.Jump is { } j && j.Cost < minCost)
            {
                throw Invalid($"agentTypes.{a.Id}", "jump.cost 不能低于该类型最低区域代价（寻路启发式需可采纳）");
            }
        }

        foreach (var u in config.UnitTypes)
        {
            if (!agentIds.Contains(u.AgentType))
            {
                throw Invalid($"unitTypes.{u.Id}.agentType", $"引用了未定义的 agentTypes \"{u.AgentType}\"");
            }
        }

        if (config.Avoidance.MaxScan < config.Avoidance.MaxNeighbors)
        {
            throw Invalid("avoidance.maxScan", "不能小于 avoidance.maxNeighbors");
        }

        if (config.Formations.Length == 0)
        {
            throw Invalid("formations", "不能为空");
        }

        for (int i = 0; i < config.Formations.Length; i++)
        {
            var f = config.Formations[i];
            if (!IsFinite(f.Spacing) || f.Spacing <= 0)
            {
                throw Invalid($"formations[{i}].spacing", "需为 > 0 的数值");
            }

            if (!IsFinite(f.Aspect) || f.Aspect <= 0)
            {
                throw Invalid($"formations[{i}].aspect", "需为 > 0 的数值");
            }
        }

        foreach (var mode in new[]
        {
            (path: "push.modes.samePlayer", value: config.Push.Modes.SamePlayer),
            (path: "push.modes.Friendly", value: config.Push.Modes.Friendly),
            (path: "push.modes.Neutral", value: config.Push.Modes.Neutral),
            (path: "push.modes.Hostile", value: config.Push.Modes.Hostile),
        })
        {
            if (mode.value is not ("priority" or "rigid"))
            {
                throw Invalid(mode.path, "需为 priority / rigid");
            }
        }
    }

    /// <summary>结构模板表校验(参考 config.js templates.structures 规则):footprint 枚举、
    /// blocker 只支持 rect、area 与 priority 同进同出、area 必须已定义、priority ≥ 0 整数、
    /// 非阻挡且无 area 的模板是无效实体、lifetimeSec &gt; 0、id 唯一。</summary>
    private static void ValidateStructureTemplates(CrowdSimulationConfig config)
    {
        var templates = config.Structures.Templates;
        if (templates == null) return;
        var areaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in config.NavAreas) areaIds.Add(area.Id);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var footprints = new HashSet<string> { "rect", "disc", "path" };
        for (int i = 0; i < templates.Length; i++)
        {
            var t = templates[i];
            string path = $"structures.templates[{i}]";
            if (string.IsNullOrWhiteSpace(t.Id) || !ids.Add(t.Id))
            {
                throw Invalid($"{path}.id", "需为非空且唯一的字符串");
            }

            if (!footprints.Contains(t.Footprint))
            {
                throw Invalid($"{path}.footprint", "需为 rect / disc / path");
            }

            if (t.Blocker && t.Footprint != "rect")
            {
                throw Invalid($"{path}", "blocker 只支持 rect 占地");
            }

            if ((t.Area == null) != (t.Priority == null))
            {
                throw Invalid($"{path}", "area 与 priority 需同时给出");
            }

            if (t.Area != null && !areaIds.Contains(t.Area))
            {
                throw Invalid($"{path}.area", "未知导航区域");
            }

            if (t.Priority is { } p && p < 0)
            {
                throw Invalid($"{path}.priority", "需为 ≥ 0 的整数");
            }

            if (!t.Blocker && t.Area == null)
            {
                throw Invalid($"{path}", "既不阻挡也不改导航区域（无效实体）");
            }

            if (t.LifetimeSec is { } life && !(life > 0))
            {
                throw Invalid($"{path}.lifetimeSec", "需 > 0");
            }
        }
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    private static IEnumerable<string> TerrainIds(IEnumerable<CrowdSimulationConfig.TerrainTypeEntry> list)
    {
        foreach (var t in list) yield return t.Id;
    }

    private static IEnumerable<string> AreaIds(IEnumerable<CrowdSimulationConfig.NavAreaEntry> list)
    {
        foreach (var t in list) yield return t.Id;
    }

    private static IEnumerable<string> NavProfileIds(IEnumerable<CrowdSimulationConfig.NavProfileEntry> list)
    {
        foreach (var t in list) yield return t.Id;
    }

    private static IEnumerable<string> AgentTypeIds(IEnumerable<CrowdSimulationConfig.AgentTypeEntry> list)
    {
        foreach (var t in list) yield return t.Id;
    }

    private static IEnumerable<string> UnitTypeIds(IEnumerable<CrowdSimulationConfig.UnitTypeEntry> list)
    {
        foreach (var t in list) yield return t.Id;
    }

    private static HashSet<string> CheckIds(string path, IEnumerable<string> ids, int max)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        foreach (var id in ids)
        {
            count++;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw Invalid(path, "id 需为非空字符串");
            }

            if (!set.Add(id))
            {
                throw Invalid(path, $"id \"{id}\" 重复");
            }
        }

        if (count < 1 || count > max)
        {
            throw Invalid(path, $"需要 1~{max} 项（当前 {count} 项）");
        }

        return set;
    }

    private static void Rule(string path, double value, bool intRequired = false, double? min = null, double? gt = null, double? max = null, double? lt = null)
    {
        bool ok = IsFinite(value)
            && (!intRequired || value == Math.Truncate(value))
            && (min == null || value >= min.Value)
            && (gt == null || value > gt.Value)
            && (max == null || value <= max.Value)
            && (lt == null || value < lt.Value);
        if (!ok)
        {
            throw Invalid(path, value, Describe(intRequired, min, gt, max, lt));
        }
    }

    private static string Describe(bool intRequired, double? min, double? gt, double? max, double? lt)
    {
        var parts = new List<string>(capacity: 5);
        if (intRequired) parts.Add("整数");
        if (min != null) parts.Add($"≥ {Fmt(min.Value)}");
        if (gt != null) parts.Add($"> {Fmt(gt.Value)}");
        if (max != null) parts.Add($"≤ {Fmt(max.Value)}");
        if (lt != null) parts.Add($"< {Fmt(lt.Value)}");
        return string.Join("、", parts);
    }

    private static string Fmt(double v) => v.ToString("G17", CultureInfo.InvariantCulture);

    private static InvalidOperationException Invalid(string path, double value, string rule)
        => new($"{FileName}: {path} = {Fmt(value)}，需为{rule}的数值");

    private static InvalidOperationException Invalid(string path, string rule)
        => new($"{FileName}: {path} {rule}");
}
