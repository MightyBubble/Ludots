using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Units;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>
/// HUD 迷雾计数与独立全格扫描逐 tick 一致:可见格、已探索格、残影(认知有、真相无足迹)。
/// 会话是 s1337 真图加 s7 迷雾脚本(行军、放置、拆除、reveal / obscure / forget、并组、
/// 乐观地形开关),并插入一条短寿命路障,让寿命到期也走进计数。
/// </summary>
public sealed class FogHudCountTests
{
    [Test]
    public void IncrementalCounts_MatchFullScanAcrossFogScript()
    {
        var (runtime, session) = Parity.S4DeployTruthTests.BuildSession("s1337");
        using var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext);
        session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
        session.BlockOnDueReplies = true;
        Assert.That(session.Fog, Is.Not.Null);
        Assert.That(session.Fog!.Store, Is.SameAs(session.Structures));

        string dir = Path.Combine(Parity.S1SurfaceTruthTests.SeedDir("s1337"), "CrowdSimulation", "parity");
        var meta = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "s7fog-truth.json")))!;
        int ticks = meta["ticks"]!.GetValue<int>();
        Assert.That(ticks, Is.GreaterThanOrEqualTo(300));
        var script = new List<CrowdCommand>();
        foreach (var entry in meta["script"]!.AsArray())
        {
            script.Add(new CrowdCommand(entry!["tick"]!.GetValue<int>(), CrowdSimCommand.Parse(entry["cmd"]!)));
        }

        script.Add(new CrowdCommand(6, CrowdSimCommand.Parse(JsonNode.Parse(
            """{"type":"placeStructure","template":"s7barrier","xCm":620000,"yCm":620000,"sizeCm":4000}""")!)));
        script.Add(new CrowdCommand(7, CrowdSimCommand.Parse(JsonNode.Parse(
            """{"type":"reveal","player":1,"rect":[610000,610000,630000,630000]}""")!)));
        script.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        session.Commands.Schedule(session, script);

        var fog = session.Fog;
        int sawVisible = 0, sawExplored = 0, sawGhost = 0;
        int entitiesAfterBarrier = -1, entitiesAfterExpiry = -1;
        for (int t = 0; t < ticks; t++)
        {
            if (!session.Step(out _))
            {
                int guard = 0;
                while (!session.Step(out _))
                {
                    if (guard++ > 4096) throw new InvalidOperationException($"tick {t} 停摆未恢复。");
                }
            }

            if (t == 6) entitiesAfterBarrier = session.Structures!.EntityIds.Count;
            if (t == 18) entitiesAfterExpiry = session.Structures!.EntityIds.Count;

            int f2 = fog.F * fog.F;
            for (int g = 0; g < fog.G; g++)
            {
                int visible = 0, explored = 0;
                int origin = g * f2;
                for (int c = 0; c < f2; c++)
                {
                    if (fog.Visible[origin + c] != 0) visible++;
                    if (fog.Explored[origin + c] != 0) explored++;
                }

                int ghosts = 0;
                foreach (int id in fog.Belief[g].Keys)
                {
                    if (!fog.Store.TryGetFootprint(id, out _)) ghosts++;
                }

                Assert.That(fog.VisibleCount[g], Is.EqualTo(visible), $"tick {t} 组 {g} 可见格");
                Assert.That(fog.ExploredCount[g], Is.EqualTo(explored), $"tick {t} 组 {g} 已探索格");
                Assert.That(fog.GhostCount[g], Is.EqualTo(ghosts), $"tick {t} 组 {g} 残影");
                if (visible > sawVisible) sawVisible = visible;
                if (explored > sawExplored) sawExplored = explored;
                if (ghosts > sawGhost) sawGhost = ghosts;
            }
        }

        Assert.That(entitiesAfterExpiry, Is.EqualTo(entitiesAfterBarrier - 1), "短寿命路障应在放置后到期离仓");
        Assert.That(sawVisible, Is.GreaterThan(0), "行军后应有可见格");
        Assert.That(sawExplored, Is.GreaterThan(0), "应有已探索格");
        Assert.That(sawGhost, Is.GreaterThan(0), "拆除或寿命到期后应出现残影");
    }
}
