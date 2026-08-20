using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Night raid flagship acceptance: the whole level flow (raid start on region entry,
/// wave advance on alive-count clear with a two-wave wait, phase flip on boss death,
/// victory panel on PhaseChanged) is authored as map JSON + one TriggerGraph.
/// The showcase assembly contributes zero level-flow code, so this test drives the
/// world directly (hero position, test-side kills) and asserts on engine truth only.
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

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "NightRaidShowcaseMod",
        "MapTriggerNightRaidMod",
    };

    [Test]
    public void NightRaid_RegionEntryWavesPhaseAndVictory_AllDataDriven()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, HeartbeatIntervalTicks * 4);

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        MapVariableStore variables = engine.CurrentMapSession?.Variables
            ?? throw new InvalidOperationException("night_raid must declare map variables.");
        World world = engine.World;

        Entity hero = FindEntity(world, "NightRaidHero");
        Entity gate = FindEntity(world, "NightRaidGate");
        Assert.That(world.Get<WorldPositionCm>(hero).Value.X.ToFloat(), Is.EqualTo(-600f).Within(0.001f),
            "Hero must spawn outside the raid circle; the raid starts on entry, not on load.");
        AssertTeamCounts(world, teamId: 2, expected: 3, "wave-1 raiders are pre-placed by map data");
        AssertTeamCounts(world, teamId: 3, expected: 2, "wave-2 raiders are pre-placed by map data");
        AssertTeamCounts(world, teamId: 4, expected: 1, "boss is pre-placed by map data");
        Assert.That(variables.ReadInt("wave"), Is.EqualTo(0), "No think wave may spring the raid on its own.");
        Assert.That(variables.ReadInt("phase"), Is.EqualTo(0));
        AssertVictoryPanelAbsent(engine);
        var phaseProbe = new PhaseChangedProbe(engine, MapId);

        MoveHeroIntoRaidCircle(world, hero, xCm: 0, yCm: 0);
        TickUntil(engine, () => variables.ReadInt("wave") == 1, HeartbeatIntervalTicks * 3,
            () => $"RegionEntered never advanced wave (wave={variables.ReadInt("wave")}).");

        Assert.Multiple(() =>
        {
            Assert.That(variables.ReadInt("wave"), Is.EqualTo(1), "RegionEntered(raid_circle) must write wave=1.");
            Assert.That(variables.ReadInt("phase"), Is.EqualTo(0));
            AssertVictoryPanelAbsent(engine);
        });

        List<Entity> waveOneRaiders = CollectTeam(world, teamId: 2);
        foreach (Entity raider in waveOneRaiders)
        {
            world.Destroy(raider);
        }

        TickUntil(engine, () => variables.ReadInt("wave") == 2, HeartbeatIntervalTicks * 8,
            () => $"EntityAliveCountChanged never advanced wave (wave={variables.ReadInt("wave")}).");

        Assert.Multiple(() =>
        {
            Assert.That(variables.ReadInt("wave"), Is.EqualTo(2),
                "Team-2 alive count crossing below 1 must, after two think-wave waits, write wave=2.");
            AssertTeamCounts(world, teamId: 3, expected: 2, "wave-2 raiders survive into wave 2.");
            AssertTeamCounts(world, teamId: 4, expected: 1, "boss survives into wave 2.");
            Assert.That(variables.ReadInt("phase"), Is.EqualTo(0));
            AssertVictoryPanelAbsent(engine);
        });

        world.Destroy(FindEntity(world, "NightRaidBoss"));

        TickUntil(engine, () => variables.ReadInt("phase") == 2, HeartbeatIntervalTicks * 3,
            () => $"EntityDied(boss team) never wrote phase (phase={variables.ReadInt("phase")}).");

        Assert.Multiple(() =>
        {
            Assert.That(variables.ReadInt("phase"), Is.EqualTo(2), "Boss death must write the phase variable.");
            Assert.That(phaseProbe.Observed, Is.True, "Writing the phase variable must fire the PhaseChanged map event.");
            Assert.That(phaseProbe.LastPhase, Is.EqualTo(2));
            Assert.That(world.IsAlive(gate), Is.True,
                "No despawn verb is authorable in TriggerGraph graphs; the gate stays and the phase var + victory panel are the gate-open truth.");
        });

        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");
        Assert.That(panelHost.Count, Is.EqualTo(1), "PhaseChanged must create exactly one victory panel.");
        PanelInstanceHandle panel = FindVictoryPanel(panelHost, hero);
        Assert.That(panelHost.TryGetAnchor(panel, out string anchor), Is.True);
        Assert.That(anchor, Is.EqualTo("screen.topRight"));
        Assert.That(panelHost.TryGetValues(panel, out PanelVariableSet values), Is.True);
        Assert.That(values.Get("heroHealth"), Is.EqualTo(100f).Within(0.001f),
            "The victory panel must project the hero template attribute, never a presentation constant.");
        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
    }

    [Test]
    public void NightRaidMod_AssemblyCarriesNoLevelFlowTypes()
    {
        Type[] types = typeof(MapTriggerNightRaidMod.MapTriggerNightRaidModEntry).Assembly.GetTypes();
        Assert.That(types, Is.EqualTo(new[] { typeof(MapTriggerNightRaidMod.MapTriggerNightRaidModEntry) }),
            "The showcase assembly must contain only the presentation-only entry; all level flow lives in map/graph data.");
    }

    [Test]
    public void GivenANewPlayer_WhenTheyFollowTheNightRaidPrompts_ThenTheyCanFinishTheRaidAndSeeWhyTheGraphAdvanced()
    {
        using GameEngine engine = CreateEngine();
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, HeartbeatIntervalTicks * 4);

        ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
            ?? throw new InvalidOperationException("Night Raid requires the shared screen overlay buffer.");
        PlayerInputHandler input = engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("Night Raid requires the shared input handler.");
        MapVariableStore variables = engine.CurrentMapSession?.Variables
            ?? throw new InvalidOperationException("night_raid must declare map variables.");
        World world = engine.World;
        Entity hero = FindEntity(world, "NightRaidHero");

        Assert.Multiple(() =>
        {
            AssertOverlayContains(overlay, "NIGHT RAID");
            AssertOverlayContains(overlay, "Left click: select");
            AssertOverlayContains(overlay, "Wave 0/2");
        });

        engine.GlobalContext[CoreServiceKeys.TabTargetEntity.Name] = hero;
        PressCommand(engine, input);
        TickUntil(engine, () => variables.ReadInt("wave") == 1, HeartbeatIntervalTicks * 3,
            () => "Right-clicking the selected hero did not enter the raid circle and start wave 1.");

        input.InjectButtonPress("TabTarget");
        Tick(engine, 1);
        Tick(engine, 1);
        Assert.That(engine.GlobalContext.TryGetValue(CoreServiceKeys.TabTargetEntity.Name, out object? tabTarget) && tabTarget is Entity,
            Is.True,
            "Tab must focus a live hostile target for a player who does not click it directly.");

        DefeatTeamWithPlayerCommands(engine, input, teamId: 2);
        TickUntil(engine, () => variables.ReadInt("wave") == 2, HeartbeatIntervalTicks * 8,
            () => "Defeating the first raider group through player commands did not advance to wave 2.");
        AssertOverlayContains(overlay, "Wave 2/2");

        DefeatTeamWithPlayerCommands(engine, input, teamId: 4);
        TickUntil(engine, () => variables.ReadInt("phase") == 2, HeartbeatIntervalTicks * 3,
            () => "Defeating the boss through player commands did not advance to phase 2.");

        Assert.Multiple(() =>
        {
            AssertOverlayContains(overlay, "VICTORY - phase 2 reached");
            Assert.That(engine.GetService(CoreServiceKeys.PanelHost)?.Count, Is.EqualTo(1),
                "Finishing the player-driven raid must reveal the existing victory panel.");
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        });
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(
            RepoModPaths.ResolveExplicit(repoRoot, Mods),
            Path.Combine(repoRoot, "assets"));
        var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
        var inputHandler = new PlayerInputHandler(new NoInputBackend(), inputConfig);
        for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
        {
            inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
        }

        engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
        engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)new NoInputBackend());
        engine.SetService(CoreServiceKeys.UiCaptured, false);
        return engine;
    }

    private static void MoveHeroIntoRaidCircle(World world, Entity hero, int xCm, int yCm)
    {
        world.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
    }

    private static void DefeatTeamWithPlayerCommands(GameEngine engine, PlayerInputHandler input, int teamId)
    {
        foreach (Entity target in CollectTeam(engine.World, teamId).ToArray())
        {
            engine.GlobalContext[CoreServiceKeys.TabTargetEntity.Name] = target;
            for (int strike = 0; strike < 40 && engine.World.IsAlive(target); strike++)
            {
                PressCommand(engine, input);
            }

            TickUntil(engine, () => !engine.World.IsAlive(target), maxFrames: 12,
                () => $"Player command strikes did not defeat team-{teamId} target {target}.");
        }
    }

    private static void PressCommand(GameEngine engine, PlayerInputHandler input)
    {
        input.InjectButtonPress("Command");
        Tick(engine, 1);
        Tick(engine, 1);
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

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
    }

    private static void AssertVictoryPanelAbsent(GameEngine engine)
    {
        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost);
        Assert.That(panelHost, Is.Not.Null);
        Assert.That(panelHost!.Count, Is.EqualTo(0), "No panel may exist before the raid is won.");
    }

    private static PanelInstanceHandle FindVictoryPanel(PanelHost host, Entity scope)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (info.Scope == scope &&
                string.Equals(info.TemplateId, VictoryPanelTemplateId, StringComparison.Ordinal))
            {
                return info.Handle;
            }
        }

        throw new InvalidOperationException("Victory panel was not instantiated for the hero.");
    }

    private static Entity FindEntity(World world, string entityName)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
            {
                result = entity;
            }
        });

        if (result == Entity.Null)
        {
            throw new InvalidOperationException($"Missing entity '{entityName}'.");
        }

        return result;
    }

    private static List<Entity> CollectTeam(World world, int teamId)
    {
        var matches = new List<Entity>();
        var query = new QueryDescription().WithAll<Ludots.Core.Gameplay.Components.Team>();
        world.Query(in query, (Entity entity, ref Ludots.Core.Gameplay.Components.Team team) =>
        {
            if (team.Id == teamId)
            {
                matches.Add(entity);
            }
        });

        return matches;
    }

    private static void AssertTeamCounts(World world, int teamId, int expected, string because)
    {
        Assert.That(CollectTeam(world, teamId).Count, Is.EqualTo(expected), because);
    }

    private static void AssertOverlayContains(ScreenOverlayBuffer overlay, string expected)
    {
        foreach (ref readonly ScreenOverlayItem item in overlay.GetSpan())
        {
            if (item.Kind != ScreenOverlayItemKind.Text)
            {
                continue;
            }

            string? text = overlay.GetString(item.StringId);
            if (!string.IsNullOrEmpty(text) && text.Contains(expected, StringComparison.Ordinal))
            {
                return;
            }
        }

        Assert.Fail($"Night Raid HUD did not contain '{expected}'.");
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

        throw new DirectoryNotFoundException("Failed to locate repository root.");
    }

    private sealed class PhaseChangedProbe : IDisposable
    {
        private readonly TriggerManager _triggers;

        public PhaseChangedProbe(GameEngine engine, string mapId)
        {
            _triggers = engine.TriggerManager;
            MapId = mapId;
            _triggers.RegisterEventHandler(
                new EventKey(MapVariableStore.PhaseChangedEventName),
                context =>
                {
                    MapId firedMap = context.Get<MapId>(CoreServiceKeys.MapId);
                    if (firedMap.Value == MapId)
                    {
                        Observed = true;
                        LastPhase = context.Get<int>(MapVariableStore.PayloadKeyPhase);
                    }

                    return Task.CompletedTask;
                });
        }

        private string MapId { get; }
        public bool Observed { get; private set; }
        public int LastPhase { get; private set; }

        public void Dispose()
        {
        }
    }

    private sealed class NoInputBackend : IInputBackend
    {
        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => Vector2.Zero;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
