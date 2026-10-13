using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ludots.Core.CrowdSimulation.Config;

/// <summary>
/// CrowdSimulationConfig.json 的数值范围与关系校验。
/// 数值范围就是下面的 Rule(...)。每边导航格数和 tile 派生格数在
/// CrowdSimulationConfigLoader.Load 里按板宽再查。
/// </summary>
public static class CrowdSimulationConfigValidator
{
    public const string FileName = "CrowdSimulationConfig.json";

    /// <summary>重烘焙分帧上限 = 反应、重规划两个后续阶段 + 命令内阶段。</summary>
    public const int MaxRebakeSlices = 3;

    public static void Validate(CrowdSimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Rule("hpa.maxEntranceWidthNavCells", config.Hpa.MaxEntranceWidthNavCells, intRequired: true, min: 1);
        Rule("navtile.sizeCm", config.Navtile.SizeCm, intRequired: true, gt: 0);
        Rule("navtile.cacheCapacity", config.Navtile.CacheCapacity, intRequired: true, min: 1);
        Rule("navmesh.maxVertsPerPoly", config.Navmesh.MaxVertsPerPoly, intRequired: true, min: 3, max: 12);
        Rule("navmesh.portalInsetNavCells", config.Navmesh.PortalInsetNavCells, min: 0, lt: 1);
        Rule("flowfield.cacheCapacity", config.Flowfield.CacheCapacity, intRequired: true, min: 1);
        Rule("flowfield.poolCapacity", config.Flowfield.PoolCapacity, intRequired: true, min: 0);
        Rule("flowfield.corridorPaddingNavTiles", config.Flowfield.CorridorPaddingNavTiles, intRequired: true, min: 0);
        Rule("flowfield.maxRebuildsPerTick", config.Flowfield.MaxRebuildsPerTick, intRequired: true, min: 1);
        Rule("planning.latencyTicks", config.Planning.LatencyTicks, intRequired: true, min: 1, max: 60);
        Rule("planning.rebakeSlices", config.Planning.RebakeSlices, intRequired: true, min: 1, max: MaxRebakeSlices);
        Rule("planning.refreshLatencyTicks", config.Planning.RefreshLatencyTicks, intRequired: true, min: 1);
        Rule("planning.maxRefreshesPerTick", config.Planning.MaxRefreshesPerTick, intRequired: true, min: 1);
        Rule("push.dominantShare", config.Push.DominantShare, min: 0, max: 0.5);
        Rule("push.movingBonus", config.Push.MovingBonus, min: 0);
        Rule("agents.avoidanceRadiusScale", config.Agents.AvoidanceRadiusScale, gt: 0);
        Rule("spawn.groupSpacingScale", config.Spawn.GroupSpacingScale, gt: 0);
        Rule("spawn.placementTries", config.Spawn.PlacementTries, intRequired: true, min: 1);
        Rule("spawn.targetSearchTries", config.Spawn.TargetSearchTries, intRequired: true, min: 1);
        Rule("spawn.navCellInsetFraction", config.Spawn.NavCellInsetFraction, min: 0, lt: 0.5);
        Rule("movement.sightHysteresis", config.Movement.SightHysteresis, min: 1);
        Rule("movement.wakeDistanceCm", config.Movement.WakeDistanceCm, min: 0);
        Rule("movement.stallDecayPerSec", config.Movement.StallDecayPerSec, min: 0);
        Rule("movement.maxSubSteps", config.Movement.MaxSubSteps, intRequired: true, min: 1, max: CrowdSimulationLimits.MaxSubSteps);
        Rule("movement.restAccelScale", config.Movement.RestAccelScale, gt: 0);
        Rule("movement.restSeparationScale", config.Movement.RestSeparationScale, min: 0);
        // 墙面测试假设单个子步位移小于一格
        Rule("movement.maxStepNavCells", config.Movement.MaxStepNavCells, gt: 0, lt: 1);
        Rule("movement.slotTimeConstantSec", config.Movement.SlotTimeConstantSec, gt: 0);
        Rule("movement.unitTurnRateRadPerSec", config.Movement.UnitTurnRateRadPerSec, gt: 0);
        Rule("movement.jumpSpeedRatio", config.Movement.JumpSpeedRatio, gt: 0);
        Rule("movement.blendRatePerSec", config.Movement.BlendRatePerSec, gt: 0);
        Rule("movement.speedCapRatio", config.Movement.SpeedCapRatio, gt: 0);
        Rule("movement.stopSpeedRatio", config.Movement.StopSpeedRatio, gt: 0);
        Rule("movement.stallSpeedRatio", config.Movement.StallSpeedRatio, gt: 0);
        Rule("movement.slotCheckIntervalSubSteps", config.Movement.SlotCheckIntervalSubSteps, intRequired: true, min: 1);
        Rule("movement.laneSpreadSpeedRatio", config.Movement.LaneSpreadSpeedRatio, min: 0, max: 1);
        Rule("movement.blendCommit", config.Movement.BlendCommit, gt: 0, lt: 1);
        Rule("avoidance.smoothing", config.Avoidance.Smoothing, min: 0, lt: 1);
        RequireRelationId("pairRules.yieldsTo.relation", config.PairRules.YieldsTo.Relation);
        RequireRelationId("pairRules.sharesVisionWith.relation", config.PairRules.SharesVisionWith.Relation);

        Rule("avoidance.rateHz", config.Avoidance.RateHz, gt: 0);
        Rule("avoidance.accelerationPerSec", config.Avoidance.AccelerationPerSec, gt: 0);
        Rule("avoidance.maxPush", config.Avoidance.MaxPush, gt: 0);
        Rule("avoidance.separationWeight", config.Avoidance.SeparationWeight, min: 0);
        Rule("avoidance.restDeadband", config.Avoidance.RestDeadband, min: 0);
        Rule("avoidance.hashRings", config.Avoidance.HashRings, intRequired: true, min: 1, max: 3);
        Rule("avoidance.maxNeighbors", config.Avoidance.MaxNeighbors, intRequired: true, min: 1);
        Rule("avoidance.maxScan", config.Avoidance.MaxScan, intRequired: true, min: 1);
        Rule("formation.spacingScale", config.Formation.SpacingScale, gt: 0);
        Rule("formation.catchUp", config.Formation.CatchUp, gt: 0);
        Rule("formation.slotSightCm", config.Formation.SlotSightCm, gt: 0);
        Rule("formation.magicBoxMaxSpreadCm", config.Formation.MagicBoxMaxSpreadCm, gt: 0);
        Rule("formation.clumpSlack", config.Formation.ClumpSlack, gt: 0);
        Rule("formation.minClumpCm", config.Formation.MinClumpCm, gt: 0);
        Rule("formation.settleTimeSec", config.Formation.SettleTimeSec, min: 0);
        Rule("formation.magicBoxPadCm", config.Formation.MagicBoxPadCm, min: 0);
        Rule("formation.leaderSpeed", config.Formation.LeaderSpeed, gt: 0);
        Rule("formation.leaderTurnRatePerSec", config.Formation.LeaderTurnRatePerSec, gt: 0);
        Rule("formation.faceTurnScale", config.Formation.FaceTurnScale, gt: 0);
        Rule("formation.slotArriveCm", config.Formation.SlotArriveCm, gt: 0);
        Rule("formation.goalArriveCm", config.Formation.GoalArriveCm, gt: 0);
        Rule("formation.settleRadiusCm", config.Formation.SettleRadiusCm, gt: 0);
        Rule("formation.leaderLookAheadCm", config.Formation.LeaderLookAheadCm, gt: 0);
        Rule("formation.headingInheritDot", config.Formation.HeadingInheritDot, min: 0, max: 1);
        Rule("formation.mirrorFlipDot", config.Formation.MirrorFlipDot, min: -1, max: 0);
        Rule("fog.rateHz", config.Fog.RateHz, gt: 0);
        Rule("fog.cellSizeCm", config.Fog.CellSizeCm, intRequired: true, min: CrowdSimulationConfigLoader.MinFogCellSizeCm);
        Rule("fog.visionCm", config.Fog.VisionCm, gt: 0);
        Rule("fog.revealTicks", config.Fog.RevealTicks, intRequired: true, min: 1);
        Rule("fog.slotCacheCapacity", config.Fog.SlotCacheCapacity, intRequired: true, min: 0, max: CrowdSimulationLimits.MaxFogSlotCacheCapacity);
        Rule("fog.variantCapacity", config.Fog.VariantCapacity, intRequired: true, min: 1);
        Rule("fog.eyeCm", config.Fog.EyeCm, intRequired: true, min: 0);
        Rule("sim.maxUnits", config.Sim.MaxUnits, intRequired: true, min: 1, max: 200000);
        Rule("sim.parallelWorkerCount", config.Sim.ParallelWorkerCount, intRequired: true, min: 1, max: CrowdSimulationLimits.MaxParallelWorkers);
        Rule("sim.simSecondsPerTickSecond", config.Sim.SimSecondsPerTickSecond, gt: 0);
        Rule("structures.blockCoverage", config.Structures.BlockCoverage, gt: 0, max: 1);
        Rule("structures.portalReachNavCells", config.Structures.PortalReachNavCells, intRequired: true, min: 1);
        ValidateStructureTemplates(config);
        Rule("deploy.initialUnits", config.Deploy.InitialUnits, intRequired: true, min: 0);
        Rule("deploy.centersPerGroup", config.Deploy.CentersPerGroup, intRequired: true, min: 1);
        Rule("deploy.spreadCm", config.Deploy.SpreadCm, min: 0);
        Rule("deploy.baseJitterCm", config.Deploy.BaseJitterCm, min: 0);

        if (config.Avoidance.MaxScan < config.Avoidance.MaxNeighbors)
        {
            throw Invalid("avoidance.maxScan", "不能小于 avoidance.maxNeighbors");
        }

        if (config.Formations.Count == 0)
        {
            throw Invalid("formations", "不能为空");
        }

        foreach (var (id, f) in config.Formations)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw Invalid("formations", "id 需为非空字符串");
            }

