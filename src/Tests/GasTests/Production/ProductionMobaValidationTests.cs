using System;
using System.IO;
using System.Linq;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class ProductionMobaValidationTests
    {
        [Test]
        public void MobaDemo_EntryMap_CastQ_DamagesEnemy()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "src", "Mods");

            var engine = new GameEngine();
            try
            {
                engine.InitializeWithConfigPipeline(
                    new()
                    {
                        Path.Combine(modsRoot, "LudotsCoreMod"),
                        Path.Combine(modsRoot, "MobaDemoMod")
                    },
                    assetsRoot);

                engine.Start();
                engine.LoadMap(engine.MergedConfig.StartupMapId);
                engine.GlobalContext.Remove(Ludots.Core.Scripting.ContextKeys.CameraControllerRequest);

                for (int i = 0; i < 5; i++)
                {
                    engine.Tick(1f / 60f);
                }

                var startErrors = engine.TriggerManager.Errors;
                if (startErrors.Count > 0)
                {
                    var mobaAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "MobaDemoMod");
                    var loc = mobaAsm?.Location ?? "<null>";
                    throw new InvalidOperationException($"Trigger errors after startup: {startErrors[0].TriggerName} ({startErrors[0].EventKey.Value}): {startErrors[0].Exception.Message}. MobaDemoMod.dll={loc}");
                }

                var (hero, enemy) = FindHeroAndEnemy(engine.World);

                ref var enemyAttrsBefore = ref engine.World.Get<AttributeBuffer>(enemy);
                int healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId("Health");
                if (healthId <= 0) healthId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("Health");
                float enemyHealthBefore = enemyAttrsBefore.GetCurrent(healthId);

                var orderQueue = (OrderQueue)engine.GlobalContext[Ludots.Core.Scripting.ContextKeys.OrderQueue];
                int castAbilityTagId = engine.MergedConfig.Constants.OrderTags["castAbility"];
                orderQueue.TryEnqueue(new Order
                {
                    OrderTagId = castAbilityTagId,
                    Actor = hero,
                    Target = enemy,
                    Args = new OrderArgs { I0 = 0 }
                });

                for (int i = 0; i < 10; i++)
                {
                    engine.Tick(1f / 60f);
                }

                ref var enemyAttrsAfter = ref engine.World.Get<AttributeBuffer>(enemy);
                float enemyHealthAfter = enemyAttrsAfter.GetCurrent(healthId);
                Assert.That(enemyHealthAfter, Is.EqualTo(enemyHealthBefore - 20f).Within(0.0001f));

                var endErrors = engine.TriggerManager.Errors;
                Assert.That(endErrors.Count, Is.EqualTo(0));
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static (Entity hero, Entity enemy) FindHeroAndEnemy(World world)
        {
            Entity hero = Entity.Null;
            Entity enemy = Entity.Null;

            var query = new QueryDescription().WithAll<Name, Team, AttributeBuffer>();
            world.Query(in query, (Entity e, ref Name name, ref Team team, ref AttributeBuffer attrs) =>
            {
                if (hero == Entity.Null && string.Equals(name.Value, "Hero", StringComparison.OrdinalIgnoreCase))
                {
                    hero = e;
                    return;
                }
                if (enemy == Entity.Null && team.Id == 2)
                {
                    enemy = e;
                }
            });

            if (hero == Entity.Null) throw new InvalidOperationException("Failed to find Hero entity in entry map.");
            if (enemy == Entity.Null) throw new InvalidOperationException("Failed to find an enemy entity (Team=2) in entry map.");
            return (hero, enemy);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }
    }
}
