using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Persistence;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class EntityIdentityPersistenceTests
{
    [Test]
    public void CoreBinarySerializerRestoresQueryableEntitiesAsAlive()
    {
        using World world = World.Create();
        world.Create(new Name { Value = "alive-after-restore" });

        using World restored = CoreRoundTrip(world);
        Entity restoredEntity = FindSingle<Name>(restored);

        Assert.That(restored.IsAlive(restoredEntity), Is.True);
    }

    [Test]
    public void MapEntityMapIdIsPreservedAfterRoundTrip()
    {
        using World world = World.Create();
        world.Create(new MapEntity { MapId = new MapId("rts_cnc_training") });

        using World restored = CoreRoundTrip(world);
        Entity restoredEntity = FindSingle<MapEntity>(restored);

        Assert.That(restored.Get<MapEntity>(restoredEntity).MapId.Value, Is.EqualTo("rts_cnc_training"));
    }

    [Test]
    public void EntityReferenceWorldIdMatchesRestoredWorldAfterSourceWorldIsDestroyed()
    {
        var serializer = new LudotsBinaryWorldSerializer();
        byte[] bytes;

        using (World source = World.Create())
        {
            Entity target = source.Create(new Name { Value = "target-after-destroy" });
            var refs = new BlackboardEntityBuffer();
            refs.Set(7, target);
            source.Create(refs);
            bytes = serializer.Serialize(source);
        }

        using World restored = serializer.Deserialize(bytes);
        Entity restoredOwner = FindSingle<BlackboardEntityBuffer>(restored);
        ref readonly BlackboardEntityBuffer refsAfterRestore = ref restored.Get<BlackboardEntityBuffer>(restoredOwner);

        Assert.That(refsAfterRestore.TryGet(7, out Entity restoredTarget), Is.True);
        Assert.That(restoredTarget.WorldId, Is.EqualTo(restored.Id));
        AssertAliveName(restored, restoredTarget, "target-after-destroy");
    }

    [Test]
    public void EntityReferenceAuditFailsFastWhenPersistentComponentReferencesExcludedEntity()
    {
        using World world = World.Create();
        Entity excluded = world.Create(new Name { Value = "excluded" }, new SaveExcludedTag());
        var refs = new BlackboardEntityBuffer();
        refs.Set(7, excluded);
        world.Create(new Name { Value = "persistent-owner" }, refs);

        var error = Assert.Throws<SaveContextException>(
            () => SaveEntityReferenceValidator.Validate(world, SaveEntityInclusionPolicy.Default));

        Assert.That(error!.Message, Does.Contain("excluded entity"));
        Assert.That(error.Message, Does.Contain(nameof(BlackboardEntityBuffer)));
    }

    [Test]
    public void BlackboardEntityBufferReferenceIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity target = world.Create(new Name { Value = "target" });
        var refs = new BlackboardEntityBuffer();
        refs.Set(7, target);
        world.Create(refs);

        using World restored = CoreRoundTrip(world);
        Entity restoredRefOwner = FindSingle<BlackboardEntityBuffer>(restored);
        ref readonly BlackboardEntityBuffer restoredRefs = ref restored.Get<BlackboardEntityBuffer>(restoredRefOwner);

        Assert.That(restoredRefs.TryGet(7, out Entity restoredTarget), Is.True);
        AssertAliveName(restored, restoredTarget, "target");
    }

    [Test]
    public void ChildrenBufferReferenceIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity firstChild = world.Create(new Name { Value = "child-a" });
        Entity secondChild = world.Create(new Name { Value = "child-b" });
        var children = new ChildrenBuffer();
        Assert.That(children.Add(firstChild), Is.True);
        Assert.That(children.Add(secondChild), Is.True);
        world.Create(children);

        using World restored = CoreRoundTrip(world);
        Entity restoredParent = FindSingle<ChildrenBuffer>(restored);
        ref readonly ChildrenBuffer restoredChildren = ref restored.Get<ChildrenBuffer>(restoredParent);

        AssertAliveName(restored, restoredChildren.Get(0), "child-a");
        AssertAliveName(restored, restoredChildren.Get(1), "child-b");
    }

    [Test]
    public void ActiveEffectContainerReferenceIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity firstEffect = world.Create(new Name { Value = "effect-a" });
        Entity secondEffect = world.Create(new Name { Value = "effect-b" });
        var activeEffects = new ActiveEffectContainer();
        Assert.That(activeEffects.Add(firstEffect), Is.True);
        Assert.That(activeEffects.Add(secondEffect), Is.True);
        world.Create(activeEffects);

        using World restored = CoreRoundTrip(world);
        Entity restoredOwner = FindSingle<ActiveEffectContainer>(restored);
        ref readonly ActiveEffectContainer restoredActiveEffects = ref restored.Get<ActiveEffectContainer>(restoredOwner);

        AssertAliveName(restored, restoredActiveEffects.GetEntity(0), "effect-a");
        AssertAliveName(restored, restoredActiveEffects.GetEntity(1), "effect-b");
    }

    [Test]
    public void AbilityStateBufferTemplateReferenceIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity template = world.Create(new Name { Value = "ability-template" });
        var abilities = new AbilityStateBuffer();
        abilities.AddAbility(template);
        world.Create(abilities);

        using World restored = CoreRoundTrip(world);
        Entity restoredActor = FindSingle<AbilityStateBuffer>(restored);
        AbilitySlotState slot = restored.Get<AbilityStateBuffer>(restoredActor).Get(0);
        Entity restoredTemplate = EntityUtil.Reconstruct(
            slot.TemplateEntityId,
            slot.TemplateEntityWorldId,
            slot.TemplateEntityVersion);

        AssertAliveName(restored, restoredTemplate, "ability-template");
    }

    [Test]
    public void TeamEntityRefValueIsAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity team = world.Create(new Name { Value = "team" });
        world.Create(new TeamEntityRef { Value = team });

        using World restored = CoreRoundTrip(world);
        Entity restoredOwner = FindSingle<TeamEntityRef>(restored);
        Entity restoredTeam = restored.Get<TeamEntityRef>(restoredOwner).Value;

        AssertAliveName(restored, restoredTeam, "team");
    }

    [Test]
    public void AbilityExecInstanceReferencesAreAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity target = world.Create(new Name { Value = "target" });
        Entity targetContext = world.Create(new Name { Value = "target-context" });
        Entity firstMultiTarget = world.Create(new Name { Value = "multi-a" });
        Entity secondMultiTarget = world.Create(new Name { Value = "multi-b" });

        var exec = new AbilityExecInstance
        {
            Target = target,
            TargetContext = targetContext
        };
        exec.AddMultiTarget(firstMultiTarget);
        exec.AddMultiTarget(secondMultiTarget);
        world.Create(exec);

        using World restored = CoreRoundTrip(world);
        Entity restoredOwner = FindSingle<AbilityExecInstance>(restored);
        ref readonly AbilityExecInstance restoredExec = ref restored.Get<AbilityExecInstance>(restoredOwner);

        AssertAliveName(restored, restoredExec.Target, "target");
        AssertAliveName(restored, restoredExec.TargetContext, "target-context");
        Assert.That(restoredExec.MultiTargetCount, Is.EqualTo(2));

        Entity restoredFirstMultiTarget;
        Entity restoredSecondMultiTarget;
        unsafe
        {
            restoredFirstMultiTarget = EntityUtil.Reconstruct(
                restoredExec.MultiTargetIds[0],
                restoredExec.MultiTargetWorldIds[0],
                restoredExec.MultiTargetVersions[0]);
            restoredSecondMultiTarget = EntityUtil.Reconstruct(
                restoredExec.MultiTargetIds[1],
                restoredExec.MultiTargetWorldIds[1],
                restoredExec.MultiTargetVersions[1]);
        }

        AssertAliveName(restored, restoredFirstMultiTarget, "multi-a");
        AssertAliveName(restored, restoredSecondMultiTarget, "multi-b");
    }

    [Test]
    public void OrderBuffersNestedEntityReferencesAreAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity target = world.Create(new Name { Value = "target" });
        Entity targetContext = world.Create(new Name { Value = "target-context" });
        Entity selectionContainer = world.Create(new Name { Value = "selection-container" });
        Entity actor = world.Create(new Name { Value = "actor" }, OrderBuffer.CreateEmpty(), new OrderContinuationBuffer());

        var order = new Order
        {
            OrderId = 101,
            OrderTypeId = 7,
            Actor = actor,
            Target = target,
            TargetContext = targetContext,
            Args = new OrderArgs
            {
                Selection = new OrderSelectionReference { Container = selectionContainer }
            },
            SubmitMode = OrderSubmitMode.Queued
        };

        ref OrderBuffer buffer = ref world.Get<OrderBuffer>(actor);
        buffer.SetActiveDirect(in order, priority: 10);
        Assert.That(buffer.Enqueue(in order, priority: 5, expireStep: -1, insertStep: 1), Is.True);
        buffer.SetPending(in order, priority: 4, expireStep: -1, insertStep: 2);

        ref OrderContinuationBuffer continuations = ref world.Get<OrderContinuationBuffer>(actor);
        Assert.That(continuations.TryAdd(order.OrderId, in order), Is.True);

        using World restored = CoreRoundTrip(world);
        Entity restoredActor = FindSingle<OrderBuffer>(restored);
        ref readonly OrderBuffer restoredBuffer = ref restored.Get<OrderBuffer>(restoredActor);
        ref readonly OrderContinuationBuffer restoredContinuations = ref restored.Get<OrderContinuationBuffer>(restoredActor);

        AssertOrderReferences(restored, in restoredBuffer.ActiveOrder.Order);
        AssertOrderReferences(restored, in restoredBuffer.PendingOrder.Order);
        QueuedOrder queued = restoredBuffer.GetQueued(0);
        AssertOrderReferences(restored, in queued.Order);
        OrderContinuationEntry entry = restoredContinuations.Get(0);
        AssertOrderReferences(restored, in entry.Order);
    }

    [Test]
    public void RuntimeEntityReferenceComponentsAreAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity source = world.Create(new Name { Value = "source" });
        Entity target = world.Create(new Name { Value = "target" });
        Entity targetContext = world.Create(new Name { Value = "target-context" });
        Entity parent = world.Create(new Name { Value = "parent" });
        Entity container = world.Create(new Name { Value = "container" });
        Entity item = world.Create(new Name { Value = "item" });
        Entity performer = world.Create(new Name { Value = "performer" });

        world.Create(new EffectContext { Source = source, Target = target, TargetContext = targetContext });
        world.Create(new ChildOf { Parent = parent });
        world.Create(new DisplacementState { SourceEntity = source, TargetEntity = target, DirectionTargetEntity = targetContext });
        world.Create(new ProjectileState { Source = source, Target = target });
        world.Create(new ItemLocationCm { Container = container });
        world.Create(new ItemMountedContainerCm { ParentItem = item });

        var itemSlots = new ItemGrantedSlotBuffer();
        itemSlots.SetOverride(0, abilityId: 12, item);
        world.Create(itemSlots);
        world.Create(new PresentationOwnerHasPerformerPayload
        {
            Count = 1,
            RootCount = 1,
            SingleRootPerformer = performer
        });

        using World restored = CoreRoundTrip(world);

        Entity restoredEffect = FindSingle<EffectContext>(restored);
        ref readonly EffectContext restoredEffectContext = ref restored.Get<EffectContext>(restoredEffect);
        AssertAliveName(restored, restoredEffectContext.Source, "source");
        AssertAliveName(restored, restoredEffectContext.Target, "target");
        AssertAliveName(restored, restoredEffectContext.TargetContext, "target-context");

        AssertAliveName(restored, restored.Get<ChildOf>(FindSingle<ChildOf>(restored)).Parent, "parent");
        AssertAliveName(restored, restored.Get<DisplacementState>(FindSingle<DisplacementState>(restored)).DirectionTargetEntity, "target-context");
        AssertAliveName(restored, restored.Get<ProjectileState>(FindSingle<ProjectileState>(restored)).Source, "source");
        AssertAliveName(restored, restored.Get<ItemLocationCm>(FindSingle<ItemLocationCm>(restored)).Container, "container");
        AssertAliveName(restored, restored.Get<ItemMountedContainerCm>(FindSingle<ItemMountedContainerCm>(restored)).ParentItem, "item");

        Entity restoredItemSlotOwner = FindSingle<ItemGrantedSlotBuffer>(restored);
        AssertAliveName(restored, restored.Get<ItemGrantedSlotBuffer>(restoredItemSlotOwner).GetSourceItem(0), "item");

        Entity restoredPayloadOwner = FindSingle<PresentationOwnerHasPerformerPayload>(restored);
        AssertAliveName(
            restored,
            restored.Get<PresentationOwnerHasPerformerPayload>(restoredPayloadOwner).SingleRootPerformer,
            "performer");
    }

    [Test]
    public void UtilityAiEntityReferencesAreAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity currentTarget = world.Create(new Name { Value = "current-target" });
        Entity bestTarget = world.Create(new Name { Value = "best-target" });
        Entity lastAttacker = world.Create(new Name { Value = "last-attacker" });
        Entity lastSeenTarget = world.Create(new Name { Value = "last-seen-target" });
        world.Create(
            new UtilityAiState { CurrentTarget = currentTarget },
            new UtilityAiDecisionTrace { BestTarget = bestTarget },
            new UtilityAiCombatMemory
            {
                LastAttacker = lastAttacker,
                LastSeenTarget = lastSeenTarget
            });

        using World restored = CoreRoundTrip(world);
        Entity restoredActor = FindSingle<UtilityAiState>(restored);

        AssertAliveName(restored, restored.Get<UtilityAiState>(restoredActor).CurrentTarget, "current-target");
        AssertAliveName(restored, restored.Get<UtilityAiDecisionTrace>(restoredActor).BestTarget, "best-target");
        AssertAliveName(restored, restored.Get<UtilityAiCombatMemory>(restoredActor).LastAttacker, "last-attacker");
        AssertAliveName(restored, restored.Get<UtilityAiCombatMemory>(restoredActor).LastSeenTarget, "last-seen-target");
    }

    [Test]
    public void SelectionEntityReferencesAreAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity owner = world.Create(new Name { Value = "owner" });
        Entity container = world.Create(new Name { Value = "container" });
        Entity target = world.Create(new Name { Value = "target" });
        Entity viewer = world.Create(new Name { Value = "viewer" });
        world.Create(new SelectionContainerOwner { Value = owner });
        world.Create(
            new SelectionMemberContainer { Value = container },
            new SelectionMemberTarget { Value = target });
        world.Create(
            new SelectionViewBindingViewer { Value = viewer },
            new SelectionViewBindingContainer { Value = container });
        world.Create(new SelectionLeaseContainer { Value = container });

        using World restored = CoreRoundTrip(world);

        AssertAliveName(restored, restored.Get<SelectionContainerOwner>(FindSingle<SelectionContainerOwner>(restored)).Value, "owner");
        Entity restoredMember = FindSingle<SelectionMemberContainer>(restored);
        AssertAliveName(restored, restored.Get<SelectionMemberContainer>(restoredMember).Value, "container");
        AssertAliveName(restored, restored.Get<SelectionMemberTarget>(restoredMember).Value, "target");
        Entity restoredBinding = FindSingle<SelectionViewBindingViewer>(restored);
        AssertAliveName(restored, restored.Get<SelectionViewBindingViewer>(restoredBinding).Value, "viewer");
        AssertAliveName(restored, restored.Get<SelectionViewBindingContainer>(restoredBinding).Value, "container");
        AssertAliveName(restored, restored.Get<SelectionLeaseContainer>(FindSingle<SelectionLeaseContainer>(restored)).Value, "container");
    }

    [Test]
    public void ScopeRefBufferReferencesAreAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity firstHost = world.Create(new Name { Value = "scope-host-a" });
        Entity secondHost = world.Create(new Name { Value = "scope-host-b" });
        var refs = new ScopeRefBuffer();
        Assert.That(refs.TryAdd(11, firstHost), Is.True);
        Assert.That(refs.TryAdd(12, secondHost), Is.True);
        world.Create(refs);

        using World restored = CoreRoundTrip(world);
        Entity restoredMember = FindSingle<ScopeRefBuffer>(restored);
        ref readonly ScopeRefBuffer restoredRefs = ref restored.Get<ScopeRefBuffer>(restoredMember);

        Assert.That(restoredRefs.TryGet(11, out Entity restoredFirstHost), Is.True);
        Assert.That(restoredRefs.TryGet(12, out Entity restoredSecondHost), Is.True);
        AssertAliveName(restored, restoredFirstHost, "scope-host-a");
        AssertAliveName(restored, restoredSecondHost, "scope-host-b");
    }

    [Test]
    public void PerformerEntityReferencesAreAliveAndReadableAfterRoundTrip()
    {
        using World world = World.Create();
        Entity owner = world.Create(new Name { Value = "owner" });
        Entity parent = world.Create(new Name { Value = "parent" });
        Entity firstChild = world.Create(new Name { Value = "child-a" });
        Entity secondChild = world.Create(new Name { Value = "child-b" });
        var children = new PerformerChildren();
        Assert.That(children.Add(firstChild), Is.True);
        Assert.That(children.Add(secondChild), Is.True);
        world.Create(
            new PerformerState
            {
                DefId = 7,
                StableId = 101,
                ScopeId = 202,
                OwnerEntity = owner,
                AnchorKind = PresentationAnchorKind.Entity
            },
            new PerformerParent { Parent = parent },
            children);

        using World restored = CoreRoundTrip(world);
        Entity restoredPerformer = FindSingle<PerformerState>(restored);
        ref readonly PerformerChildren restoredChildren = ref restored.Get<PerformerChildren>(restoredPerformer);

        AssertAliveName(restored, restored.Get<PerformerState>(restoredPerformer).OwnerEntity, "owner");
        AssertAliveName(restored, restored.Get<PerformerParent>(restoredPerformer).Parent, "parent");
        AssertAliveName(restored, restoredChildren.Get(0), "child-a");
        AssertAliveName(restored, restoredChildren.Get(1), "child-b");
    }

    private static World CoreRoundTrip(World world)
    {
        var serializer = new LudotsBinaryWorldSerializer();
        byte[] bytes = serializer.Serialize(world);
        return serializer.Deserialize(bytes);
    }

    private static void AssertAliveName(World world, Entity entity, string expectedName)
    {
        Assert.That(entity.WorldId, Is.EqualTo(world.Id));
        Assert.That(world.IsAlive(entity), Is.True);
        Assert.That(world.Has<Name>(entity), Is.True);
        Assert.That(world.Get<Name>(entity).Value, Is.EqualTo(expectedName));
    }

    private static void AssertOrderReferences(World world, in Order order)
    {
        AssertAliveName(world, order.Actor, "actor");
        AssertAliveName(world, order.Target, "target");
        AssertAliveName(world, order.TargetContext, "target-context");
        AssertAliveName(world, order.Args.Selection.Container, "selection-container");
    }

    private static Entity FindSingle<T>(World world)
    {
        var query = new QueryDescription().WithAll<T>();
        Entity found = Entity.Null;
        int matches = 0;

        world.Query(in query, entity =>
        {
            found = entity;
            matches++;
        });

        Assert.That(matches, Is.EqualTo(1));
        return found;
    }
}
