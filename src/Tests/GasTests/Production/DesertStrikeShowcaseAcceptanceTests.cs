using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using NUnit.Framework;
using DesertStrikeShowcaseMod.Runtime;
using DesertStrikeShowcaseMod.Triggers;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class DesertStrikeShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;

        private static readonly string[] ModIds =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "EntityCommandPanelMod",
            "RtsDemoMod",
            "DesertStrikeShowcaseMod",
        };

        [Test]
        public void DesertStrike_PurchaseDeductsAndSpawnsOnNextWave()
        {
            using var engine = CreateEngine();
            LoadMap(engine);
            World world = engine.World;
            DesertStrikeState state = GetState(engine);
            Entity playerBase = FindEntity(world, "Command Center P1");
            int mineralsId = EnsureAttribute("Minerals");

            Assert.That(world.Get<AttributeBuffer>(playerBase).GetCurrent(mineralsId), Is.EqualTo(600f).Within(0.01f));

            SubmitPurchase(engine, playerBase, slot: 0);
            Tick(engine, 10);
            Assert.That(
                world.Get<AttributeBuffer>(playerBase).GetCurrent(mineralsId),
                Is.EqualTo(525f).Within(0.01f),
                "marine purchase should deduct 75 minerals");

            SubmitPurchase(engine, playerBase, slot: 2);
            Tick(engine, 10);
            Assert.That(
                world.Get<AttributeBuffer>(playerBase).GetCurrent(mineralsId),
                Is.EqualTo(225f).Within(0.01f),
                "goliath purchase should deduct 300 minerals");

            TickUntilFixedFrame(engine, 1850, 20000, "first wave should fire by fixed frame 1850");
            TickUntil(engine, () => CountEntitiesByName(world, "Goliath") >= 1, 2000, "purchased goliath should spawn");

            Assert.That(CountEntitiesByName(world, "Marine"), Is.GreaterThanOrEqualTo(12), "starter marines on both sides");
            Assert.That(state.UnitsSpawned, Is.GreaterThanOrEqualTo(13));
            Assert.That(state.PlayerQueue.Count, Is.EqualTo(0), "wave spawns consume the purchase queue");
            WriteArtifacts(engine, state, "purchase_and_spawn");
        }

        [Test]
        public void DesertStrike_UnitsMarchAndClash()
        {
            using var engine = CreateEngine();
            LoadMap(engine);
            World world = engine.World;
            DesertStrikeState state = GetState(engine);

            TickUntilFixedFrame(engine, 1850, 20000, "first wave should fire by fixed frame 1850");
            TickUntil(engine, () => state.UnitsSpawned >= 12, 2000, "wave units should exist");

            Entity playerMarine = FindUnitByName(world, "Marine", team: 1);
            int startX = world.Get<WorldPositionCm>(playerMarine).Value.ToWorldCmInt2().X;
            TickUntilFixedFrame(engine, 2100, 4000, "units should march for a while");
            if (world.IsAlive(playerMarine))
            {
                int laterX = world.Get<WorldPositionCm>(playerMarine).Value.ToWorldCmInt2().X;
                Assert.That(laterX, Is.GreaterThan(startX + 300), "player marine should march toward the enemy base");
            }

            TickUntilFixedFrame(engine, 4200, 30000, "opposing waves should clash and kill each other");
            Assert.That(state.UnitsDestroyed, Is.GreaterThanOrEqualTo(1));
            WriteArtifacts(engine, state, "march_and_clash");
        }

        [Test]
        public void DesertStrike_PurchaseRejectedWithoutMinerals()
        {
            using var engine = CreateEngine();
            LoadMap(engine);
            World world = engine.World;
            DesertStrikeState state = GetState(engine);
            Entity playerBase = FindEntity(world, "Command Center P1");
            int mineralsId = EnsureAttribute("Minerals");

            world.Get<AttributeBuffer>(playerBase).SetCurrent(mineralsId, 30f);
            int queueBefore = state.PlayerQueue.Count;
            SubmitPurchase(engine, playerBase, slot: 3);
            Tick(engine, 5);

            Assert.That(world.Get<AttributeBuffer>(playerBase).GetCurrent(mineralsId), Is.EqualTo(30f).Within(0.01f));
            Assert.That(state.PurchaseDeniedCount, Is.EqualTo(1));
            Assert.That(state.PlayerQueue.Count, Is.EqualTo(queueBefore), "rejected purchase must not enqueue");
            WriteArtifacts(engine, state, "rejected_purchase");
        }

        [Test]
        public void DesertStrike_BaseDestructionEndsGame()
        {
            using var engine = CreateEngine();
            LoadMap(engine);
            World world = engine.World;
            DesertStrikeState state = GetState(engine);
            Entity aiBase = FindEntity(world, "Command Center P2");
            int healthId = EnsureAttribute("Health");

            world.Get<AttributeBuffer>(aiBase).SetCurrent(healthId, 0f);
            Tick(engine, 3);

            Assert.That(world.IsAlive(aiBase), Is.False, "destroyed base entity should be removed");
            Assert.That(state.GameOver, Is.True);
            Assert.That(state.WinnerPlayerId, Is.EqualTo(1));
            Assert.That(state.DestroyedBaseTeam, Is.EqualTo(2));
            WriteArtifacts(engine, state, "base_destruction");
        }

        [Test]
        public void DesertStrike_HudPanel_ActivatesAndProjectsValues()
        {
            using var engine = CreateEngine();
            LoadMap(engine);
            DesertStrikeState state = GetState(engine);

            var hudRuntime = engine.GlobalContext[InstallDesertStrikeOnGameStartTrigger.HudPanelRuntimeKey] as DesertStrikeHudPanelRuntime
                ?? throw new InvalidOperationException("DesertStrike HUD panel runtime missing from GlobalContext.");

            Assert.That(hudRuntime.ActivationStore.IsVisible(DesertStrikeHudPanelRuntime.PanelType), Is.True, "HUD panel should be shown on map load");

            Tick(engine, 10);

            var outputs = engine.GetService(CoreServiceKeys.GraphOutputValueStore);
            Entity seatRep = Ludots.Core.Client.ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            Assert.That(outputs.TryGet(seatRep, "desert_strike.hud.minerals", out _), Is.True, "HUD values should project through GraphOutputValueStore");
            Assert.That(outputs.TryGet(seatRep, "desert_strike.hud.winner", out _), Is.True);
            WriteArtifacts(engine, state, "hud_panel");
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, ModIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            AcceptanceUiHostInstaller.Install(engine);
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine)
        {
            engine.LoadMap("desert_strike");
            Tick(engine, 5, new List<double>());
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        }

        private static DesertStrikeState GetState(GameEngine engine)
        {
            return engine.GlobalContext[InstallDesertStrikeOnGameStartTrigger.StateKey] as DesertStrikeState
                   ?? throw new InvalidOperationException("DesertStrike state missing from GlobalContext.");
        }

        private static void SubmitPurchase(GameEngine engine, Entity actor, int slot)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"],
                PlayerId = 1,
                Actor = actor,
                Target = actor,
                Args = new OrderArgs { I0 = slot },
                SubmitMode = OrderSubmitMode.Immediate,
            });

            Assert.That(enqueued, Is.True);
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
            for (int i = 0; i < frames; i++)
            {
                if (stepPolicy.Mode == GasStepMode.Manual)
                {
                    stepPolicy.RequestStep(1);
                }

                var stopwatch = Stopwatch.StartNew();
                engine.Tick(DeltaTime);
                stopwatch.Stop();
                frameTimesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private static void Tick(GameEngine engine, int frames) => Tick(engine, frames, new List<double>());

        private static void TickUntil(GameEngine engine, Func<bool> condition, int maxFrames, string because)
        {
            var frameTimesMs = new List<double>();
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(condition(), Is.True, $"{because} (fixedFrame={engine.GetService(CoreServiceKeys.Clock).Now(ClockDomainId.FixedFrame)})");
        }

        private static void TickUntilFixedFrame(GameEngine engine, int targetFixedFrame, int maxTicks, string because)
        {
            var clock = engine.GetService(CoreServiceKeys.Clock);
            var frameTimesMs = new List<double>();
            for (int i = 0; i < maxTicks; i++)
            {
                if (clock.Now(ClockDomainId.FixedFrame) >= targetFixedFrame)
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(clock.Now(ClockDomainId.FixedFrame) >= targetFixedFrame, Is.True, $"{because} (fixedFrame={clock.Now(ClockDomainId.FixedFrame)}, maxTicks={maxTicks})");
        }

        private static int EnsureAttribute(string attributeName)
        {
            int id = AttributeRegistry.GetId(attributeName);
            return id > 0 ? id : AttributeRegistry.Register(attributeName);
        }

        private static Entity FindEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Missing entity '{entityName}'.");
            }

            return result;
        }

        private static Entity FindUnitByName(World world, string entityName, int team)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name, Team, WorldPositionCm>();
            world.Query(in query, (Entity entity, ref Name name, ref Team entityTeam, ref WorldPositionCm _) =>
            {
                if (result == Entity.Null &&
                    entityTeam.Id == team &&
                    string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Missing '{entityName}' on team {team}.");
            }

            return result;
        }

        private static int CountEntitiesByName(World world, string entityName)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity _, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });

            return count;
        }

        private static void WriteArtifacts(GameEngine engine, DesertStrikeState state, string scenario)
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "desert-strike-showcase");
            Directory.CreateDirectory(artifactDir);

            var timeline = new StringBuilder();
            timeline.AppendLine("# Desert Strike acceptance timeline");
            timeline.AppendLine();
            timeline.AppendLine($"- scenario: {scenario}");
            timeline.AppendLine($"- wave: {state.WaveNumber}");
            timeline.AppendLine($"- unitsSpawned: {state.UnitsSpawned}");
            timeline.AppendLine($"- unitsDestroyed: {state.UnitsDestroyed}");
            timeline.AppendLine($"- gameOver: {state.GameOver}");
            timeline.AppendLine($"- winnerPlayerId: {state.WinnerPlayerId}");
            timeline.AppendLine($"- purchaseDenied: {state.PurchaseDeniedCount}");
            File.AppendAllText(
                Path.Combine(artifactDir, "trace.jsonl"),
                $"{{\"scenario\":\"{scenario}\",\"wave\":{state.WaveNumber},\"spawned\":{state.UnitsSpawned},\"destroyed\":{state.UnitsDestroyed},\"gameOver\":{(state.GameOver ? "true" : "false")},\"winner\":{state.WinnerPlayerId}}}\n",
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, $"battle-report-{scenario}.md"), timeline.ToString(), Encoding.UTF8);
        }

        private static string FindRepoRoot()
        {
            string current = AppContext.BaseDirectory;
            var directory = new DirectoryInfo(current);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "launcher.config.json")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Repository root not found (launcher.config.json).");
        }
    }
}
