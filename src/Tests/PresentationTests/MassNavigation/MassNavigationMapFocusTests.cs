using System;
using System.Collections.Generic;
using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

/// <summary>
/// 挂起记住地图。同一张图恢复沿用仿真；LoadMap 到另一张大规模地图先卸载再新建。
/// </summary>
[TestFixture]
public sealed class MassNavigationMapFocusTests
{
    [Test]
    public void LoadMap_MassOverMass_RebuildsSimulation_SameMapResumeKeepsIt()
    {
        string repoRoot = FindRepoRoot();
        string probeModRoot = Path.Combine(Path.GetTempPath(), "Ludots_MassFocusProbe_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(probeModRoot, "assets", "Maps"));
        Directory.CreateDirectory(Path.Combine(probeModRoot, "assets", "Navigation"));
        try
        {
            File.WriteAllText(Path.Combine(probeModRoot, "mod.json"),
                """{"name":"LudotsMassFocusProbe","version":"1.0.0","priority":0,"dependencies":{"MassNavigationMod":"^1.0.0"}}""");
            File.WriteAllText(Path.Combine(probeModRoot, "assets", "MassNavigationConfig.json"), """
{
  "scenarioRuntime": { "autoSpawnConfiguredScenario": false },
  "presentation": { "teams": [] },
  "world": {
    "solverWindowWidthCm": 10000,
    "solverWindowHeightCm": 10000,
    "workAreaPaddingCm": 0,
    "workAreaMaxWidthCm": 10000,
    "workAreaMaxHeightCm": 10000,
    "activeHotZoneId": "central",
    "hotZones": [
      { "id": "central", "label": "Center", "centerXCm": 10000, "centerYCm": 10000, "widthCm": 10000, "heightCm": 10000 }
    ]
  }
}
""");
            File.WriteAllText(Path.Combine(probeModRoot, "assets", "Navigation", "navmesh.json"), """
{
  "maps": {
    "mass_probe_a": { "solver": "mass-navigation" },
    "mass_probe_b": { "solver": "mass-navigation" }
  }
}
""");
            File.WriteAllText(
                Path.Combine(probeModRoot, "assets", "Maps", "mass_probe_a.json"),
                ProbeMap("mass_probe_a", 20000));
            File.WriteAllText(
                Path.Combine(probeModRoot, "assets", "Maps", "mass_probe_b.json"),
                ProbeMap("mass_probe_b", 20000));
            File.WriteAllText(
                Path.Combine(probeModRoot, "assets", "Maps", "plain_hold.json"),
                ProbeMap("plain_hold", 6400));

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(new List<string>
            {
                Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "input", "SelectionInteractionMod"),
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod"),
                probeModRoot,
            }, Path.Combine(repoRoot, "assets"));
            engine.SetService(CoreServiceKeys.CameraBehaviorInputState, new Ludots.Core.Gameplay.Camera.CameraBehaviorInputState());
            engine.SetService(CoreServiceKeys.ViewController, new StubView());
            engine.Start();

            engine.LoadMap("mass_probe_a");
            MassNavigationSimulationRuntime first = RequireSimulation(engine, "mass_probe_a");
            engine.PushMap("plain_hold");
            Assert.That(engine.GetService(MassNavigationKeys.RuntimeBinding).Current, Is.Null);
            engine.PopMap();
            MassNavigationSimulationRuntime resumed = RequireSimulation(engine, "mass_probe_a");
            Assert.That(resumed, Is.SameAs(first));

            engine.LoadMap("mass_probe_b");
            MassNavigationSimulationRuntime second = RequireSimulation(engine, "mass_probe_b");
            Assert.That(second, Is.Not.SameAs(first));
        }
        finally
        {
            if (Directory.Exists(probeModRoot)) Directory.Delete(probeModRoot, recursive: true);
        }
    }

    private static string ProbeMap(string mapId, int sizeCm) => $$"""
{
  "Id": "{{mapId}}",
  "Teams": [ { "TeamId": 1, "RepresentativeInstanceId": "player_team" } ],
  "Players": [ { "PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": "local_player" } ],
  "Boards": [
    {
      "Name": "default",
      "SpatialType": "Grid",
      "WidthCm": {{sizeCm}},
      "HeightCm": {{sizeCm}},
      "Grid": { "CellSizeCm": 100 }
    }
  ],
  "Entities": [
    { "InstanceId": "player_team", "Template": "mass_navigation_player_team" },
    { "InstanceId": "local_player", "Template": "mass_navigation_local_player" }
  ]
}
""";

    private static MassNavigationSimulationRuntime RequireSimulation(GameEngine engine, string mapId)
    {
        MassNavigationRuntimeBinding binding = engine.GetService(MassNavigationKeys.RuntimeBinding);
        Assert.That(binding.Current, Is.Not.Null);
        Assert.That(binding.CurrentMapId.Value, Is.EqualTo(mapId));
        return binding.Current!;
    }

    private sealed class StubView : Ludots.Core.Presentation.Camera.IViewController
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
