using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class C2FriendlyUnitHealTests
    {
        private const float DeltaTime = 1f / 60f;
        private static readonly int HealthAttributeId = AttributeRegistry.Register("Health");
        private static readonly int ManaAttributeId = AttributeRegistry.Register("Mana");

        [Test]
        public void C2_BasicHeal_FriendlyTargetRestored()
        {
            using var scenario = StartScenario();
            C2Snapshot submitted = TickUntil(
                scenario.Engine,
                snapshot => snapshot.CastSubmitted &&
                    string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2AllyTargetName, StringComparison.OrdinalIgnoreCase));
            C2Snapshot applied = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "heal_applied", StringComparison.OrdinalIgnoreCase));

            Assert.That(submitted.CastSubmitted, Is.True);
            Assert.That(submitted.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C2AllyTargetName));
            Assert.That(applied.Mana, Is.EqualTo(100f).Within(0.001f));
            Assert.That(applied.AllyTargetHealth, Is.EqualTo(350f).Within(0.001f));
            Assert.That(applied.HostileTargetHealth, Is.EqualTo(400f).Within(0.001f));
            Assert.That(applied.DeadAllyTargetHealth, Is.EqualTo(0f).Within(0.001f));
            Assert.That(applied.HealAmount, Is.EqualTo(150f).Within(0.001f));
            Assert.That(applied.HealApplied, Is.True);
            Assert.That(applied.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C2AllyTargetName));
        }

        [Test]
        public void C2_HostileTarget_ShowcaseLocalGuard_HealthUnchangedAndReasonReported()
        {
            using var scenario = StartScenario();
            C2Snapshot hostileBlocked = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "hostile_target_blocked", StringComparison.OrdinalIgnoreCase));

            Assert.That(hostileBlocked.LastCastFailReason, Is.EqualTo("InvalidTarget"));
            Assert.That(hostileBlocked.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C2HostileTargetName));
            Assert.That(hostileBlocked.AllyTargetHealth, Is.EqualTo(350f).Within(0.001f));
            Assert.That(hostileBlocked.HostileTargetHealth, Is.EqualTo(400f).Within(0.001f));
            Assert.That(hostileBlocked.HealAmount, Is.EqualTo(150f).Within(0.001f));
        }

        [Test]
        public void C2_DeadAlly_ShowcaseLocalGuard_HealthUnchangedAndReasonReported()
        {
            using var scenario = StartScenario();
            C2Snapshot deadAllyBlocked = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "dead_ally_blocked", StringComparison.OrdinalIgnoreCase));

            Assert.That(deadAllyBlocked.LastCastFailReason, Is.EqualTo("InvalidTarget"));
            Assert.That(deadAllyBlocked.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C2DeadAllyTargetName));
            Assert.That(deadAllyBlocked.AllyTargetHealth, Is.EqualTo(350f).Within(0.001f));
            Assert.That(deadAllyBlocked.HostileTargetHealth, Is.EqualTo(400f).Within(0.001f));
            Assert.That(deadAllyBlocked.DeadAllyTargetHealth, Is.EqualTo(0f).Within(0.001f));
            Assert.That(deadAllyBlocked.HealAmount, Is.EqualTo(150f).Within(0.001f));
        }

        private static ScenarioRuntime StartScenario()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var engine = new GameEngine();

            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, new[]
            {
                "LudotsCoreMod",
                "ArpgDemoMod",
                "InteractionShowcaseMod"
            });

            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallDummyInput(engine);
            engine.Start();
            engine.LoadMap(InteractionShowcaseIds.C2FriendlyUnitHealMapId);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Map load should not emit trigger errors.");
            return new ScenarioRuntime(engine);
        }

        private static C2Snapshot TickUntil(GameEngine engine, Func<C2Snapshot, bool> predicate, int maxTicks = 480)
        {
            for (int tick = 0; tick <= maxTicks; tick++)
            {
                C2Snapshot snapshot = Sample(engine);
                if (predicate(snapshot))
                {
                    return snapshot;
                }

                engine.Tick(DeltaTime);
            }

            Assert.Fail($"Scenario did not reach the expected state within {maxTicks} ticks.");
            return default;
        }

        private static C2Snapshot Sample(GameEngine engine)
        {
            Entity ally = FindEntity(engine.World, InteractionShowcaseIds.C2AllyTargetName);
            Entity hostile = FindEntity(engine.World, InteractionShowcaseIds.C2HostileTargetName);
            Entity deadAlly = FindEntity(engine.World, InteractionShowcaseIds.C2DeadAllyTargetName);
            Entity hero = FindEntity(engine.World, InteractionShowcaseIds.HeroName);

            return new C2Snapshot(
                Tick: engine.GameSession.CurrentTick,
                Stage: ReadString(engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
                Mana: ReadAttribute(engine.World, hero, ManaAttributeId),
                AllyTargetHealth: ReadAttribute(engine.World, ally, HealthAttributeId),
                HostileTargetHealth: ReadAttribute(engine.World, hostile, HealthAttributeId),
                DeadAllyTargetHealth: ReadAttribute(engine.World, deadAlly, HealthAttributeId),
                HealAmount: ReadFloat(engine, InteractionShowcaseRuntimeKeys.C2HealAmount),
                HealApplied: ReadBool(engine, InteractionShowcaseRuntimeKeys.C2HealApplied),
                CastSubmitted: ReadBool(engine, InteractionShowcaseRuntimeKeys.CastSubmitted),
                LastAttemptTargetName: ReadString(engine, InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty),
                LastCastFailReason: ReadString(engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty));
        }

        private static Entity FindEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result != Entity.Null)
                {
                    return;
                }

                if (world.IsAlive(entity) && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static float ReadAttribute(World world, Entity entity, int attributeId)
        {
            if (entity == Entity.Null || !world.IsAlive(entity) || !world.Has<AttributeBuffer>(entity))
            {
                return 0f;
            }

            return world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
        }

        private static string ReadString(GameEngine engine, string key, string fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is string text
                ? text
                : fallback;
        }

        private static float ReadFloat(GameEngine engine, string key)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is float number
                ? number
                : 0f;
        }

        private static bool ReadBool(GameEngine engine, string key)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is bool flag && flag;
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repo root.");
        }

        private readonly record struct C2Snapshot(
            int Tick,
            string Stage,
            float Mana,
            float AllyTargetHealth,
            float HostileTargetHealth,
            float DeadAllyTargetHealth,
            float HealAmount,
            bool HealApplied,
            bool CastSubmitted,
            string LastAttemptTargetName,
            string LastCastFailReason);

        private sealed class ScenarioRuntime : IDisposable
        {
            public ScenarioRuntime(GameEngine engine)
            {
                Engine = engine;
            }

            public GameEngine Engine { get; }

            public void Dispose()
            {
                Engine.Dispose();
            }
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable)
            {
            }

            public void SetIMECandidatePosition(int x, int y)
            {
            }

            public string GetCharBuffer() => string.Empty;
        }
    }
}
