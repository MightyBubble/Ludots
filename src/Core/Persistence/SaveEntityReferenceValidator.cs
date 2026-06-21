using System;
using System.Collections.Generic;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.AI.Components;
using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Performers;

namespace Ludots.Core.Persistence
{
    public static class SaveEntityReferenceValidator
    {
        private static readonly IReadOnlySet<Type> CoveredComponentTypes = new HashSet<Type>
        {
            typeof(BlackboardEntityBuffer),
            typeof(ChildrenBuffer),
            typeof(ActiveEffectContainer),
            typeof(AbilityStateBuffer),
            typeof(GrantedSlotBuffer),
            typeof(AbilityFormSlotBuffer),
            typeof(AbilityExecInstance),
            typeof(AbilityTaskInstance),
            typeof(TeamEntityRef),
            typeof(OrderBuffer),
            typeof(OrderContinuationBuffer),
            typeof(ChildOf),
            typeof(EffectContext),
            typeof(DisplacementState),
            typeof(ProjectileState),
            typeof(ScopeRefBuffer),
            typeof(UtilityAiState),
            typeof(UtilityAiDecisionTrace),
            typeof(UtilityAiCombatMemory),
            typeof(SelectionContainerOwner),
            typeof(SelectionMemberContainer),
            typeof(SelectionMemberTarget),
            typeof(SelectionViewBindingViewer),
            typeof(SelectionViewBindingContainer),
            typeof(SelectionLeaseContainer),
            typeof(ItemLocationCm),
            typeof(ItemMountedContainerCm),
            typeof(ItemGrantedSlotBuffer),
            typeof(PresentationOwnerHasPerformerPayload),
            typeof(PerformerState),
            typeof(PerformerParent),
            typeof(PerformerChildren)
        };

        public static IReadOnlySet<Type> GetCoveredComponentTypes()
        {
            return CoveredComponentTypes;
        }

        public static void Validate(World world, SaveEntityInclusionPolicy policy)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            ValidateBlackboardEntityBuffer(world, policy);
            ValidateChildrenBuffer(world, policy);
            ValidateActiveEffectContainer(world, policy);
            ValidateAbilityStateBuffer(world, policy);
            ValidateGrantedSlotBuffer(world, policy);
            ValidateAbilityFormSlotBuffer(world, policy);
            ValidateAbilityExecInstance(world, policy);
            ValidateAbilityTaskInstance(world, policy);
            ValidateTeamEntityRef(world, policy);
            ValidateOrderBuffer(world, policy);
            ValidateOrderContinuationBuffer(world, policy);
            ValidateChildOf(world, policy);
            ValidateEffectContext(world, policy);
            ValidateDisplacementState(world, policy);
            ValidateProjectileState(world, policy);
            ValidateScopeRefBuffer(world, policy);
            ValidateUtilityAiState(world, policy);
            ValidateUtilityAiDecisionTrace(world, policy);
            ValidateUtilityAiCombatMemory(world, policy);
            ValidateSelectionContainerOwner(world, policy);
            ValidateSelectionMemberContainer(world, policy);
            ValidateSelectionMemberTarget(world, policy);
            ValidateSelectionViewBindingViewer(world, policy);
            ValidateSelectionViewBindingContainer(world, policy);
            ValidateSelectionLeaseContainer(world, policy);
            ValidateItemLocationCm(world, policy);
            ValidateItemMountedContainerCm(world, policy);
            ValidateItemGrantedSlotBuffer(world, policy);
            ValidatePresentationOwnerHasPerformerPayload(world, policy);
            ValidatePerformerState(world, policy);
            ValidatePerformerParent(world, policy);
            ValidatePerformerChildren(world, policy);
        }

        private static void ValidateBlackboardEntityBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<BlackboardEntityBuffer>();
            world.Query(in query, (Entity owner, ref BlackboardEntityBuffer refs) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < refs.Count; i++)
                {
                    int key;
                    Entity target;
                    unsafe
                    {
                        key = refs.Keys[i];
                        target = EntityUtil.Reconstruct(refs.EntityIds[i], refs.WorldIds[i], refs.Versions[i]);
                    }

                    ValidateTarget(world, policy, owner, target, nameof(BlackboardEntityBuffer), $"key={key}");
                }
            });
        }

        private static void ValidateChildrenBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ChildrenBuffer>();
            world.Query(in query, (Entity owner, ref ChildrenBuffer children) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < children.Count; i++)
                {
                    ValidateTarget(world, policy, owner, children.Get(i), nameof(ChildrenBuffer), $"index={i}");
                }
            });
        }

        private static void ValidateActiveEffectContainer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ActiveEffectContainer>();
            world.Query(in query, (Entity owner, ref ActiveEffectContainer activeEffects) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < activeEffects.Count; i++)
                {
                    ValidateTarget(world, policy, owner, activeEffects.GetEntity(i), nameof(ActiveEffectContainer), $"index={i}");
                }
            });
        }

        private static void ValidateAbilityStateBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<AbilityStateBuffer>();
            world.Query(in query, (Entity owner, ref AbilityStateBuffer abilities) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < abilities.Count; i++)
                {
                    ValidateAbilitySlot(world, policy, owner, abilities.Get(i), nameof(AbilityStateBuffer), $"slot={i}");
                }
            });
        }

        private static void ValidateGrantedSlotBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<GrantedSlotBuffer>();
            world.Query(in query, (Entity owner, ref GrantedSlotBuffer slots) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < GrantedSlotBuffer.CAPACITY; i++)
                {
                    ValidateAbilitySlot(world, policy, owner, slots.GetOverride(i), nameof(GrantedSlotBuffer), $"slot={i}");
                }
            });
        }

        private static void ValidateAbilityFormSlotBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<AbilityFormSlotBuffer>();
            world.Query(in query, (Entity owner, ref AbilityFormSlotBuffer slots) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < AbilityFormSlotBuffer.CAPACITY; i++)
                {
                    ValidateAbilitySlot(world, policy, owner, slots.GetOverride(i), nameof(AbilityFormSlotBuffer), $"slot={i}");
                }
            });
        }

        private static void ValidateAbilityExecInstance(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<AbilityExecInstance>();
            world.Query(in query, (Entity owner, ref AbilityExecInstance exec) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, exec.Target, nameof(AbilityExecInstance), nameof(AbilityExecInstance.Target));
                ValidateTarget(world, policy, owner, exec.TargetContext, nameof(AbilityExecInstance), nameof(AbilityExecInstance.TargetContext));

                unsafe
                {
                    for (int i = 0; i < exec.MultiTargetCount; i++)
                    {
                        ValidateFlattenedTarget(
                            world,
                            policy,
                            owner,
                            exec.MultiTargetIds[i],
                            exec.MultiTargetWorldIds[i],
                            exec.MultiTargetVersions[i],
                            nameof(AbilityExecInstance),
                            $"multiTarget={i}");
                    }
                }
            });
        }

        private static void ValidateAbilityTaskInstance(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<AbilityTaskInstance>();
            world.Query(in query, (Entity owner, ref AbilityTaskInstance task) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, task.Target, nameof(AbilityTaskInstance), nameof(AbilityTaskInstance.Target));
                ValidateTarget(world, policy, owner, task.TargetContext, nameof(AbilityTaskInstance), nameof(AbilityTaskInstance.TargetContext));

                unsafe
                {
                    for (int i = 0; i < task.MultiTargetCount; i++)
                    {
                        ValidateFlattenedTarget(
                            world,
                            policy,
                            owner,
                            task.MultiTargetIds[i],
                            task.MultiTargetWorldIds[i],
                            task.MultiTargetVersions[i],
                            nameof(AbilityTaskInstance),
                            $"multiTarget={i}");
                    }
                }
            });
        }

        private static void ValidateTeamEntityRef(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<TeamEntityRef>();
            world.Query(in query, (Entity owner, ref TeamEntityRef teamRef) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, teamRef.Value, nameof(TeamEntityRef), nameof(TeamEntityRef.Value));
            });
        }

        private static void ValidateOrderBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<OrderBuffer>();
            world.Query(in query, (Entity owner, ref OrderBuffer orders) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                if (orders.HasActive)
                {
                    ValidateQueuedOrder(world, policy, owner, in orders.ActiveOrder, nameof(OrderBuffer), "active");
                }

                if (orders.HasPending)
                {
                    ValidateQueuedOrder(world, policy, owner, in orders.PendingOrder, nameof(OrderBuffer), "pending");
                }

                for (int i = 0; i < orders.QueuedCount; i++)
                {
                    QueuedOrder queued = orders.GetQueued(i);
                    ValidateQueuedOrder(world, policy, owner, in queued, nameof(OrderBuffer), $"queued={i}");
                }
            });
        }

        private static void ValidateOrderContinuationBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<OrderContinuationBuffer>();
            world.Query(in query, (Entity owner, ref OrderContinuationBuffer continuations) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < continuations.Count; i++)
                {
                    OrderContinuationEntry entry = continuations.Get(i);
                    ValidateOrder(world, policy, owner, in entry.Order, nameof(OrderContinuationBuffer), $"entry={i}");
                }
            });
        }

        private static void ValidateChildOf(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ChildOf>();
            world.Query(in query, (Entity owner, ref ChildOf childOf) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, childOf.Parent, nameof(ChildOf), nameof(ChildOf.Parent));
            });
        }

        private static void ValidateEffectContext(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<EffectContext>();
            world.Query(in query, (Entity owner, ref EffectContext context) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, context.Source, nameof(EffectContext), nameof(EffectContext.Source));
                ValidateTarget(world, policy, owner, context.Target, nameof(EffectContext), nameof(EffectContext.Target));
                ValidateTarget(world, policy, owner, context.TargetContext, nameof(EffectContext), nameof(EffectContext.TargetContext));
            });
        }

        private static void ValidateDisplacementState(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<DisplacementState>();
            world.Query(in query, (Entity owner, ref DisplacementState displacement) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, displacement.TargetEntity, nameof(DisplacementState), nameof(DisplacementState.TargetEntity));
                ValidateTarget(world, policy, owner, displacement.SourceEntity, nameof(DisplacementState), nameof(DisplacementState.SourceEntity));
                ValidateTarget(world, policy, owner, displacement.DirectionTargetEntity, nameof(DisplacementState), nameof(DisplacementState.DirectionTargetEntity));
            });
        }

        private static void ValidateProjectileState(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ProjectileState>();
            world.Query(in query, (Entity owner, ref ProjectileState projectile) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, projectile.Source, nameof(ProjectileState), nameof(ProjectileState.Source));
                ValidateTarget(world, policy, owner, projectile.Target, nameof(ProjectileState), nameof(ProjectileState.Target));

                unsafe
                {
                    for (int i = 0; i < projectile.DistinctHitCount; i++)
                    {
                        ValidateFlattenedTarget(
                            world,
                            policy,
                            owner,
                            projectile.HitIds[i],
                            projectile.HitWorldIds[i],
                            projectile.HitVersions[i],
                            nameof(ProjectileState),
                            $"hit={i}");
                    }
                }
            });
        }

        private static void ValidateScopeRefBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ScopeRefBuffer>();
            world.Query(in query, (Entity owner, ref ScopeRefBuffer refs) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                unsafe
                {
                    for (int i = 0; i < refs.Count; i++)
                    {
                        ValidateFlattenedTarget(
                            world,
                            policy,
                            owner,
                            refs.EntityIds[i],
                            refs.EntityWorldIds[i],
                            refs.EntityVersions[i],
                            nameof(ScopeRefBuffer),
                            $"scope={refs.ScopeKeyIds[i]}");
                    }
                }
            });
        }

        private static void ValidateUtilityAiState(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<UtilityAiState>();
            world.Query(in query, (Entity owner, ref UtilityAiState state) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, state.CurrentTarget, nameof(UtilityAiState), nameof(UtilityAiState.CurrentTarget));
            });
        }

        private static void ValidateUtilityAiDecisionTrace(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<UtilityAiDecisionTrace>();
            world.Query(in query, (Entity owner, ref UtilityAiDecisionTrace trace) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, trace.BestTarget, nameof(UtilityAiDecisionTrace), nameof(UtilityAiDecisionTrace.BestTarget));
            });
        }

        private static void ValidateUtilityAiCombatMemory(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<UtilityAiCombatMemory>();
            world.Query(in query, (Entity owner, ref UtilityAiCombatMemory memory) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, memory.LastAttacker, nameof(UtilityAiCombatMemory), nameof(UtilityAiCombatMemory.LastAttacker));
                ValidateTarget(world, policy, owner, memory.LastSeenTarget, nameof(UtilityAiCombatMemory), nameof(UtilityAiCombatMemory.LastSeenTarget));
            });
        }

        private static void ValidateSelectionContainerOwner(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<SelectionContainerOwner>();
            world.Query(in query, (Entity owner, ref SelectionContainerOwner value) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, value.Value, nameof(SelectionContainerOwner), nameof(SelectionContainerOwner.Value));
            });
        }

        private static void ValidateSelectionMemberContainer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<SelectionMemberContainer>();
            world.Query(in query, (Entity owner, ref SelectionMemberContainer value) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, value.Value, nameof(SelectionMemberContainer), nameof(SelectionMemberContainer.Value));
            });
        }

        private static void ValidateSelectionMemberTarget(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<SelectionMemberTarget>();
            world.Query(in query, (Entity owner, ref SelectionMemberTarget value) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, value.Value, nameof(SelectionMemberTarget), nameof(SelectionMemberTarget.Value));
            });
        }

        private static void ValidateSelectionViewBindingViewer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<SelectionViewBindingViewer>();
            world.Query(in query, (Entity owner, ref SelectionViewBindingViewer value) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, value.Value, nameof(SelectionViewBindingViewer), nameof(SelectionViewBindingViewer.Value));
            });
        }

        private static void ValidateSelectionViewBindingContainer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<SelectionViewBindingContainer>();
            world.Query(in query, (Entity owner, ref SelectionViewBindingContainer value) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, value.Value, nameof(SelectionViewBindingContainer), nameof(SelectionViewBindingContainer.Value));
            });
        }

        private static void ValidateSelectionLeaseContainer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<SelectionLeaseContainer>();
            world.Query(in query, (Entity owner, ref SelectionLeaseContainer value) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, value.Value, nameof(SelectionLeaseContainer), nameof(SelectionLeaseContainer.Value));
            });
        }

        private static void ValidateItemLocationCm(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ItemLocationCm>();
            world.Query(in query, (Entity owner, ref ItemLocationCm location) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, location.Container, nameof(ItemLocationCm), nameof(ItemLocationCm.Container));
            });
        }

        private static void ValidateItemMountedContainerCm(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ItemMountedContainerCm>();
            world.Query(in query, (Entity owner, ref ItemMountedContainerCm mounted) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, mounted.ParentItem, nameof(ItemMountedContainerCm), nameof(ItemMountedContainerCm.ParentItem));
            });
        }

        private static void ValidateItemGrantedSlotBuffer(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<ItemGrantedSlotBuffer>();
            world.Query(in query, (Entity owner, ref ItemGrantedSlotBuffer slots) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                unsafe
                {
                    for (int i = 0; i < ItemGrantedSlotBuffer.CAPACITY; i++)
                    {
                        ValidateFlattenedTarget(
                            world,
                            policy,
                            owner,
                            slots.SourceItemIds[i],
                            slots.SourceItemWorldIds[i],
                            slots.SourceItemVersions[i],
                            nameof(ItemGrantedSlotBuffer),
                            $"slot={i}");
                    }
                }
            });
        }

        private static void ValidatePresentationOwnerHasPerformerPayload(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<PresentationOwnerHasPerformerPayload>();
            world.Query(in query, (Entity owner, ref PresentationOwnerHasPerformerPayload payload) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(
                    world,
                    policy,
                    owner,
                    payload.SingleRootPerformer,
                    nameof(PresentationOwnerHasPerformerPayload),
                    nameof(PresentationOwnerHasPerformerPayload.SingleRootPerformer));
            });
        }

        private static void ValidatePerformerState(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<PerformerState>();
            world.Query(in query, (Entity owner, ref PerformerState state) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, state.OwnerEntity, nameof(PerformerState), nameof(PerformerState.OwnerEntity));
            });
        }

        private static void ValidatePerformerParent(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<PerformerParent>();
            world.Query(in query, (Entity owner, ref PerformerParent parent) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                ValidateTarget(world, policy, owner, parent.Parent, nameof(PerformerParent), nameof(PerformerParent.Parent));
            });
        }

        private static void ValidatePerformerChildren(World world, SaveEntityInclusionPolicy policy)
        {
            var query = new QueryDescription().WithAll<PerformerChildren>();
            world.Query(in query, (Entity owner, ref PerformerChildren children) =>
            {
                if (!policy.ShouldInclude(world, owner))
                {
                    return;
                }

                for (int i = 0; i < children.Count; i++)
                {
                    ValidateTarget(world, policy, owner, children.Get(i), nameof(PerformerChildren), $"index={i}");
                }
            });
        }

        private static void ValidateQueuedOrder(
            World world,
            SaveEntityInclusionPolicy policy,
            Entity owner,
            in QueuedOrder queued,
            string componentName,
            string lane)
        {
            ValidateOrder(world, policy, owner, in queued.Order, componentName, lane);
        }

        private static void ValidateOrder(
            World world,
            SaveEntityInclusionPolicy policy,
            Entity owner,
            in Order order,
            string componentName,
            string lane)
        {
            ValidateTarget(world, policy, owner, order.Actor, componentName, $"{lane}.Actor");
            ValidateTarget(world, policy, owner, order.Target, componentName, $"{lane}.Target");
            ValidateTarget(world, policy, owner, order.TargetContext, componentName, $"{lane}.TargetContext");
            ValidateTarget(world, policy, owner, order.Args.Selection.Container, componentName, $"{lane}.Args.Selection.Container");
        }

        private static void ValidateAbilitySlot(
            World world,
            SaveEntityInclusionPolicy policy,
            Entity owner,
            in AbilitySlotState slot,
            string componentName,
            string lane)
        {
            ValidateFlattenedTarget(
                world,
                policy,
                owner,
                slot.TemplateEntityId,
                slot.TemplateEntityWorldId,
                slot.TemplateEntityVersion,
                componentName,
                lane);
        }

        private static void ValidateFlattenedTarget(
            World world,
            SaveEntityInclusionPolicy policy,
            Entity owner,
            int id,
            int worldId,
            int version,
            string componentName,
            string lane)
        {
            if (IsEmptyEntityReference(id, worldId, version))
            {
                return;
            }

            Entity target = EntityUtil.Reconstruct(id, worldId, version);
            ValidateTarget(world, policy, owner, target, componentName, lane);
        }

        private static void ValidateTarget(
            World world,
            SaveEntityInclusionPolicy policy,
            Entity owner,
            Entity target,
            string componentName,
            string lane)
        {
            if (IsEmptyEntityReference(target))
            {
                return;
            }

            if (!world.IsAlive(target))
            {
                throw new SaveContextException(
                    $"Save entity reference validation failed: {componentName} on entity {owner.Id}:{owner.WorldId}:{owner.Version} references missing entity {target.Id}:{target.WorldId}:{target.Version} ({lane}).");
            }

            if (!policy.ShouldInclude(world, target))
            {
                throw new SaveContextException(
                    $"Save entity reference validation failed: {componentName} on entity {owner.Id}:{owner.WorldId}:{owner.Version} references excluded entity {target.Id}:{target.WorldId}:{target.Version} ({lane}).");
            }
        }

        private static bool IsEmptyEntityReference(Entity entity)
        {
            return entity == Entity.Null ||
                IsEmptyEntityReference(entity.Id, entity.WorldId, entity.Version);
        }

        private static bool IsEmptyEntityReference(int id, int worldId, int version)
        {
            return (id == 0 && worldId == 0 && version == 0) ||
                (id == Entity.Null.Id && worldId == Entity.Null.WorldId && version == Entity.Null.Version);
        }
    }
}
