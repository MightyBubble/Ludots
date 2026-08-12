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

        string presentationDir = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "vfx_forge_raylib",
            "VfxForgeRaylibShowcaseMod",
            "assets",
            "Presentation");
        JsonArray authoredParticles = ReadJsonArray(Path.Combine(presentationDir, "particle_effects.json"));
        JsonArray authoredMeshAssets = ReadJsonArray(Path.Combine(presentationDir, "mesh_assets.json"));
        Assert.That(
            authoredParticles.Select(node => node?["id"]?.GetValue<string>()).Where(id => id != null).ToArray(),
            Is.EquivalentTo(ParticleEffectKeys));

        for (int i = 0; i < ParticleEffectKeys.Length; i++)
        {
            JsonObject authoredParticle = RequireObjectById(authoredParticles, ParticleEffectKeys[i]);
            Assert.That(authoredParticle.ContainsKey("overflowPolicy"), Is.False);
            Assert.That(authoredParticle["spawnMode"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);

            int particleId = particleEffects.GetId(ParticleEffectKeys[i]);
            Assert.That(particleId, Is.GreaterThan(0), $"Particle effect '{ParticleEffectKeys[i]}' should be registered.");
            Assert.That(particleEffects.TryGet(particleId, out ParticleEffectAssetData particleEffect), Is.True);
            Assert.That(particleEffect.IsValid, Is.True);
            Assert.That(
                particleEffect.BlendMode.ToString(),
                Is.EqualTo(authoredParticle["blendMode"]?.GetValue<string>()));
            Assert.That(
                particleEffect.RenderMode.ToString(),
                Is.EqualTo(authoredParticle["renderMode"]?.GetValue<string>()));
            Assert.That(
                particleEffect.SpawnMode.ToString(),
                Is.EqualTo(authoredParticle["spawnMode"]?.GetValue<string>()));

            JsonObject authoredMesh = RequireObjectById(authoredMeshAssets, VfxAssetKeys[i]);
            JsonObject authoredVfx = authoredMesh["vfx"] as JsonObject
                ?? throw new InvalidOperationException($"Mesh asset '{VfxAssetKeys[i]}' must declare vfx.");
            Assert.That(authoredVfx.Count, Is.EqualTo(1));
            Assert.That(authoredVfx["particleEffectId"]?.GetValue<string>(), Is.EqualTo(ParticleEffectKeys[i]));
            Assert.That(authoredVfx.ContainsKey("emitter"), Is.False);
            Assert.That(authoredVfx.ContainsKey("particleSystem"), Is.False);
            Assert.That(authoredVfx.ContainsKey("spawnMode"), Is.False);

            int vfxAssetId = meshes.GetId(VfxAssetKeys[i]);
            Assert.That(vfxAssetId, Is.GreaterThan(0), $"VFX asset '{VfxAssetKeys[i]}' should be registered.");
            Assert.That(meshes.TryGetDescriptor(vfxAssetId, out MeshAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.VfxEffectData.IsValid, Is.True);
            Assert.That(descriptor.VfxEffectData.ParticleEffectAssetId, Is.EqualTo(particleId));
            Assert.That(descriptor.VfxEffectData.ParticleSystem, Is.SameAs(particleEffect));
            Assert.That(descriptor.VfxEffectData.SpawnMode, Is.EqualTo(particleEffect.SpawnMode));
        }

        foreach (string textureAssetKey in TextureAssetKeys)
        {
            int textureAssetId = meshes.GetId(textureAssetKey);
            Assert.That(textureAssetId, Is.GreaterThan(0), $"Texture billboard asset '{textureAssetKey}' should be registered.");
            Assert.That(meshes.TryGetDescriptor(textureAssetId, out MeshAssetDescriptor textureDescriptor), Is.True);
            Assert.That(textureDescriptor.Type, Is.EqualTo(MeshAssetType.Billboard));
        }

        AssertTextureParticleFromJson(particleEffects, authoredParticles, "vfx_forge.flame_flipbook");
        AssertTextureParticleFromJson(particleEffects, authoredParticles, "vfx_forge.smoke_flipbook");
        AssertTextureParticleFromJson(particleEffects, authoredParticles, "vfx_forge.stretched_sparks");
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
        Assert.That(
            showcase["docsPath"]?.GetValue<string>(),
            Is.EqualTo("gitbook/architecture/quarks-particle-schema.md"));

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

    private static void AssertTextureParticleFromJson(
        ParticleEffectRegistry particleEffects,
        JsonArray authoredParticles,
        string key)
    {
        JsonObject authored = RequireObjectById(authoredParticles, key);
        JsonObject textureSheet = authored["textureSheet"] as JsonObject
            ?? throw new InvalidOperationException($"Particle '{key}' must author textureSheet.");
        int id = particleEffects.GetId(key);
        Assert.That(particleEffects.TryGet(id, out ParticleEffectAssetData particleEffect), Is.True);
        Assert.That(particleEffect.RenderMode.ToString(), Is.EqualTo(authored["renderMode"]?.GetValue<string>()));
        Assert.That(particleEffect.BlendMode.ToString(), Is.EqualTo(authored["blendMode"]?.GetValue<string>()));
        Assert.That(particleEffect.TextureSheet, Is.Not.Null);
        Assert.That(
            particleEffect.TextureSheet!.TextureAssetId,
            Is.EqualTo(textureSheet["textureAssetId"]?.GetValue<string>()));
        Assert.That(
            particleEffect.TextureSheet.PlaybackMode.ToString(),
            Is.EqualTo(textureSheet["playbackMode"]?.GetValue<string>()));
        Assert.That(particleEffect.TextureSheet.Columns, Is.EqualTo(textureSheet["columns"]?.GetValue<int>()));
        Assert.That(particleEffect.TextureSheet.Rows, Is.EqualTo(textureSheet["rows"]?.GetValue<int>()));
        Assert.That(particleEffect.TextureSheet.FrameCount, Is.EqualTo(textureSheet["frameCount"]?.GetValue<int>()));
        if (particleEffect.RenderMode == ParticleRenderMode.StretchedBillboard)
        {
            Assert.That(particleEffect.StretchedLengthScale, Is.GreaterThan(0f));
            Assert.That(
                particleEffect.StretchedLengthScale,
                Is.EqualTo(authored["stretchedLengthScale"]?.GetValue<float>()).Within(0.0001f));
        }
    }

    private static void AssertRaylibHostTextureRows(string repoRoot)
    {
        string presentationDir = Path.Combine(
            repoRoot,
            "mods",
            "showcases",
            "vfx_forge_raylib",
            "VfxForgeRaylibShowcaseMod",
            "assets",
            "Presentation");
        JsonArray hostAssets = ReadJsonArray(Path.Combine(presentationDir, "host_assets.json"));
        Assert.That(hostAssets.Count, Is.EqualTo(TextureAssetKeys.Length));

        foreach (JsonNode? node in hostAssets)
        {
            JsonObject row = node as JsonObject
                ?? throw new InvalidOperationException("host_assets rows must be objects.");
            Assert.That(row["assetKind"]?.GetValue<string>(), Is.EqualTo("Mesh"));
            Assert.That(row["backendId"]?.GetValue<string>(), Is.EqualTo("raylib"));
            string assetId = row["assetId"]?.GetValue<string>()
                ?? throw new InvalidOperationException("host_assets row requires assetId.");
            Assert.That(TextureAssetKeys, Does.Contain(assetId));
            JsonArray sourceUris = row["sourceUris"]?.AsArray()
                ?? throw new InvalidOperationException($"Host texture row for '{assetId}' must declare sourceUris.");
            Assert.That(sourceUris.Count, Is.EqualTo(1));
            string sourceUri = sourceUris[0]?.GetValue<string>()
                ?? throw new InvalidOperationException($"Host texture row for '{assetId}' sourceUri must be a string.");
            string prefix = "VfxForgeRaylibShowcaseMod:assets/Presentation/";
            Assert.That(sourceUri.StartsWith(prefix, StringComparison.Ordinal), Is.True);
            string relativePath = sourceUri[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.Combine(presentationDir, relativePath);
            Assert.That(File.Exists(fullPath), Is.True, $"Flipbook PNG must exist on disk: {fullPath}");
        }
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
