using System.Collections.Generic;
using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2DTests;

[TestFixture]
public sealed class PathingBootstrapTests
{
    private static readonly string[] PlaygroundMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "Navigation2DPlaygroundMod"
    };

    [Test]
    public void LoadMap_SkipsMapScopedPathingServices_WhenMapHasNoGraphOrNavMesh()
    {
        using var engine = CreateEngine();

        engine.LoadMap(engine.MergedConfig.StartupMapId);

        Assert.That(engine.CurrentMapSession, Is.Not.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathingConfig), Is.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathStore), Is.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathService), Is.Null);
    }

    [Test]
    public void UnloadLastMap_Clears_MapScopedPathingServices()
    {
        using var engine = CreateEngine();
        string mapId = engine.MergedConfig.StartupMapId;

        engine.LoadMap(mapId);
        engine.UnloadMap(mapId);

        Assert.That(engine.CurrentMapSession, Is.Null);
        Assert.That(engine.GetService(CoreServiceKeys.MapSession), Is.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathStore), Is.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathService), Is.Null);
    }

    private static GameEngine CreateEngine()
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        var modPaths = new List<string>(PlaygroundMods.Length);
        for (int i = 0; i < PlaygroundMods.Length; i++)
        {
            modPaths.Add(Path.Combine(repoRoot, "mods", PlaygroundMods[i]));
        }

        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
        return engine;
    }

    private static string FindRepoRoot()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (Directory.Exists(Path.Combine(dir, "assets")) &&
                Directory.Exists(Path.Combine(dir, "mods")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException("Repository root not found from test directory.");
    }
}
