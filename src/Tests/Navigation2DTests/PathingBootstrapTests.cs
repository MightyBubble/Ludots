using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation.Pathing;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2DTests;

[TestFixture]
public sealed class PathingBootstrapTests
{
    private static readonly QueryDescription NavScenarioEntitiesQuery = new QueryDescription().WithAll<NavAgent2D>();

    private static readonly string[] PlaygroundMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "Navigation2DPlaygroundMod"
    };

    private static readonly string[] FormationPlaygroundMods =
    {
        "LudotsCoreMod",
        "CoreInputMod",
        "FormationPhysicsPlaygroundMod"
    };

    [Test]
    public void LoadMap_Registers_MapScopedPathingServices()
    {
        using var engine = CreateEngine();

        engine.LoadMap(engine.MergedConfig.StartupMapId);

        Assert.That(engine.CurrentMapSession, Is.Not.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathingConfig), Is.Not.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathStore), Is.Not.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathService), Is.Not.Null);
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

    [Test]
    public void FormationPlayground_LoadMap_BootstrapsScenarioEntities()
    {
        using var engine = CreateEngine(FormationPlaygroundMods);

        engine.Start();
        Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo("formation_physics_playground"));

        engine.LoadMap(engine.MergedConfig.StartupMapId);

        Assert.That(engine.CurrentMapSession, Is.Not.Null);
        Assert.That(engine.CurrentMapSession!.MapId.Value, Is.EqualTo("formation_physics_playground"));
        Assert.That(engine.World.CountEntities(in NavScenarioEntitiesQuery), Is.GreaterThan(0));
        Assert.That(engine.GetService(CoreServiceKeys.PathingConfig), Is.Not.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathStore), Is.Not.Null);
        Assert.That(engine.GetService(CoreServiceKeys.PathService), Is.Not.Null);
    }

    private static GameEngine CreateEngine(params string[] modIds)
    {
        string repoRoot = FindRepoRoot();
        string assetsRoot = Path.Combine(repoRoot, "assets");
        string[] effectiveMods = modIds.Length == 0 ? PlaygroundMods : modIds;
        var modPaths = new List<string>(effectiveMods.Length);
        for (int i = 0; i < effectiveMods.Length; i++)
        {
            modPaths.Add(Path.Combine(repoRoot, "mods", effectiveMods[i]));
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
