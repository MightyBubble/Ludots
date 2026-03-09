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
        public void MergeGameConfig_ParsesExplicitNavigation2DSteeringConfig()
        {
            string root = Path.Combine(Path.GetTempPath(), "Ludots_Navigation2DConfigPipelineTests", Guid.NewGuid().ToString("N"));
            string core = Path.Combine(root, "Core");
            string mod = Path.Combine(root, "ModNav");
            Directory.CreateDirectory(Path.Combine(core, "Configs"));
            Directory.CreateDirectory(Path.Combine(mod, "assets"));

            File.WriteAllText(Path.Combine(core, "Configs", "game.json"), "{ \"Navigation2D\": { \"Enabled\": true } }");
            File.WriteAllText(Path.Combine(mod, "assets", "game.json"), @"{
  ""Navigation2D"": {
    ""Enabled"": true,
    ""MaxAgents"": 4096,
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
            Assert.That(gameConfig.Navigation2D.Steering.Mode, Is.EqualTo(Navigation2DAvoidanceMode.Hybrid));
            Assert.That(gameConfig.Navigation2D.Steering.QueryBudget.MaxNeighborsPerAgent, Is.EqualTo(12));
            Assert.That(gameConfig.Navigation2D.Steering.QueryBudget.MaxCandidateChecksPerAgent, Is.EqualTo(48));
            Assert.That(gameConfig.Navigation2D.Steering.Sonar.PredictionTimeScale, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(gameConfig.Navigation2D.Steering.Hybrid.DenseNeighborThreshold, Is.EqualTo(5));
            Assert.That(gameConfig.Navigation2D.Steering.Hybrid.MinOpposingNeighborsForOrca, Is.EqualTo(2));
            Assert.That(gameConfig.Navigation2D.Steering.SmartStop.MaxNeighbors, Is.EqualTo(6));
            Assert.That(runtime.Config.Steering.Mode, Is.EqualTo(Navigation2DAvoidanceMode.Hybrid));
        }
    }
}
