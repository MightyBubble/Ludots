using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Fields;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using NUnit.Framework;

namespace CrowdSimulationTests.Integration;

/// <summary>
/// 同一张带呈现的地图聚焦、卸载、再聚焦:场投影器列表不增长,固定步与呈现系统不叠加。
/// </summary>
public class PresentationRefocusTests
{
    [Test]
    public void PathPresentation_Refocus_DoesNotStackProjectorsOrSystems()
    {
        string repoRoot = FindRepoRoot();
        var engine = new GameEngine();
        engine.InitializeWithConfigPipeline(new List<string>
        {
            Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
            Path.Combine(repoRoot, "mods", "capabilities", "navigation", "CrowdSimulationMod"),
            Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS1Terrain1337Mod"),
            Path.Combine(repoRoot, "mods", "showcases", "crowd_simulation", "CrowdSimulationS3PathMod"),
        }, Path.Combine(repoRoot, "assets"));
        engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
        engine.SetService(CoreServiceKeys.ViewController, new StubViewController());
        engine.Start();

        var registry = engine.GetService(CoreServiceKeys.GlobalFieldVisualProjectorRegistry);
        Assert.That(registry, Is.Not.Null);
        Assert.That(registry!.Projectors, Has.Count.EqualTo(1));
        Assert.That(registry.Projectors[0], Is.InstanceOf<CrowdFieldProjectorSlot>());
        var slot = (CrowdFieldProjectorSlot)registry.Projectors[0];
        Assert.That(slot.Current, Is.Null);
        Assert.That(engine.GetService(CoreServiceKeys.RouteVisualBuffer), Is.Not.Null);

        engine.LoadMap("crowd_simulation_s1337");
        AssertInstalledOnce(engine, registry, slot);
        Assert.That(slot.Current, Is.Not.Null);

        engine.UnloadMap("crowd_simulation_s1337");
        Assert.That(registry.Projectors, Has.Count.EqualTo(1));
        Assert.That(slot.Current, Is.Null);
        Assert.That(engine.CountSystemsOfType(SystemGroup.PostMovement, typeof(S3PathInputSystem)), Is.EqualTo(0));
        Assert.That(engine.CountSystemsOfType(SystemGroup.PostMovement, typeof(S3PathDemoSimulationSystem)), Is.EqualTo(0));
        Assert.That(engine.CountPresentationSystemsOfType(typeof(S3PathDemoPresentationSystem)), Is.EqualTo(0));

        engine.LoadMap("crowd_simulation_s1337");
        AssertInstalledOnce(engine, registry, slot);
        Assert.That(slot.Current, Is.Not.Null);
    }

    private static void AssertInstalledOnce(
        GameEngine engine,
        GlobalFieldVisualProjectorRegistry registry,
        CrowdFieldProjectorSlot slot)
    {
        Assert.That(registry.Projectors, Has.Count.EqualTo(1));
        Assert.That(registry.Projectors[0], Is.SameAs(slot));
        Assert.That(engine.CountSystemsOfType(SystemGroup.PostMovement, typeof(S3PathInputSystem)), Is.EqualTo(1));
        Assert.That(engine.CountSystemsOfType(SystemGroup.PostMovement, typeof(S3PathDemoSimulationSystem)), Is.EqualTo(1));
        Assert.That(engine.CountPresentationSystemsOfType(typeof(S3PathDemoPresentationSystem)), Is.EqualTo(1));
    }

    private sealed class StubViewController : Ludots.Core.Presentation.Camera.IViewController
    {
        public System.Numerics.Vector2 Resolution { get; } = new(1280, 720);
        public float Fov { get; } = 50f;
        public float AspectRatio { get; } = 1280f / 720f;
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "mods")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("找不到仓库根(向上查找 mods 目录失败)");
    }
}
