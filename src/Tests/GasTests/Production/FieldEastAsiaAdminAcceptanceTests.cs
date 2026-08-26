using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class FieldEastAsiaAdminAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "east_asia_visual_heightmap";
    private const string LayerKey = "ownership.east_asia.admin";

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "EastAsiaPlayableTerrainMod",
        "FieldEastAsiaAdminMod",
    };

    private static readonly string[] ExpectedRegionNames =
    {
        "admin.anhui",
        "admin.chongqing",
        "admin.fujian",
        "admin.gansu_east",
        "admin.guangdong",
        "admin.guangxi",
        "admin.guizhou",
        "admin.hainan",
        "admin.hebei",
        "admin.heilongjiang",
        "admin.henan",
        "admin.hubei",
        "admin.hunan",
        "admin.jiangsu",
        "admin.jiangxi",
        "admin.jilin",
        "admin.liaoning",
        "admin.neimenggu_east",
        "admin.ningxia",
        "admin.shaanxi",
        "admin.shandong",
        "admin.shanghai",
        "admin.shanxi",
        "admin.sichuan",
        "admin.yunnan",
        "admin.zhejiang",
    };

    private static readonly int[] ExpectedRegionCellCounts =
    {
        59,
        90,
        115,
        70,
        192,
        166,
        85,
        42,
        118,
        351,
        127,
        97,
        129,
        121,
        107,
        197,
        184,
        213,
        48,
        136,
        164,
        11,
        83,
        180,
        277,
        109,
    };

    [Test]
    public void EastAsiaAdmin_LoadsMaterializesAndProjectsDiscreteOwnership()
    {
        using GameEngine engine = CreateEngine(Mods);
        engine.Start();
        engine.LoadMap(MapId);
        Tick(engine, 2);

        MapSession session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("map session missing");
        Assert.That(session.Fields, Is.Not.Null);
        Assert.That(session.RegionIndex, Is.Not.Null);
        Assert.That(session.Fields!.TryGetByKey(LayerKey, out FieldLayerData layerData), Is.True);
        Assert.That(layerData, Is.InstanceOf<DiscreteIdFieldLayerData>());
        var layer = (DiscreteIdFieldLayerData)layerData;

        Assert.That(layer.Regions.Count, Is.EqualTo(ExpectedRegionNames.Length));
        Assert.That(session.RegionIndex!.Count, Is.EqualTo(ExpectedRegionCellCounts.Length));
        Assert.That(layer.Field.NonDefaultCount, Is.EqualTo(3471));

        var regionCells = new FieldCell2D[layer.Field.NonDefaultCount];
        int expectedTotal = 0;
        for (int regionIndex = 0; regionIndex < ExpectedRegionCellCounts.Length; regionIndex++)
        {
            int regionId = regionIndex + 1;
            Assert.That(layer.Regions.GetName(regionId), Is.EqualTo(ExpectedRegionNames[regionIndex]));
            Assert.That(
                session.RegionIndex.TryResolve(layer.LayerId, regionId, out Entity regionEntity),
                Is.True);
            Assert.That(engine.World.IsAlive(regionEntity), Is.True);

            int regionCellCount = layer.EnumerateRegionCells(regionId, regionCells);
            Assert.That(regionCellCount, Is.EqualTo(ExpectedRegionCellCounts[regionIndex]));
            expectedTotal += regionCellCount;
        }

        Assert.That(layer.Field.NonDefaultCount, Is.EqualTo(expectedTotal));

        var buffer = new GlobalFieldVisualBuffer(
            recordCapacity: 4,
            cellCapacity: expectedTotal,
            dirtyRectCapacity: 64);
        var projector = new FieldDiscreteVisualProjector();
        FieldDiscreteVisualMapMode leafMode = FieldDiscreteVisualMapMode.Leaf;
        buffer.BeginFrame();
        projector.Project(
            scopeKeyId: 1,
            session.Fields,
            session.RegionGroups,
            in leafMode,
            buffer);

        GlobalFieldVisualRecord record = FindDiscreteOwnershipRecord(buffer);
        Assert.That(record.Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.DiscreteOwnership));
        Assert.That(record.CellCount, Is.EqualTo(layer.Field.NonDefaultCount));
        Assert.That(projector.LastProjectedCellCount, Is.EqualTo(layer.Field.NonDefaultCount));
        Assert.That(buffer.GetCells(record).Length, Is.EqualTo(layer.Field.NonDefaultCount));
    }

    [Test]
    public void EastAsiaAdminMod_IsDataOnly()
    {
        string modRoot = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "field_east_asia_admin",
            "FieldEastAsiaAdminMod");
        Assert.That(File.Exists(Path.Combine(modRoot, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modRoot, "FieldEastAsiaAdminMod.csproj")), Is.False);
    }

    private static GlobalFieldVisualRecord FindDiscreteOwnershipRecord(GlobalFieldVisualBuffer buffer)
    {
        ReadOnlySpan<GlobalFieldVisualRecord> records = buffer.GetRecords();
        for (int i = 0; i < records.Length; i++)
        {
            if (records[i].IsActive &&
                records[i].Descriptor.Id.Kind == GlobalFieldVisualKind.DiscreteOwnership)
            {
                return records[i];
            }
        }

        throw new AssertionException("Discrete ownership projection record was not active.");
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