            if (!IsFinite(f.SpacingDiameterScale) || f.SpacingDiameterScale <= 0)
            {
                throw Invalid($"formations.{id}.spacingDiameterScale", "需为 > 0 的数值");
            }

            if (!IsFinite(f.Aspect) || f.Aspect <= 0)
            {
                throw Invalid($"formations.{id}.aspect", "需为 > 0 的数值");
            }
        }

    }

    private static void RequireRelationId(string path, string relation)
    {
        if (string.IsNullOrWhiteSpace(relation))
        {
            throw Invalid(path, "需为非空的关系类型 id");
        }
    }

    /// <summary>只查 footprint 枚举、blocker 仅 rect、area 与 priority 同进同出、priority ≥ 0、
    /// 非阻挡且无 area 拒绝、lifetimeSec 若写则 &gt; 0。区域名是否存在由装载解析。</summary>
    private static void ValidateStructureTemplates(CrowdSimulationConfig config)
    {
        var templates = config.Structures.Templates;
        if (templates == null) return;
        var footprints = new HashSet<string> { "rect", "disc", "path" };
        foreach (var (id, t) in templates)
        {
            string path = $"structures.templates.{id}";
            if (string.IsNullOrWhiteSpace(id))
            {
                throw Invalid(path, "id 需为非空字符串");
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
