using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Particles;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[NonParallelizable]
public sealed class VfxForgeRaylibShowcaseAcceptanceTests
{
    private const string MapId = "vfx_forge_raylib_showcase";
    private const string ShowcaseId = "vfx_forge_raylib";

    private static readonly string[] ParticleEffectKeys =
    [
        "vfx_forge.spark_column",
        "vfx_forge.energy_orbit",
        "vfx_forge.trail_arc",
        "vfx_forge.ember_rain",
        "vfx_forge.shield_dome",
        "vfx_forge.gravity_well",
        "vfx_forge.flame_flipbook",
        "vfx_forge.smoke_flipbook",
        "vfx_forge.stretched_sparks"
    ];

    private static readonly string[] VfxAssetKeys =
    [
        "vfx_forge.spark_column.effect",
        "vfx_forge.energy_orbit.effect",
        "vfx_forge.trail_arc.effect",
        "vfx_forge.ember_rain.effect",
        "vfx_forge.shield_dome.effect",
        "vfx_forge.gravity_well.effect",
        "vfx_forge.flame_flipbook.effect",
        "vfx_forge.smoke_flipbook.effect",
        "vfx_forge.stretched_sparks.effect"
    ];

    private static readonly string[] TextureAssetKeys =
    [
        "vfx_forge.texture.flame_flipbook",
        "vfx_forge.texture.smoke_flipbook",
        "vfx_forge.texture.spark_streak_flipbook"
    ];

    private static readonly string[] PerformerDefinitionKeys =
    [
        "vfx_forge_root",
        "vfx_forge_left_pedestal",
        "vfx_forge_center_pedestal",
        "vfx_forge_right_pedestal",
        "vfx_forge_spark_column",
        "vfx_forge_energy_orbit",
        "vfx_forge_trail_arc",
        "vfx_forge_ember_pedestal",
        "vfx_forge_shield_pedestal",
        "vfx_forge_gravity_pedestal",
        "vfx_forge_flame_flipbook_pedestal",
        "vfx_forge_smoke_flipbook_pedestal",
        "vfx_forge_stretched_sparks_pedestal",
        "vfx_forge_ember_rain",
        "vfx_forge_shield_dome",
        "vfx_forge_gravity_well",
        "vfx_forge_flame_flipbook",
        "vfx_forge_smoke_flipbook",
        "vfx_forge_stretched_sparks"
    ];

    [Test]
    public void MapLoad_WiresQuarksParticleAssetsIntoRaylibVfxPerformerPath()
    {
        string repoRoot = FindRepoRoot();
        JsonObject map = ReadJsonObject(Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "vfx_forge_raylib",
            "VfxForgeRaylibShowcaseMod",
            "assets",
            "Maps",
            "vfx_forge_raylib_showcase.json"));
        List<string> modPaths = RepoModPaths.ResolveExplicit(
            repoRoot,
            new[] { "LudotsCoreMod", "VfxForgeRaylibShowcaseMod" });

        Assert.That(
            map["Metadata"]?["vfxForge"]?["effectCount"]?.GetValue<int>(),
            Is.EqualTo(ParticleEffectKeys.Length));

        using var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
        HeadlessPresentationTestHost.Install(engine);

        ParticleEffectRegistry particleEffects = engine.GetService(CoreServiceKeys.PresentationParticleEffectRegistry)
            ?? throw new InvalidOperationException("PresentationParticleEffectRegistry missing.");
        MeshAssetRegistry meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
            ?? throw new InvalidOperationException("PresentationMeshAssetRegistry missing.");
        PerformerDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
            ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");

