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
        "vfx_forge.trail_arc"
    ];

    private static readonly string[] VfxAssetKeys =
    [
        "vfx_forge.spark_column.effect",
        "vfx_forge.energy_orbit.effect",
        "vfx_forge.trail_arc.effect"
    ];

    private static readonly string[] PerformerDefinitionKeys =
    [
        "vfx_forge_root",
        "vfx_forge_spark_column",
        "vfx_forge_energy_orbit",
        "vfx_forge_trail_arc"
    ];

    [Test]
    public void MapLoad_WiresQuarksParticleAssetsIntoRaylibVfxPerformerPath()
    {
        string repoRoot = FindRepoRoot();
        List<string> modPaths = RepoModPaths.ResolveExplicit(
            repoRoot,
            new[] { "LudotsCoreMod", "VfxForgeRaylibShowcaseMod" });

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

            int vfxAssetId = meshes.GetId(VfxAssetKeys[i]);
            Assert.That(vfxAssetId, Is.GreaterThan(0), $"VFX asset '{VfxAssetKeys[i]}' should be registered.");
            Assert.That(meshes.TryGetDescriptor(vfxAssetId, out MeshAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.VfxEffectData.IsValid, Is.True);
            Assert.That(descriptor.VfxEffectData.ParticleEffectAssetId, Is.EqualTo(particleId));
            Assert.That(descriptor.VfxEffectData.ParticleSystem, Is.SameAs(particleEffect));
        }

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

        Assert.That(visibleVfxCount, Is.GreaterThanOrEqualTo(3), "The player-facing scene should emit all three VFX performer visuals.");
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
