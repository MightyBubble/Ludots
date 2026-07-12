using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class GasExecutionBudgetTests
    {
        private static readonly QueryDescription AbilityExecQuery = new QueryDescription()
            .WithAll<AbilityExecInstance>();

        private static readonly QueryDescription EffectQuery = new QueryDescription()
            .WithAll<GameplayEffect>();

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void GasRuntimeCapacity_RejectsNonFiniteAbilityWorkBudget(int invalidBudget)
        {
            var config = CreateValidRuntimeCapacity();
            config.AbilityExecMaxWorkUnitsPerSlice = invalidBudget;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("abilityExecMaxWorkUnitsPerSlice"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void GasRuntimeCapacity_RejectsNonFiniteEffectWorkBudget(int invalidBudget)
        {
            var config = CreateValidRuntimeCapacity();
            config.EffectProcessingMaxWorkUnitsPerSlice = invalidBudget;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("effectProcessingMaxWorkUnitsPerSlice"));
        }

        [Test]
        public void AbilityExec_AdvancesMoreThanTwoThousandEntitiesAcrossSlices()
        {
            using var world = World.Create();
            const int actorCount = 2_500;
            var definitions = CreateImmediateAbilityDefinitions();
            for (int i = 0; i < actorCount; i++)
            {
                AbilityStateBuffer abilities = default;
                abilities.AddAbility(1);
                world.Create(
                    abilities,
                    new AbilityExecInstance
                    {
                        AbilitySlot = 0,
                        AbilityId = 1,
                        State = AbilityExecRunState.Running,
                        ActiveClockId = GasClockId.Step,
                    });
            }

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                snapshotCapacity: 3_000,
                abilityDefinitions: definitions)
            {
                MaxWorkUnitsPerSlice = 127,
            };

            int slices = 0;
            while (!system.UpdateSlice(0f, int.MaxValue))
            {
                slices++;
                Assert.That(system.DeferredEntityCount, Is.GreaterThanOrEqualTo(0));
                Assert.That(slices, Is.LessThan(100));
            }

            Assert.That(slices, Is.GreaterThan(1));
            Assert.That(world.CountEntities(in AbilityExecQuery), Is.EqualTo(0));
        }

        [Test]
        public void AbilityExec_RejectsSnapshotOverflowExplicitly()
        {
            using var world = World.Create();
            var definitions = CreateImmediateAbilityDefinitions();
            for (int i = 0; i < 6; i++)
            {
                AbilityStateBuffer abilities = default;
                abilities.AddAbility(1);
                world.Create(abilities, new AbilityExecInstance { AbilitySlot = 0, AbilityId = 1 });
            }

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                snapshotCapacity: 5,
                abilityDefinitions: definitions);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                system.UpdateSlice(0f, int.MaxValue))!;

            Assert.That(ex.Message, Does.Contain("GAS.ABILITY_EXEC.ERR.SnapshotCapacityExceeded"));
            Assert.That(ex.Message, Does.Contain("required=6"));
        }

        [Test]
        public void EffectLifetime_ResumesExpiredEffectsAcrossWorkSlices()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer());
            ref ActiveEffectContainer container = ref world.Get<ActiveEffectContainer>(target);
            for (int i = 0; i < 10; i++)
            {
                Entity effect = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: i + 1,
                    source,
                    target,
                    durationTicks: 0,
                    lifetimeKind: EffectLifetimeKind.After);
                world.Get<GameplayEffect>(effect).State = EffectState.Committed;
                Assert.That(container.Add(effect), Is.True);
            }

            var system = new EffectLifetimeSystem(
                world,
                clock,
                new GasConditionRegistry(),
                snapshotCapacity: 32)
            {
                MaxWorkUnitsPerSlice = 3,
            };

            Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);
            Assert.That(system.DeferredEntityCount, Is.EqualTo(7));

            int slices = 1;
            while (!system.UpdateSlice(0f, int.MaxValue))
            {
                slices++;
                Assert.That(slices, Is.LessThan(30));
            }

            Assert.That(slices, Is.GreaterThan(1));
            Assert.That(world.CountEntities(in EffectQuery), Is.EqualTo(0));
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(0));
        }

        [Test]
        public void EffectLifetime_RejectsSnapshotOverflowExplicitly()
        {
            using var world = World.Create();
            Entity source = world.Create();
            Entity target = world.Create();
            for (int i = 0; i < 6; i++)
            {
                GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: i + 1,
                    source,
                    target,
                    durationTicks: 10,
                    lifetimeKind: EffectLifetimeKind.After);
            }

            var system = new EffectLifetimeSystem(
                world,
                new DiscreteClock(),
                new GasConditionRegistry(),
                snapshotCapacity: 5);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                system.UpdateSlice(0f, int.MaxValue))!;

            Assert.That(ex.Message, Does.Contain("GAS.EFFECT_LIFETIME.ERR.SnapshotCapacityExceeded"));
            Assert.That(ex.Message, Does.Contain("required=6"));
        }

        private static AbilityDefinitionRegistry CreateImmediateAbilityDefinitions()
        {
            AbilityExecSpec spec = default;
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.End, tick: 0);
            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(1, new AbilityDefinition { ExecSpec = spec });
            return definitions;
        }

        private static GasRuntimeCapacityConfig CreateValidRuntimeCapacity()
        {
            return new GasRuntimeCapacityConfig
            {
                AbilityExecSnapshotCapacity = 64,
                EffectLifetimeSnapshotCapacity = 64,
                OrderTerminalResultCapacity = 64,
                AbilityExecMaxWorkUnitsPerSlice = 32,
                EffectProcessingMaxWorkUnitsPerSlice = 32,
            };
        }
    }
}
