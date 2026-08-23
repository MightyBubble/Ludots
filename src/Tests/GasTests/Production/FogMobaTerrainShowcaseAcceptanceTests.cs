using System;
using System.IO;
using FogMobaTerrainShowcaseMod;
using FogMobaTerrainShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.Vision;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

[NonParallelizable]
[TestFixture]
[Category("ci-gate")]
[Category("acceptance")]
public sealed class FogMobaTerrainShowcaseAcceptanceTests
{
    [Test]
    public void FogMobaTerrainShowcase_UsesProductionVisionFieldAndRuntimeControls()
    {
        string repoRoot = FindRepoRoot();
        string[] mods = { "LudotsCoreMod", "CoreInputMod", "FogMobaTerrainShowcaseMod" };
        using GameEngine engine = new();
        engine.InitializeWithConfigPipeline(RepoModPaths.ResolveExplicit(repoRoot, mods), Path.Combine(repoRoot, "assets"));
        engine.Start();
        engine.LoadMap(FogMobaTerrainIds.MapId);

        FogMobaTerrainRuntime runtime = engine.GlobalContext[FogMobaTerrainIds.RuntimeServiceKey] as FogMobaTerrainRuntime
            ?? throw new InvalidOperationException("FogMobaTerrain runtime missing.");
        for (int i = 0; i < 12; i++) engine.Tick(1f / 60f);

        FogMobaTerrainSnapshot initial = runtime.Snapshot;
        Assert.That(initial.Shape, Is.EqualTo("Cone"));
        Assert.That(initial.RulesEnabled, Is.True);
        Assert.That(initial.WallCells, Is.GreaterThan(0));
        Assert.That(engine.GetService(CoreServiceKeys.VisionFogFieldStore), Is.TypeOf<FogFieldStore>());

        Assert.That(initial.RangeCm, Is.EqualTo(5200));
    }

    private static string FindRepoRoot()
    {
        string path = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrEmpty(path) && !File.Exists(Path.Combine(path, "launcher.config.json")))
            path = Directory.GetParent(path)?.FullName ?? string.Empty;
        return path;
    }
}
