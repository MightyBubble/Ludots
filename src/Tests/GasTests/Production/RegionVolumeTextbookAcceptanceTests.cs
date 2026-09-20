using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Region volume textbook acceptance: the whole ambush is authored data — a polygon
/// volume template with an army-only tag filter placed through PositionXCm, a
/// TriggerGraph listening on RegionEntered with a filters.region match, and the
/// one-shot wave gate expressed in the graph body via a map variable chain (the
/// entry-level once field is deliberately unused). The engine truth checked here:
/// untagged crossers never fire, the first army crossing spawns exactly one wave,
/// later crossings increment the blocked counter without spawning, and the volume
/// anchor comes from the placement position fallback.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class RegionVolumeTextbookAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "xx_city_ambush";
    private const int HeartbeatIntervalTicks = 6;

    private static readonly string[] BaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "RegionVolumeTextbookMod",
    };

    [Test]
    public void Ambush_FiresOncePerCrossingArmy_TagFiltered_AndPlacementAnchored()
    {
        using GameEngine engine = CreateEngine(BaseMods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, HeartbeatIntervalTicks * 2);

        MapVariableStore variables = RequireVariables(engine);
        var world = engine.World;

        Assert.That(variables.ReadInt("ambush.fired"), Is.EqualTo(0), "Nothing fires before any crossing.");
        Assert.That(CountByName(world, "TextbookGoblin"), Is.EqualTo(0), "No raiders before the ambush.");
        AssertVolumeAnchoredAtPlacementPosition(world);

        // Untagged scout crossing: the volume's tag filter must swallow it whole.
        Entity scout = FindByName(world, "TextbookScout");
        MoveTo(world, scout, 3200, 1800);
        Tick(engine, HeartbeatIntervalTicks * 3);
        Assert.That(variables.ReadInt("ambush.fired"), Is.EqualTo(0),
            "A scout without Unit.Army must not trigger the ambush (volume entityTags any-of filter).");
        Assert.That(CountByName(world, "TextbookGoblin"), Is.EqualTo(0), "No raiders from the scout crossing.");

        // First army crossing: exactly one wave of two raiders.
        Entity army = FindByName(world, "TextbookArmy");
        MoveTo(world, army, 3200, 1800);
        TickUntil(engine, () => variables.ReadInt("ambush.fired") == 1, HeartbeatIntervalTicks * 4,
            () => "The first army crossing must latch ambush.fired=1.");
        TickUntil(engine, () => CountByName(world, "TextbookGoblin") == 2, HeartbeatIntervalTicks * 4,
            () => "The ambush wave must spawn exactly two goblins via SpawnTemplate.");
        Assert.That(variables.ReadInt("ambush.raiders"), Is.EqualTo(2), "The graph must record the wave size.");

        // Second crossing of the same army: the graph-body one-shot gate blocks the wave.
        MoveTo(world, army, 0, 0);
        Tick(engine, HeartbeatIntervalTicks * 3);
        MoveTo(world, army, 3400, 1900);
        TickUntil(engine, () => variables.ReadInt("ambush.blocked") == 1, HeartbeatIntervalTicks * 4,
            () => "The second crossing must take the blocked branch of the map-variable gate.");
        Assert.That(variables.ReadInt("ambush.raiders"), Is.EqualTo(2),
            "The blocked crossing must not spawn a second wave.");
        Assert.That(CountByName(world, "TextbookGoblin"), Is.EqualTo(2), "Raider count stays at one wave.");
    }

    private static void AssertVolumeAnchoredAtPlacementPosition(World world)
    {
        foreach (ref var chunk in world.Query(in VolumeQuery))
        {
            var volumes = chunk.GetSpan<RegionVolumeCm>();
            var positions = chunk.GetSpan<WorldPositionCm>();
            foreach (var index in chunk)
            {
                if (volumes[index].VolumeKey == "xx_city")
                {
                    Assert.That(positions[index].Value, Is.EqualTo(Fix64Vec2.FromInt(2800, 1400)),
                        "The volume anchor must come from the placement PositionXCm/PositionYCm fallback.");
                    return;
                }
            }
        }

        Assert.Fail("The xx_city volume entity must exist after map load.");
    }

    private static readonly Arch.Core.QueryDescription VolumeQuery = new Arch.Core.QueryDescription()
        .WithAll<MapEntity, RegionVolumeCm, WorldPositionCm>();

    private static MapVariableStore RequireVariables(GameEngine engine)
    {
        MapSession? session = engine.MapSessions?.FocusedSession;
        if (session?.Variables == null)
        {
            throw new InvalidOperationException("The xx_city_ambush session must own its variable store.");
        }

        return session.Variables;
    }

    private static Entity FindByName(World world, string name)
    {
        foreach (ref var chunk in world.Query(in NameQuery))
        {
            ref var entityFirst = ref chunk.Entity(0);
            var names = chunk.GetSpan<Name>();
            foreach (var index in chunk)
            {
                if (string.Equals(names[index].Value, name, StringComparison.Ordinal))
                {
                    return System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                }
            }
        }

        throw new InvalidOperationException($"Entity '{name}' must exist on the map.");
    }

    private static readonly Arch.Core.QueryDescription NameQuery = new Arch.Core.QueryDescription()
        .WithAll<MapEntity, Name>();

    private static int CountByName(World world, string name)
    {
        int count = 0;
        foreach (ref var chunk in world.Query(in NameQuery))
        {
            var names = chunk.GetSpan<Name>();
            foreach (var index in chunk)
            {
                if (string.Equals(names[index].Value, name, StringComparison.Ordinal))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void MoveTo(World world, Entity entity, int xCm, int yCm)
    {
        world.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
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
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Repository root not found.");
    }
}