        for (int i = 0; i < ParticleEffectKeys.Length; i++)
        {
            int particleId = particleEffects.GetId(ParticleEffectKeys[i]);
            Assert.That(particleId, Is.GreaterThan(0), $"Particle effect '{ParticleEffectKeys[i]}' should be registered.");
            Assert.That(particleEffects.TryGet(particleId, out ParticleEffectAssetData particleEffect), Is.True);
            Assert.That(particleEffect.IsValid, Is.True);
            Assert.That(particleEffect.BlendMode, Is.EqualTo(ExpectedBlendMode(ParticleEffectKeys[i])));

            int vfxAssetId = meshes.GetId(VfxAssetKeys[i]);
            Assert.That(vfxAssetId, Is.GreaterThan(0), $"VFX asset '{VfxAssetKeys[i]}' should be registered.");
            Assert.That(meshes.TryGetDescriptor(vfxAssetId, out MeshAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.VfxEffectData.IsValid, Is.True);
            Assert.That(descriptor.VfxEffectData.ParticleEffectAssetId, Is.EqualTo(particleId));
            Assert.That(descriptor.VfxEffectData.ParticleSystem, Is.SameAs(particleEffect));
        }

        foreach (string textureAssetKey in TextureAssetKeys)
        {
            int textureAssetId = meshes.GetId(textureAssetKey);
            Assert.That(textureAssetId, Is.GreaterThan(0), $"Texture billboard asset '{textureAssetKey}' should be registered.");
            Assert.That(meshes.TryGetDescriptor(textureAssetId, out MeshAssetDescriptor textureDescriptor), Is.True);
            Assert.That(textureDescriptor.Type, Is.EqualTo(MeshAssetType.Billboard));
        }

        AssertTextureParticle(
            particleEffects,
            "vfx_forge.flame_flipbook",
            ParticleRenderMode.Billboard,
            ParticleBlendMode.Additive,
            "vfx_forge.texture.flame_flipbook",
            ParticleTextureSheetPlaybackMode.Loop,
            expectedColumns: 4,
            expectedRows: 2,
            expectedFrames: 8);
        AssertTextureParticle(
            particleEffects,
            "vfx_forge.smoke_flipbook",
            ParticleRenderMode.Billboard,
            ParticleBlendMode.PremultipliedAlpha,
            "vfx_forge.texture.smoke_flipbook",
            ParticleTextureSheetPlaybackMode.Clamp,
            expectedColumns: 4,
            expectedRows: 2,
            expectedFrames: 8);
        AssertTextureParticle(
            particleEffects,
            "vfx_forge.stretched_sparks",
            ParticleRenderMode.StretchedBillboard,
            ParticleBlendMode.Additive,
            "vfx_forge.texture.spark_streak_flipbook",
            ParticleTextureSheetPlaybackMode.Loop,
            expectedColumns: 4,
            expectedRows: 1,
            expectedFrames: 4);
        AssertRaylibHostTextureRows(repoRoot);

        foreach (string definitionKey in PerformerDefinitionKeys)
        {
            Assert.That(definitions.GetId(definitionKey), Is.GreaterThan(0), $"Performer '{definitionKey}' should be registered.");
        }

        engine.Start();
        engine.LoadMap(MapId);
        for (int frame = 0; frame < 12; frame++)
        {
            engine.Tick(1f / 60f);
            HeadlessPresentationTestHost.UpdateCamera(engine);
        }

        foreach (string definitionKey in PerformerDefinitionKeys)
        {
            int definitionId = definitions.GetId(definitionKey);
            Assert.That(
                CountPerformersByDefinition(engine, definitionId),
                Is.GreaterThanOrEqualTo(1),
                $"Performer '{definitionKey}' should be alive in the player-facing VFX forge scene.");
        }

        PrimitiveDrawBuffer primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer)
            ?? throw new InvalidOperationException("PresentationPrimitiveDrawBuffer missing.");
        int visibleVfxCount = 0;
        foreach (ref readonly PrimitiveDrawItem item in primitives.GetSpan())
        {
            if (item.AssetKind == AssetKind.VFX &&
                item.Visibility == VisualVisibility.Visible)
            {
                visibleVfxCount++;
            }
        }

