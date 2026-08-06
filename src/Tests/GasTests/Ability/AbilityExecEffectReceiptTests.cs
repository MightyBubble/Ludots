using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS.Ability
{
    [TestFixture]
    public sealed class AbilityExecEffectReceiptTests
    {
        private const int CastOrderTypeId = 100;
        private const int AbilityId = 91;
        private const int TemplateId = 11;

        [Test]
        public void AbilityExec_EffectSignal_WaitsForTransactionReceiptBeforeCompleting()
        {
            using var world = World.Create();
            CreateRuntime(
                world,
                TemplateId,
                out AbilityExecSystem abilityExec,
                out EffectProposalProcessingSystem proposal,
                out EffectRequestQueue effectRequests,
                out EffectTransactionReceiptBuffer receipts,
                out OrderTypeRegistry orderTypes,
                out AbilityDefinitionRegistry definitions);

            RegisterEffectSignalAbility(definitions, AbilityId, TemplateId, ExecEffectDispatchTarget.Target);
            Entity target = CreateTarget(world);
            Entity actor = CreateCastingActor(world, AbilityId, target, orderId: 42);

            abilityExec.Update(0f);

            That(effectRequests.Count, Is.EqualTo(1));
            That(world.Get<AbilityExecInstance>(actor).State, Is.EqualTo(AbilityExecRunState.GateWaiting));
            That(world.Get<AbilityExecInstance>(actor).WaitRequestId, Is.EqualTo(effectRequests[0].RootId));

            proposal.Update(0f);

            That(receipts.Count, Is.EqualTo(1));
            That(world.Get<AbilityExecInstance>(actor).State, Is.EqualTo(AbilityExecRunState.GateWaiting));

            abilityExec.Update(0f);
            That(world.Get<AbilityExecInstance>(actor).State, Is.EqualTo(AbilityExecRunState.Running));
            That(receipts.Count, Is.EqualTo(0));

            abilityExec.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(42));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Completed));
        }

        [Test]
        public void AbilityExec_EffectSignal_FailsWhenTransactionReceiptReportsFailure()
        {
            using var world = World.Create();
            const int missingTemplateId = 42;
            CreateRuntime(
                world,
                TemplateId,
                out AbilityExecSystem abilityExec,
                out EffectProposalProcessingSystem proposal,
                out EffectRequestQueue effectRequests,
                out EffectTransactionReceiptBuffer receipts,
                out OrderTypeRegistry orderTypes,
                out AbilityDefinitionRegistry definitions);

            RegisterEffectSignalAbility(definitions, AbilityId, missingTemplateId, ExecEffectDispatchTarget.Source);
            Entity actor = CreateCastingActor(world, AbilityId, target: Entity.Null, orderId: 77);

            abilityExec.Update(0f);

            That(effectRequests.Count, Is.EqualTo(1));
            That(world.Get<AbilityExecInstance>(actor).State, Is.EqualTo(AbilityExecRunState.GateWaiting));
            int rootId = world.Get<AbilityExecInstance>(actor).WaitRequestId;

            proposal.Update(0f);

            That(receipts.TryConsume(rootId, out var written), Is.True);
            That(written.Outcome, Is.EqualTo(EffectTransactionOutcome.Failed));
            receipts.Write(in written);

            abilityExec.Update(0f);

            That(world.Has<AbilityExecInstance>(actor), Is.False);
            That(receipts.Count, Is.EqualTo(0));
            That(orderTypes.TerminalResults.Count, Is.EqualTo(1));
            That(orderTypes.TerminalResults[0].OrderId, Is.EqualTo(77));
            That(orderTypes.TerminalResults[0].State, Is.EqualTo(OrderTerminalState.Failed));
            That(orderTypes.TerminalResults[0].FailureReason, Is.EqualTo(OrderFailureReason.PreconditionFailed));
        }

        private static void CreateRuntime(
            World world,
            int registeredTemplateId,
            out AbilityExecSystem abilityExec,
            out EffectProposalProcessingSystem proposal,
            out EffectRequestQueue effectRequests,
            out EffectTransactionReceiptBuffer receipts,
            out OrderTypeRegistry orderTypes,
            out AbilityDefinitionRegistry definitions)
        {
            var templates = new EffectTemplateRegistry();
            var mods = new EffectModifiers();
            mods.Add(attrId: 0, ModifierOp.Add, -1f);
            templates.Register(registeredTemplateId, new EffectTemplateData
            {
                TagId = 0,
                PresetType = EffectPresetType.InstantDamage,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.Step,
                DurationTicks = 0,
                PeriodTicks = 0,
                ParticipatesInResponse = false,
                Modifiers = mods,
            });

            var presetTypes = new PresetTypeRegistry();
            var graphPrograms = new GraphProgramRegistry();
            var instantDamagePreset = new PresetTypeDefinition
            {
                Type = EffectPresetType.InstantDamage,
                Components = ComponentFlags.ModifierParams,
                ActivePhases = PhaseFlags.OnApply,
                AllowedLifetimes = LifetimeFlags.InstantOnly,
            };
            instantDamagePreset.DefaultPhaseHandlers[EffectPhaseId.OnApply] =
                GasTestGraphPrograms.BuiltinGraph(graphPrograms, 3_101, BuiltinHandlerId.ApplyModifiers);
            presetTypes.Register(in instantDamagePreset);
            var builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
            GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                graphPrograms,
                "Test/AbilityExecEffectReceiptTests.json");

            GasTestPhaseRuntime.Create(
                world,
                templates,
                effectRequests: null,
                out EffectPhaseExecutor phaseExecutor,
                out GasGraphRuntimeApi graphApi,
                graphPrograms,
                presetTypes,
                builtinHandlers);

            definitions = new AbilityDefinitionRegistry();
            orderTypes = new OrderTypeRegistry(new OrderTerminalResultBuffer(16));
            orderTypes.Register(new OrderTypeConfig
            {
                OrderTypeId = CastOrderTypeId,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                SpatialBlackboardKey = -1,
            }.UseCastAbilityPayload());

            effectRequests = new EffectRequestQueue();
            receipts = new EffectTransactionReceiptBuffer(16);
            var tagOps = new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry());
            abilityExec = new AbilityExecSystem(
                world,
                new DiscreteClock(),
                new InputRequestQueue(),
                new InputResponseBuffer(),
                effectRequests,
                snapshotCapacity: 16,
                abilityDefinitions: definitions,
                castAbilityOrderTypeId: CastOrderTypeId,
                presentationEvents: new GasPresentationEventBuffer(8),
                orderTypeRegistry: orderTypes,
                tagOps: tagOps,
                effectReceipts: receipts);
            proposal = new EffectProposalProcessingSystem(
                world,
                effectRequests,
                GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                new DiscreteClock(),
                budget: new GasBudget(),
                templates: templates,
                responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                phaseExecutor: phaseExecutor,
                graphApi: graphApi,
                tagOps: tagOps,
                effectReceipts: receipts);
        }

        private static void RegisterEffectSignalAbility(
            AbilityDefinitionRegistry definitions,
            int abilityId,
            int templateId,
            ExecEffectDispatchTarget dispatchTarget)
        {
            var spec = default(AbilityExecSpec);
            spec.ClockId = GasClockId.Step;
            spec.SetItem(0, ExecItemKind.EffectSignal, tick: 0, templateId: templateId, payloadA: (int)dispatchTarget);
            spec.SetItem(1, ExecItemKind.End, tick: 0);
            definitions.Register(abilityId, new AbilityDefinition { ExecSpec = spec });
        }

        private static Entity CreateTarget(World world)
        {
            Entity target = world.Create(new AttributeBuffer(), new DirtyFlags());
            ref var attributes = ref world.Get<AttributeBuffer>(target);
            attributes.SetCurrent(0, 100f);
            return target;
        }

        private static Entity CreateCastingActor(World world, int abilityId, Entity target, int orderId)
        {
            Entity actor = world.Create(
                OrderBuffer.CreateEmpty(),
                new BlackboardIntBuffer(),
                new BlackboardEntityBuffer(),
                new AbilityStateBuffer(),
                new GameplayTagContainer(),
                new TagCountContainer(),
                new DirtyFlags());
            world.Get<AbilityStateBuffer>(actor).AddAbility(abilityId);

            var order = new Order
            {
                OrderId = orderId,
                Actor = actor,
                Target = target,
                OrderTypeId = CastOrderTypeId,
                Args = new OrderArgs { I0 = 0 },
            };
            world.Get<OrderBuffer>(actor).SetActiveDirect(in order, priority: 100);
            world.Get<BlackboardIntBuffer>(actor).Set(OrderBlackboardKeys.Cast_SlotIndex, 0);
            if (target != Entity.Null && world.IsAlive(target))
            {
                world.Get<BlackboardEntityBuffer>(actor).Set(OrderBlackboardKeys.Cast_TargetEntity, target);
            }

            return actor;
        }
    }
}
