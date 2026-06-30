using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Morph
{
    internal readonly struct MorphSnapshot
    {
        public readonly bool HasPlayerOwner;
        public readonly PlayerOwner PlayerOwner;
        public readonly bool HasTeam;
        public readonly Team Team;
        public readonly AttributeBuffer Attributes;
        public readonly bool HasAttributes;
        public readonly GameplayTagContainer Tags;
        public readonly bool HasTags;
        public readonly int StableId;
        public readonly bool HasStableId;

        public static MorphSnapshot Capture(World world, Entity source, in MorphProfileDescriptor profile)
        {
            bool hasPlayerOwner = profile.CopyPlayerOwner && world.Has<PlayerOwner>(source);
            bool hasTeam = profile.CopyTeam && world.Has<Team>(source);
            bool hasAttributes = profile.AttributeInheritMode != MorphAttributeInheritMode.None && world.Has<AttributeBuffer>(source);
            bool hasTags = (profile.CarryTagIds.Length > 0 || profile.StripTagIds.Length > 0) && world.Has<GameplayTagContainer>(source);
            bool hasStableId = world.Has<PresentationStableId>(source);

            return new MorphSnapshot(
                hasPlayerOwner,
                hasPlayerOwner ? world.Get<PlayerOwner>(source) : default,
                hasTeam,
                hasTeam ? world.Get<Team>(source) : default,
                hasAttributes ? world.Get<AttributeBuffer>(source) : default,
                hasAttributes,
                hasTags ? world.Get<GameplayTagContainer>(source) : default,
                hasTags,
                hasStableId ? world.Get<PresentationStableId>(source).Value : 0,
                hasStableId);
        }

        private MorphSnapshot(
            bool hasPlayerOwner,
            PlayerOwner playerOwner,
            bool hasTeam,
            Team team,
            AttributeBuffer attributes,
            bool hasAttributes,
            GameplayTagContainer tags,
            bool hasTags,
            int stableId,
            bool hasStableId)
        {
            HasPlayerOwner = hasPlayerOwner;
            PlayerOwner = playerOwner;
            HasTeam = hasTeam;
            Team = team;
            Attributes = attributes;
            HasAttributes = hasAttributes;
            Tags = tags;
            HasTags = hasTags;
            StableId = stableId;
            HasStableId = hasStableId;
        }
    }

    internal static class MorphInheritanceApplier
    {
        public static void Apply(World world, Entity target, in MorphSnapshot snapshot, in MorphProfileDescriptor profile)
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

            if (snapshot.HasAttributes && world.Has<AttributeBuffer>(target))
            {
                ApplyAttributes(world, target, in snapshot.Attributes, in profile);
            }

            if (snapshot.HasTags && world.Has<GameplayTagContainer>(target))
            {
                ref var targetTags = ref world.Get<GameplayTagContainer>(target);
                ApplyTags(ref targetTags, in snapshot.Tags, in profile);
            }

            if (profile.EffectInheritMode == MorphEffectInheritMode.StripAll)
            {
                MorphEffectCleanup.EnsureTargetStartsWithoutInheritedEffects(world, target);
            }
        }

        private static void ApplyAttributes(World world, Entity target, in AttributeBuffer source, in MorphProfileDescriptor profile)
        {
            switch (profile.AttributeInheritMode)
            {
                case MorphAttributeInheritMode.AllDefined:
                    CopyAllDefinedAttributes(world, target, in source);
                    break;
                case MorphAttributeInheritMode.IntersectByName:
                    CopyNamedAttributes(world, target, in source, profile.InheritAttributeIds);
                    break;
            }
        }

        private static void CopyAllDefinedAttributes(World world, Entity target, in AttributeBuffer source)
        {
            for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
            {
                if (!source.HasAttribute(attributeId) || !world.Get<AttributeBuffer>(target).HasAttribute(attributeId))
                {
                    continue;
                }

                AttributeMutationOps.SetBase(world, target, attributeId, source.GetCurrent(attributeId));
            }
        }

        private static void CopyNamedAttributes(World world, Entity target, in AttributeBuffer source, int[] attributeIds)
        {
            for (int i = 0; i < attributeIds.Length; i++)
            {
                int attributeId = attributeIds[i];
                if (!source.HasAttribute(attributeId) || !world.Get<AttributeBuffer>(target).HasAttribute(attributeId))
                {
                    continue;
                }

                AttributeMutationOps.SetBase(world, target, attributeId, source.GetCurrent(attributeId));
            }
        }

        private static void ApplyTags(ref GameplayTagContainer target, in GameplayTagContainer source, in MorphProfileDescriptor profile)
        {
            for (int i = 0; i < profile.StripTagIds.Length; i++)
            {
                target.RemoveTag(profile.StripTagIds[i]);
            }

            for (int i = 0; i < profile.CarryTagIds.Length; i++)
            {
                int tagId = profile.CarryTagIds[i];
                if (source.HasTag(tagId))
                {
                    target.AddTag(tagId);
                }
            }
        }
    }

    internal static class MorphEffectCleanup
    {
        public static void EnsureTargetStartsWithoutInheritedEffects(World world, Entity target)
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
    }
}
