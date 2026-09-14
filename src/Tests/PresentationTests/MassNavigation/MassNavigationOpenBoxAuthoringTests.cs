using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// 开箱即用作者面合同：单位模板带 nav 参数、地图摆实体、零 MassNavigationConfig.json——
    /// 装图即激活、即绑定、即可下令寻路。世界尺寸从板（cells）推导，无任何必填调参键。
    /// </summary>
    [TestFixture]
    public sealed class MassNavigationOpenBoxAuthoringTests
    {
        private const float FixedDeltaSeconds = 1f / 60f;
        private const int MaxWarmupFrames = 600;

        private string _modRoot = string.Empty;

        [SetUp]
        public void WriteOpenBoxMod()
        {
            _modRoot = Path.Combine(Path.GetTempPath(), "Ludots_OpenBox_Nav", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_modRoot, "assets", "Maps"));
            Directory.CreateDirectory(Path.Combine(_modRoot, "assets", "Entities"));
            Directory.CreateDirectory(Path.Combine(_modRoot, "assets", "GAS"));

            File.WriteAllText(Path.Combine(_modRoot, "mod.json"), """
            {
              "name": "OpenBoxNavMod",
              "version": "1.0.0",
              "priority": 10,
              "dependencies": { "LudotsCoreMod": "^1.0.0" },
              "tags": ["test"]
            }
            """);

            File.WriteAllText(Path.Combine(_modRoot, "assets", "game.json"), """
            {
              "startupMapId": "openbox_nav_map",
              "startupLocalSeats": [{ "seatId": "seat.0", "playerId": 1 }]
            }
            """);

            File.WriteAllText(Path.Combine(_modRoot, "assets", "Maps", "openbox_nav_map.json"), """
            {
              "Id": "openbox_nav_map",
              "Boards": [
                {
                  "Name": "default",
                  "SpatialType": "Grid",
                  "WidthInCells": 1024,
                  "HeightInCells": 1024,
                  "GridCellSizeCm": 100,
                  "ChunkSizeCells": 64,
                  "LoadedChunkCapacity": 64
                }
              ],
              "Entities": [
                { "InstanceId": "team_rep", "Template": "openbox_team_rep" },
                { "InstanceId": "local_player", "Template": "openbox_local_player" },
                {
                  "InstanceId": "scout_a",
                  "Template": "openbox_scout",
                  "Overrides": {
                    "WorldPositionCm": { "Value": { "X": -1000, "Y": -1000 } },
                    "Team": { "Id": 1 }
                  }
                },
                {
                  "InstanceId": "scout_b",
                  "Template": "openbox_scout_heavy",
                  "Overrides": {
                    "WorldPositionCm": { "Value": { "X": -1000, "Y": -800 } },
                    "Team": { "Id": 1 }
                  }
                }
              ],
              "Teams": [{ "TeamId": 1, "RepresentativeInstanceId": "team_rep" }],
              "Players": [{ "PlayerId": 1, "TeamId": 1, "RepresentativeInstanceId": "local_player" }]
            }
            """);

            File.WriteAllText(Path.Combine(_modRoot, "assets", "Entities", "templates.json"), """
            [
              {
                "id": "openbox_team_rep",
                "components": { "Name": { "Value": "OpenBox.Team" } }
              },
              {
                "id": "openbox_local_player",
                "components": {
                  "Name": { "Value": "OpenBox.Player" },
                  "PlayerOwner": { "PlayerId": 1 }
                }
              },
              {
                "id": "openbox_scout",
                "components": {
                  "Name": { "Value": "OpenBox.Scout" },
                  "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
                  "FacingDirection": { "AngleRad": 0 },
                  "EntityLayer": {
                    "category": ["massNavigation.agent"],
                    "mask": ["massNavigation.agent"]
                  },
                  "MassNavigationAgent": { "speedCmPerSecond": 500, "radiusCm": 30 }
                }
              },
              {
                "id": "openbox_scout_heavy",
                "components": {
                  "Name": { "Value": "OpenBox.ScoutHeavy" },
                  "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
                  "FacingDirection": { "AngleRad": 0 },
                  "EntityLayer": {
                    "category": ["massNavigation.agent"],
                    "mask": ["massNavigation.agent"]
                  },
                  "MassNavigationAgent": { "speedCmPerSecond": 350, "radiusCm": 45, "heavy": true }
                }
              }
            ]
            """);

            string repoRoot = FindRepoRoot();
            File.Copy(
                Path.Combine(repoRoot, "mods", "capabilities", "navigation", "MassNavigationMod", "assets", "GAS", "order_types.json"),
                Path.Combine(_modRoot, "assets", "GAS", "order_types.json"));
        }

        [TearDown]
        public void RemoveOpenBoxMod()
        {
            try
            {
                if (Directory.Exists(_modRoot))
                {
                    Directory.Delete(_modRoot, recursive: true);
                }
            }
            catch
            {
            }
        }

        [Test]
        public void MapWithTemplatedNavUnits_BindsAndPathfindsWithoutConfigFile()
        {
            Assert.That(File.Exists(Path.Combine(_modRoot, "assets", "MassNavigationConfig.json")), Is.False,
                "This contract requires the zero-config authoring path.");

            string repoRoot = FindRepoRoot();
            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new System.Collections.Generic.List<string>
                {
                    Path.Combine(repoRoot, "mods", "LudotsCoreMod"),
                    _modRoot,
                },
                Path.Combine(repoRoot, "assets"));
            HeadlessPresentationTestHost.Install(engine);
            engine.Start();
            engine.LoadStartupMap();

            MassNavigationSimulationRuntime simulation = WaitForNavigationRuntime(engine);

            Assert.That(simulation.Config.MapId, Is.EqualTo("openbox_nav_map"),
                "Config-file absence must activate the engine defaults scoped to the focused map.");
            Assert.That(simulation.WorldConfig.StreamingChunkSizeCm, Is.EqualTo(6400),
                "streamingChunkSizeCm must be derived from the board chunk (64 cells x 100cm).");
            Assert.That(simulation.NavigationAgentCount, Is.EqualTo(2),
                "Both map-placed nav units must bind through the authored agent binding system.");

            MassNavigationFlowSolverState flow = simulation.GetFlowSolverForTests();
            int heavyIndex = flow.IsHeavyProfile(1) ? 1 : 0;
            int lightIndex = 1 - heavyIndex;
            Assert.That(flow.GetSpeedCmPerSecond(lightIndex), Is.EqualTo(500f).Within(0.001f),
                "Template speedCmPerSecond must reach the solver profile.");
            Assert.That(flow.GetBodyRadiusCm(lightIndex), Is.EqualTo(30f).Within(0.001f));
            Assert.That(flow.GetSpeedCmPerSecond(heavyIndex), Is.EqualTo(350f).Within(0.001f));
            Assert.That(flow.GetBodyRadiusCm(heavyIndex), Is.EqualTo(45f).Within(0.001f));

            AssertNavTargetMovesAgent(engine, simulation, lightIndex);
        }

        private static void AssertNavTargetMovesAgent(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            int agentIndex)
        {
            Vector2 before = simulation.GetAgentWorldPositionCm(agentIndex);
            Vector2 target = before + new Vector2(600f, 0f);
            Assert.That(
                simulation.SetAgentNavigationTargetWorldCm(agentIndex, target.X, target.Y),
                Is.True,
                "Setting a world-space navigation target must succeed for a bound agent.");
            for (int frame = 0; frame < 90; frame++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FixedDeltaSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }

            Vector2 after = simulation.GetAgentWorldPositionCm(agentIndex);
            float moved = Vector2.Distance(before, after);
            Assert.That(moved, Is.GreaterThan(100f),
                $"The templated unit must pathfind toward its target without any tuning config (moved {moved:F1}cm).");
            Assert.That(after.X, Is.GreaterThan(before.X),
                "The unit must move toward the +X target.");
        }

        private static MassNavigationSimulationRuntime WaitForNavigationRuntime(GameEngine engine)
        {
            for (int frame = 0; frame < MaxWarmupFrames; frame++)
            {
                if (MassNavigationIds.IsCurrentNavigationRuntimeReady(engine) &&
                    engine.GetService(MassNavigationKeys.RuntimeBinding) is { Current: { } simulation } &&
                    simulation.NavigationAgentCount >= 2)
                {
                    return simulation;
                }

                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FixedDeltaSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }

            Assert.Fail("Open-box navigation runtime did not bind the map-placed units in time.");
            throw new InvalidOperationException("unreachable");
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
    }
}
