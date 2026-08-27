using System;
using System.IO;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Production;

[NonParallelizable]
[TestFixture]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class FieldEastAsiaCountryAcceptanceTests
{
    private const float DeltaTime = 1f / 60f;
    private const string MapId = "east_asia_visual_heightmap";
    private const string LayerKey = "ownership.east_asia.country";
    private const int ExpectedNonDefaultCells = 203193;

    private static readonly string[] Mods =
    {
        "LudotsCoreMod",
        "EastAsiaPlayableTerrainMod",
        "FieldEastAsiaCountryMod",
    };

    private static readonly string[] ExpectedRegionNames =
    {
        "country.bangladesh",
        "country.bhutan",
        "country.cambodia",
        "country.china",
        "country.india",
        "country.japan",
        "country.laos",
        "country.mongolia",
        "country.myanmar",
        "country.nepal",
        "country.north_korea",
        "country.philippines",
        "country.russia",
        "country.south_korea",
        "country.taiwan",
        "country.thailand",
        "country.vietnam",
    };

    [Test]
    public void EastAsiaCountry_LoadsMaterializesAndProjectsDiscreteOwnership()
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
        Assert.That(layer.Field.NonDefaultCount, Is.EqualTo(ExpectedNonDefaultCells));

        for (int regionIndex = 0; regionIndex < ExpectedRegionNames.Length; regionIndex++)
        {
            int regionId = regionIndex + 1;
            Assert.That(layer.Regions.GetName(regionId), Is.EqualTo(ExpectedRegionNames[regionIndex]));
            Assert.That(
                session.RegionIndex!.TryResolve(layer.LayerId, regionId, out Entity regionEntity),
                Is.True);
            Assert.That(engine.World.IsAlive(regionEntity), Is.True);
        }

        var buffer = new GlobalFieldVisualBuffer(
            recordCapacity: 4,
            cellCapacity: ExpectedNonDefaultCells,
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

        Assert.That(session.MapConfig!.Tags, Does.Contain("Raylib.FieldOverlays:Off"));
        Assert.That(FindNamed(engine, "EastAsia.CountryDecal"), Is.Not.EqualTo(Entity.Null));
    }

    [Test]
    public void BordersLandSeaPreset_DoesNotAutoEnableNavMeshDebugLaunch()
    {
        using var presets = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepoRoot(), "launcher.presets.json")));
        JsonElement found = default;
        bool present = false;
        foreach (JsonElement preset in presets.RootElement.GetProperty("presets").EnumerateArray())
        {
            if (preset.GetProperty("id").GetString() == "east_asia_borders_land_sea_raylib")
            {
                found = preset;
                present = true;
                break;
            }
        }

        Assert.That(present, Is.True);
        foreach (JsonElement selector in found.GetProperty("selectors").EnumerateArray())
        {
            Assert.That(selector.GetString(), Is.Not.EqualTo("mod:NavMeshDebugLaunchMod"));
        }
    }

    [Test]
    public void EastAsiaCountryMod_IsDataOnly()
    {
        string modRoot = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "field_east_asia_country",
            "FieldEastAsiaCountryMod");
        Assert.That(File.Exists(Path.Combine(modRoot, "mod.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(modRoot, "FieldEastAsiaCountryMod.csproj")), Is.False);
    }

    [Test]
    public void EastAsiaCountry_DecalStampMatchesFieldBoardAndPalette()
    {
        string repoRoot = FindRepoRoot();
        string textures = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "field_east_asia_country",
            "FieldEastAsiaCountryMod",
            "assets",
            "Textures");
        using var palette = JsonDocument.Parse(File.ReadAllText(Path.Combine(textures, "country_palette.json")));
        using var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(textures, "country_borders.png.meta.json")));
        using var field = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "field_east_asia_country",
            "FieldEastAsiaCountryMod",
            "assets",
            "Fields",
            "cells",
            "ownership.east_asia.country.json")));

        Assert.That(meta.RootElement.GetProperty("width").GetInt32(), Is.EqualTo(896));
        Assert.That(meta.RootElement.GetProperty("height").GetInt32(), Is.EqualTo(512));
        Assert.That(meta.RootElement.GetProperty("paintedCells").GetInt32(), Is.EqualTo(ExpectedNonDefaultCells));
        Assert.That(palette.RootElement.GetProperty("fillAlpha").GetInt32(), Is.EqualTo(230));
        Assert.That(palette.RootElement.GetProperty("borderAlpha").GetInt32(), Is.EqualTo(255));
        Assert.That(File.Exists(Path.Combine(textures, "country_borders.png")), Is.True);

        foreach (JsonElement region in field.RootElement.GetProperty("regions").EnumerateArray())
        {
            string key = region.GetString() ?? throw new InvalidOperationException("blank region");
            Assert.That(palette.RootElement.GetProperty("regions").TryGetProperty(key, out _), Is.True, key);
        }
    }

    private static Entity FindNamed(GameEngine engine, string name)
    {
        Entity found = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        engine.World.Query(in query, (Entity entity, ref Name named) =>
        {
            if (named.Value == name)
            {
                found = entity;
            }
        });
        return found;
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
