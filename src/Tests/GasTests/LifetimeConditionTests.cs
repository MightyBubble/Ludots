using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    public class LifetimeConditionTests
    {
        private readonly TagOps _tagOps = new TagOps();

        [Test]
        public void TagSense_Effective_RespectsDisabledIf()
        {
            var world = World.Create();
            try
            {
                var entity = world.Create();
                world.Add(entity, new GameplayTagContainer());
                world.Add(entity, new TagCountContainer());

                int tagA = 1;
                int tagDisableA = 2;

                var ruleSetA = new TagRuleSet();
                unsafe
                {
                    ruleSetA.DisabledIfTags[0] = tagDisableA;
                    ruleSetA.DisabledIfCount = 1;
                }
                _tagOps.ClearRuleRegistry();
                _tagOps.RegisterTagRuleSet(tagA, ruleSetA);

                ref var tags = ref world.Get<GameplayTagContainer>(entity);
                ref var counts = ref world.Get<TagCountContainer>(entity);

                _tagOps.AddTag(ref tags, ref counts, tagDisableA);
                _tagOps.AddTag(ref tags, ref counts, tagA);

                That(_tagOps.HasTag(ref tags, tagA, TagSense.Present), Is.True);
                That(_tagOps.HasTag(ref tags, tagA, TagSense.Effective), Is.False);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EffectLifetime_ExpireCondition_TagPresent_ExpiresWhenMissing()
        {
            var world = World.Create();
            try
            {
                _tagOps.ClearRuleRegistry();

                var clock = new DiscreteClock();
                var clocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();
                var requests = new EffectRequestQueue();
                var lifetime = new EffectLifetimeSystem(world, clock, conditions, requests);

                int keepAliveTag = 3;
                var keepAlive = conditions.Register(new GasCondition(GasConditionKind.TagPresent, keepAliveTag, TagSense.Present));

                var source = world.Create();
                var target = world.Create();
                world.Add(target, new GameplayTagContainer());
                world.Add(target, new TagCountContainer());
                world.Add(target, new ActiveEffectContainer());

                ref var tags = ref world.Get<GameplayTagContainer>(target);
                ref var counts = ref world.Get<TagCountContainer>(target);
                _tagOps.AddTag(ref tags, ref counts, keepAliveTag);

                var effect = GameplayEffectFactory.CreateEffect(world, rootId: 1, source, target, durationTicks: 0, lifetimeKind: EffectLifetimeKind.Infinite, periodTicks: 0, targetContext: default, clockId: GasClockId.FixedFrame, expireCondition: keepAlive);
                ref var ge = ref world.Get<GameplayEffect>(effect);
                ge.State = EffectState.Committed;
                ref var container = ref world.Get<ActiveEffectContainer>(target);
                container.Add(effect);

                clocks.AdvanceFixedFrame();
                clocks.AdvanceStep();
                lifetime.Update(0.016f);
                That(world.IsAlive(effect), Is.True);

                _tagOps.RemoveTag(ref tags, ref counts, keepAliveTag);

                clocks.AdvanceFixedFrame();
                clocks.AdvanceStep();
                lifetime.Update(0.016f);
                That(world.IsAlive(effect), Is.False);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EffectLifetime_CancelRequested_RemovesInfiniteEffect()
        {
            using var world = World.Create();
            var clock = new DiscreteClock();
            var conditions = new GasConditionRegistry();
            var lifetime = new EffectLifetimeSystem(world, clock, conditions);

            var source = world.Create();
            var target = world.Create(new ActiveEffectContainer());
            var effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 0,
                lifetimeKind: EffectLifetimeKind.Infinite,
                periodTicks: 0,
                targetContext: default,
                clockId: GasClockId.FixedFrame);
            world.Get<GameplayEffect>(effect).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(effect);

            world.Get<GameplayEffect>(effect).CancelRequested = true;
            lifetime.Update(0.016f);

            That(world.IsAlive(effect), Is.False);
            That(world.Get<ActiveEffectContainer>(target).Count, Is.EqualTo(0));
        }

        [Test]
        public void EffectLifetime_OnPeriodPostGraph_ModifiesTargetAttributeCurrent()
        {
            using var world = World.Create();

            int durabilityId = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register("Durability");

            var target = world.Create(new AttributeBuffer(), new ActiveEffectContainer());
            ref var targetAttributes = ref world.Get<AttributeBuffer>(target);
            targetAttributes.SetBase(durabilityId, 100f);
            targetAttributes.SetCurrent(durabilityId, 100f);

            var source = world.Create();
            var clock = new DiscreteClock();
            var conditions = new GasConditionRegistry();
            var templates = new EffectTemplateRegistry();
            var programs = new GraphProgramRegistry();
            var presetTypes = new PresetTypeRegistry();
            var builtinHandlers = new BuiltinHandlerRegistry();
            var graphApi = new GasGraphRuntimeApi(world);
            var executor = new EffectPhaseExecutor(
                programs,
                presetTypes,
                builtinHandlers,
                new GasGraphOpHandlerTable(),
                templates);
            var lifetime = new EffectLifetimeSystem(
                world,
                clock,
                conditions,
                templates: templates,
                phaseExecutor: executor,
                graphApi: graphApi);
            var aggregator = new AttributeAggregatorSystem(world);

            const int graphId = 9001;
            const int templateId = 701;
            programs.Register(graphId, new[]
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTarget, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ConstFloat, Dst = 1, ImmF = -7f },
                new GraphInstruction { Op = (ushort)GraphNodeOp.ModifyAttributeAdd, A = 0, B = 1, Imm = durabilityId },
            });

            var bindings = new EffectPhaseGraphBindings();
            That(bindings.TryAddStep(EffectPhaseId.OnPeriod, PhaseSlot.Post, graphId), Is.True);
            templates.Register(templateId, new EffectTemplateData
            {
                TagId = 0,
                PresetType = EffectPresetType.None,
                LifetimeKind = EffectLifetimeKind.Infinite,
                ClockId = GasClockId.FixedFrame,
                DurationTicks = 0,
                PeriodTicks = 2,
                PhaseGraphBindings = bindings,
            });

            var effect = GameplayEffectFactory.CreateEffect(
                world,
                rootId: 1,
                source,
                target,
                durationTicks: 0,
                lifetimeKind: EffectLifetimeKind.Infinite,
                periodTicks: 2,
                targetContext: target,
                clockId: GasClockId.FixedFrame);
            world.Add(effect, new EffectTemplateRef { TemplateId = templateId });
            world.Get<GameplayEffect>(effect).State = EffectState.Committed;
            world.Get<ActiveEffectContainer>(target).Add(effect);

            clock.Advance(ClockDomainId.FixedFrame);
            lifetime.Update(0.016f);
            That(world.Get<AttributeBuffer>(target).GetCurrent(durabilityId), Is.EqualTo(100f));
            aggregator.Update(0.016f);
            int firstPeriodTick = world.Get<GameplayEffect>(effect).NextTickAtTick;
            That(firstPeriodTick, Is.InRange(2, 3));
            That(world.Get<AttributeBuffer>(target).GetCurrent(durabilityId), Is.EqualTo(100f));

            clock.Advance(ClockDomainId.FixedFrame);
            lifetime.Update(0.016f);
            float afterSecondTick = world.Get<AttributeBuffer>(target).GetCurrent(durabilityId);
            if (firstPeriodTick == 2)
            {
                That(afterSecondTick, Is.EqualTo(93f).Within(0.001f));
            }
            else
            {
                That(afterSecondTick, Is.EqualTo(100f));
            }
            aggregator.Update(0.016f);
            That(world.Get<GameplayEffect>(effect).NextTickAtTick, Is.EqualTo(firstPeriodTick == 2 ? 4 : 3));
            That(world.Get<AttributeBuffer>(target).GetCurrent(durabilityId), Is.EqualTo(afterSecondTick).Within(0.001f));

            clock.Advance(ClockDomainId.FixedFrame);
            lifetime.Update(0.016f);
            float expectedAfterThirdTick = firstPeriodTick == 2 ? 93f : 93f;
            That(world.Get<AttributeBuffer>(target).GetCurrent(durabilityId), Is.EqualTo(expectedAfterThirdTick).Within(0.001f));
            aggregator.Update(0.016f);

            That(world.Get<GameplayEffect>(effect).NextTickAtTick, Is.EqualTo(firstPeriodTick == 2 ? 4 : 5));
            That(world.Get<AttributeBuffer>(target).GetCurrent(durabilityId), Is.EqualTo(expectedAfterThirdTick).Within(0.001f));
        }
    }
}
