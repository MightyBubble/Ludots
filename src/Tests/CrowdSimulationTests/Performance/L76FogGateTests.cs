using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Vision;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace CrowdSimulationTests.Performance;

/// <summary>
/// 换雾之后对照 l76-fog.json。容差锁死：可见格每组每采样 |Δ| ≤ max(4, ceil(0.01×前))，
/// 脚本结束时已探索 |Δ| ≤ max(8, ceil(0.01×前))，双向共享对称差为 0，
/// 单向或无关系时只被 A 照亮的格不出现在 B。tick 中位 ≤ 前×1.05，稳态分配 ≤ 前。
/// 没有雾层时 VisionSystem.Update 中位 ≤ 0.05 ms，0 单位和 5 万单位都过这条线。
/// </summary>
[TestFixture]
public sealed class L76FogGateTests
{
    [Test]
    public void FogCountsSharesAndTickCost_StayWithinBaseline()
    {
        JsonNode baseline = JsonNode.Parse(File.ReadAllText(BaselinePath()))!;
        AssertCounts(baseline["fog"]!, L76FogBaselineHarness.SampleTruth("s7fog-truth.json"));
        AssertCounts(baseline["recycle"]!, L76FogBaselineHarness.SampleTruth("s7crecycle-truth.json"));
        AssertShare(L76FogBaselineHarness.SampleShare());
        AssertPerf(baseline["perf"]!, L76FogBaselineHarness.SamplePerf());
    }

    [Test]
    public void MismatchedLayerCellSize_NamesBothNumbers()
    {
        var layers = new FogLayerRegistry();
        layers.Register(CrowdFogLayerBinding.LayerKey, 100, 20);
        InvalidOperationException mismatch = Assert.Throws<InvalidOperationException>(
            () => CrowdFogLayerBinding.Require(layers, 25000, 4))!;
        Assert.That(mismatch.Message, Does.Contain("100"));
        Assert.That(mismatch.Message, Does.Contain("25000"));

        FogLayerId id = CrowdFogLayerBinding.Require(layers, 100, 4);
        Assert.That(layers.IsExternalStamp(id), Is.True);
    }

    [Test]
    public void VisionSystem_WithoutFogLayer_StaysUnderIdleCeiling()
    {
        double empty = MedianUpdateMs(0);
        double crowded = MedianUpdateMs(50_000);
        TestContext.Out.WriteLine($"idle empty {empty:F4} ms, 50k {crowded:F4} ms");
        Assert.That(empty, Is.LessThanOrEqualTo(0.05));
        Assert.That(crowded, Is.LessThanOrEqualTo(0.05));
    }

