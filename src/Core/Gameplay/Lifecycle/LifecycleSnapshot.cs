using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Lifecycle
{
    internal readonly struct LifecycleSnapshot
    {
        public readonly bool HasPlayerOwner;
        public readonly PlayerOwner PlayerOwner;
        public readonly bool HasTeam;
        public readonly Team Team;
        public readonly AttributeBuffer Attributes;
        public readonly bool HasAttributes;
        public readonly int StableId;
        public readonly bool HasStableId;

        public static LifecycleSnapshot CaptureDeployConsumeSource(World world, Entity source)
        {
            bool hasPlayerOwner = world.Has<PlayerOwner>(source);
            bool hasTeam = world.Has<Team>(source);
            bool hasAttributes = world.Has<AttributeBuffer>(source);
            bool hasStableId = world.Has<PresentationStableId>(source);

            return new LifecycleSnapshot(
                hasPlayerOwner,
                hasPlayerOwner ? world.Get<PlayerOwner>(source) : default,
                hasTeam,
                hasTeam ? world.Get<Team>(source) : default,
                hasAttributes ? world.Get<AttributeBuffer>(source) : default,
                hasAttributes,
                hasStableId ? world.Get<PresentationStableId>(source).Value : 0,
                hasStableId);
        }

        private LifecycleSnapshot(
            bool hasPlayerOwner,
            PlayerOwner playerOwner,
            bool hasTeam,
            Team team,
            AttributeBuffer attributes,
            bool hasAttributes,
            int stableId,
            bool hasStableId)
        {
            HasPlayerOwner = hasPlayerOwner;
            PlayerOwner = playerOwner;
            HasTeam = hasTeam;
            Team = team;
            Attributes = attributes;
            HasAttributes = hasAttributes;
            StableId = stableId;
            HasStableId = hasStableId;
        }
    }

    internal static class LifecycleDeployConsumeSourceApplier
    {
        public static void Apply(World world, Entity target, in LifecycleSnapshot snapshot)
        {
            CopyIdentity(world, target, in snapshot);
            CopyHealthFromCurrent(world, target, in snapshot);
            ClearActiveEffects(world, target);
        }

        private static void CopyIdentity(World world, Entity target, in LifecycleSnapshot snapshot)
        {
            if (snapshot.HasPlayerOwner)
            {
                if (world.Has<PlayerOwner>(target))
                {
                    world.Set(target, snapshot.PlayerOwner);
                }
                else
                {
                    world.Add(target, snapshot.PlayerOwner);
                }
            }

            if (snapshot.HasTeam)
            {
                if (world.Has<Team>(target))
                {
                    world.Set(target, snapshot.Team);
                }
                else
                {
                    world.Add(target, snapshot.Team);
                }
            }
        }

        private static void CopyHealthFromCurrent(World world, Entity target, in LifecycleSnapshot snapshot)
        {
            int healthAttributeId = AttributeRegistry.GetId("Health");
            if (!snapshot.HasAttributes || healthAttributeId < 0)
            {
                return;
            }

            if (!snapshot.Attributes.HasAttribute(healthAttributeId))
            {
                return;
            }

            if (!world.Has<AttributeBuffer>(target))
            {
                throw new LifecycleExecutionException(
                    "DeployConsumeSource failed because target template is missing AttributeBuffer.");
            }

            ref AttributeBuffer targetAttributes = ref world.Get<AttributeBuffer>(target);
            if (!targetAttributes.HasAttribute(healthAttributeId))
            {
                throw new LifecycleExecutionException(
                    "DeployConsumeSource failed because target template is missing Health attribute.");
            }

            AttributeMutationOps.SetBase(
                world,
                target,
                healthAttributeId,
                snapshot.Attributes.GetCurrent(healthAttributeId));
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
                    "DeployConsumeSource failed because source PresentationStableId is required.");
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
    }
}
