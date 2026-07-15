using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Quests;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class QuestRuntimeTests
    {
        [Test]
        public void StartQuestCreatesIndexedQuestEntity()
        {
            using World world = World.Create();
            var definitions = CreateDefinitions();
            var runtime = new QuestRuntimeService(world, definitions);

            Entity questEntity = runtime.StartQuest("trial");

            Assert.That(questEntity, Is.Not.EqualTo(Entity.Null));
            Assert.That(runtime.TryResolveQuestEntity("trial", out Entity resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(questEntity));
            Assert.That(world.Has<QuestInstanceCm>(questEntity), Is.True);
            Assert.That(world.Has<AttributeBuffer>(questEntity), Is.True);
            Assert.That(world.Has<GameplayTagContainer>(questEntity), Is.True);
            Assert.That(world.Has<ActiveEffectContainer>(questEntity), Is.True);
            Assert.That(runtime.TryGetQuestState("trial", out QuestState state, out string stageId), Is.True);
            Assert.That(state, Is.EqualTo(QuestState.Active));
            Assert.That(stageId, Is.EqualTo("start"));
        }

        [Test]
        public void StartQuestCreatesScopedIndexedQuestEntity()
        {
            using World world = World.Create();
            var definitions = CreateDefinitions();
            var runtime = new QuestRuntimeService(world, definitions);
            Entity scopeHost = world.Create();

            Entity questEntity = runtime.StartQuest("trial", scopeHost);

            Assert.That(runtime.TryResolveQuestEntity("trial", scopeHost, out Entity resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(questEntity));
            Assert.That(runtime.TryResolveQuestEntity("trial", out _), Is.False);
            Assert.That(world.Get<QuestInstanceCm>(questEntity).ScopeHost, Is.EqualTo(scopeHost));
        }

        [Test]
        public void EmitSignalAdvancesSignalGatedStage()
        {
            using World world = World.Create();
            var runtime = new QuestRuntimeService(world, CreateDefinitions());

            runtime.StartQuest("trial");
            runtime.AdvanceQuestStage("trial", "done");

            runtime.EmitSignal("closed");

            Assert.That(runtime.TryGetQuestState("trial", out QuestState state, out string stageId), Is.True);
            Assert.That(state, Is.EqualTo(QuestState.Completed));
            Assert.That(stageId, Is.EqualTo("done"));
        }

        [Test]
        public void QuestEntityAttributesReceiveGasBuffs()
        {
            using World world = World.Create();
            var definitions = CreateDefinitions();
            var runtime = new QuestRuntimeService(world, definitions);
            int urgencyId = AttributeRegistry.GetId("QuestUrgency");
            int effectTagId = EnsureTag("Effect.Test.QuestUrgencyBuff");

            Entity questEntity = runtime.StartQuest("trial");
            Assert.That(runtime.TryResolveQuestEntity("trial", out questEntity), Is.True);
            Assert.That(world.Get<AttributeBuffer>(questEntity).GetCurrent(urgencyId), Is.EqualTo(1f));

            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(urgencyId, ModifierOp.Add, 2f);
            templates.Register(2201, new EffectTemplateData
            {
                TagId = effectTagId,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.Step,
                DurationTicks = 10,
                PeriodTicks = 0,
                Modifiers = modifiers,
            });

            var requests = new EffectRequestQueue();
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: tagOps);
            var application = new EffectApplicationSystem(world, requests, templates: templates, tagOps: tagOps);
            var aggregator = new AttributeAggregatorSystem(world, tagOps: tagOps);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = Entity.Null,
                Target = questEntity,
                TargetContext = Entity.Null,
                TemplateId = 2201,
            });

            proposal.Update(0f);
            application.Update(0f);
            aggregator.Update(0f);

            Assert.That(world.Get<AttributeBuffer>(questEntity).GetCurrent(urgencyId), Is.EqualTo(3f));
        }

        [Test]
        public void QuestCommandsFailFastForInvalidContentIds()
        {
            using World world = World.Create();
            var runtime = new QuestRuntimeService(world, CreateDefinitions());

            Assert.Throws<InvalidOperationException>(() => runtime.StartQuest("missing"));
            Assert.Throws<ArgumentException>(() => runtime.EmitSignal(""));

            runtime.StartQuest("trial");

            Assert.Throws<InvalidOperationException>(() => runtime.AdvanceQuestStage("trial", "missing-stage"));
            Assert.Throws<InvalidOperationException>(() => runtime.AdvanceQuestStage("missing"));
            Assert.Throws<InvalidOperationException>(() => runtime.CompleteQuest("missing"));
            Assert.Throws<InvalidOperationException>(() => runtime.FailQuest("missing"));
        }

        [Test]
        public void RebuildIndexFailsFastForDuplicateQuestProjection()
        {
            using World world = World.Create();
            var definitions = CreateDefinitions();
            int definitionId = definitions.GetId("trial");
            world.Create(new QuestInstanceCm
            {
                DefinitionId = definitionId,
                State = QuestState.Active,
                StageIndex = 0,
            });
            world.Create(new QuestInstanceCm
            {
                DefinitionId = definitionId,
                State = QuestState.Active,
                StageIndex = 0,
            });

            Assert.Throws<InvalidOperationException>(() => new QuestRuntimeService(world, definitions));
        }

        private static QuestDefinitionRegistry CreateDefinitions()
        {
            var definitions = new QuestDefinitionRegistry();
            definitions.Register("trial", new QuestDefinition
            {
                DisplayName = "Trial",
                Summary = "Core quest runtime test.",
                Tags = { "quest.test" },
                Attributes =
                {
                    new QuestAttributeDefinition
                    {
                        AttributeId = "QuestUrgency",
                        BaseValue = 1f
                    }
                },
                Stages =
                {
                    new QuestStageDefinition { Id = "start", Title = "Start" },
                    new QuestStageDefinition
                    {
                        Id = "done",
                        Title = "Done",
                        RequiredSignals = { "closed" }
                    }
                }
            });
            return definitions;
        }

        private static int EnsureTag(string tag)
        {
            int id = TagRegistry.GetId(tag);
            return id != TagRegistry.InvalidId ? id : TagRegistry.Register(tag);
        }
    }
}
