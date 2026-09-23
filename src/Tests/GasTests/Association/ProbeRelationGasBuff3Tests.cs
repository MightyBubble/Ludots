using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ProbeRelationGasBuff3Tests
    {
        [Test]
        public void Probe3_RelationEntity_PipelineStages()
        {
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            int socialBondTypeId = types.Register("Tests.Probe3.SocialBond");
            int pressureId = EnsureAttribute("Tests.Probe3.Pressure");
            int effectCategoryId = EffectCategoryRegistry.Register("Effect.Test.Probe3Buff");
            Entity source = world.Create();
            Entity target = world.Create();
            runtime.EnsureLink(source, target, socialBondTypeId);
            Assert.That(runtime.TryResolveRelationshipEntity(source, target, socialBondTypeId, out Entity relationEntity), Is.True);
            world.Get<AttributeBuffer>(relationEntity).SetBase(pressureId, 1f);

            var templates = new EffectTemplateRegistry();
            var modifiers = default(EffectModifiers);
            modifiers.Add(pressureId, ModifierOp.Add, 2f);
            templates.Register(2301, new EffectTemplateData
            {
                CategoryId = effectCategoryId,
                PresetType = EffectPresetType.Buff,
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.Step,
                DurationTicks = 10,
                PeriodTicks = 0,
                Modifiers = modifiers,
            });
            FinalizeBuffTemplates(templates, out GraphProgramRegistry programs, out PresetTypeRegistry presetTypes, out BuiltinHandlerRegistry builtinHandlers);

            int tplIdx = templates.TryGetRef(2301, out int idx) ? idx : -1;
            ref readonly EffectTemplateData tpl = ref templates.GetRef(idx);
            TestContext.Out.WriteLine($"DIAG tpl presetTypeId={tpl.PresetTypeId} phaseSteps={tpl.PhaseGraphBindings.StepCount} listenerSetup={tpl.ListenerSetup.Count}");
            unsafe
            {
                for (int st = 0; st < tpl.PhaseGraphBindings.StepCount; st++)
                {
                    TestContext.Out.WriteLine($"DIAG step phase={tpl.PhaseGraphBindings.StepPhases[st]} slot={tpl.PhaseGraphBindings.StepSlots[st]} graphId={tpl.PhaseGraphBindings.StepGraphIds[st]}");
                }
            }

            var requests = new EffectRequestQueue();
            var aggregateDirty = new Ludots.Core.Gameplay.GAS.AttributeAggregateDirtyRegistry();
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
            var graphApi = new GasGraphRuntimeApi(world, tagOps: tagOps) { AggregateDirty = aggregateDirty };
            var phaseExecutor = new EffectPhaseExecutor(
                programs,
                presetTypes,
                builtinHandlers,
                GasGraphOpHandlerTable.Instance,
                templates);
            var application = new EffectApplicationSystem(
                world,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new DiscreteClock(),
                requests,
                templates: templates,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps,
                aggregateDirty: aggregateDirty);
            var aggregator = new AttributeAggregatorSystem(world, tagOps: tagOps, aggregateDirty: tagOps.AggregateDirty);

            requests.Publish(new EffectRequest
            {
                RootId = 1,
                Source = Entity.Null,
                Target = relationEntity,
                TargetContext = Entity.Null,
                TemplateId = 2301,
            });

            proposal.Update(0f);
            TestContext.Out.WriteLine($"DIAG post-proposal requests={requests.Count}");
            application.Update(0f);
            TestContext.Out.WriteLine($"DIAG post-application current={world.Get<AttributeBuffer>(relationEntity).GetCurrent(pressureId)} changedBits={world.Has<GameplayAttributeChangedBits>(relationEntity)} dirtyAttr={world.Get<DirtyFlags>(relationEntity).IsAnyAttributeDirty()}");
            aggregator.Update(0f);
            TestContext.Out.WriteLine($"DIAG post-aggregator current={world.Get<AttributeBuffer>(relationEntity).GetCurrent(pressureId)} cap={world.Get<AttributeBuffer>(relationEntity).GetCap(pressureId)}");

            Assert.That(world.Get<AttributeBuffer>(relationEntity).GetCurrent(pressureId), Is.EqualTo(3f), "relation entity");
        }

        private static RelationshipRuntime CreateRuntime(World world, out RelationshipTypeRegistry types)
        {
            types = new RelationshipTypeRegistry();
            return new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(),
                new RelationshipReverseIndex(world));
        }

        private static void FinalizeBuffTemplates(
            EffectTemplateRegistry templates,
            out GraphProgramRegistry programs,
            out PresetTypeRegistry presetTypes,
            out BuiltinHandlerRegistry builtinHandlers)
        {
            programs = new GraphProgramRegistry();
            presetTypes = new PresetTypeRegistry();
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

            builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                programs,
                "Test/Probe3.json");
        }

        private static int EnsureAttribute(string attribute)
        {
            int id = AttributeRegistry.GetId(attribute);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(attribute);
        }
    }
}
