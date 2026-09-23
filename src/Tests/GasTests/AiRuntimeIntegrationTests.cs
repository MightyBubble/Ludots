using System.Collections.Generic;
using System.IO;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class AiRuntimeIntegrationTests
    {
        [Test]
        public void GameEngine_AiPipeline_CompilesDemoAction_AndActivatesAttackOrder()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod", "AIDemoMod" });

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            engine.SimulationBudgetMsPerFrame = 100;
            engine.SimulationMaxSlicesPerLogicFrame = 1024;

            Assert.That(engine.AiRuntime.ActionLibrary.Count, Is.EqualTo(1));
            Assert.That(engine.AiRuntime.ActionLibrary.OrderSpec[0].OrderTypeId, Is.EqualTo(102));

            engine.Start();

            _ = engine.World.Create();
            Entity target = engine.World.Create();
            var blackboardEntities = new BlackboardEntityBuffer();
            blackboardEntities.Set(1000, target);

            Entity agent = engine.World.Create(
                new AIAgent(),
                new AIWorldState256(),
                new AIGoalSelection(),
                new AIPlanningState(),
                new AIPlan32(),
                OrderBuffer.CreateEmpty(),
                new GameplayTagContainer(),
                new BlackboardIntBuffer(),
                blackboardEntities);

            for (int i = 0; i < 4; i++)
            {
                engine.Tick(1f / 60f);
            }

            ref OrderBuffer orderBuffer = ref engine.World.Get<OrderBuffer>(agent);
            Assert.That(orderBuffer.HasActive, Is.True, "AI pipeline should submit and activate the authored attack order.");
            Assert.That(orderBuffer.ActiveOrder.Order.OrderTypeId, Is.EqualTo(102));
            Assert.That(orderBuffer.ActiveOrder.Order.Target, Is.EqualTo(target));

            ref BlackboardEntityBuffer entities = ref engine.World.Get<BlackboardEntityBuffer>(agent);
            Assert.That(entities.TryGet(120, out Entity attackTarget), Is.True, "attackTarget order should write its configured entity blackboard target.");
            Assert.That(attackTarget, Is.EqualTo(target));
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
}
