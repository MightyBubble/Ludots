using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// Field editor paint acceptance: data-only mod whose Fields/cells assets match
/// field-editor CLI schemaVersion 2 rect output (regions paint.a / paint.b).
/// Teleporting the hero between paint.a and paint.b cells fires FieldRegionEntered.
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class FieldEditorPaintAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "field_editor_paint";
    private const string LayerKey = "ownership.paint";

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FieldEditorPaintMod",
    };

    [Test]
    public void EditorPaintMod_IsDataOnly()
    {
        string modRoot = Path.Combine(FindRepoRoot(), "mods", "showcases", "field_editor_paint", "FieldEditorPaintMod");
        Assert.That(File.Exists(Path.Combine(modRoot, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modRoot, "FieldEditorPaintMod.csproj")), Is.False,
            "Paint showcase must stay code-free: Fields + map + graphs + panels only.");
        // Assets match field-editor v2 format: schemaVersion 2 + regions + rects (no cells[]).
        string cells = File.ReadAllText(Path.Combine(modRoot, "assets", "Fields", "cells", "ownership.paint.json"));
        Assert.That(cells, Does.Contain("\"schemaVersion\": 2"));
        Assert.That(cells, Does.Contain("paint.a"));
        Assert.That(cells, Does.Contain("paint.b"));
        Assert.That(cells, Does.Contain("\"rects\""));
        Assert.That(cells, Does.Not.Contain("\"cells\""));
    }

    [Test]
    public void EditorPaint_Teleport_FieldRegionEntered()
    {
        using GameEngine engine = CreateEngine(Mods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("map session missing");
        MapVariableStore variables = session.Variables
            ?? throw new InvalidOperationException("map variables missing");
        Assert.That(session.Fields, Is.Not.Null);
        Assert.That(session.RegionIndex, Is.Not.Null);
        Assert.That(session.Fields!.TryGetByKey(LayerKey, out FieldLayerData layerData), Is.True);
        var layer = (DiscreteIdFieldLayerData)layerData;
        Assert.That(session.RegionIndex!.Count, Is.EqualTo(2));
        Assert.That(layer.Regions.GetName(1), Is.EqualTo("paint.a"));
        Assert.That(layer.Regions.GetName(2), Is.EqualTo("paint.b"));

        Assert.That(variables.ReadInt("stage"), Is.EqualTo(1), "MapLoaded writes stage=1");

        // Hero starts in paint.b → FieldRegionEntered(paint.b) → stage=2, region_code=2.
        TickUntil(engine, () => variables.ReadInt("stage") == 2, maxFrames: 12,
            () => $"Expected FieldRegionEntered(paint.b) (got stage={variables.ReadInt("stage")}).");
        Assert.That(variables.ReadInt("region_code"), Is.EqualTo(2));
        Assert.That(variables.ReadInt("last_enter_code"), Is.EqualTo(2));

        Entity hero = FindHero(engine.World);
        engine.World.Set(hero, new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 150) }); // paint.a cell

        TickUntil(engine, () => variables.ReadInt("stage") == 3, maxFrames: 12,
            () => $"Expected FieldRegionEntered(paint.a) (got stage={variables.ReadInt("stage")}).");
        Assert.That(variables.ReadInt("region_code"), Is.EqualTo(1));
        Assert.That(variables.ReadInt("last_enter_code"), Is.EqualTo(1));
        Assert.That(variables.ReadInt("last_exit_code"), Is.EqualTo(2));

        Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0),
            string.Join("; ", engine.TriggerManager.Errors));
    }

    private static Entity FindHero(World world)
    {
        Entity found = Entity.Null;
        world.Query(
            new QueryDescription().WithAll<Name, FieldTrackedCm, WorldPositionCm>(),
            (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, "PaintHero", StringComparison.Ordinal))
                {
                    found = entity;
                }
            });
        Assert.That(found, Is.Not.EqualTo(Entity.Null), "PaintHero missing");
        return found;
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
                Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }
}
