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
                abilityDefinitions: definitions,
                tagOps: new TagOps())
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
                abilityDefinitions: definitions,
                tagOps: new TagOps());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                system.UpdateSlice(0f, int.MaxValue))!;

            Assert.That(ex.Message, Does.Contain("GAS.ABILITY_EXEC.ERR.SnapshotCapacityExceeded"));
            Assert.That(ex.Message, Does.Contain("required=6"));
        }

        [Test]
        public void AbilityExec_TagSignalWithoutInstalledTagState_FailsBeforeStructuralChange()
        {
            using var world = World.Create();
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.TagSignal, tick: 0, tagId: 17);
            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(1, new AbilityDefinition { ExecSpec = spec });

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(1);
            Entity actor = world.Create(
                abilities,
                new AbilityExecInstance
                {
                    AbilitySlot = 0,
                    AbilityId = 1,
                    State = AbilityExecRunState.Running,
                    ActiveClockId = GasClockId.Step,
                });

            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                snapshotCapacity: 4,
                abilityDefinitions: definitions,
                tagOps: new TagOps());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                system.UpdateSlice(0f, int.MaxValue))!;

            Assert.That(ex.Message, Is.EqualTo(TagOps.MissingGameplayTagContainerError));
            Assert.That(world.Has<GameplayTagContainer>(actor), Is.False);
            Assert.That(world.Has<TagCountContainer>(actor), Is.False);
            Assert.That(world.Has<DirtyFlags>(actor), Is.False);
            Assert.That(world.Has<TimedTagBuffer>(actor), Is.False);
        }

        [Test]
        public void AbilityExec_SixteenthTimedTagClip_CommitsTagAndExpirationTogether()
        {
            using var world = World.Create();
            const int abilityId = 2;
            const int acceptedTagId = 45;
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(
                0,
                ExecItemKind.TagClip,
                tick: 0,
                durationTicks: 30,
                clockId: GasClockId.Step,
                tagId: acceptedTagId);
            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition { ExecSpec = spec });

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(abilityId);
            TimedTagBuffer timed = default;
            for (int i = 0; i < TimedTagBuffer.Capacity - 1; i++)
            {
                Assert.That(timed.TryAdd(tagId: 100 + i, expireAt: 500 + i, clockId: GasClockId.Step), Is.True);
            }

            Entity actor = world.Create(
                abilities,
                new AbilityExecInstance
                {
                    AbilitySlot = 0,
                    AbilityId = abilityId,
                    State = AbilityExecRunState.Running,
                    ActiveClockId = GasClockId.Step,
                },
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags(),
                timed);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                snapshotCapacity: 4,
                abilityDefinitions: definitions,
                tagOps: new TagOps());

            system.UpdateSlice(0f, int.MaxValue);

            Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(acceptedTagId), Is.True);
            Assert.That(world.Get<TagCountContainer>(actor).GetCount(acceptedTagId), Is.EqualTo(1));
            Assert.That(world.Get<DirtyFlags>(actor).IsTagDirty(acceptedTagId), Is.True);
            ref TimedTagBuffer actualTimed = ref world.Get<TimedTagBuffer>(actor);
            Assert.That(actualTimed.Count, Is.EqualTo(TimedTagBuffer.Capacity));
            Assert.That(actualTimed.GetTagId(TimedTagBuffer.Capacity - 1), Is.EqualTo(acceptedTagId));
            Assert.That(actualTimed.GetExpireAt(TimedTagBuffer.Capacity - 1), Is.EqualTo(30));
            Assert.That(actualTimed.GetClockId(TimedTagBuffer.Capacity - 1), Is.EqualTo(GasClockId.Step));
        }

        [Test]
        public void AbilityExec_SeventeenthTimedTagClip_FailsBeforeAnyTagStateChanges()
        {
            using var world = World.Create();
            const int abilityId = 2;
            const int rejectedTagId = 46;
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(
                0,
                ExecItemKind.TagClip,
                tick: 0,
                durationTicks: 30,
                clockId: GasClockId.Step,
                tagId: rejectedTagId);
            var definitions = new AbilityDefinitionRegistry();
            definitions.Register(abilityId, new AbilityDefinition { ExecSpec = spec });

            AbilityStateBuffer abilities = default;
            abilities.AddAbility(abilityId);
            TimedTagBuffer timed = default;
            for (int i = 0; i < TimedTagBuffer.Capacity; i++)
            {
                Assert.That(timed.TryAdd(tagId: 100 + i, expireAt: 500 + i, clockId: GasClockId.Step), Is.True);
            }

            Entity actor = world.Create(
                abilities,
                new AbilityExecInstance
                {
                    AbilitySlot = 0,
                    AbilityId = abilityId,
                    State = AbilityExecRunState.Running,
                    ActiveClockId = GasClockId.Step,
                },
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags(),
                timed);
            var system = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                new EffectRequestQueue(),
                snapshotCapacity: 4,
                abilityDefinitions: definitions,
                tagOps: new TagOps());

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                system.UpdateSlice(0f, int.MaxValue))!;

            Assert.That(ex.Message, Is.EqualTo(AbilityExecSystem.TimedTagCapacityExceededError));
            Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(rejectedTagId), Is.False);
            Assert.That(world.Get<TagCountContainer>(actor).GetCount(rejectedTagId), Is.EqualTo(0));
            Assert.That(world.Get<DirtyFlags>(actor).IsTagDirty(rejectedTagId), Is.False);
            ref TimedTagBuffer actualTimed = ref world.Get<TimedTagBuffer>(actor);
            Assert.That(actualTimed.Count, Is.EqualTo(TimedTagBuffer.Capacity));
            for (int i = 0; i < TimedTagBuffer.Capacity; i++)
            {
                Assert.That(actualTimed.GetTagId(i), Is.EqualTo(100 + i));
                Assert.That(actualTimed.GetExpireAt(i), Is.EqualTo(500 + i));
                Assert.That(actualTimed.GetClockId(i), Is.EqualTo(GasClockId.Step));
            }
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

        [Test]
        public void EffectProcessingLoop_AllStagesShareOneDeterministicWorkBudget()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var requests = new EffectRequestQueue();
            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(0, ModifierOp.Add, -1f);
            templates.Register(1, new EffectTemplateData
            {
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.FixedFrame,
                DurationTicks = 10,
                ParticipatesInResponse = false,
                Modifiers = modifiers,
            });

            Entity source = world.Create();
            Entity target = world.Create(new AttributeBuffer(), new ActiveEffectContainer(), new DirtyFlags());
            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = source,
                Target = target,
                TemplateId = 1,
            });

            Entity expired = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 2,
                source,
                target,
                durationTicks: 0,
                lifetimeKind: EffectLifetimeKind.After);
            world.Get<GameplayEffect>(expired).State = EffectState.Committed;
            Assert.That(world.Get<ActiveEffectContainer>(target).Add(expired), Is.True);

            var loop = new EffectProcessingLoopSystem(
                world,
                requests,
                clock,
                new GasConditionRegistry(),
                lifetimeSnapshotCapacity: 16,
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                maxWorkUnitsPerSlice: 4);

            bool completed;
            bool sawProposal = false;
            bool sawApplication = false;
            bool sawLifetime = false;
            int slices = 0;
            do
            {
                completed = loop.UpdateSlice(0f, int.MaxValue);
                slices++;
                sawProposal |= loop.ProposalProcessedLastSlice > 0;
                sawApplication |= loop.ApplicationProcessedLastSlice > 0;
                sawLifetime |= loop.LifetimeProcessedLastSlice > 0;
                Assert.That(loop.ProcessedLastSlice, Is.EqualTo(
                    loop.ProposalProcessedLastSlice +
                    loop.ApplicationProcessedLastSlice +
                    loop.LifetimeProcessedLastSlice));
                Assert.That(loop.ProcessedLastSlice, Is.LessThanOrEqualTo(loop.MaxWorkUnitsPerSlice));
                Assert.That(slices, Is.LessThan(32));
            } while (!completed);

            Assert.That(slices, Is.GreaterThan(1));
            Assert.That(sawProposal, Is.True);
            Assert.That(sawApplication, Is.True);
            Assert.That(sawLifetime, Is.True);
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
                DeferredTriggerActiveEntityCapacity = 64,
                AbilityExecMaxWorkUnitsPerSlice = 32,
                EffectProcessingMaxWorkUnitsPerSlice = 32,
            };
        }
    }
}
