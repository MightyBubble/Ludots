using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Presentation.Components;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class EffectRequestTargetLifecycleTests
    {
        private const int BuffTemplateId = 2411;

        [Test]
        public void Request_TargetPendingPresentationDestroy_IsDroppedBeforeEffectCreation()
        {
            int attributeId = EnsureAttribute("Tests.EffectRequestTargetLifecycle.Value");
            using var world = World.Create();
            Entity live = world.Create(new AttributeBuffer(), new DirtyFlags());
            Entity doomed = world.Create(new AttributeBuffer(), new DirtyFlags(), new PresentationDestroyPending());

            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(attributeId, ModifierOp.Add, 1f);
            templates.Register(BuffTemplateId, new EffectTemplateData
            {
                CategoryId = EffectCategoryRegistry.Register("Effect.Test.EffectRequestTargetLifecycle"),
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.Step,
                DurationTicks = 10,
                PeriodTicks = 0,
                Modifiers = modifiers,
            });
            FinalizeBuffTemplates(templates);

            var requests = new EffectRequestQueue();
            var aggregateDirty = new AttributeAggregateDirtyRegistry();
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry(), aggregateDirty: aggregateDirty);
            var proposal = new EffectProposalProcessingSystem(
                world,
                requests,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new DiscreteClock(),
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                tagOps: tagOps,
                aggregateDirty: aggregateDirty);
            var application = new EffectApplicationSystem(world, GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME, new DiscreteClock(), requests, templates: templates, tagOps: tagOps, aggregateDirty: aggregateDirty);

            requests.Publish(new EffectRequest { RootId = 1, Source = live, Target = live, TargetContext = live, TemplateId = BuffTemplateId });
            requests.Publish(new EffectRequest { RootId = 2, Source = doomed, Target = doomed, TargetContext = doomed, TemplateId = BuffTemplateId });

            proposal.Update(0f);

            Assert.That(requests.Count, Is.Zero);
            Assert.That(CountEffectsTargeting(world, live), Is.EqualTo(1));
            Assert.That(CountEffectsTargeting(world, doomed), Is.Zero,
                "An entity waiting for presentation destroy is logically gone; a new effect on it would outlive its target.");

            world.Destroy(doomed);
            Assert.That(() => application.Update(0f), Throws.Nothing);
        }

        private static int CountEffectsTargeting(World world, Entity target)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<GameplayEffect, EffectContext>();
            world.Query(in query, (ref EffectContext context) =>
            {
                if (context.Target == target)
                {
                    count++;
                }
            });
            return count;
        }

        private static void FinalizeBuffTemplates(EffectTemplateRegistry templates)
        {
            var presetTypes = new PresetTypeRegistry();
            var buff = new PresetTypeDefinition
            {
                Type = EffectPresetType.Buff,
                Components = ComponentFlags.ModifierParams | ComponentFlags.DurationParams,
                ActivePhases = PhaseFlags.OnApply,
                AllowedLifetimes = LifetimeFlags.Duration,
            };
            buff.DefaultPhaseHandlers[EffectPhaseId.OnApply] =
                PhaseHandler.Builtin(BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in buff);

            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                new GraphProgramRegistry(),
                "Test/EffectRequestTargetLifecycleTests.json");
        }

        private static int EnsureAttribute(string attribute)
        {
            int id = AttributeRegistry.GetId(attribute);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(attribute);
        }
    }
}
