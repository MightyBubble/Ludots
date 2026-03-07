using System;
using System.IO;
using Ludots.Core.Config;
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
    ""Spatial"": {
      ""UpdateMode"": ""Adaptive"",
      ""RebuildCellMigrationsThreshold"": 64,
      ""RebuildAccumulatedCellMigrationsThreshold"": 256
    },
    ""Playground"": {
      ""DefaultAgentsPerTeam"": 3200,
      ""AgentsPerTeamStep"": 250,
      ""DefaultScenarioIndex"": 1,
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
            Assert.That(runtime.Config.Playground.DefaultAgentsPerTeam, Is.EqualTo(3200));
            Assert.That(runtime.Config.Playground.Scenarios[0].Kind, Is.EqualTo(Navigation2DPlaygroundScenarioKind.GoalQueue));
            Assert.That(runtime.Config.Spatial.UpdateMode, Is.EqualTo(Navigation2DSpatialUpdateMode.Adaptive));
            Assert.That(runtime.Config.FlowStreaming.MaxActiveTilesPerFlow, Is.EqualTo(320));
            Assert.That(runtime.Config.FlowStreaming.MaxActivationWindowWidthTiles, Is.EqualTo(18));
            Assert.That(runtime.Config.FlowStreaming.MaxActivationWindowHeightTiles, Is.EqualTo(14));
            Assert.That(runtime.Config.FlowStreaming.WorldBoundsEnabled, Is.True);
            Assert.That(runtime.Config.FlowStreaming.WorldMaxTileX, Is.EqualTo(399));
            Assert.That(runtime.Config.Steering.Mode, Is.EqualTo(Navigation2DAvoidanceMode.Hybrid));
            Assert.That(runtime.Config.Steering.TemporalCoherence.Enabled, Is.True);
            Assert.That(runtime.Config.Steering.TemporalCoherence.MaxReuseTicks, Is.EqualTo(9));
            Assert.That(runtime.Config.Steering.TemporalCoherence.NeighborVelocityQuantizationCmPerSec, Is.EqualTo(12));
        }
    }
}


