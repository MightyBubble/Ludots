using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Contact sensor textbook acceptance (#1480): the pressure plate and the ball are
/// authored purely as entity template data (Position2D / Collider2D / Mass2D /
/// static-state / emitter + the unified emission contract), the graph listens on
/// the declared custom contact events, and engine truth proves the chain — the
/// kinematic ball rolling onto the static plate fires demo.plate.pressed through
/// the real physics pipeline (broadphase sensor pairing, narrowphase edges,
/// routing tap), rolling off fires demo.plate.released.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
public sealed class ContactSensorTextbookAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "contact_sensor_textbook";

    private static readonly string[] BaseMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "ContactSensorTextbookMod",
    };

    [Test]
    public void BallRollsOntoPlate_FiresPressedContract_ThenReleasedWhenLeaving()
    {
        using GameEngine engine = CreateEngine(BaseMods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 8);

        MapVariableStore variables = RequireVariables(engine);
        var world = engine.World;
        Assert.That(variables.ReadInt("press.count"), Is.EqualTo(0), "Nothing pressed before the ball arrives.");

        Entity ball = FindByName(world, "TextbookBall");
        Entity plate = FindByName(world, "TextbookPlate");
        Assert.That(world.Has<Physics2DStaticBodyState>(plate), Is.True,
            "The plate must be a template-authored static physics body.");
        Assert.That(world.TryGet(ball, out Mass2D ballMass) && ballMass.IsKinematic, Is.True,
            "The ball must be a template-authored kinematic physics body.");

        // Roll the ball toward the plate (plate box half-width 150 at x=600; ball radius 40
        // means contact once the ball center passes x >= 410).
        for (int x = 0; x <= 500 && variables.ReadInt("press.count") == 0; x += 50)
        {
            MoveBall(world, ball, x, 0);
            Tick(engine, 6);
        }

        Assert.That(variables.ReadInt("press.count"), Is.EqualTo(1),
            "Rolling onto the plate must fire demo.plate.pressed through the real physics pipeline.");
        Assert.That(variables.ReadInt("release.count"), Is.EqualTo(0), "No release while still on the plate.");

        // Roll back off.
        for (int x = 500; x >= 0 && variables.ReadInt("release.count") == 0; x -= 50)
        {
            MoveBall(world, ball, x, 0);
            Tick(engine, 6);
        }

        Assert.That(variables.ReadInt("release.count"), Is.EqualTo(1),
            "Rolling off the plate must fire demo.plate.released.");
        Assert.That(variables.ReadInt("press.count"), Is.EqualTo(1), "No double press during the single visit.");
    }

    private static void MoveBall(World world, Entity ball, int xCm, int yCm)
    {
        world.Set(ball, new Position2D { Value = Ludots.Core.Mathematics.FixedPoint.Fix64Vec2.FromInt(xCm, yCm) });
    }

    private static MapVariableStore RequireVariables(GameEngine engine)
    {
        MapSession? session = engine.MapSessions?.FocusedSession;
        if (session?.Variables == null)
        {
            throw new InvalidOperationException("The contact_sensor_textbook session must own its variable store.");
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
