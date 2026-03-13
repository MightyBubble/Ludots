using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class SelfBuffTests
    {
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void B1_BasicBuff_AttributeModifierApplied()
        {
            using var scenario = StartScenario();
            B1Snapshot active = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "buff_active", StringComparison.OrdinalIgnoreCase));

            Assert.That(active.AttackDamage, Is.EqualTo(150f).Within(0.001f));
            Assert.That(active.Mana, Is.EqualTo(100f).Within(0.001f), "B1 showcase validates the mana gate only; the successful cast path does not deduct mana.");
            Assert.That(active.EmpoweredCount, Is.EqualTo(1));
            Assert.That(active.HasEmpoweredTag, Is.True);
        }

        [Test]
        public void B1_BuffExpiry_ModifierRemoved()
        {
            using var scenario = StartScenario();
            TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "buff_active", StringComparison.OrdinalIgnoreCase));
            B1Snapshot expired = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "buff_expired", StringComparison.OrdinalIgnoreCase));

            Assert.That(expired.AttackDamage, Is.EqualTo(100f).Within(0.001f));
            Assert.That(expired.EmpoweredCount, Is.EqualTo(0));
            Assert.That(expired.HasEmpoweredTag, Is.False);
        }

        [Test]
        public void B1_Silenced_ActivationBlocked()
        {
            using var scenario = StartScenario();
            B1Snapshot silencedBlocked = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "silenced_blocked", StringComparison.OrdinalIgnoreCase));

            Assert.That(silencedBlocked.LastCastFailReason, Is.EqualTo("BlockedByTag"));
            Assert.That(silencedBlocked.AttackDamage, Is.EqualTo(100f).Within(0.001f));
            Assert.That(silencedBlocked.EmpoweredCount, Is.EqualTo(0));
        }

        [Test]
        public void B1_Stunned_ActivationBlocked()
        {
            using var scenario = StartScenario();
            Entity hero = FindEntity(scenario.Engine.World, InteractionShowcaseIds.HeroName);
            Assert.That(hero, Is.Not.EqualTo(Entity.Null), "Expected showcase hero to be present.");
            Assert.That(
                scenario.Engine.GameSession.CurrentTick,
                Is.LessThan(5),
                "Manual stunned cast must execute before autoplay submits its own showcase cast.");

            ApplyTag(scenario.Engine, hero, "Status.Stunned");
            SubmitSelfCast(scenario.Engine, hero);
            TickFrames(scenario.Engine, 3);

            B1Snapshot stunnedBlocked = Sample(scenario.Engine);
            Assert.That(stunnedBlocked.LastCastFailReason, Is.EqualTo("BlockedByTag"));
            Assert.That(stunnedBlocked.AttackDamage, Is.EqualTo(100f).Within(0.001f));
            Assert.That(stunnedBlocked.Mana, Is.EqualTo(100f).Within(0.001f));
            Assert.That(stunnedBlocked.EmpoweredCount, Is.EqualTo(0));
            Assert.That(stunnedBlocked.HasEmpoweredTag, Is.False);
        }

        [Test]
        public void B1_InsufficientMana_ActivationFailed()
        {
            using var scenario = StartScenario();
            B1Snapshot insufficientManaBlocked = TickUntil(
                scenario.Engine,
                snapshot => string.Equals(snapshot.Stage, "insufficient_mana_blocked", StringComparison.OrdinalIgnoreCase));

            Assert.That(insufficientManaBlocked.LastCastFailReason, Is.EqualTo("InsufficientResource"));
            Assert.That(insufficientManaBlocked.LastCastFailAttribute, Is.EqualTo("Mana"));
            Assert.That(insufficientManaBlocked.LastCastFailDelta, Is.EqualTo(50f).Within(0.001f));
            Assert.That(insufficientManaBlocked.Mana, Is.EqualTo(0f).Within(0.001f));
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
            engine.LoadMap(InteractionShowcaseIds.B1SelfBuffMapId);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Map load should not emit trigger errors.");
            return new ScenarioRuntime(engine);
        }

        private static void SubmitSelfCast(GameEngine engine, Entity hero)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue);
            Assert.That(orderQueue, Is.Not.Null, "Expected order queue service to be available.");

            int castAbilityOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["castAbility"];
            bool enqueued = orderQueue!.TryEnqueue(new Order
            {
                OrderTypeId = castAbilityOrderTypeId,
                Actor = hero,
                Target = hero,
                Args = new OrderArgs
                {
                    I0 = InteractionShowcaseIds.B1SelfBuffSlot
                }
            });

            Assert.That(enqueued, Is.True, "Expected manual self-cast test order to be enqueued.");
        }

        private static void ApplyTag(GameEngine engine, Entity entity, string tagName)
        {
            Assert.That(engine.GetService(CoreServiceKeys.TagOps), Is.TypeOf<TagOps>(), "Expected TagOps service to be available.");
            var tagOps = (TagOps)engine.GetService(CoreServiceKeys.TagOps)!;
            int tagId = TagRegistry.Register(tagName);

            if (!engine.World.Has<GameplayTagContainer>(entity))
            {
                engine.World.Add(entity, new GameplayTagContainer());
            }

            if (!engine.World.Has<TagCountContainer>(entity))
            {
                engine.World.Add(entity, new TagCountContainer());
            }

            if (!engine.World.Has<DirtyFlags>(entity))
            {
                engine.World.Add(entity, new DirtyFlags());
            }

            ref var tags = ref engine.World.Get<GameplayTagContainer>(entity);
            ref var counts = ref engine.World.Get<TagCountContainer>(entity);
            ref var dirtyFlags = ref engine.World.Get<DirtyFlags>(entity);
            tagOps.AddTag(ref tags, ref counts, tagId, ref dirtyFlags);
        }

        private static B1Snapshot TickUntil(GameEngine engine, Func<B1Snapshot, bool> predicate, int maxTicks = 960)
        {
            for (int tick = 0; tick <= maxTicks; tick++)
            {
                B1Snapshot snapshot = Sample(engine);
                if (predicate(snapshot))
                {
                    return snapshot;
                }

                engine.Tick(DeltaTime);
            }

            Assert.Fail($"Scenario did not reach the expected state within {maxTicks} ticks.");
            return default;
        }

        private static void TickFrames(GameEngine engine, int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                engine.Tick(DeltaTime);
            }
        }

        private static B1Snapshot Sample(GameEngine engine)
        {
            Entity hero = FindEntity(engine.World, InteractionShowcaseIds.HeroName);
            float attackDamage = 0f;
            float mana = 0f;
            bool empoweredTag = false;
            int empoweredCount = 0;

            if (hero != Entity.Null && engine.World.IsAlive(hero))
            {
                int attackDamageId = AttributeRegistry.Register("AttackDamage");
                int manaId = AttributeRegistry.Register("Mana");
                int empoweredTagId = TagRegistry.Register("Status.Empowered");

                if (engine.World.Has<AttributeBuffer>(hero))
                {
                    ref var attributes = ref engine.World.Get<AttributeBuffer>(hero);
                    attackDamage = attributes.GetCurrent(attackDamageId);
                    mana = attributes.GetCurrent(manaId);
                }

                if (engine.World.Has<GameplayTagContainer>(hero) && engine.GetService(CoreServiceKeys.TagOps) is TagOps tagOps)
                {
                    ref var tags = ref engine.World.Get<GameplayTagContainer>(hero);
                    empoweredTag = tagOps.HasTag(ref tags, empoweredTagId, TagSense.Effective);
                }

                if (engine.World.Has<TagCountContainer>(hero))
                {
                    empoweredCount = engine.World.Get<TagCountContainer>(hero).GetCount(empoweredTagId);
                }
            }

            return new B1Snapshot(
                Tick: engine.GameSession.CurrentTick,
                Stage: ReadString(engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
                AttackDamage: attackDamage,
                Mana: mana,
                HasEmpoweredTag: empoweredTag,
                EmpoweredCount: empoweredCount,
                LastCastFailReason: ReadString(engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty),
                LastCastFailAttribute: ReadString(engine, InteractionShowcaseRuntimeKeys.LastCastFailAttribute, string.Empty),
                LastCastFailDelta: ReadFloat(engine, InteractionShowcaseRuntimeKeys.LastCastFailDelta, 0f));
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

        private static string ReadString(GameEngine engine, string key, string fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is string text
                ? text
                : fallback;
        }

        private static float ReadFloat(GameEngine engine, string key, float fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is float number
                ? number
                : fallback;
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

        private readonly record struct B1Snapshot(
            int Tick,
            string Stage,
            float AttackDamage,
            float Mana,
            bool HasEmpoweredTag,
            int EmpoweredCount,
            string LastCastFailReason,
            string LastCastFailAttribute,
            float LastCastFailDelta);

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
