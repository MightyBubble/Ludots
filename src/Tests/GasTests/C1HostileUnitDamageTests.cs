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
    public sealed class C1HostileUnitDamageTests
    {
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void C1_BasicDamage_MitigatedHealthApplied()
        {
            using var scenario = StartScenario();
            C1Snapshot applied = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "damage_applied", StringComparison.OrdinalIgnoreCase));

            Assert.That(applied.HeroBaseDamage, Is.EqualTo(200f).Within(0.001f));
            Assert.That(applied.PrimaryTargetHealth, Is.EqualTo(300f).Within(0.001f));
            Assert.That(applied.PrimaryTargetArmor, Is.EqualTo(50f).Within(0.001f));
            Assert.That(applied.DamageAmount, Is.EqualTo(300f).Within(0.001f));
            Assert.That(applied.FinalDamage, Is.EqualTo(200f).Within(0.001f));
            Assert.That(applied.DamageApplied, Is.True);
            Assert.That(applied.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C1PrimaryTargetName));
        }

        [Test]
        public void C1_InvalidTarget_HealthUnchangedAndReasonReported()
        {
            using var scenario = StartScenario();
            C1Snapshot invalidBlocked = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "invalid_target_blocked", StringComparison.OrdinalIgnoreCase));

            Assert.That(invalidBlocked.LastCastFailReason, Is.EqualTo("InvalidTarget"));
            Assert.That(invalidBlocked.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C1InvalidTargetName));
            Assert.That(invalidBlocked.InvalidTargetHealth, Is.EqualTo(0f).Within(0.001f));
            Assert.That(invalidBlocked.PrimaryTargetHealth, Is.EqualTo(300f).Within(0.001f));
            Assert.That(invalidBlocked.DamageAmount, Is.EqualTo(300f).Within(0.001f));
            Assert.That(invalidBlocked.FinalDamage, Is.EqualTo(200f).Within(0.001f));
        }

        [Test]
        public void C1_OutOfRange_HealthUnchangedAndReasonReported()
        {
            using var scenario = StartScenario();
            C1Snapshot outOfRangeBlocked = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "out_of_range_blocked", StringComparison.OrdinalIgnoreCase));

            Assert.That(outOfRangeBlocked.LastCastFailReason, Is.EqualTo("OutOfRange"));
            Assert.That(outOfRangeBlocked.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C1FarTargetName));
            Assert.That(outOfRangeBlocked.FarTargetHealth, Is.EqualTo(500f).Within(0.001f));
            Assert.That(outOfRangeBlocked.PrimaryTargetHealth, Is.EqualTo(300f).Within(0.001f));
            Assert.That(outOfRangeBlocked.DamageAmount, Is.EqualTo(300f).Within(0.001f));
            Assert.That(outOfRangeBlocked.FinalDamage, Is.EqualTo(200f).Within(0.001f));
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
            engine.LoadMap(InteractionShowcaseIds.C1HostileUnitDamageMapId);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Map load should not emit trigger errors.");
            return new ScenarioRuntime(engine);
        }

        private static C1Snapshot TickUntil(GameEngine engine, Func<C1Snapshot, bool> predicate, int maxTicks = 480)
        {
            for (int tick = 0; tick <= maxTicks; tick++)
            {
                C1Snapshot snapshot = Sample(engine);
                if (predicate(snapshot))
                {
                    return snapshot;
                }

                engine.Tick(DeltaTime);
            }

            Assert.Fail($"Scenario did not reach the expected state within {maxTicks} ticks.");
            return default;
        }

        private static C1Snapshot Sample(GameEngine engine)
        {
            Entity hero = FindEntity(engine.World, InteractionShowcaseIds.HeroName);
            Entity primary = FindEntity(engine.World, InteractionShowcaseIds.C1PrimaryTargetName);
            Entity invalid = FindEntity(engine.World, InteractionShowcaseIds.C1InvalidTargetName);
            Entity far = FindEntity(engine.World, InteractionShowcaseIds.C1FarTargetName);

            int baseDamageId = AttributeRegistry.Register("BaseDamage");
            int healthId = AttributeRegistry.Register("Health");
            int armorId = AttributeRegistry.Register("Armor");
            int manaId = AttributeRegistry.Register("Mana");
            int damageAmountKeyId = ConfigKeyRegistry.Register("Interaction.C1.DamageAmount");
            int finalDamageKeyId = ConfigKeyRegistry.Register("Interaction.C1.FinalDamage");

            return new C1Snapshot(
                Tick: engine.GameSession.CurrentTick,
                Stage: ReadString(engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
                HeroBaseDamage: ReadAttribute(engine.World, hero, baseDamageId),
                Mana: ReadAttribute(engine.World, hero, manaId),
                PrimaryTargetHealth: ReadAttribute(engine.World, primary, healthId),
                PrimaryTargetArmor: ReadAttribute(engine.World, primary, armorId),
                InvalidTargetHealth: ReadAttribute(engine.World, invalid, healthId),
                FarTargetHealth: ReadAttribute(engine.World, far, healthId),
                DamageAmount: ReadBlackboardFloat(engine.World, primary, damageAmountKeyId),
                FinalDamage: ReadBlackboardFloat(engine.World, primary, finalDamageKeyId),
                DamageApplied: ReadBool(engine, InteractionShowcaseRuntimeKeys.DamageApplied),
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

        private static float ReadBlackboardFloat(World world, Entity entity, int keyId)
        {
            if (entity == Entity.Null || !world.IsAlive(entity) || !world.Has<BlackboardFloatBuffer>(entity))
            {
                return 0f;
            }

            ref var buffer = ref world.Get<BlackboardFloatBuffer>(entity);
            return buffer.TryGet(keyId, out float value) ? value : 0f;
        }

        private static string ReadString(GameEngine engine, string key, string fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is string text
                ? text
                : fallback;
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

        private readonly record struct C1Snapshot(
            int Tick,
            string Stage,
            float HeroBaseDamage,
            float Mana,
            float PrimaryTargetHealth,
            float PrimaryTargetArmor,
            float InvalidTargetHealth,
            float FarTargetHealth,
            float DamageAmount,
            float FinalDamage,
            bool DamageApplied,
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
