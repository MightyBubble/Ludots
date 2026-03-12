using System;
using System.IO;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Navigation2D
{
    [TestFixture]
    public sealed class Navigation2DConfigPipelineTests
    {
        [Test]
        public void MergeGameConfig_ParsesExplicitNavigation2DSteeringAndPlaygroundConfig()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_Navigation2DConfigPipelineTests", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            string mod = Path.Combine(root, "ModNav");
            Directory.CreateDirectory(Path.Combine(core, "Configs"));
            Directory.CreateDirectory(Path.Combine(mod, "assets"));

            File.WriteAllText(Path.Combine(core, "Configs", "game.json"), @"{ ""Navigation2D"": { ""Enabled"": true } }");
            File.WriteAllText(Path.Combine(mod, "assets", "game.json"), @"{
  ""Navigation2D"": {
    ""Enabled"": true,
    ""MaxAgents"": 4096,
    ""FlowStreaming"": {
      ""Enabled"": true,
      ""ActivationRadiusTiles"": 4,
      ""MaxActiveTilesPerFlow"": 320,
      ""UnloadGraceTicks"": 10,
      ""MaxPotentialCells"": 420,
      ""MaxActivationWindowWidthTiles"": 18,
      ""MaxActivationWindowHeightTiles"": 14,
      ""WorldBoundsEnabled"": true,
      ""WorldMinTileX"": -400,
      ""WorldMinTileY"": -300,
      ""WorldMaxTileX"": 399,
      ""WorldMaxTileY"": 299
    },
    ""FlowCrowd"": {
      ""Enabled"": true,
      ""Density"": {
        ""Min"": 0.4,
        ""Max"": 2.1,
        ""Exponent"": 0.45
      },
      ""Speed"": {
        ""MinFactor"": 0.25,
        ""MaxFactor"": 1.1,
        ""FlowVelocityScaleCmPerSec"": 720
      },
      ""Cost"": {
        ""DistanceWeight"": 0.35,
        ""TimeWeight"": 0.65,
        ""DiscomfortWeight"": 1.75,
        ""NormalizeDistanceAndTimeWeights"": true
      },
      ""Discomfort"": {
        ""Enabled"": true,
        ""ObstacleHaloRadiusCm"": 280,
        ""ObstacleHaloValue"": 1.6,
        ""ObstacleHaloEdgeValue"": 0.25
      }
    },
    ""Spatial"": {
      ""UpdateMode"": ""Adaptive"",
      ""RebuildCellMigrationsThreshold"": 64,
      ""RebuildAccumulatedCellMigrationsThreshold"": 256
    },
    ""Playground"": {
      ""DefaultAgentsPerTeam"": 3200,
      ""AgentsPerTeamStep"": 250,
      ""DefaultScenarioIndex"": 1,
      ""DefaultSpawnBatch"": 96,
      ""SpawnBatchStep"": 24,
      ""CommandGoalRadiusCm"": 140,
      ""CommandFormationSpacingCm"": 180,
      ""DynamicSpawnSpacingCm"": 160,
      ""DynamicBlockerRadiusCm"": 150,
      ""Scenarios"": [
        {
          ""Id"": ""queue"",
          ""Name"": ""Goal Queue"",
          ""Kind"": ""GoalQueue"",
          ""TeamCount"": 1,
          ""GoalRadiusCm"": 180,
          ""BlockerCount"": 18,
          ""BlockerSpacingCm"": 220
        },
        {
          ""Id"": ""merge"",
          ""Name"": ""Lane Merge"",
          ""Kind"": ""LaneMerge"",
          ""TeamCount"": 2,
          ""LaneOffsetCm"": 2600,
          ""FormationSpacingCm"": 140
        }
      ]
    },
    ""Steering"": {
      ""Mode"": ""Hybrid"",
      ""QueryBudget"": {
        ""MaxNeighborsPerAgent"": 12,
        ""MaxCandidateChecksPerAgent"": 48
      },
      ""Orca"": {
        ""Enabled"": true,
        ""FallbackToPreferredVelocity"": true
      },
      ""Sonar"": {
        ""Enabled"": true,
        ""PredictionTimeScale"": 0.75,
        ""BlockedStop"": false
      },
      ""Hybrid"": {
        ""Enabled"": true,
        ""DenseNeighborThreshold"": 5,
        ""MinOpposingNeighborsForOrca"": 2
      },
      ""SmartStop"": {
        ""Enabled"": true,
        ""MaxNeighbors"": 6,
        ""GoalToleranceCm"": 90
      },
      ""Separation"": {
        ""Enabled"": true,
        ""RadiusCm"": 180,
        ""Weight"": 0.75
      },
      ""TemporalCoherence"": {
        ""Enabled"": true,
        ""RequireSteadyStateWorld"": false,
        ""MaxReuseTicks"": 9,
        ""PositionToleranceCm"": 3,
        ""VelocityToleranceCmPerSec"": 6,
        ""PreferredVelocityToleranceCmPerSec"": 7,
        ""NeighborPositionQuantizationCm"": 10,
        ""NeighborVelocityQuantizationCmPerSec"": 12
      }
    }
  }
}");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", core);
            vfs.Mount("ModNav", mod);

            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            modLoader.LoadedModIds.Add("ModNav");

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var gameConfig = pipeline.MergeGameConfig();
            using var runtime = new Navigation2DRuntime(gameConfig.Navigation2D, gridCellSizeCm: 100, loadedChunks: null);

            Assert.That(gameConfig.Navigation2D.MaxAgents, Is.EqualTo(4096));
            Assert.That(gameConfig.Navigation2D.Spatial.UpdateMode, Is.EqualTo(Navigation2DSpatialUpdateMode.Adaptive));
            Assert.That(gameConfig.Navigation2D.Spatial.RebuildCellMigrationsThreshold, Is.EqualTo(64));
            Assert.That(gameConfig.Navigation2D.Spatial.RebuildAccumulatedCellMigrationsThreshold, Is.EqualTo(256));
            Assert.That(gameConfig.Navigation2D.Playground.DefaultAgentsPerTeam, Is.EqualTo(3200));
            Assert.That(gameConfig.Navigation2D.Playground.AgentsPerTeamStep, Is.EqualTo(250));
            Assert.That(gameConfig.Navigation2D.Playground.DefaultScenarioIndex, Is.EqualTo(1));
            Assert.That(gameConfig.Navigation2D.Playground.DefaultSpawnBatch, Is.EqualTo(96));
            Assert.That(gameConfig.Navigation2D.Playground.SpawnBatchStep, Is.EqualTo(24));
            Assert.That(gameConfig.Navigation2D.Playground.CommandGoalRadiusCm, Is.EqualTo(140));
            Assert.That(gameConfig.Navigation2D.Playground.CommandFormationSpacingCm, Is.EqualTo(180));
            Assert.That(gameConfig.Navigation2D.Playground.DynamicSpawnSpacingCm, Is.EqualTo(160));
            Assert.That(gameConfig.Navigation2D.Playground.DynamicBlockerRadiusCm, Is.EqualTo(150));
            Assert.That(gameConfig.Navigation2D.Playground.Scenarios.Count, Is.EqualTo(2));
            Assert.That(gameConfig.Navigation2D.Playground.Scenarios[0].Kind, Is.EqualTo(Navigation2DPlaygroundScenarioKind.GoalQueue));
            Assert.That(gameConfig.Navigation2D.Playground.Scenarios[0].TeamCount, Is.EqualTo(1));
            Assert.That(gameConfig.Navigation2D.Playground.Scenarios[0].BlockerCount, Is.EqualTo(18));
            Assert.That(gameConfig.Navigation2D.Playground.Scenarios[1].Kind, Is.EqualTo(Navigation2DPlaygroundScenarioKind.LaneMerge));
            Assert.That(gameConfig.Navigation2D.Playground.Scenarios[1].LaneOffsetCm, Is.EqualTo(2600));
            Assert.That(gameConfig.Navigation2D.Steering.Mode, Is.EqualTo(Navigation2DAvoidanceMode.Hybrid));
            Assert.That(gameConfig.Navigation2D.Steering.QueryBudget.MaxNeighborsPerAgent, Is.EqualTo(12));
            Assert.That(gameConfig.Navigation2D.Steering.QueryBudget.MaxCandidateChecksPerAgent, Is.EqualTo(48));
            Assert.That(gameConfig.Navigation2D.Steering.Sonar.PredictionTimeScale, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.Steering.Hybrid.DenseNeighborThreshold, Is.EqualTo(5));
            Assert.That(gameConfig.Navigation2D.Steering.Hybrid.MinOpposingNeighborsForOrca, Is.EqualTo(2));
            Assert.That(gameConfig.Navigation2D.Steering.SmartStop.MaxNeighbors, Is.EqualTo(6));
            Assert.That(gameConfig.Navigation2D.Steering.Separation.Enabled, Is.True);
            Assert.That(gameConfig.Navigation2D.Steering.Separation.RadiusCm, Is.EqualTo(180));
            Assert.That(gameConfig.Navigation2D.Steering.Separation.Weight, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.Steering.TemporalCoherence.Enabled, Is.True);
            Assert.That(gameConfig.Navigation2D.Steering.TemporalCoherence.RequireSteadyStateWorld, Is.False);
            Assert.That(gameConfig.Navigation2D.Steering.TemporalCoherence.MaxReuseTicks, Is.EqualTo(9));
            Assert.That(gameConfig.Navigation2D.Steering.TemporalCoherence.PositionToleranceCm, Is.EqualTo(3));
            Assert.That(gameConfig.Navigation2D.Steering.TemporalCoherence.VelocityToleranceCmPerSec, Is.EqualTo(6));
            Assert.That(gameConfig.Navigation2D.Steering.TemporalCoherence.PreferredVelocityToleranceCmPerSec, Is.EqualTo(7));
            Assert.That(gameConfig.Navigation2D.Steering.TemporalCoherence.NeighborPositionQuantizationCm, Is.EqualTo(10));
            Assert.That(gameConfig.Navigation2D.Steering.TemporalCoherence.NeighborVelocityQuantizationCmPerSec, Is.EqualTo(12));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.Enabled, Is.True);
            Assert.That(gameConfig.Navigation2D.FlowStreaming.ActivationRadiusTiles, Is.EqualTo(4));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.MaxActiveTilesPerFlow, Is.EqualTo(320));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.UnloadGraceTicks, Is.EqualTo(10));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.MaxPotentialCells, Is.EqualTo(420f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.MaxActivationWindowWidthTiles, Is.EqualTo(18));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.MaxActivationWindowHeightTiles, Is.EqualTo(14));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.WorldBoundsEnabled, Is.True);
            Assert.That(gameConfig.Navigation2D.FlowStreaming.WorldMinTileX, Is.EqualTo(-400));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.WorldMinTileY, Is.EqualTo(-300));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.WorldMaxTileX, Is.EqualTo(399));
            Assert.That(gameConfig.Navigation2D.FlowStreaming.WorldMaxTileY, Is.EqualTo(299));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Enabled, Is.True);
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Density.Min, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Density.Max, Is.EqualTo(2.1f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Density.Exponent, Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Speed.MinFactor, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Speed.MaxFactor, Is.EqualTo(1.1f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Speed.FlowVelocityScaleCmPerSec, Is.EqualTo(720f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Cost.DistanceWeight, Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Cost.TimeWeight, Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Cost.DiscomfortWeight, Is.EqualTo(1.75f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Discomfort.ObstacleHaloRadiusCm, Is.EqualTo(280));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Discomfort.ObstacleHaloValue, Is.EqualTo(1.6f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.FlowCrowd.Discomfort.ObstacleHaloEdgeValue, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(runtime.Config.Playground.DefaultAgentsPerTeam, Is.EqualTo(3200));
            Assert.That(runtime.Config.Playground.DefaultSpawnBatch, Is.EqualTo(96));
            Assert.That(runtime.Config.Playground.SpawnBatchStep, Is.EqualTo(24));
            Assert.That(runtime.Config.Playground.CommandGoalRadiusCm, Is.EqualTo(140));
            Assert.That(runtime.Config.Playground.CommandFormationSpacingCm, Is.EqualTo(180));
            Assert.That(runtime.Config.Playground.DynamicSpawnSpacingCm, Is.EqualTo(160));
            Assert.That(runtime.Config.Playground.DynamicBlockerRadiusCm, Is.EqualTo(150));
            Assert.That(runtime.Config.Playground.Scenarios[0].Kind, Is.EqualTo(Navigation2DPlaygroundScenarioKind.GoalQueue));
            Assert.That(runtime.Config.Spatial.UpdateMode, Is.EqualTo(Navigation2DSpatialUpdateMode.Adaptive));
            Assert.That(runtime.Config.FlowStreaming.MaxActiveTilesPerFlow, Is.EqualTo(320));
            Assert.That(runtime.Config.FlowStreaming.MaxActivationWindowWidthTiles, Is.EqualTo(18));
            Assert.That(runtime.Config.FlowStreaming.MaxActivationWindowHeightTiles, Is.EqualTo(14));
            Assert.That(runtime.Config.FlowStreaming.WorldBoundsEnabled, Is.True);
            Assert.That(runtime.Config.FlowStreaming.WorldMaxTileX, Is.EqualTo(399));
            Assert.That(runtime.Config.FlowCrowd.Density.Max, Is.EqualTo(2.1f).Within(0.001f));
            Assert.That(runtime.Config.FlowCrowd.Speed.FlowVelocityScaleCmPerSec, Is.EqualTo(720f).Within(0.001f));
            Assert.That(runtime.Config.FlowCrowd.Cost.DiscomfortWeight, Is.EqualTo(1.75f).Within(0.001f));
            Assert.That(runtime.Config.Steering.Mode, Is.EqualTo(Navigation2DAvoidanceMode.Hybrid));
            Assert.That(runtime.Config.Steering.Separation.RadiusCm, Is.EqualTo(180));
            Assert.That(runtime.Config.Steering.Separation.Weight, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(runtime.Config.Steering.TemporalCoherence.Enabled, Is.True);
            Assert.That(runtime.Config.Steering.TemporalCoherence.MaxReuseTicks, Is.EqualTo(9));
            Assert.That(runtime.Config.Steering.TemporalCoherence.NeighborVelocityQuantizationCmPerSec, Is.EqualTo(12));
        }

        [Test]
        public void GameEngine_UsesMergedNavigation2DConfigWhenCreatingRuntime()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "mods");

            var engine = new GameEngine();
            try
            {
                engine.InitializeWithConfigPipeline(
                    new List<string>
                    {
                        Path.Combine(modsRoot, "LudotsCoreMod"),
                        Path.Combine(modsRoot, "CoreInputMod"),
                        Path.Combine(modsRoot, "Navigation2DPlaygroundMod")
                    },
                    assetsRoot);

                var runtime = engine.GetService(CoreServiceKeys.Navigation2DRuntime);
                Assert.That(runtime, Is.Not.Null);
                Assert.That(runtime.Config.Steering.Mode, Is.EqualTo(engine.MergedConfig.Navigation2D.Steering.Mode));
                Assert.That(runtime.Config.Steering.TemporalCoherence.Enabled, Is.EqualTo(engine.MergedConfig.Navigation2D.Steering.TemporalCoherence.Enabled));
                Assert.That(runtime.Config.Steering.TemporalCoherence.RequireSteadyStateWorld, Is.EqualTo(engine.MergedConfig.Navigation2D.Steering.TemporalCoherence.RequireSteadyStateWorld));
                Assert.That(runtime.Config.Spatial.UpdateMode, Is.EqualTo(engine.MergedConfig.Navigation2D.Spatial.UpdateMode));
                Assert.That(runtime.Config.FlowStreaming.MaxActiveTilesPerFlow, Is.EqualTo(engine.MergedConfig.Navigation2D.FlowStreaming.MaxActiveTilesPerFlow));
                Assert.That(runtime.Config.Playground.DefaultAgentsPerTeam, Is.EqualTo(engine.MergedConfig.Navigation2D.Playground.DefaultAgentsPerTeam));
                Assert.That(runtime.Config.Playground.DefaultSpawnBatch, Is.EqualTo(engine.MergedConfig.Navigation2D.Playground.DefaultSpawnBatch));
                Assert.That(runtime.Config.Playground.CommandFormationSpacingCm, Is.EqualTo(engine.MergedConfig.Navigation2D.Playground.CommandFormationSpacingCm));
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "src")) &&
                    Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent ?? string.Empty;
            }

            throw new DirectoryNotFoundException("Unable to locate repository root from test directory.");
        }
    }
}


