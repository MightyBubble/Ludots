using Arch.Buffer;
using Arch.Core;
using Arch.System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.RuntimeBudget
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
        public void GasRuntimeCapacity_RequiresPositiveOrderAdmissionResultCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.OrderAdmissionResultCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("orderAdmissionResultCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresAdmissionResultsForGlobalAndEntityIntake()
        {
            var config = CreateValidRuntimeCapacity();
            config.OrderQueueCapacity = 64;
            config.OrderAdmissionResultCapacity = 64;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("orderAdmissionResultCapacity"));
            Assert.That(ex.Message, Does.Contain("orderQueueCapacity * 2"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresPositiveEffectFanOutCommandCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.EffectFanOutCommandCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("effectFanOutCommandCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresPositiveOrderAdmissionRejectionCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.OrderAdmissionRejectionCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("orderAdmissionRejectionCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresAdmissionRejectionsForFullQueuedBatch()
        {
            var config = CreateValidRuntimeCapacity();
            config.OrderQueueCapacity = 64;
            config.OrderAdmissionRejectionCapacity = 63;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("orderAdmissionRejectionCapacity"));
            Assert.That(ex.Message, Does.Contain("orderQueueCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresPositiveOrderQueueCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.OrderQueueCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("orderQueueCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresPositiveResponseChainOrderQueueCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.ResponseChainOrderQueueCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("responseChainOrderQueueCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresPositiveProjectileCollisionCandidateCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.ProjectileCollisionCandidateCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("projectileCollisionCandidateCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresPositiveProjectileRuntimeEntityCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.ProjectileRuntimeEntityCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("projectileRuntimeEntityCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresPositiveGraphOutputValueCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.GraphOutputValueCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("graphOutputValueCapacity"));
        }

        [Test]
        public void GasRuntimeCapacity_RequiresPositiveEffectPhaseGraphProgramScratchCapacity()
        {
            var config = CreateValidRuntimeCapacity();
            config.EffectPhaseGraphProgramScratchCapacity = 0;

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(config.Validate)!;
            Assert.That(ex.Message, Does.Contain("effectPhaseGraphProgramScratchCapacity"));
        }

        [Test]
        public void DefaultGameConfig_GasRuntimeCapacity_ValidatesAdmissionResultHeadroom()
        {
            string repoRoot = FindRepoRoot();
            string configPath = Path.Combine(repoRoot, "assets", "Configs", "game.json");
            string json = File.ReadAllText(configPath);
            GameConfig config = JsonSerializer.Deserialize<GameConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            Assert.DoesNotThrow(() => config.GasRuntimeCapacity.Validate());
            Assert.That(
                config.GasRuntimeCapacity.OrderAdmissionResultCapacity,
                Is.GreaterThanOrEqualTo(config.GasRuntimeCapacity.OrderQueueCapacity * 2));
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
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()))
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
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

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
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

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
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

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
                tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()));

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
                snapshotCapacity: 32,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME)
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
        public void EffectLifetime_ResetSlice_DoesNotDrainRemainingGameplayWork()
        {
            using var world = World.Create();
            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer());
            ref ActiveEffectContainer container = ref world.Get<ActiveEffectContainer>(target);
            for (int i = 0; i < 2; i++)
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
                new DiscreteClock(),
                new GasConditionRegistry(),
                snapshotCapacity: 4,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME)
            {
                MaxWorkUnitsPerSlice = 1,
            };

            Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);
            int aliveBeforeReset = world.CountEntities(in EffectQuery);

            system.ResetSlice();

            Assert.That(aliveBeforeReset, Is.EqualTo(2));
            Assert.That(world.CountEntities(in EffectQuery), Is.EqualTo(aliveBeforeReset));
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(2));
            Assert.That(system.MaxWorkUnitsPerSlice, Is.EqualTo(1));
        }

        [Test]
        public void EffectLifetime_ResetSlice_RestoresScannedEffectStateAndExternalBuffers()
        {
            const int grantedTagId = 41;
            const int templateId = 73;

            using var world = World.Create();
            var dirtyEntities = new DirtyEntityQueue(capacity: 4);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry());
            var presentationEvents = new GasPresentationEventBuffer(capacity: 4);
            Entity source = world.Create();
            Entity target = world.Create(
                new ActiveEffectContainer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags());
            Assert.That(tagOps.AddTag(world, target, grantedTagId), Is.True);
            Assert.That(tagOps.AddTag(world, target, grantedTagId), Is.True);
            dirtyEntities.TryDequeue(out _);
            world.Get<DirtyFlags>(target).ClearTagDirty(grantedTagId);
            world.Get<DirtyFlags>(target).DeferredTriggerQueued = 0;

            var grantedTags = new EffectGrantedTags();
            Assert.That(grantedTags.Add(new TagContribution
            {
                TagId = grantedTagId,
                Formula = TagContributionFormula.Fixed,
                Amount = 1,
            }), Is.True);

            ref ActiveEffectContainer container = ref world.Get<ActiveEffectContainer>(target);
            for (int i = 0; i < 2; i++)
            {
                Entity effect = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: i + 1,
                    source,
                    target,
                    durationTicks: 0,
                    lifetimeKind: EffectLifetimeKind.After);
                world.Get<GameplayEffect>(effect).State = EffectState.Committed;
                world.Add(effect, new EffectTemplateRef { TemplateId = templateId });
                world.Add(effect, grantedTags);
                Assert.That(container.Add(effect), Is.True);
            }

            var system = new EffectLifetimeSystem(
                world,
                new DiscreteClock(),
                new GasConditionRegistry(),
                snapshotCapacity: 4,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                tagOps: tagOps,
                presentationEvents: presentationEvents)
            {
                MaxWorkUnitsPerSlice = 1,
            };

            Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);
            system.ResetSlice();

            Assert.That(world.CountEntities(in EffectQuery), Is.EqualTo(2));
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(2));
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(grantedTagId), Is.True);
            Assert.That(world.Get<TagCountContainer>(target).GetCount(grantedTagId), Is.EqualTo(2));
            Assert.That(world.Get<DirtyFlags>(target).IsAnyTagDirty(), Is.False);
            Assert.That(dirtyEntities.Count, Is.Zero);
            Assert.That(presentationEvents.Count, Is.Zero);
        }

        [Test]
        public void EffectLifetime_IncompleteSlice_DoesNotExposeCleanupBeforeCommit()
        {
            const int grantedTagId = 42;
            const int templateId = 74;

            using var world = World.Create();
            var dirtyEntities = new DirtyEntityQueue(capacity: 4);
            var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry());
            var presentationEvents = new GasPresentationEventBuffer(capacity: 4);
            Entity source = world.Create();
            Entity target = world.Create(
                new ActiveEffectContainer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags());
            Assert.That(tagOps.AddTag(world, target, grantedTagId), Is.True);
            Assert.That(tagOps.AddTag(world, target, grantedTagId), Is.True);
            dirtyEntities.TryDequeue(out _);
            world.Get<DirtyFlags>(target).ClearTagDirty(grantedTagId);
            world.Get<DirtyFlags>(target).DeferredTriggerQueued = 0;

            var grantedTags = new EffectGrantedTags();
            Assert.That(grantedTags.Add(new TagContribution
            {
                TagId = grantedTagId,
                Formula = TagContributionFormula.Fixed,
                Amount = 1,
            }), Is.True);

            ref ActiveEffectContainer container = ref world.Get<ActiveEffectContainer>(target);
            for (int i = 0; i < 2; i++)
            {
                Entity effect = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: i + 1,
                    source,
                    target,
                    durationTicks: 0,
                    lifetimeKind: EffectLifetimeKind.After);
                world.Get<GameplayEffect>(effect).State = EffectState.Committed;
                world.Add(effect, new EffectTemplateRef { TemplateId = templateId });
                world.Add(effect, grantedTags);
                Assert.That(container.Add(effect), Is.True);
            }

            var system = new EffectLifetimeSystem(
                world,
                new DiscreteClock(),
                new GasConditionRegistry(),
                snapshotCapacity: 4,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                tagOps: tagOps,
                presentationEvents: presentationEvents)
            {
                MaxWorkUnitsPerSlice = 1,
            };

            Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);

            Assert.That(presentationEvents.Count, Is.Zero);
            Assert.That(world.CountEntities(in EffectQuery), Is.EqualTo(2));
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(2));
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(grantedTagId), Is.True);
            Assert.That(world.Get<TagCountContainer>(target).GetCount(grantedTagId), Is.EqualTo(2));
            Assert.That(dirtyEntities.Count, Is.Zero);

            int slices = 1;
            while (!system.UpdateSlice(0f, int.MaxValue))
            {
                slices++;
                Assert.That(slices, Is.LessThan(16));
            }

            Assert.That(slices, Is.GreaterThan(1));
            Assert.That(world.CountEntities(in EffectQuery), Is.Zero);
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.Zero);
            Assert.That(world.Get<GameplayTagContainer>(target).HasTag(grantedTagId), Is.False);
            Assert.That(world.Get<TagCountContainer>(target).GetCount(grantedTagId), Is.Zero);
            Assert.That(dirtyEntities.Count, Is.EqualTo(1));
            Assert.That(CountGasPresentationEvents(presentationEvents, GasPresentationEventKind.EffectExpired), Is.EqualTo(2));
        }

        [Test]
        public void EffectLifetime_ResetSlice_AfterExternalCommit_CompletesCommittedCleanup()
        {
            using var world = World.Create();
            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer());
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 0,
                lifetimeKind: EffectLifetimeKind.After);
            ref GameplayEffect gameplayEffect = ref world.Get<GameplayEffect>(effect);
            gameplayEffect.State = EffectState.Committed;
            gameplayEffect.AggregatesModifiers = true;
            Assert.That(world.Get<ActiveEffectContainer>(target).Add(effect), Is.True);

            var system = new EffectLifetimeSystem(
                world,
                new DiscreteClock(),
                new GasConditionRegistry(),
                snapshotCapacity: 4,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME)
            {
                MaxWorkUnitsPerSlice = 2,
            };

            Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);
            Assert.DoesNotThrow(system.ResetSlice);

            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.Zero);
            Assert.That(world.Has<AttributeAggregateDirty>(target), Is.True);
        }

        [Test]
        public void RealtimePacemaker_BudgetFuse_AfterLifetimeCommit_ResetsWithoutPartialCleanup()
        {
            float fixedDeltaBefore = Time.FixedDeltaTime;
            try
            {
                Time.FixedDeltaTime = 0.02f;
                using var world = World.Create();
                var requests = new EffectRequestQueue();
                Entity source = world.Create();
                Entity target = world.Create(new ActiveEffectContainer());
                Entity effect = GameplayEffectFactory.CreateEffect(
                    world,
                    rootId: 1,
                    source,
                    target,
                    durationTicks: 0,
                    lifetimeKind: EffectLifetimeKind.After);
                ref GameplayEffect gameplayEffect = ref world.Get<GameplayEffect>(effect);
                gameplayEffect.State = EffectState.Committed;
                gameplayEffect.AggregatesModifiers = true;
                Assert.That(world.Get<ActiveEffectContainer>(target).Add(effect), Is.True);

                using var loop = new EffectProcessingLoopSystem(
                    world,
                    requests,
                    new DiscreteClock(),
                    new GasConditionRegistry(),
                    lifetimeSnapshotCapacity: 4,
                    fanOutCommandCapacity: 4,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    maxWorkUnitsPerSlice: 2);
                var systems = new Dictionary<SystemGroup, List<ISystem<float>>>
                {
                    [SystemGroup.EffectProcessing] = new List<ISystem<float>> { loop },
                };
                var simulation = new PhaseOrderedCooperativeSimulation(systems);
                var pacemaker = new RealtimePacemaker();
                pacemaker.Reset();

                Assert.DoesNotThrow(() => pacemaker.Update(
                    Time.FixedDeltaTime,
                    simulation,
                    timeBudgetMs: 1_000,
                    maxSlicesPerLogicFrame: 1));

                Assert.That(pacemaker.IsBudgetFused, Is.True);
                Assert.That(world.IsAlive(effect), Is.False);
                Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.Zero);
                Assert.That(world.Has<AttributeAggregateDirty>(target), Is.True);
            }
            finally
            {
                Time.FixedDeltaTime = fixedDeltaBefore;
            }
        }

        [Test]
        public void GameplayEffectFactory_CommandBufferRejectsInstantEntityMaterialization()
        {
            using var commandBuffer = new CommandBuffer();

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                GameplayEffectFactory.CreateEffect(
                    commandBuffer,
                    rootId: 1,
                    Entity.Null,
                    Entity.Null,
                    durationTicks: 0,
                    lifetimeKind: EffectLifetimeKind.Instant))!;

            Assert.That(error.Message, Does.StartWith("GAS.INSTANT.ERR.EntityMaterializationForbidden"));
        }

        [Test]
        public void EffectApplication_ResetSlice_RollsBackPendingPersistentAttachment()
        {
            using var world = World.Create();
            Entity source = world.Create();
            Entity target = world.Create(new ActiveEffectContainer());
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 30,
                lifetimeKind: EffectLifetimeKind.After);
            var system = new EffectApplicationSystem(world, GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, new DiscreteClock())
            {
                MaxWorkUnitsPerSlice = 1,
            };

            Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(1));

            system.ResetSlice();

            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Get<ActiveEffectContainer>(target).Count, Is.Zero);
        }

        [Test]
        public void EffectApplication_ResetSlice_DestroysPendingUnattachedPersistentEffect()
        {
            using var world = World.Create();
            Entity source = world.Create();
            Entity target = world.Create();
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 30,
                lifetimeKind: EffectLifetimeKind.After);
            var system = new EffectApplicationSystem(world, GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, new DiscreteClock())
            {
                MaxWorkUnitsPerSlice = 1,
            };

            Assert.That(system.UpdateSlice(0f, int.MaxValue), Is.False);
            Assert.That(world.Has<ActiveEffectContainer>(target), Is.False);

            system.ResetSlice();

            Assert.That(world.IsAlive(effect), Is.False);
            Assert.That(world.Has<ActiveEffectContainer>(target), Is.False);
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
                snapshotCapacity: 5,
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME);

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
                fanOutCommandCapacity: GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
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

        [Test]
        public void EffectProcessingLoop_ExactProposalBudgetBoundary_DoesNotOverrunWhenClosingWindow()
        {
            const int workBudget = 4096;
            const int ordinaryRequestCount = 1364;
            const int ordinaryTemplateId = 1;
            const int respondingTemplateId = 2;
            const int respondingTagId = 20;

            using var world = World.Create();
            var requests = new EffectRequestQueue(initialCapacity: workBudget);
            var templates = new EffectTemplateRegistry();
            templates.Register(ordinaryTemplateId, new EffectTemplateData
            {
                LifetimeKind = EffectLifetimeKind.Instant,
                ParticipatesInResponse = false,
            });
            templates.Register(respondingTemplateId, new EffectTemplateData
            {
                TagId = respondingTagId,
                LifetimeKind = EffectLifetimeKind.Instant,
                ParticipatesInResponse = true,
            });

            var listener = default(ResponseChainListener);
            Assert.That(listener.Add(respondingTagId, ResponseType.Modify, priority: 2, modifyValue: 1f), Is.True);
            Assert.That(listener.Add(respondingTagId, ResponseType.Modify, priority: 1, modifyValue: 1f), Is.True);
            world.Create(listener);

            Entity source = world.Create();
            Entity target = world.Create();
            for (int i = 0; i < ordinaryRequestCount; i++)
            {
                requests.Publish(new EffectRequest
                {
                    RootId = i + 1,
                    Source = source,
                    Target = target,
                    TemplateId = ordinaryTemplateId,
                });
            }
            requests.Publish(new EffectRequest
            {
                RootId = ordinaryRequestCount + 1,
                Source = source,
                Target = target,
                TemplateId = respondingTemplateId,
            });

            var loop = new EffectProcessingLoopSystem(
                world,
                requests,
                new DiscreteClock(),
                new GasConditionRegistry(),
                lifetimeSnapshotCapacity: 16,
                fanOutCommandCapacity: workBudget,
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                maxWorkUnitsPerSlice: workBudget);

            Assert.That(loop.UpdateSlice(0f, int.MaxValue), Is.False);
            Assert.That(loop.ProposalProcessedLastSlice, Is.EqualTo(workBudget));
            Assert.That(loop.ProcessedLastSlice, Is.EqualTo(workBudget));
            Assert.That(requests.Count, Is.EqualTo(ordinaryRequestCount + 1));

            Assert.That(loop.UpdateSlice(0f, int.MaxValue), Is.True);
            Assert.That(loop.ProcessedLastSlice, Is.LessThanOrEqualTo(workBudget));
            Assert.That(requests.Count, Is.Zero);
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
                EffectFanOutCommandCapacity = 64,
                OrderQueueCapacity = 64,
                ResponseChainOrderQueueCapacity = 64,
                OrderAdmissionResultCapacity = 128,
                OrderAdmissionRejectionCapacity = 64,
                OrderTerminalResultCapacity = 64,
                DeferredTriggerActiveEntityCapacity = 64,
                ProjectileCollisionCandidateCapacity = 64,
                ProjectileRuntimeEntityCapacity = 64,
                EffectPhaseGraphProgramScratchCapacity = 64,
                GraphOutputValueCapacity = 64,
                AbilityExecMaxWorkUnitsPerSlice = 32,
                EffectProcessingMaxWorkUnitsPerSlice = 32,
            };
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string gitPath = Path.Combine(directory.FullName, ".git");
                string defaultConfigPath = Path.Combine(directory.FullName, "assets", "Configs", "game.json");
                if ((File.Exists(gitPath) || Directory.Exists(gitPath)) &&
                    File.Exists(defaultConfigPath) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test directory.");
        }

        private static int CountGasPresentationEvents(
            GasPresentationEventBuffer events,
            GasPresentationEventKind kind)
        {
            int count = 0;
            ReadOnlySpan<GasPresentationEvent> span = events.Events;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Kind == kind)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
