using System;
using System.IO;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

/// <summary>
/// 运行时重划验收：不移动单位的前提下重涂其所在归属，成员关系必须经 chunk 变更戳
/// 在下一拍重估并照常发 FieldRegion 进出事件；新区域 key 运行时出生即物化。
/// </summary>
[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class FieldRegionRedrawAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "field_jing_yang_transit";
    private const string LayerKey = "ownership.transit";

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FieldJingYangTransitMod",
    };

    [Test]
    public void Redraw_StationaryUnit_Reevaluates_And_NewRegionMaterializes()
    {
        using GameEngine engine = CreateEngine(Mods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("map session missing");
        MapVariableStore variables = session.Variables
            ?? throw new InvalidOperationException("map variables missing");
        Assert.That(session.Fields!.TryGetByKey(LayerKey, out FieldLayerData layerData), Is.True);
        var layer = (DiscreteIdFieldLayerData)layerData;
        Entity hero = FindTrackedHero(engine.World);

        // Initial membership: hero authored inside yang.
        TickUntil(engine, () => variables.ReadInt("region_code") == 2, maxFrames: 12,
            () => $"expected initial enter(yang), got region={variables.ReadInt("region_code")}");
        int jingId = layer.Regions.GetId("jing");
        Assert.That(jingId, Is.EqualTo(1));
        Assert.That(
            session.RegionIndex!.TryResolve(layer.LayerId, jingId, out Entity jingEntity), Is.True);
        int jingBefore = engine.World.Get<RegionFootprintCm>(jingEntity).CellCount;

        // Redraw jing over the hero's neighborhood; the hero never moves.
        ref var pos = ref engine.World.Get<WorldPositionCm>(hero);
        var cell = layer.Field.WorldToCell(pos.Value.ToWorldCmInt2());
        var strokes = new[]
        {
            new FieldCellRectStroke(cell.X - 3, cell.Y - 3, cell.X + 3, cell.Y + 3, jingId),
        };
        FieldRedrawResult result = FieldRegionRedraw.ApplyDiscrete(
            engine.World,
            session,
            LayerKey,
            new[]
            {
                new FieldRegionStrokeEdit("jing", strokes),
            });

        Assert.That(result.CellsChanged, Is.GreaterThanOrEqualTo(1), "redraw must repaint the hero's cell");
        Assert.That(
            engine.World.Get<RegionFootprintCm>(jingEntity).CellCount,
            Is.EqualTo(jingBefore + result.CellsChanged));

        TickUntil(engine,
            () => variables.ReadInt("region_code") == 1 && variables.ReadInt("last_exit_code") == 2,
            maxFrames: 12,
            () => $"stationary hero must re-evaluate after redraw (region={variables.ReadInt("region_code")}, last_exit={variables.ReadInt("last_exit_code")})");
        Assert.That(
            FieldRegionQueries.TryIsInFieldRegion(engine.World, session, hero, LayerKey, "jing", out bool inJing),
            Is.True);
        Assert.That(inJing, Is.True);

        // A brand-new key is registered, materialized and footprinted in the same call.
        FieldRedrawResult fresh = FieldRegionRedraw.ApplyDiscrete(
            engine.World,
            session,
            LayerKey,
            new[]
            {
                new FieldRegionStrokeEdit("k3", new[]
                {
                    new FieldCellRectStroke(14, 0, 15, 1, 1),
                }),
            });
        Assert.That(fresh.RegionsRegistered, Is.EqualTo(1));
        Assert.That(session.RegionIndex!.Count, Is.EqualTo(3), "new key materializes at runtime");
        Assert.That(layer.Regions.GetId("k3"), Is.EqualTo(3));
        Assert.That(
            session.RegionIndex.TryResolve(layer.LayerId, 3, out Entity k3Entity), Is.True);
        Assert.That(engine.World.Get<RegionFootprintCm>(k3Entity).CellCount, Is.EqualTo(4));
    }

    private static Entity FindTrackedHero(World world)
    {
        var query = new QueryDescription().WithAll<FieldTrackedCm>();
        Entity hero = Entity.Null;
        foreach (ref var chunk in world.Query(in query))
        {
            ref var first = ref chunk.Entity(0);
            foreach (var index in chunk)
            {
                hero = Unsafe.Add(ref first, index);
            }
        }

        if (hero == Entity.Null)
        {
            throw new InvalidOperationException("no FieldTrackedCm hero on map.");
        }

        return hero;
    }

    private static void TickUntil(GameEngine engine, Func<bool> done, int maxFrames, Func<string> fail)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
            if (done())
            {
                return;
            }
        }

        Assert.Fail(fail());
    }

    private static void Tick(GameEngine engine, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.Tick(DeltaTime);
        }
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
