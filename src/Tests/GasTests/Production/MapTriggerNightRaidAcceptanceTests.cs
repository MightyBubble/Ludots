using System;
using System.Linq;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Night raid two-phase chain acceptance. The hardcoded interaction layer only exists in
/// the companion mod for human play; the tests drive the same chain through engine
/// truth: region entry starts the raid, entity deaths (via the map's DeathRule) count
/// into the graph, the counted threshold flips phases, the boss death runs a two-beat
/// Yield delay, and the victory panel lands. Custom events (NightRaid.KillTool.Used)
/// are declared in mod data, fired through the validated facade, and counted by both
/// the base and the override mod's stacked graph.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class MapTriggerNightRaidAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "night_raid";
    private const string VictoryPanelTemplateId = "panel.night_raid.victory";
    private const int HeartbeatIntervalTicks = 6;

    private static readonly string[] BaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "MapTriggerNightRaidMod",
    };

    [Test]
    public void NightRaid_TwoPhaseChain_Region_KillCount_Delay_Panel()
    {
        using GameEngine engine = CreateEngine(BaseMods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        MapVariableStore variables = RequireVariables(engine);
        var world = engine.World;

        Assert.That(variables.ReadInt("stage"), Is.EqualTo(1), "MapLoaded must write stage=1.");

        // Phase gate: hero enters the raid circle -> stage 2.
        Entity hero = FindHero(world);
        world.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(0, 0) });
        TickUntil(engine, () => variables.ReadInt("stage") == 2, HeartbeatIntervalTicks * 3,
            () => "RegionEntered(raid_circle) must write stage=2.");

        Assert.That(CountBossEntities(world), Is.EqualTo(0),
            "The boss must not exist before the raid threshold — two-phase reveal is graph-spawned.");

        TickUntil(engine, () => CountTeamEntities(world, teamId: 2) == 3, HeartbeatIntervalTicks * 3,
            () => "Entering the raid circle must materialize the three wave 1 raiders before they can be defeated.");

        // Kill wave1 through the data path: zero health -> DeathRule destroys
        // -> EntityDied(team 2) -> graph increments kill_count. The clear event
        // then spawns wave2 from its independent team/template data.
        for (int kill = 1; kill <= 3; kill++)
        {
            KillOneTeamEntity(world, teamId: 2, maxKills: 1);
            int expected = kill;
            TickUntil(engine, () => variables.ReadInt("kill_count") == expected, HeartbeatIntervalTicks * 3,
                () => $"Raider kill {kill} must increment kill_count to {expected} via EntityDied (got {variables.ReadInt("kill_count")}).");
        }

        TickUntil(engine, () => CountTeamEntities(world, teamId: 3) == 2, HeartbeatIntervalTicks * 3,
            () => "Clearing team 2 must spawn two team 3 elite raiders.");

        for (int kill = 4; kill <= 5; kill++)
        {
            KillOneTeamEntity(world, teamId: 3, maxKills: 1);
            int expected = kill;
            TickUntil(engine, () => variables.ReadInt("kill_count") == expected, HeartbeatIntervalTicks * 3,
                () => $"Elite raider kill {kill - 3} must increment kill_count to {expected} (got {variables.ReadInt("kill_count")}).");
        }

        Assert.That(variables.ReadInt("stage"), Is.EqualTo(3),
            "Reaching kill_threshold=5 must advance to the boss phase.");

        // Boss phase: the graph spawns the boss at stage 3 — before that it must not exist.
        TickUntil(engine, () => CountBossEntities(world) == 1, HeartbeatIntervalTicks * 3,
            () => "Stage 3 must spawn the boss template via the SpawnTemplate graph op.");
        KillBoss(engine, world);
        TickUntil(engine, () => variables.ReadInt("stage") == 4, HeartbeatIntervalTicks * 3,
            () => "Boss EntityDied must write stage=4.");
        Tick(engine, HeartbeatIntervalTicks);
        Assert.That(variables.ReadInt("stage"), Is.EqualTo(4),
            "One beat after stage 4 the graph must still be waiting in its Yield delay.");
        TickUntil(engine, () => variables.ReadInt("stage") == 5, HeartbeatIntervalTicks * 4,
            () => "Two beats after stage 4 the Yield delay must release into stage 5.");

        var panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        int victoryCount = 0;
        foreach (PanelHostInstanceInfo info in panelHost.SnapshotInstances())
        {
            if (string.Equals(info.TemplateId, VictoryPanelTemplateId, StringComparison.Ordinal))
            {
                victoryCount++;
            }
        }

        Assert.That(victoryCount, Is.EqualTo(1), "Stage 5 must create exactly one victory panel.");
        Assert.That(panelHost.Count, Is.EqualTo(3),
            "Map load (progress), boss spawn (alert), and victory each own one panel instance.");
        Assert.That(FindVictoryPanelValues(panelHost), Is.True,
            "The victory panel must project the hero template attribute, never a presentation constant.");
        float heroHealth = engine.World.Get<Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(hero).GetCurrent(AttributeRegistry.GetId("Health"));
        Assert.That(FindVictoryPanelValue(panelHost, "heroHealth"), Is.EqualTo(heroHealth),
            "heroHealth must equal the mount anchor hero's current health — pinning the LoadSelfAttribute query-graph scope semantics.");
        Assert.That(FindPanelValue(panelHost, "panel.night_raid.progress", "kill_count"), Is.EqualTo(5f),
            "progress panel kill_count must flow through the Schema v2 query graph reading the map variable.");
        Assert.That(FindPanelValue(panelHost, "panel.night_raid.progress", "stage"), Is.EqualTo(5f),
            "progress panel stage must flow through the Schema v2 query graph reading the map variable.");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
    }

    [Test]
    public void NightRaid_CustomEvents_DeclaredFiredCounted_AndFailClosed()
    {
        using GameEngine engine = CreateEngine(BaseMods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        MapVariableStore variables = RequireVariables(engine);
        var registry = engine.GetService(CoreServiceKeys.CustomEventNameRegistry)
            ?? throw new InvalidOperationException("CustomEventNameRegistry missing.");

        Assert.That(registry.IsDeclaredCustom("NightRaid.KillTool.Used"), Is.True,
            "The mod's Events/custom_events.json declaration must load into the registry.");

        var context = engine.CreateContext();
        context.Set(CoreServiceKeys.MapId, engine.CurrentMapSession!.MapId);
        context.Set(CoreServiceKeys.MapSession, engine.CurrentMapSession);
        for (int i = 1; i <= 3; i++)
        {
            engine.TriggerManager.FireMapCustomEvent(
                engine.CurrentMapSession.MapId, "NightRaid.KillTool.Used", context, registry);
            int expected = i;
            TickUntil(engine, () => variables.ReadInt("tool_uses") == expected, 2,
                () => $"Custom event fire {i} must increment tool_uses to {expected}.");
        }

        Assert.Throws<InvalidOperationException>(() =>
            engine.TriggerManager.FireMapCustomEvent(
                engine.CurrentMapSession.MapId, "No.Such.Event", context, registry),
            "Firing an undeclared custom event must fail closed.");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
    }

    [Test]
    public void NightRaid_InterModOverride_ThresholdAndStackedFlows()
    {
        string[] mods = { BaseMods[0], BaseMods[1], "NightRaidOverrideMod", BaseMods[2] };
        using GameEngine engine = CreateEngine(mods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        MapVariableStore variables = RequireVariables(engine);
        var world = engine.World;

        Assert.That(variables.ReadInt("kill_threshold"), Is.EqualTo(2),
            "The override mod's map fragment must replace the base kill threshold with 2.");
        Assert.That(variables.ReadInt("override_active"), Is.EqualTo(1),
            "The stacked override graph must run on MapLoaded alongside the base graph.");

        Entity hero = FindHero(world);
        world.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(0, 0) });
        TickUntil(engine, () => variables.ReadInt("stage") == 2, HeartbeatIntervalTicks * 3,
            () => "RegionEntered must still start the raid with the override mod loaded.");

        TickUntil(engine, () => CountTeamEntities(world, teamId: 2) == 3, HeartbeatIntervalTicks * 3,
            () => "The override flow must also materialize wave 1 before the first kill.");

        KillOneTeamEntity(world, teamId: 2, maxKills: 1);
        TickUntil(engine, () => variables.ReadInt("kill_count") == 1, HeartbeatIntervalTicks * 3,
            () => "First raider kill must count.");
        KillOneTeamEntity(world, teamId: 2, maxKills: 1);
        TickUntil(engine, () => variables.ReadInt("stage") >= 3, HeartbeatIntervalTicks * 3,
            () => "With threshold 2 the second raider kill must leave the raider phase immediately.");
        Assert.That(variables.ReadInt("kill_count"), Is.LessThan(3),
            "The phase must flip via the overridden threshold, not the base one.");

        // N3 regression: kills past the threshold restart the raider-death chain; the
        // equality crossing guard must keep the boss population at exactly one.
        TickUntil(engine, () => CountBossEntities(world) == 1, HeartbeatIntervalTicks * 3,
            () => "Crossing the overridden threshold must spawn exactly one boss.");
        KillOneTeamEntity(world, teamId: 2, maxKills: 1);
        KillOneTeamEntity(world, teamId: 2, maxKills: 1);
        Tick(engine, HeartbeatIntervalTicks * 2);
        Assert.That(CountBossEntities(world), Is.EqualTo(1),
            "Post-threshold kills must never spawn a second boss (equality crossing guard).");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
    }

    [Test]
    public void NightRaidMod_ContainsNoShowcaseAssembly()
    {
        string modRoot = Path.Combine(FindRepoRoot(), "mods", "showcases", "map_trigger_night_raid", "MapTriggerNightRaidMod");
        Assert.That(File.Exists(Path.Combine(modRoot, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modRoot, "MapTriggerNightRaidMod.csproj")), Is.False,
            "The night raid level flow must stay code-free: map + graphs + events + panel data only.");
    }

    private static int CountTeamEntities(World world, int teamId)
    {
        int count = 0;
        world.Query(
            new QueryDescription().WithAll<Ludots.Core.Gameplay.Components.Team, Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(),
            (Entity entity, ref Ludots.Core.Gameplay.Components.Team team) =>
            {
                if (team.Id == teamId)
                {
                    count++;
                }
            });
        return count;
    }

    private static void KillOneTeamEntity(World world, int teamId, int maxKills)
    {
        ZeroTeamHealth(world, teamId, maxKills);
    }

    private static int CountBossEntities(World world)
    {
        int count = 0;
        world.Query(
            new QueryDescription().WithAll<Ludots.Core.Gameplay.Components.Team, Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(),
            (Entity entity, ref Ludots.Core.Gameplay.Components.Team team) =>
            {
                if (team.Id == 4)
                {
                    count++;
                }
            });
        return count;
    }

    private static void KillBoss(GameEngine engine, World world)
    {
        ZeroTeamHealth(world, teamId: 4, maxKills: int.MaxValue);
    }

    private static void ZeroTeamHealth(World world, int teamId, int maxKills)
    {
        int killed = 0;
        world.Query(new QueryDescription().WithAll<Ludots.Core.Gameplay.Components.Team, Ludots.Core.Gameplay.GAS.Components.AttributeBuffer>(),
            (Entity entity, ref Ludots.Core.Gameplay.Components.Team team, ref Ludots.Core.Gameplay.GAS.Components.AttributeBuffer attributes) =>
            {
                if (team.Id == teamId && killed < maxKills &&
                    attributes.GetCurrent(AttributeRegistry.GetId("Health")) > 0f)
                {
                    attributes.SetBase(AttributeRegistry.GetId("Health"), 0f);
                    killed++;
                }
            });
    }

    private static MapVariableStore RequireVariables(GameEngine engine)
    {
        return engine.CurrentMapSession?.Variables
            ?? throw new InvalidOperationException("night_raid must declare map variables.");
    }

    private static float FindVictoryPanelValue(PanelHost panelHost, string variable)
    {
        return FindPanelValue(panelHost, VictoryPanelTemplateId, variable);
    }

    private static float FindPanelValue(PanelHost panelHost, string templateId, string variable)
    {
        foreach (PanelHostInstanceInfo info in panelHost.SnapshotInstances())
        {
            if (string.Equals(info.TemplateId, templateId, StringComparison.Ordinal) &&
                panelHost.TryGetValues(info.Handle, out PanelVariableSet values))
            {
                return values.Get(variable);
            }
        }

        throw new InvalidOperationException($"Panel instance '{templateId}' missing.");
    }

    private static bool FindVictoryPanelValues(PanelHost panelHost)
    {
        foreach (PanelHostInstanceInfo info in panelHost.SnapshotInstances())
        {
            if (string.Equals(info.TemplateId, VictoryPanelTemplateId, StringComparison.Ordinal) &&
                panelHost.TryGetValues(info.Handle, out PanelVariableSet values))
            {
                return values.Get("heroHealth") > 0f;
            }
        }

        return false;
    }

    private static Entity FindHero(World world)
    {
        Entity found = Entity.Null;
        world.Query(new QueryDescription().WithAll<Name>(), (Entity entity, ref Name name) =>
        {
            if (found == Entity.Null && string.Equals(name.Value, "NightRaidHero", StringComparison.Ordinal))
            {
                found = entity;
            }
        });
        return found != Entity.Null ? found : throw new InvalidOperationException("NightRaidHero missing.");
    }

    private static GameEngine CreateEngine(string[] mods)
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, mods),
            Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        return engine;
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames, Func<string> describeFailure)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            Tick(engine, 1);
            if (condition())
            {
                return;
            }
        }

        Assert.Fail(describeFailure());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "mods")) &&
                File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found.");
    }
}
