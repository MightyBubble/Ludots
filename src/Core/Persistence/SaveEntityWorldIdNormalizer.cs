using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Gameplay.AI.Components;
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
    public static class SaveEntityWorldIdNormalizer
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

        public static void Normalize(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            NormalizeBlackboardEntityBuffer(world);
            NormalizeChildrenBuffer(world);
            NormalizeActiveEffectContainer(world);
            NormalizeAbilityStateBuffer(world);
            NormalizeGrantedSlotBuffer(world);
            NormalizeAbilityFormSlotBuffer(world);
            NormalizeAbilityExecInstance(world);
            NormalizeAbilityTaskInstance(world);
            NormalizeTeamEntityRef(world);
            NormalizeOrderBuffer(world);
            NormalizeOrderContinuationBuffer(world);
            NormalizeChildOf(world);
            NormalizeEffectContext(world);
            NormalizeDisplacementState(world);
            NormalizeProjectileState(world);
            NormalizeScopeRefBuffer(world);
            NormalizeUtilityAiState(world);
            NormalizeUtilityAiDecisionTrace(world);
            NormalizeUtilityAiCombatMemory(world);
            NormalizeSelectionContainerOwner(world);
            NormalizeSelectionMemberContainer(world);
            NormalizeSelectionMemberTarget(world);
            NormalizeSelectionViewBindingViewer(world);
            NormalizeSelectionViewBindingContainer(world);
            NormalizeSelectionLeaseContainer(world);
            NormalizeItemLocationCm(world);
            NormalizeItemMountedContainerCm(world);
            NormalizeItemGrantedSlotBuffer(world);
            NormalizePresentationOwnerHasPerformerPayload(world);
            NormalizePerformerState(world);
            NormalizePerformerParent(world);
            NormalizePerformerChildren(world);
        }

        private static void NormalizeBlackboardEntityBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<BlackboardEntityBuffer>();
            world.Query(in query, (ref BlackboardEntityBuffer refs) =>
            {
                unsafe
                {
                    for (int i = 0; i < refs.Count; i++)
                    {
                        NormalizeFlattenedWorldId(ref refs.EntityIds[i], ref refs.WorldIds[i], ref refs.Versions[i], worldId);
                    }
                }
            });
        }

        private static void NormalizeChildrenBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ChildrenBuffer>();
            world.Query(in query, (ref ChildrenBuffer children) =>
            {
                unsafe
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        NormalizeFlattenedWorldId(ref children.ChildIds[i], ref children.ChildWorldIds[i], ref children.ChildVersions[i], worldId);
                    }
                }
            });
        }

        private static void NormalizeActiveEffectContainer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ActiveEffectContainer>();
            world.Query(in query, (ref ActiveEffectContainer activeEffects) =>
            {
                unsafe
                {
                    for (int i = 0; i < activeEffects.Count; i++)
                    {
                        NormalizeFlattenedWorldId(ref activeEffects.Ids[i], ref activeEffects.WorldIds[i], ref activeEffects.Versions[i], worldId);
                    }
                }
            });
        }

        private static void NormalizeAbilityStateBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<AbilityStateBuffer>();
            world.Query(in query, (ref AbilityStateBuffer abilities) =>
            {
                unsafe
                {
                    for (int i = 0; i < abilities.Count; i++)
                    {
                        NormalizeFlattenedWorldId(ref abilities.TemplateIds[i], ref abilities.TemplateWorldIds[i], ref abilities.TemplateVersions[i], worldId);
                    }
                }
            });
        }

        private static void NormalizeGrantedSlotBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<GrantedSlotBuffer>();
            world.Query(in query, (ref GrantedSlotBuffer slots) =>
            {
                unsafe
                {
                    for (int i = 0; i < GrantedSlotBuffer.CAPACITY; i++)
                    {
                        NormalizeFlattenedWorldId(ref slots.TemplateIds[i], ref slots.TemplateWorldIds[i], ref slots.TemplateVersions[i], worldId);
                    }
                }
            });
        }

        private static void NormalizeAbilityFormSlotBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<AbilityFormSlotBuffer>();
            world.Query(in query, (ref AbilityFormSlotBuffer slots) =>
            {
                unsafe
                {
                    for (int i = 0; i < AbilityFormSlotBuffer.CAPACITY; i++)
                    {
                        NormalizeFlattenedWorldId(ref slots.TemplateIds[i], ref slots.TemplateWorldIds[i], ref slots.TemplateVersions[i], worldId);
                    }
                }
            });
        }

        private static void NormalizeAbilityExecInstance(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<AbilityExecInstance>();
            world.Query(in query, (ref AbilityExecInstance exec) =>
            {
                exec.Target = NormalizeEntity(exec.Target, worldId);
                exec.TargetContext = NormalizeEntity(exec.TargetContext, worldId);

                unsafe
                {
                    for (int i = 0; i < exec.MultiTargetCount; i++)
                    {
                        NormalizeFlattenedWorldId(
                            ref exec.MultiTargetIds[i],
                            ref exec.MultiTargetWorldIds[i],
                            ref exec.MultiTargetVersions[i],
                            worldId);
                    }
                }
            });
        }

        private static void NormalizeAbilityTaskInstance(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<AbilityTaskInstance>();
            world.Query(in query, (ref AbilityTaskInstance task) =>
            {
                task.Target = NormalizeEntity(task.Target, worldId);
                task.TargetContext = NormalizeEntity(task.TargetContext, worldId);

                unsafe
                {
                    for (int i = 0; i < task.MultiTargetCount; i++)
                    {
                        NormalizeFlattenedWorldId(
                            ref task.MultiTargetIds[i],
                            ref task.MultiTargetWorldIds[i],
                            ref task.MultiTargetVersions[i],
                            worldId);
                    }
                }
            });
        }

        private static void NormalizeTeamEntityRef(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<TeamEntityRef>();
            world.Query(in query, (ref TeamEntityRef teamRef) =>
            {
                teamRef.Value = NormalizeEntity(teamRef.Value, worldId);
            });
        }

        private static void NormalizeOrderBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<OrderBuffer>();
            world.Query(in query, (ref OrderBuffer orders) =>
            {
                if (orders.HasActive)
                {
                    QueuedOrder active = orders.ActiveOrder;
                    NormalizeQueuedOrder(ref active, worldId);
                    orders.ActiveOrder = active;
                }

                if (orders.HasPending)
                {
                    QueuedOrder pending = orders.PendingOrder;
                    NormalizeQueuedOrder(ref pending, worldId);
                    orders.PendingOrder = pending;
                }

                for (int i = 0; i < orders.QueuedCount; i++)
                {
                    QueuedOrder queued = orders.GetQueued(i);
                    NormalizeQueuedOrder(ref queued, worldId);
                    orders.SetQueued(i, in queued);
                }
            });
        }

        private static void NormalizeOrderContinuationBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<OrderContinuationBuffer>();
            world.Query(in query, (ref OrderContinuationBuffer continuations) =>
            {
                for (int i = 0; i < continuations.Count; i++)
                {
                    OrderContinuationEntry entry = continuations.Get(i);
                    NormalizeOrder(ref entry.Order, worldId);
                    continuations.Set(i, in entry);
                }
            });
        }

        private static void NormalizeChildOf(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ChildOf>();
            world.Query(in query, (ref ChildOf childOf) =>
            {
                childOf.Parent = NormalizeEntity(childOf.Parent, worldId);
            });
        }

        private static void NormalizeEffectContext(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<EffectContext>();
            world.Query(in query, (ref EffectContext context) =>
            {
                context.Source = NormalizeEntity(context.Source, worldId);
                context.Target = NormalizeEntity(context.Target, worldId);
                context.TargetContext = NormalizeEntity(context.TargetContext, worldId);
            });
        }

        private static void NormalizeDisplacementState(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<DisplacementState>();
            world.Query(in query, (ref DisplacementState displacement) =>
            {
                displacement.TargetEntity = NormalizeEntity(displacement.TargetEntity, worldId);
                displacement.SourceEntity = NormalizeEntity(displacement.SourceEntity, worldId);
                displacement.DirectionTargetEntity = NormalizeEntity(displacement.DirectionTargetEntity, worldId);
            });
        }

        private static void NormalizeProjectileState(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ProjectileState>();
            world.Query(in query, (ref ProjectileState projectile) =>
            {
                projectile.Source = NormalizeEntity(projectile.Source, worldId);
                projectile.Target = NormalizeEntity(projectile.Target, worldId);

                unsafe
                {
                    for (int i = 0; i < projectile.DistinctHitCount; i++)
                    {
                        NormalizeFlattenedWorldId(
                            ref projectile.HitIds[i],
                            ref projectile.HitWorldIds[i],
                            ref projectile.HitVersions[i],
                            worldId);
                    }
                }
            });
        }

        private static void NormalizeScopeRefBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ScopeRefBuffer>();
            world.Query(in query, (ref ScopeRefBuffer refs) =>
            {
                unsafe
                {
                    for (int i = 0; i < refs.Count; i++)
                    {
                        NormalizeFlattenedWorldId(
                            ref refs.EntityIds[i],
                            ref refs.EntityWorldIds[i],
                            ref refs.EntityVersions[i],
                            worldId);
                    }
                }
            });
        }

        private static void NormalizeUtilityAiState(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<UtilityAiState>();
            world.Query(in query, (ref UtilityAiState state) =>
            {
                state.CurrentTarget = NormalizeEntity(state.CurrentTarget, worldId);
            });
        }

        private static void NormalizeUtilityAiDecisionTrace(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<UtilityAiDecisionTrace>();
            world.Query(in query, (ref UtilityAiDecisionTrace trace) =>
            {
                trace.BestTarget = NormalizeEntity(trace.BestTarget, worldId);
            });
        }

        private static void NormalizeUtilityAiCombatMemory(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<UtilityAiCombatMemory>();
            world.Query(in query, (ref UtilityAiCombatMemory memory) =>
            {
                memory.LastAttacker = NormalizeEntity(memory.LastAttacker, worldId);
                memory.LastSeenTarget = NormalizeEntity(memory.LastSeenTarget, worldId);
            });
        }

        private static void NormalizeSelectionContainerOwner(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<SelectionContainerOwner>();
            world.Query(in query, (ref SelectionContainerOwner value) =>
            {
                value.Value = NormalizeEntity(value.Value, worldId);
            });
        }

        private static void NormalizeSelectionMemberContainer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<SelectionMemberContainer>();
            world.Query(in query, (ref SelectionMemberContainer value) =>
            {
                value.Value = NormalizeEntity(value.Value, worldId);
            });
        }

        private static void NormalizeSelectionMemberTarget(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<SelectionMemberTarget>();
            world.Query(in query, (ref SelectionMemberTarget value) =>
            {
                value.Value = NormalizeEntity(value.Value, worldId);
            });
        }

        private static void NormalizeSelectionViewBindingViewer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<SelectionViewBindingViewer>();
            world.Query(in query, (ref SelectionViewBindingViewer value) =>
            {
                value.Value = NormalizeEntity(value.Value, worldId);
            });
        }

        private static void NormalizeSelectionViewBindingContainer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<SelectionViewBindingContainer>();
            world.Query(in query, (ref SelectionViewBindingContainer value) =>
            {
                value.Value = NormalizeEntity(value.Value, worldId);
            });
        }

        private static void NormalizeSelectionLeaseContainer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<SelectionLeaseContainer>();
            world.Query(in query, (ref SelectionLeaseContainer value) =>
            {
                value.Value = NormalizeEntity(value.Value, worldId);
            });
        }

        private static void NormalizeItemLocationCm(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ItemLocationCm>();
            world.Query(in query, (ref ItemLocationCm location) =>
            {
                location.Container = NormalizeEntity(location.Container, worldId);
            });
        }

        private static void NormalizeItemMountedContainerCm(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ItemMountedContainerCm>();
            world.Query(in query, (ref ItemMountedContainerCm mounted) =>
            {
                mounted.ParentItem = NormalizeEntity(mounted.ParentItem, worldId);
            });
        }

        private static void NormalizeItemGrantedSlotBuffer(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<ItemGrantedSlotBuffer>();
            world.Query(in query, (ref ItemGrantedSlotBuffer slots) =>
            {
                unsafe
                {
                    for (int i = 0; i < ItemGrantedSlotBuffer.CAPACITY; i++)
                    {
                        NormalizeFlattenedWorldId(ref slots.SourceItemIds[i], ref slots.SourceItemWorldIds[i], ref slots.SourceItemVersions[i], worldId);
                    }
                }
            });
        }

        private static void NormalizePresentationOwnerHasPerformerPayload(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<PresentationOwnerHasPerformerPayload>();
            world.Query(in query, (ref PresentationOwnerHasPerformerPayload payload) =>
            {
                payload.SingleRootPerformer = NormalizeEntity(payload.SingleRootPerformer, worldId);
            });
        }

        private static void NormalizePerformerState(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<PerformerState>();
            world.Query(in query, (ref PerformerState state) =>
            {
                state.OwnerEntity = NormalizeEntity(state.OwnerEntity, worldId);
            });
        }

        private static void NormalizePerformerParent(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<PerformerParent>();
            world.Query(in query, (ref PerformerParent parent) =>
            {
                parent.Parent = NormalizeEntity(parent.Parent, worldId);
            });
        }

        private static void NormalizePerformerChildren(World world)
        {
            int worldId = world.Id;
            var query = new QueryDescription().WithAll<PerformerChildren>();
            world.Query(in query, (ref PerformerChildren children) =>
            {
                unsafe
                {
                    for (int i = 0; i < children.Count; i++)
                    {
                        NormalizeFlattenedWorldId(
                            ref children.ChildIds[i],
                            ref children.ChildWorldIds[i],
                            ref children.ChildVersions[i],
                            worldId);
                    }
                }
            });
        }

        private static void NormalizeQueuedOrder(ref QueuedOrder queued, int worldId)
        {
            NormalizeOrder(ref queued.Order, worldId);
        }

        private static void NormalizeOrder(ref Order order, int worldId)
        {
            order.Actor = NormalizeEntity(order.Actor, worldId);
            order.Target = NormalizeEntity(order.Target, worldId);
            order.TargetContext = NormalizeEntity(order.TargetContext, worldId);
            order.Args.Selection.Container = NormalizeEntity(order.Args.Selection.Container, worldId);
        }

        private static Entity NormalizeEntity(Entity value, int worldId)
        {
            if (IsEmptyEntityReference(value))
            {
                return value;
            }

            return EntityUtil.Reconstruct(value.Id, worldId, value.Version);
        }

        private static void NormalizeFlattenedWorldId(ref int id, ref int targetWorldId, ref int version, int sourceWorldId)
        {
            if (IsEmptyEntityReference(id, targetWorldId, version))
            {
                return;
            }

            targetWorldId = sourceWorldId;
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
