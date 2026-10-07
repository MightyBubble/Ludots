using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public static class EntityLifecycleAtomicOps
    {
        public static Entity MaterializeTemplate(
            EntityLifecycleRuntimeServices services,
            Entity source,
            string templateId,
            Fix64Vec2 positionCm)
        {
            EntityTemplate template = services.RequireTemplate(templateId);
            World world = services.World;
            Entity entity = services.Builder
                .UseTemplate(templateId)
                .WithEntityContext($"EntityLifecycle MaterializeTemplate '{templateId}'")
                .Build();

            ApplyTemplateKey(services, entity, templateId);
            if (world.Has<PresentationStableId>(entity))
            {
                world.Remove<PresentationStableId>(entity);
            }

            ApplyWorldPosition(world, entity, positionCm);
            RuntimeEntityMapOwnershipSupport.TryCopyMapEntityFromSource(world, source, entity);
            services.PresenterBootstrap.TryBootstrap(entity, templateId);
            services.EntityTriggerGraphMounts?.MountRuntimeSpawned(entity, templateId, template.TriggerGraphs);
            return entity;
        }

        public static void CopyIdentityComponents(World world, Entity target, in LifecycleSnapshot snapshot)
        {
            CopyIdentityComponents(world, target, Entity.Null, in snapshot, ownership: null, relationships: null, memberOfTypeId: -1);
        }

        public static void CopyIdentityComponents(
            World world,
            Entity target,
            Entity source,
            in LifecycleSnapshot snapshot,
            OwnershipResolver? ownership,
            RelationshipRuntime? relationships,
            int memberOfTypeId)
        {
            if (ownership != null &&
                world.IsAlive(source) &&
                ownership.TryResolveRootOwner(source, out Entity root) &&
                world.IsAlive(root) &&
                world.Has<PlayerIdentity>(root))
            {
                ownership.EnsureOwnership(root, target);
                ParticipantIdentityProjector.SyncPlayerOwner(world, target, ownership);
            }
            else if (snapshot.HasPlayerOwner)
            {
                ParticipantIdentityProjector.UpsertPlayerOwner(world, target, snapshot.PlayerOwner.PlayerId);
            }

            if (relationships != null &&
                memberOfTypeId >= 0 &&
                world.IsAlive(source) &&
                ParticipantIdentityProjector.TryFindTeamRepresentative(world, relationships, memberOfTypeId, source, out Entity teamRepresentative))
            {
                relationships.EnsureLink(target, teamRepresentative, memberOfTypeId);
                ParticipantIdentityProjector.ProjectTeam(world, target, teamRepresentative);
            }
            else if (snapshot.HasTeam)
            {
                ParticipantIdentityProjector.UpsertTeam(world, target, snapshot.Team.Id);
            }
        }

        public static void CopyAttributeSlice(
            EntityLifecycleRuntimeServices services,
            Entity target,
            in LifecycleSnapshot snapshot,
            LifecycleTransactionState state)
        {
            World world = services.World;
            if (state.AttributeSliceCount == 0)
            {
                throw new InvalidOperationException("CopyAttributeSlice requires at least one configured lifecycle attribute slice.");
            }

            if (!snapshot.HasAttributes)
            {
                throw new LifecycleExecutionException(
                    "CopyAttributeSlice failed because source is missing AttributeBuffer.");
            }

            if (!world.Has<AttributeBuffer>(target))
            {
                throw new LifecycleExecutionException(
                    "CopyAttributeSlice failed because target template is missing AttributeBuffer.");
            }

            ref AttributeBuffer targetAttributes = ref world.Get<AttributeBuffer>(target);
            for (int i = 0; i < state.AttributeSliceCount; i++)
            {
                int attributeId = state.GetAttributeSliceId(i);
                if (attributeId < AttributeBuffer.MAX_ATTRS
                    ? !snapshot.Attributes.HasAttribute(attributeId)
                    : !SnapshotHasHighAttribute(snapshot, attributeId))
                {
                    throw new LifecycleExecutionException(
                        $"CopyAttributeSlice failed because source is missing attribute id '{attributeId}'.");
                }

                if (!targetAttributes.HasAttribute(attributeId))
                {
                    string attributeName = AttributeRegistry.GetName(attributeId) ?? attributeId.ToString();
                    throw new LifecycleExecutionException(
                        $"CopyAttributeSlice failed because target template is missing attribute '{attributeName}'.");
                }

                float value;
                if (attributeId >= AttributeBuffer.MAX_ATTRS)
                {
                    if (state.AttributeSliceSource != LifecycleAttributeValueSource.Current)
                    {
                        throw new LifecycleExecutionException(
                            $"CopyAttributeSlice: 高槽位（id {attributeId} ≥ 64）切片只支持 Current 值源（RFC-0067 P1）。");
                    }

                    value = snapshot.HighAttributes![attributeId - AttributeBuffer.MAX_ATTRS];
                }
                else
                {
                    value = state.AttributeSliceSource switch
                    {
                        LifecycleAttributeValueSource.Base => snapshot.Attributes.GetBase(attributeId),
                        LifecycleAttributeValueSource.Current => snapshot.Attributes.GetCurrent(attributeId),
                        _ => throw new InvalidOperationException($"Unsupported lifecycle attribute value source '{state.AttributeSliceSource}'."),
                    };
                }

                AttributeMutationOps.SetBase(world, target, attributeId, value, services.TagOps);
            }
        }

        private static bool SnapshotHasHighAttribute(LifecycleSnapshot snapshot, int attributeId)
        {
            return snapshot.HighAttributes != null &&
                attributeId >= AttributeBuffer.MAX_ATTRS &&
                attributeId - AttributeBuffer.MAX_ATTRS < snapshot.HighAttributes.Length;
        }

        public static void ClearActiveEffects(World world, Entity target)
        {
            if (!world.IsAlive(target) || !world.Has<ActiveEffectContainer>(target))
            {
                return;
            }

            ref var container = ref world.Get<ActiveEffectContainer>(target);
            while (container.Count > 0)
            {
                Entity effectEntity = container.GetEntity(0);
                if (world.IsAlive(effectEntity))
                {
                    world.Destroy(effectEntity);
                }

                container.Remove(effectEntity);
            }
        }

        public static void TransferStableId(World world, Entity target, in LifecycleSnapshot snapshot)
        {
            if (!snapshot.HasStableId)
            {
                throw new LifecycleExecutionException(
                    "TransferStableId failed because source PresentationStableId is required.");
            }

            var stableId = new PresentationStableId { Value = snapshot.StableId };
            if (world.Has<PresentationStableId>(target))
            {
                world.Set(target, stableId);
            }
            else
            {
                world.Add(target, stableId);
            }
        }

        public static void ConsumeEntity(World world, Entity source, string reason)
        {
            PresentationEntityLifecycle.RequestDestroy(world, source, reason);
        }

        public static void RollbackMaterializedTarget(World world, Entity target)
        {
            if (!world.IsAlive(target))
            {
                return;
            }

            if (world.Has<PresentationStableId>(target))
            {
                PresentationEntityLifecycle.RequestDestroy(world, target, "Entity lifecycle transaction rollback");
                return;
            }

            world.Destroy(target);
        }

        private static void ApplyTemplateKey(EntityLifecycleRuntimeServices services, Entity entity, string templateId)
        {
            World world = services.World;
            int templateKeyId = services.TemplateKeys.GetId(templateId);
            if (templateKeyId <= 0)
            {
                templateKeyId = services.TemplateKeys.Register(templateId);
            }

            var templateKey = new EntityTemplateKeyRef { TemplateKeyId = templateKeyId };
            if (world.Has<EntityTemplateKeyRef>(entity))
            {
                world.Set(entity, templateKey);
            }
            else
            {
                world.Add(entity, templateKey);
            }
        }

        private static void ApplyWorldPosition(World world, Entity entity, Fix64Vec2 worldPositionCm)
        {
            var position = new WorldPositionCm { Value = worldPositionCm };
            var previous = new PreviousWorldPositionCm { Value = worldPositionCm };

            if (world.Has<WorldPositionCm>(entity))
            {
                world.Set(entity, position);
            }
            else
            {
                world.Add(entity, position);
            }

            if (world.Has<PreviousWorldPositionCm>(entity))
            {
                world.Set(entity, previous);
            }
            else
            {
                world.Add(entity, previous);
            }
        }
    }
}