    [Test]
    public void TerrainDecal_UsesTheSeatScope()
    {
        var buffer = new GlobalFieldVisualBuffer();
        buffer.BeginFrame();
        UpsertFog(buffer, scopeKeyId: 1, layerKeyId: 1);
        UpsertFog(buffer, scopeKeyId: 2, layerKeyId: 1);
        Assert.That(TerrainDecalSelection.TrySelect(buffer, seatScopeResolved: false, scopeKeyId: 1, out _), Is.False);

        Assert.That(TerrainDecalSelection.TrySelect(buffer, seatScopeResolved: true, scopeKeyId: 2, out GlobalFieldVisualDescriptor second), Is.True);
        Assert.That(second.Id.ScopeKeyId, Is.EqualTo(2));

        buffer.BeginFrame();
        UpsertFog(buffer, scopeKeyId: 2, layerKeyId: 1);
        UpsertKind(buffer, GlobalFieldVisualKind.Flow, scopeKeyId: 9);
        Assert.That(TerrainDecalSelection.TrySelect(buffer, seatScopeResolved: true, scopeKeyId: 2, out GlobalFieldVisualDescriptor flow), Is.True);
        Assert.That(flow.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Flow));
    }

    [Test]
    public void RevealObscureForgetAndFogShare_MatchTheLockedRows()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(120), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        CrowdFog fog = session.Fog!;
        int period = fog.Period;
        int side = fog.FogCellsPerSide;
        int cellSize = fog.FogCellSizeCm;

        Schedule(session, """{"type":"spawnAt","player":1,"xCm":480000,"yCm":560000,"count":8,"unitType":0,"rIdx":0}""");
        Schedule(session, """{"type":"spawnAt","player":2,"xCm":1120000,"yCm":1040000,"count":8,"unitType":0,"rIdx":0}""");
        Step(session);
        for (int i = 0; i < period * 2; i++) Step(session);

        int groupA = fog.GroupOf[session.Config.PairRules.IndexByPlayerId[1]];
        int groupB = fog.GroupOf[session.Config.PairRules.IndexByPlayerId[2]];
        Assert.That(groupA, Is.Not.EqualTo(groupB));
        var beforeA = VisibleCells(fog, groupA);
        var beforeB = VisibleCells(fog, groupB);
        Assert.That(beforeA, Is.Not.Empty);
        Assert.That(beforeB, Is.Not.Empty);

        var seats = new ClientLocalSeatRegistry();
        seats.Add(new ClientLocalSeat("seat.0")
        {
            PossessedPlayerId = 1,
            PossessedRep = session.World.Create(),
        });
        var seatScope = new CrowdSeatVisionScope(seats, () => session);
        Assert.That(seatScope.TryGetScopeKeyId("seat.0", out int localScope), Is.True);
        Assert.That(localScope, Is.EqualTo(groupA + 1));
        Assert.That(seatScope.TryGetScopeKeyId("missing", out _), Is.False);

        const string revealJson = """{"type":"reveal","player":1,"rect":[20000,20000,90000,90000]}""";
        var revealShape = CrowdFogShape.Parse("reveal", JsonNode.Parse(revealJson)!);
        int[] revealCells = CrowdFogArea.AreaCells(side, cellSize, revealShape).Cells;
        Assert.That(revealCells, Is.Not.Empty);
        foreach (int cell in revealCells)
        {
            Assert.That(fog.CellVisibilityOf(groupA, cell), Is.EqualTo(CellVisibility.Unseen));
        }

        Schedule(session, revealJson);
        Step(session);
        var revealed = new List<int>();
        foreach (int cell in revealCells)
        {
            Assert.That(fog.CellVisibilityOf(groupA, cell), Is.EqualTo(CellVisibility.Explored));
            revealed.Add(cell);
        }

        Assert.That(revealed, Is.EqualTo(revealCells));

        int lit = beforeA[0];
        string obscureJson = CellRectCommand("obscure", lit, side, cellSize, period);
        var obscureShape = CrowdFogShape.Parse("obscure", JsonNode.Parse(obscureJson)!);
        int[] obscureCells = CrowdFogArea.AreaCells(side, cellSize, obscureShape).Cells;
        while (session.TickCount % period != 0) Step(session);
        int obscureAt = session.TickCount;
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            new(obscureAt, CrowdSimCommand.Parse(JsonNode.Parse(obscureJson)!)),
        });
        Step(session);
        foreach (int cell in obscureCells)
        {
            Assert.That(fog.CellVisibilityOf(groupA, cell), Is.Not.EqualTo(CellVisibility.Visible), $"遮蔽中格 {cell}");
        }

        while (session.TickCount <= obscureAt + period) Step(session);
        Assert.That(fog.CellVisibilityOf(groupA, lit), Is.EqualTo(CellVisibility.Visible));

        int structureId = FirstStructure(fog);
        Assert.That(fog.Belief[groupA].ContainsKey(structureId), Is.True);
        fog.Store.TryGetFootprint(structureId, out var footprint);
        int[] footprintCells = FootprintCells(fog, footprint);
        string forgetJson = CellsRectCommand("forget", footprintCells, side, cellSize);
        var forgetShape = CrowdFogShape.Parse("forget", JsonNode.Parse(forgetJson)!);
        int[] forgetCells = CrowdFogArea.AreaCells(side, cellSize, forgetShape).Cells;
        Assert.That(new HashSet<int>(forgetCells).IsSupersetOf(footprintCells), Is.True);
        Schedule(session, forgetJson.Replace("\"type\":\"forget\"", "\"type\":\"reveal\""));
        Step(session);
        int previouslyKnown = 0;
        foreach (int cell in forgetCells)
        {
            CellVisibility visibility = fog.CellVisibilityOf(groupA, cell);
            if (visibility is CellVisibility.Explored or CellVisibility.Visible) previouslyKnown++;
        }

        Schedule(session, forgetJson);
        Step(session);
        int nowUnseen = 0;
        foreach (int cell in forgetCells)
        {
            if (fog.CellVisibilityOf(groupA, cell) == CellVisibility.Unseen) nowUnseen++;
        }

        Assert.That(nowUnseen, Is.EqualTo(forgetCells.Length));
        Assert.That(previouslyKnown, Is.EqualTo(forgetCells.Length));
        Assert.That(fog.Belief[groupA].ContainsKey(structureId), Is.False);

        var union = new HashSet<int>(VisibleCells(fog, groupA));
        union.UnionWith(VisibleCells(fog, groupB));
        int sourceScope = fog.VisionField(groupB).ScopeKeyId;
        var sourceLayer = fog.VisionField(groupB).LayerId;
        Assert.That(fog.Fields.TryGet(sourceScope, sourceLayer, out _), Is.True);
        Schedule(session, """{"type":"fogShare","player":1,"with":2}""");
        Step(session);
        Assert.That(fog.ScopeRetired(groupB), Is.True);
        Assert.That(fog.Fields.TryGet(sourceScope, sourceLayer, out _), Is.False);
        Assert.That(fog.GroupOf[session.Config.PairRules.IndexByPlayerId[2]], Is.EqualTo((byte)groupA));
        while (session.TickCount % period != 1) Step(session);
        var merged = new HashSet<int>(VisibleCells(fog, groupA));
        Assert.That(merged.SetEquals(union), Is.True, $"并组后 {merged.Count} 格，并集 {union.Count} 格");
        Assert.That(fog.Fields.TryGet(sourceScope, sourceLayer, out _), Is.False);
        Assert.That(seatScope.TryGetScopeKeyId("seat.0", out int scopeAfterShare), Is.True);
        Assert.That(scopeAfterShare, Is.EqualTo(groupA + 1));
    }

    /// <summary>认知导航版本和到达集合要对照 L75 基线。L75 还没落地，这行先不测。</summary>
    [Test]
    public void CognitiveNav_AgainstL75Baseline_WaitsForL75()
    {
        Assert.Ignore("认知导航版本和到达集合对照 L75 基线。L75 还没落地，这行先不测。");
    }

    private static void AssertCounts(JsonNode before, JsonNode after)
    {
        JsonArray beforeSamples = before["samples"]!.AsArray();
        JsonArray afterSamples = after["samples"]!.AsArray();
        Assert.That(afterSamples.Count, Is.EqualTo(beforeSamples.Count));
        for (int i = 0; i < beforeSamples.Count; i++)
        {
            JsonArray beforeVisible = beforeSamples[i]!["visible"]!.AsArray();
            JsonArray afterVisible = afterSamples[i]!["visible"]!.AsArray();
            Assert.That(afterVisible.Count, Is.EqualTo(beforeVisible.Count));
            for (int g = 0; g < beforeVisible.Count; g++)
            {
                int previous = beforeVisible[g]!.GetValue<int>();
                int current = afterVisible[g]!.GetValue<int>();
                int tolerance = Math.Max(4, (int)Math.Ceiling(0.01 * previous));
                Assert.That(
                    Math.Abs(current - previous),
                    Is.LessThanOrEqualTo(tolerance),
                    $"tick {beforeSamples[i]!["tick"]} 组 {g} 可见 {current}，基线 {previous}，容差 {tolerance}");
            }
        }

        JsonArray beforeEnd = before["exploredEnd"]!.AsArray();
        JsonArray afterEnd = after["exploredEnd"]!.AsArray();
        Assert.That(afterEnd.Count, Is.EqualTo(beforeEnd.Count));
        for (int g = 0; g < beforeEnd.Count; g++)
        {
            int previous = beforeEnd[g]!.GetValue<int>();
            int current = afterEnd[g]!.GetValue<int>();
            int tolerance = Math.Max(8, (int)Math.Ceiling(0.01 * previous));
            Assert.That(
                Math.Abs(current - previous),
                Is.LessThanOrEqualTo(tolerance),
                $"已探索结束 组 {g} {current}，基线 {previous}，容差 {tolerance}");
        }
    }

    private static void AssertShare(JsonNode share)
    {
        JsonNode bidirectional = share["bidirectional"]!;
        Assert.That(bidirectional["sameGroup"]!.GetValue<bool>(), Is.True);
        Assert.That(bidirectional["symmetricDiff"]!.GetValue<int>(), Is.EqualTo(0));

        JsonNode noRelation = share["noRelation"]!;
        Assert.That(noRelation["sameGroup"]!.GetValue<bool>(), Is.False);
        Assert.That(noRelation["onlyAInB"]!.GetValue<int>(), Is.EqualTo(0));

        JsonNode oneWay = share["oneWay"]!;
        Assert.That(oneWay["sameGroup"]!.GetValue<bool>(), Is.False);
        Assert.That(oneWay["onlyAInB"]!.GetValue<int>(), Is.EqualTo(0));
    }

    private static void AssertPerf(JsonNode before, JsonNode after)
    {
        double before12 = before["tick12kMedianMs"]!.GetValue<double>();
        double after12 = after["tick12kMedianMs"]!.GetValue<double>();
        double before50 = before["tick50kMedianMs"]!.GetValue<double>();
        double after50 = after["tick50kMedianMs"]!.GetValue<double>();
        long allocBefore12 = before["steadyAlloc12kMedian"]!.GetValue<long>();
        long allocAfter12 = after["steadyAlloc12kMedian"]!.GetValue<long>();
        long allocBefore50 = before["steadyAlloc50kMedian"]!.GetValue<long>();
        long allocAfter50 = after["steadyAlloc50kMedian"]!.GetValue<long>();
        TestContext.Out.WriteLine(
            $"12k {after12:F4} ms (基线 {before12:F4}, 上限 {before12 * 1.05:F4}) alloc {allocAfter12} (基线 {allocBefore12})");
        TestContext.Out.WriteLine(
            $"50k {after50:F4} ms (基线 {before50:F4}, 上限 {before50 * 1.05:F4}) alloc {allocAfter50} (基线 {allocBefore50})");
        Assert.That(after12, Is.LessThanOrEqualTo(before12 * 1.05));
        Assert.That(after50, Is.LessThanOrEqualTo(before50 * 1.05));
        Assert.That(allocAfter12, Is.LessThanOrEqualTo(allocBefore12));
        Assert.That(allocAfter50, Is.LessThanOrEqualTo(allocBefore50));
    }

    private static void UpsertFog(GlobalFieldVisualBuffer buffer, int scopeKeyId, int layerKeyId) =>
        UpsertKind(buffer, GlobalFieldVisualKind.Fog, scopeKeyId, layerKeyId);

    private static void UpsertKind(GlobalFieldVisualBuffer buffer, GlobalFieldVisualKind kind, int scopeKeyId, int layerKeyId = 1)
    {
        buffer.Upsert(
            new GlobalFieldVisualDescriptor(
                new GlobalFieldVisualId(kind, scopeKeyId, layerKeyId, surfaceKeyId: 0),
                cellSizeCm: 25000,
                WorldCmInt2.Zero,
                new IntRect(0, 0, 1, 1),
                GlobalFieldVisualValueKind.Byte),
            ReadOnlySpan<GlobalFieldVisualCell>.Empty,
            ReadOnlySpan<IntRect>.Empty);
    }

    private static void Schedule(CrowdSimSession session, string json)
    {
        session.Commands.Schedule(session, new List<CrowdCommand>
        {
            new(session.TickCount, CrowdSimCommand.Parse(JsonNode.Parse(json)!)),
        });
    }

    private static void Step(CrowdSimSession session)
    {
        if (session.Step(out _)) return;
        int guard = 0;
        while (!session.Step(out _))
        {
            if (guard++ > 4096) throw new InvalidOperationException($"tick {session.TickCount} 停摆未恢复。");
        }
    }

    private static List<int> VisibleCells(CrowdFog fog, int group)
    {
        int f2 = fog.FogCellsPerSide * fog.FogCellsPerSide;
        var cells = new List<int>();
        for (int c = 0; c < f2; c++)
        {
            if (fog.CellVisibilityOf(group, c) == CellVisibility.Visible) cells.Add(c);
        }

        return cells;
    }

    private static string CellRectCommand(string type, int cell, int side, int cellSize, int? ticks = null)
    {
        int x = cell % side;
        int y = cell / side;
        return RectCommand(type, x * cellSize, y * cellSize, (x + 1) * cellSize, (y + 1) * cellSize, ticks);
    }

    private static string CellsRectCommand(string type, int[] cells, int side, int cellSize)
    {
        int minX = side, minY = side, maxX = 0, maxY = 0;
        foreach (int cell in cells)
        {
            int x = cell % side;
            int y = cell / side;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        return RectCommand(type, minX * cellSize, minY * cellSize, (maxX + 1) * cellSize, (maxY + 1) * cellSize, ticks: null);
    }

    private static string RectCommand(string type, int x0, int y0, int x1, int y1, int? ticks)
    {
        string extra = ticks is int value ? $",\"ticks\":{value}" : string.Empty;
        return $$"""{"type":"{{type}}"{{extra}},"player":1,"rect":[{{x0}},{{y0}},{{x1}},{{y1}}]}""";
    }

    private static int FirstStructure(CrowdFog fog)
    {
        foreach (int id in fog.Store.EntityIds)
        {
            if (fog.Store.TryGetFootprint(id, out _)) return id;
        }

        throw new InvalidOperationException("图上没有结构足迹。");
    }

    private static int[] FootprintCells(CrowdFog fog, CrowdStructureFootprint footprint)
    {
        var (minX, minY, maxX, maxY) = footprint.Bbox();
        Fix64 fogCellCm = Fix64.FromInt(fog.FogCellSizeCm);
        int x0 = Math.Max(0, (int)(minX / fogCellCm).ToLong());
        int y0 = Math.Max(0, (int)(minY / fogCellCm).ToLong());
        int x1 = Math.Min(fog.FogCellsPerSide - 1, (int)(maxX / fogCellCm).ToLong());
        int y1 = Math.Min(fog.FogCellsPerSide - 1, (int)(maxY / fogCellCm).ToLong());
        var cells = new List<int>();
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++) cells.Add(y * fog.FogCellsPerSide + x);
        }

        return cells.ToArray();
    }

    private static double MedianUpdateMs(int units)
    {
        using World world = World.Create();
        var layers = new FogLayerRegistry();
        var fields = new FogFieldStore();
        var knowledge = new KnowledgeProjectionStore();
        var system = new VisionSystem(
            world,
            new GameSession(),
            layers,
            fields,
            new VisionResolver(layers, fields),
            new FogKnowledgeProjector(knowledge),
            knowledge,
            new PlayerOwnedEntityObserverResolver(new PlayerEntityLookup()));
        for (int i = 0; i < units; i++)
        {
            world.Create(WorldPositionCm.FromCm(i % 4000, i / 4000));
        }

        for (int i = 0; i < 30; i++) system.Update(0.02f);
        var samples = new double[5];
        for (int run = 0; run < samples.Length; run++)
        {
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) system.Update(0.02f);
            watch.Stop();
            samples[run] = watch.Elapsed.TotalMilliseconds / 200.0;
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static string BaselinePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "src", "Tests", "CrowdSimulationTests", "Baselines", "l76-fog.json");
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到 l76-fog.json。");
    }
}