        Assert.That(visibleVfxCount, Is.GreaterThanOrEqualTo(ParticleEffectKeys.Length), "The player-facing scene should emit all nine VFX performer visuals.");
    }

    [Test]
    public void ShowcaseRegistry_ProvidesPlayableRaylibLauncherPreset()
    {
        string repoRoot = FindRepoRoot();
        JsonObject registry = ReadJsonObject(Path.Combine(repoRoot, "showcase.registry.json"));
        JsonObject launcherConfig = ReadJsonObject(Path.Combine(repoRoot, "launcher.config.json"));
        JsonObject launcherPresets = ReadJsonObject(Path.Combine(repoRoot, "launcher.presets.json"));

        JsonObject showcase = RequireObjectById(registry["showcases"]?.AsArray(), ShowcaseId);
        Assert.That(showcase["binding"]?.GetValue<string>(), Is.EqualTo(ShowcaseId));
        Assert.That(showcase["preset"]?.GetValue<string>(), Is.EqualTo(ShowcaseId));

        JsonObject binding = RequireObjectByField(
            launcherConfig["bindings"]?.AsArray(),
            "name",
            ShowcaseId);
        Assert.That(binding["target"]?["value"]?.GetValue<string>(), Is.EqualTo("mods/showcases/vfx_forge_raylib/VfxForgeRaylibShowcaseMod"));

        JsonObject preset = RequireObjectById(launcherPresets["presets"]?.AsArray(), ShowcaseId);
        Assert.That(preset["adapterId"]?.GetValue<string>(), Is.EqualTo("raylib"));
        Assert.That(preset["selectors"]?.AsArray().Any(selector => selector?.GetValue<string>() == "$vfx_forge_raylib"), Is.True);
    }

    private static int CountPerformersByDefinition(GameEngine engine, int definitionId)
    {
        int count = 0;
        var query = new QueryDescription().WithAll<PerformerState>();
        engine.World.Query(in query, (Entity entity, ref PerformerState state) =>
        {
            if (state.DefId == definitionId)
            {
                count++;
            }
        });

        return count;
    }

    private static ParticleBlendMode ExpectedBlendMode(string key)
    {
        return key switch
        {
            "vfx_forge.spark_column" => ParticleBlendMode.Additive,
            "vfx_forge.energy_orbit" => ParticleBlendMode.Additive,
            "vfx_forge.trail_arc" => ParticleBlendMode.Additive,
            "vfx_forge.ember_rain" => ParticleBlendMode.Additive,
            "vfx_forge.shield_dome" => ParticleBlendMode.Alpha,
            "vfx_forge.gravity_well" => ParticleBlendMode.Multiply,
            "vfx_forge.flame_flipbook" => ParticleBlendMode.Additive,
            "vfx_forge.smoke_flipbook" => ParticleBlendMode.PremultipliedAlpha,
            "vfx_forge.stretched_sparks" => ParticleBlendMode.Additive,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown VFX Forge particle effect key."),
        };
    }

    private static void AssertTextureParticle(
        ParticleEffectRegistry particleEffects,
        string key,
        ParticleRenderMode expectedRenderMode,
        ParticleBlendMode expectedBlendMode,
        string expectedTextureAssetId,
        ParticleTextureSheetPlaybackMode expectedPlaybackMode,
        int expectedColumns,
        int expectedRows,
        int expectedFrames)
    {
        int id = particleEffects.GetId(key);
        Assert.That(particleEffects.TryGet(id, out ParticleEffectAssetData particleEffect), Is.True);
        Assert.That(particleEffect.RenderMode, Is.EqualTo(expectedRenderMode));
        Assert.That(particleEffect.BlendMode, Is.EqualTo(expectedBlendMode));
        Assert.That(particleEffect.TextureSheet, Is.Not.Null);
        Assert.That(particleEffect.TextureSheet!.TextureAssetId, Is.EqualTo(expectedTextureAssetId));
        Assert.That(particleEffect.TextureSheet.PlaybackMode, Is.EqualTo(expectedPlaybackMode));
        Assert.That(particleEffect.TextureSheet.Columns, Is.EqualTo(expectedColumns));
        Assert.That(particleEffect.TextureSheet.Rows, Is.EqualTo(expectedRows));
        Assert.That(particleEffect.TextureSheet.FrameCount, Is.EqualTo(expectedFrames));
        if (expectedRenderMode == ParticleRenderMode.StretchedBillboard)
        {
            Assert.That(particleEffect.StretchedLengthScale, Is.GreaterThan(0f));
        }
    }

    private static void AssertRaylibHostTextureRows(string repoRoot)
    {
        JsonArray hostAssets = ReadJsonArray(Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "vfx_forge_raylib",
            "VfxForgeRaylibShowcaseMod",
            "assets",
            "Presentation",
            "host_assets.json"));

        AssertRaylibHostTextureRow(
            hostAssets,
            "vfx_forge.texture.flame_flipbook.raylib",
            "vfx_forge.texture.flame_flipbook",
            "VfxForgeRaylibShowcaseMod:assets/Presentation/textures/flame_flipbook.png");
        AssertRaylibHostTextureRow(
            hostAssets,
            "vfx_forge.texture.smoke_flipbook.raylib",
            "vfx_forge.texture.smoke_flipbook",
            "VfxForgeRaylibShowcaseMod:assets/Presentation/textures/smoke_flipbook.png");
        AssertRaylibHostTextureRow(
            hostAssets,
            "vfx_forge.texture.spark_streak_flipbook.raylib",
            "vfx_forge.texture.spark_streak_flipbook",
            "VfxForgeRaylibShowcaseMod:assets/Presentation/textures/spark_streak_flipbook.png");
    }

    private static void AssertRaylibHostTextureRow(JsonArray hostAssets, string rowId, string assetId, string sourceUri)
    {
        JsonObject row = RequireObjectById(hostAssets, rowId);
        Assert.That(row["assetKind"]?.GetValue<string>(), Is.EqualTo("Mesh"));
        Assert.That(row["assetId"]?.GetValue<string>(), Is.EqualTo(assetId));
        Assert.That(row["backendId"]?.GetValue<string>(), Is.EqualTo("raylib"));
        JsonArray sourceUris = row["sourceUris"]?.AsArray()
            ?? throw new InvalidOperationException($"Host texture row '{rowId}' must declare sourceUris.");
        Assert.That(sourceUris.Select(node => node?.GetValue<string>()).ToArray(), Is.EqualTo(new[] { sourceUri }));
    }

    private static string FindRepoRoot()
    {
        string current = TestContext.CurrentContext.WorkDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "mods")) &&
                File.Exists(Path.Combine(current, "AGENTS.md")))
            {
                return current;
            }

            current = Path.GetDirectoryName(current)!;
        }

        throw new DirectoryNotFoundException("Repository root not found from test work directory.");
    }

    private static JsonObject ReadJsonObject(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new InvalidOperationException($"JSON file '{path}' must contain an object.");
    }

    private static JsonArray ReadJsonArray(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))?.AsArray()
            ?? throw new InvalidOperationException($"JSON file '{path}' must contain an array.");
    }

    private static JsonObject RequireObjectById(JsonArray? array, string id)
    {
        return RequireObjectByField(array, "id", id);
    }

    private static JsonObject RequireObjectByField(JsonArray? array, string field, string expected)
    {
        if (array == null)
        {
            throw new InvalidOperationException($"JSON array for '{field}' lookup is missing.");
        }

        foreach (JsonNode? node in array)
        {
            if (node is JsonObject obj &&
                string.Equals(obj[field]?.GetValue<string>(), expected, StringComparison.Ordinal))
            {
                return obj;
            }
        }

        throw new InvalidOperationException($"Object with {field}='{expected}' was not found.");
    }
}
