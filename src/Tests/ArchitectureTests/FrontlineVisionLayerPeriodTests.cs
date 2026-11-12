using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;
using Ludots.Core.Vision.Config;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class FrontlineVisionLayerPeriodTests
{
    [Test]
    public void ThirtyHertzClock_WithTwentyHertzLayer_RefreshesEveryOtherTick()
    {
        Assert.That(VisionUpdatePeriod.FromFixedDelta(1f / 30f, 20), Is.EqualTo(2));
        Assert.That(VisionUpdatePeriod.FromFixedDelta(1f / 30f, 30), Is.EqualTo(1));
        Assert.That(VisionUpdatePeriod.FromFixedDelta(1f / 20f, 20), Is.EqualTo(1));
    }

    [Test]
    public void FrontlineVisionLayers_MatchFixedHz_SoTheyRefreshEveryTick()
    {
        string repoRoot = FindRepoRoot();
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", Path.Combine(repoRoot, "assets"));
        var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
        vfs.Mount(
            "RtsMultiplayerFrontlineMod",
            Path.Combine(repoRoot, "mods/showcases/rts_multiplayer_frontline/RtsMultiplayerFrontlineMod"));
        modLoader.LoadedModIds.Add("RtsMultiplayerFrontlineMod");

        var pipeline = new ConfigPipeline(vfs, modLoader);
        ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
        EngineClockConfig clock = new EngineClockConfigLoader(pipeline).Load(catalog);
        var layers = new FogLayerRegistry();
        new VisionFogLayerConfigLoader(pipeline, layers).Load(catalog);

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        ConfigCatalogEntry templates = ConfigPipeline.RequireEntry(
            catalog, "Entities/templates.json", ConfigMergePolicy.ArrayById, "id");
        IReadOnlyList<MergedConfigEntry> merged = pipeline.MergeArrayByIdFromCatalog(in templates);
        for (int i = 0; i < merged.Count; i++)
        {
            if (merged[i].Node["components"] is not JsonObject components ||
                components["VisionEmitterCm"] is not JsonObject emitter ||
                emitter["layers"] is not JsonArray names)
            {
                continue;
            }

            for (int n = 0; n < names.Count; n++)
            {
                referenced.Add(names[n]!.GetValue<string>());
            }
        }

        Assert.That(referenced, Is.Not.Empty);
        Assert.That(clock.FixedHz, Is.EqualTo(30));
        float fixedDelta = 1f / clock.FixedHz;
        foreach (string key in referenced)
        {
            FogLayerDefinition definition = layers.Get(layers.GetId(key));
            Assert.That(definition.UpdateHz, Is.EqualTo(clock.FixedHz), key);
            Assert.That(VisionUpdatePeriod.FromFixedDelta(fixedDelta, definition.UpdateHz), Is.EqualTo(1), key);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
