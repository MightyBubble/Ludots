using System;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Systems;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class AbilityTimedTagOrderIntegrationTests
{
    private const int CastOrderTypeId = 100;
    private const int AbilityId = 7001;
    private const int UnavailableTagId = 81;
    private const int DurationSteps = 3;

    [TestCase(false, TestName = "Player order uses GAS timed tag through expiry")]
    [TestCase(true, TestName = "AI order uses GAS timed tag through expiry")]
    public void OrderToAbilityExec_TimedTagBlocksAndExpires_ThenSourceCanCastAgain(bool useAiSource)
    {
        using var world = World.Create();
        var clock = new DiscreteClock();
        var admissionResults = new OrderAdmissionResultBuffer(32, 32);
        var orders = new OrderQueue(32, admissionResults);
        var terminalResults = new OrderTerminalResultBuffer(32);
        var orderTypes = CreateOrderTypes(terminalResults);
        var tagOps = new TagOps(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry());
        var definitions = CreateAbilityDefinitions();

        var actor = world.Create(
            OrderBuffer.CreateEmpty(),
            new BlackboardIntBuffer(),
            new BlackboardEntityBuffer(),
            new AbilityStateBuffer(),
            new GameplayTagContainer(),
            new TagCountContainer(),
            new TimedTagBuffer(),
            new DirtyFlags());
        ref var abilityState = ref world.Get<AbilityStateBuffer>(actor);
        abilityState.AddAbility(AbilityId);
        world.Add(actor, WorldPositionCm.FromCm(0, 0));
        world.Add(actor, new UtilityAiAgent { ProfileId = 0 });
        world.Add(actor, new UtilityAiState { CurrentDecisionId = -1, NextThinkStep = 0 });
        world.Add(actor, new UtilityAiDecisionTrace());
        world.Add(actor, new UtilityAiCombatMemory());

        var orderBufferSystem = new OrderBufferSystem(
            world,
            clock,
            orderTypes,
            new OrderRuleRegistry(),
            admissionResults,
            orders,
            stepRateHz: 30);
        var abilityExecSystem = new AbilityExecSystem(
            world,
            clock,
            new InputRequestQueue(),
            new InputResponseBuffer(),
            new EffectRequestQueue(),
            snapshotCapacity: 16,
            definitions,
            castAbilityOrderTypeId: CastOrderTypeId,
            orderTypeRegistry: orderTypes,
            tagOps: tagOps);
        var expirationSystem = new TimedTagExpirationSystem(world, clock, tagOps);
        var aiSystem = CreateAiSystem(world, clock, orders, definitions, out UtilityAiCompiledRuntime aiRuntime);
        var aiOrderResults = new UtilityAiOrderResultSystem(
            world,
            clock,
            aiRuntime,
            admissionResults,
            terminalResults);

        SubmitAndExecute(useAiSource, actor, admissionResults, orders, orderBufferSystem, abilityExecSystem, aiSystem, aiOrderResults);

        Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(UnavailableTagId), Is.True);
        Assert.That(world.Get<TimedTagBuffer>(actor).Count, Is.EqualTo(1));

        clock.Advance(ClockDomainId.Step, 1);
        expirationSystem.Update(0f);
        int terminalCountBeforeBlockedAttempt = terminalResults.Count;

        SubmitAndExecute(useAiSource, actor, admissionResults, orders, orderBufferSystem, abilityExecSystem, aiSystem, aiOrderResults);

        Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(UnavailableTagId), Is.True);
        Assert.That(world.Get<TimedTagBuffer>(actor).Count, Is.EqualTo(1));
        if (useAiSource)
        {
            Assert.That(orders.Count, Is.Zero);
            Assert.That(
                world.Get<UtilityAiDecisionTrace>(actor).LastReadinessBlockReason,
                Is.EqualTo((int)UtilityAiReadinessBlockReason.ActivationBlockTags));
        }
        else
        {
            Assert.That(terminalResults.Count, Is.EqualTo(terminalCountBeforeBlockedAttempt + 1));
            Assert.That(terminalResults[terminalResults.Count - 1].FailureReason, Is.EqualTo(OrderFailureReason.ActivationBlocked));
        }

        clock.Advance(ClockDomainId.Step, DurationSteps - 1);
        expirationSystem.Update(0f);

        Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(UnavailableTagId), Is.False);
        Assert.That(world.Get<TimedTagBuffer>(actor).Count, Is.Zero);

        SubmitAndExecute(useAiSource, actor, admissionResults, orders, orderBufferSystem, abilityExecSystem, aiSystem, aiOrderResults);

        Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(UnavailableTagId), Is.True);
        Assert.That(world.Get<TimedTagBuffer>(actor).Count, Is.EqualTo(1));
    }

    private static AbilityDefinitionRegistry CreateAbilityDefinitions()
    {
        var exec = default(AbilityExecSpec);
        exec.ClockId = GasClockId.Step;
        exec.SetItem(
            0,
            ExecItemKind.TagClip,
            tick: 0,
            durationTicks: DurationSteps,
            clockId: GasClockId.Step,
            tagId: UnavailableTagId);
        exec.SetItem(1, ExecItemKind.End, tick: 0);

        var blockTags = default(AbilityActivationBlockTags);
        blockTags.BlockedAny.AddTag(UnavailableTagId);

        var definitions = new AbilityDefinitionRegistry();
        definitions.Register(AbilityId, new AbilityDefinition
        {
            ExecSpec = exec,
            HasActivationBlockTags = true,
            ActivationBlockTags = blockTags
        });
        return definitions;
    }

    private static OrderTypeRegistry CreateOrderTypes(OrderTerminalResultBuffer terminalResults)
    {
        var orderTypes = new OrderTypeRegistry(terminalResults);
        orderTypes.Register(new OrderTypeConfig
        {
            Key = "castAbility",
            OrderTypeId = CastOrderTypeId,
            Priority = 100,
            BufferWindowMs = 0,
            PendingBufferWindowMs = 0,
            SameTypePolicy = SameTypePolicy.Replace,
            QueueFullPolicy = QueueFullPolicy.RejectNew,
            MaxQueueSize = 1,
            QueuedModeMaxSize = 1,
            AllowQueuedMode = false,
            ClearQueueOnActivate = true,
            SpatialBlackboardKey = -1,
            EntityBlackboardKey = -1,
            IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex
        });
        return orderTypes;
    }

    private static UtilityAiDecisionSystem CreateAiSystem(
        World world,
        DiscreteClock clock,
        OrderQueue orders,
        AbilityDefinitionRegistry definitions,
        out UtilityAiCompiledRuntime runtime)
    {
        GameplayTagContainer noTags = default;
        runtime = new UtilityAiCompiledRuntime(
            new[] { new UtilityAiProfileDefinition(0, 1, 1, 1, 4096, -1) },
            new[] { new UtilityAiDecisionMakerDefinition(0, 1, UtilityAiSelectionMode.FixedPriority, 0f) },
            new[]
            {
                new UtilityAiDecisionDefinition(
                    0,
                    0,
                    0,
                    0,
                    1,
                    1f,
                    1f,
                    0f,
                    0,
                    0,
                    AbilityId,
                    0,
                    false)
            },
            Array.Empty<UtilityAiConsiderationDefinition>(),
            new[] { new UtilityAiTargetFilterDefinition(0, 1, 1) },
            new[]
            {
                new UtilityAiTargetFilterOpDefinition(
                    UtilityAiTargetFilterOpKind.SourceSelf,
                    0,
                    0,
                    Ludots.Core.Gameplay.Teams.RelationshipFilter.All,
                    in noTags)
            },
            Array.Empty<UtilityAiInputDefinition>(),
            Array.Empty<UtilityAiNormalizationDefinition>(),
            Array.Empty<UtilityAiCurveDefinition>(),
            new[]
            {
                new UtilityAiTaskDefinition(
                    UtilityAiTaskKind.SubmitOrder,
                    CastOrderTypeId,
                    AbilityId,
                    0,
                    (int)OrderSubmitMode.Immediate,
                    0,
                    -1,
                    0)
            },
            Array.Empty<UtilityAiStanceDefinition>(),
            Array.Empty<UtilityAiActuatorDefinition>());

        var partition = new ChunkedGridSpatialPartitionWorld(4);
        var worldSpec = new WorldSizeSpec(new WorldAabbCm(-100, -100, 200, 200), 100);
        var spatial = new SpatialQueryService(new ChunkedGridSpatialPartitionBackend(partition, worldSpec));
        spatial.SetPositionProvider(entity => world.Get<WorldPositionCm>(entity).ToWorldCmInt2());
        var graphs = new GraphProgramRegistry();
        var eligibility = new AbilityActivationEligibilityQuery(
            world,
            definitions,
            new TagOps(
                new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                new TagRuleRegistry()),
            graphs,
            graphApi: null,
            progressionRequirements: null,
            actuatorGate: new UtilityAiAbilityActuatorGate(world, runtime));

        return new UtilityAiDecisionSystem(
            world,
            clock,
            runtime,
            spatial,
            definitions,
            graphs,
            null,
            orders,
            eligibility);
    }

    private static void SubmitAndExecute(
        bool useAiSource,
        Entity actor,
        OrderAdmissionResultBuffer admissionResults,
        OrderQueue orders,
        OrderBufferSystem orderBufferSystem,
        AbilityExecSystem abilityExecSystem,
        UtilityAiDecisionSystem aiSystem,
        UtilityAiOrderResultSystem aiOrderResults)
    {
        admissionResults.BeginLogicStep();
        if (useAiSource)
        {
            aiSystem.Update(0f);
        }
        else
        {
            var order = new Order
            {
                Actor = actor,
                OrderTypeId = CastOrderTypeId,
                PlayerId = 1,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs { I0 = 0 }
            };
            Assert.That(orders.TryEnqueue(in order), Is.True);
        }

        orderBufferSystem.Update(0f);
        abilityExecSystem.Update(0f);
        aiOrderResults.Update(0f);
        admissionResults.EndLogicStep();
    }
}
