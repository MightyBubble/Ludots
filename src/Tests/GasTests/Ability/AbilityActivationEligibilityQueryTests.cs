using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Progression;
using Ludots.Core.Gameplay.Progression.Components;
using Ludots.Core.Gameplay.Progression.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class AbilityActivationEligibilityQueryTests
{
    [SetUp]
    public void SetUp()
    {
        ProgressionIdRegistry.Clear();
        ProgressionRequirementIdRegistry.Clear();
    }

    [Test]
    public void Evaluate_UsesTemporaryThenItemThenFormThenBaseSlotPrecedence()
    {
        using var world = World.Create();
        const int baseAbilityId = 7101;
        const int formAbilityId = 7102;
        const int itemAbilityId = 7103;
        const int temporaryAbilityId = 7104;

        var definitions = new AbilityDefinitionRegistry();
        definitions.Register(baseAbilityId, new AbilityDefinition());
        definitions.Register(formAbilityId, new AbilityDefinition());
        definitions.Register(itemAbilityId, new AbilityDefinition());
        definitions.Register(temporaryAbilityId, new AbilityDefinition());

        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(baseAbilityId);
        var formSlots = new AbilityFormSlotBuffer();
        formSlots.SetOverride(0, formAbilityId);
        var itemSlots = new ItemGrantedSlotBuffer();
        itemSlots.SetOverride(0, itemAbilityId, Entity.Null);
        var temporarySlots = new GrantedSlotBuffer();
        temporarySlots.Grant(0, temporaryAbilityId, sourceTagId: 91);
        Entity actor = world.Create(abilities, formSlots, itemSlots, temporarySlots);
        AbilityActivationEligibilityQuery query = CreateQuery(world, definitions);
        var request = new AbilityActivationEligibilityRequest(actor, abilitySlotIndex: 0);

        Assert.That(query.Evaluate(in request).EffectiveSlot.AbilityId, Is.EqualTo(temporaryAbilityId));

        world.Get<GrantedSlotBuffer>(actor).Revoke(0);
        Assert.That(query.Evaluate(in request).EffectiveSlot.AbilityId, Is.EqualTo(itemAbilityId));

        world.Get<ItemGrantedSlotBuffer>(actor).ClearAll();
        Assert.That(query.Evaluate(in request).EffectiveSlot.AbilityId, Is.EqualTo(formAbilityId));

        world.Get<AbilityFormSlotBuffer>(actor).Clear(0);
        Assert.That(query.Evaluate(in request).EffectiveSlot.AbilityId, Is.EqualTo(baseAbilityId));
    }

    [Test]
    public void Evaluate_ReturnsTypedProgressionRefusalUntilRequirementCompletes()
    {
        using var world = World.Create();
        const int abilityId = 7201;
        int progressionId = ProgressionIdRegistry.Register("Progression.Query.Unlock");
        int requirementId = ProgressionRequirementIdRegistry.Register("Requirement.Query.Unlock");
        GameplayTagContainer noRequiredTags = default;
        var requirementNodes = new[]
        {
            new ProgressionRequirementNode(
                ProgressionRequirementNodeKind.ProgressionCompleted,
                ScopeKey.Self,
                RoleSlot.Actor,
                firstChild: 0,
                childCount: 0,
                progressionId,
                requiredCount: 1,
                graphProgramId: 0,
                in noRequiredTags)
        };
        var requirements = new ProgressionRequirementRegistry();
        requirements.Register(
            requirementId,
            new ProgressionRequirementDefinition(requirementId, requirementNodes, Array.Empty<int>()));
        TagOps tagOps = CreateTagOps();
        var progression = new ProgressionRequirementEvaluator(
            world,
            requirements,
            new ScopeKeyRegistry(),
            tagOps: tagOps);

        var definitions = new AbilityDefinitionRegistry();
        definitions.Register(abilityId, new AbilityDefinition
        {
            HasUseProgressionRequirement = true,
            UseProgressionRequirementId = requirementId
        });
        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(abilityId);
        Entity actor = world.Create(abilities, new ProgressionStateBuffer());
        var query = new AbilityActivationEligibilityQuery(
            world,
            definitions,
            tagOps,
            graphPrograms: null,
            graphApi: null,
            progression);
        var request = new AbilityActivationEligibilityRequest(actor, abilitySlotIndex: 0);

        AbilityActivationEligibilityResult blocked = query.Evaluate(in request);
        Assert.That(blocked.IsEligible, Is.False);
        Assert.That(blocked.RefusalReason, Is.EqualTo(AbilityActivationRefusalReason.ProgressionRequirementFailed));

        Assert.That(progression.TryComplete(actor, progressionId), Is.True);
        Assert.That(query.Evaluate(in request).IsEligible, Is.True);
    }

    [Test]
    public void Evaluate_PassesTargetEntityPointAndContextToReadOnlyPreconditionGraphs()
    {
        using var world = World.Create();
        const int contextAbilityId = 7301;
        const int pointAbilityId = 7302;
        const int contextGraphId = 7311;
        const int pointGraphId = 7312;
        var graphs = new GraphProgramRegistry();
        graphs.Register(contextGraphId, new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadExplicitTarget, Dst = 2 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadContextTargetContext, Dst = 3 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqEntity, Dst = 0, A = 2, B = 3 }
        }, GraphKind.Validation);
        graphs.Register(pointGraphId, new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.LoadTargetPosX, Dst = 0 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.ConstInt, Dst = 1, Imm = 840 },
            new GraphInstruction { Op = (ushort)GraphNodeOp.CompareEqInt, Dst = 0, A = 0, B = 1 }
        }, GraphKind.Validation);

        var definitions = new AbilityDefinitionRegistry();
        definitions.Register(contextAbilityId, new AbilityDefinition
        {
            HasActivationPrecondition = true,
            ActivationPrecondition = new AbilityActivationPrecondition { ValidationGraphId = contextGraphId }
        });
        definitions.Register(pointAbilityId, new AbilityDefinition
        {
            HasActivationPrecondition = true,
            ActivationPrecondition = new AbilityActivationPrecondition { ValidationGraphId = pointGraphId }
        });
        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(contextAbilityId);
        abilities.AddAbility(pointAbilityId);
        Entity actor = world.Create(abilities);
        Entity target = world.Create();
        Entity otherContext = world.Create();
        var query = new AbilityActivationEligibilityQuery(
            world,
            definitions,
            CreateTagOps(),
            graphs,
            new GasGraphRuntimeApi(world),
            progressionRequirements: null);

        var matchingContext = new AbilityActivationEligibilityRequest(
            actor,
            abilitySlotIndex: 0,
            targetEntity: target,
            targetContext: target);
        Assert.That(query.Evaluate(in matchingContext).IsEligible, Is.True);

        var mismatchedContext = new AbilityActivationEligibilityRequest(
            actor,
            abilitySlotIndex: 0,
            targetEntity: target,
            targetContext: otherContext);
        Assert.That(
            query.Evaluate(in mismatchedContext).RefusalReason,
            Is.EqualTo(AbilityActivationRefusalReason.ActivationPreconditionFailed));

        var matchingPoint = new AbilityActivationEligibilityRequest(
            actor,
            abilitySlotIndex: 1,
            targetPositionCm: new IntVector2(840, 120),
            hasTargetPosition: true);
        Assert.That(query.Evaluate(in matchingPoint).IsEligible, Is.True);

        var mismatchedPoint = new AbilityActivationEligibilityRequest(
            actor,
            abilitySlotIndex: 1,
            targetPositionCm: new IntVector2(839, 120),
            hasTargetPosition: true);
        Assert.That(
            query.Evaluate(in mismatchedPoint).RefusalReason,
            Is.EqualTo(AbilityActivationRefusalReason.ActivationPreconditionFailed));
    }

    [Test]
    public void Evaluate_IsAllocationFreeAndDoesNotMutateAbilityTagsOrExecutionState()
    {
        using var world = World.Create();
        const int abilityId = 7401;
        const int requiredTagId = 74;
        var requiredTags = default(GameplayTagContainer);
        requiredTags.AddTag(requiredTagId);
        var definitions = new AbilityDefinitionRegistry();
        definitions.Register(abilityId, new AbilityDefinition
        {
            HasActivationBlockTags = true,
            ActivationBlockTags = new AbilityActivationBlockTags { RequiredAll = requiredTags }
        });
        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(abilityId);
        var actorTags = default(GameplayTagContainer);
        actorTags.AddTag(requiredTagId);
        Entity actor = world.Create(abilities, actorTags);
        AbilityActivationEligibilityQuery query = CreateQuery(world, definitions);
        var request = new AbilityActivationEligibilityRequest(actor, abilitySlotIndex: 0);
        Entity actorMissingTags = world.Create(abilities);
        var missingTagsRequest = new AbilityActivationEligibilityRequest(actorMissingTags, abilitySlotIndex: 0);

        Assert.That(
            query.Evaluate(in missingTagsRequest).RefusalReason,
            Is.EqualTo(AbilityActivationRefusalReason.RequiredActivationTagMissing));

        for (int i = 0; i < 16; i++)
        {
            _ = query.Evaluate(in request);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        AbilityActivationEligibilityResult result = default;
        for (int i = 0; i < 1_000; i++)
        {
            result = query.Evaluate(in request);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(result.IsEligible, Is.True);
        Assert.That(allocated, Is.Zero);
        Assert.That(world.Get<AbilityStateBuffer>(actor).Get(0).AbilityId, Is.EqualTo(abilityId));
        Assert.That(world.Get<GameplayTagContainer>(actor).HasTag(requiredTagId), Is.True);
        Assert.That(world.Has<AbilityExecInstance>(actor), Is.False);
    }

    [Test]
    public void AbilitySystem_UsesInjectedEligibilityJudgmentWithoutPublishingBlockedEffects()
    {
        using var world = World.Create();
        const int abilityId = 7501;
        const int blockedTagId = 75;
        var blockedTags = default(GameplayTagContainer);
        blockedTags.AddTag(blockedTagId);
        var definitions = new AbilityDefinitionRegistry();
        definitions.Register(abilityId, new AbilityDefinition
        {
            HasActivationBlockTags = true,
            ActivationBlockTags = new AbilityActivationBlockTags { BlockedAny = blockedTags }
        });
        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(abilityId);
        var actorTags = default(GameplayTagContainer);
        actorTags.AddTag(blockedTagId);
        Entity actor = world.Create(abilities, actorTags);
        TagOps tagOps = CreateTagOps();
        var query = new AbilityActivationEligibilityQuery(world, definitions, tagOps);
        var effects = new EffectRequestQueue();
        var system = new AbilitySystem(
            world,
            effects,
            definitions,
            tagOps,
            activationEligibility: query);
        var request = new AbilityActivationEligibilityRequest(actor, abilitySlotIndex: 0);

        Assert.That(
            query.Evaluate(in request).RefusalReason,
            Is.EqualTo(AbilityActivationRefusalReason.BlockedActivationTagPresent));
        Assert.That(system.TryActivateAbility(actor, slotIndex: 0), Is.False);
        Assert.That(effects.Count, Is.Zero);
    }

    [Test]
    public void Evaluate_FailsClosedBeforeExecutingSideEffectingPreconditionProgram()
    {
        using var world = World.Create();
        const int abilityId = 7601;
        const int graphId = 7602;
        var graphs = new GraphProgramRegistry();
        graphs.Register(graphId, new[]
        {
            new GraphInstruction { Op = (ushort)GraphNodeOp.ApplyEffectTemplate, A = 1, Imm = 99 }
        }, GraphKind.Validation);
        var definitions = new AbilityDefinitionRegistry();
        definitions.Register(abilityId, new AbilityDefinition
        {
            HasActivationPrecondition = true,
            ActivationPrecondition = new AbilityActivationPrecondition { ValidationGraphId = graphId }
        });
        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(abilityId);
        Entity actor = world.Create(abilities);
        Entity target = world.Create();
        var effects = new EffectRequestQueue();
        var query = new AbilityActivationEligibilityQuery(
            world,
            definitions,
            CreateTagOps(),
            graphs,
            new GasGraphRuntimeApi(world, effectRequests: effects));
        var request = new AbilityActivationEligibilityRequest(actor, 0, target);

        var ex = Assert.Throws<InvalidOperationException>(() => query.Evaluate(in request));

        Assert.That(ex!.Message, Does.Contain(nameof(GraphNodeOp.ApplyEffectTemplate)));
        Assert.That(effects.Count, Is.Zero);
    }

    private static AbilityActivationEligibilityQuery CreateQuery(
        World world,
        AbilityDefinitionRegistry definitions)
    {
        return new AbilityActivationEligibilityQuery(
            world,
            definitions,
            CreateTagOps(),
            graphPrograms: null,
            graphApi: null,
            progressionRequirements: null);
    }

    private static TagOps CreateTagOps()
    {
        return new TagOps(
            new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
            new TagRuleRegistry());
    }
}
