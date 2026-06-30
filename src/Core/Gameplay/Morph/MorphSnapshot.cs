using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
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
            bool hasTags = profile.TagInheritMode != MorphTagInheritMode.None && world.Has<GameplayTagContainer>(source);
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
                    CopyAllDefinedAttributes(world, target, in source, profile.AttributeValueSource);
                    break;
                case MorphAttributeInheritMode.IntersectByName:
                    CopyNamedAttributes(world, target, in source, profile.InheritAttributeIds, profile.AttributeValueSource);
                    break;
            }
        }

        private static void CopyAllDefinedAttributes(World world, Entity target, in AttributeBuffer source, MorphAttributeValueSource valueSource)
        {
            for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
            {
                if (!source.HasAttribute(attributeId))
                {
                    continue;
                }

                if (!world.Get<AttributeBuffer>(target).HasAttribute(attributeId))
                {
                    continue;
                }

                AttributeMutationOps.SetBase(world, target, attributeId, ReadAttributeValue(in source, attributeId, valueSource));
            }
        }

        private static void CopyNamedAttributes(
            World world,
            Entity target,
            in AttributeBuffer source,
            int[] attributeIds,
            MorphAttributeValueSource valueSource)
        {
            ref AttributeBuffer targetAttributes = ref world.Get<AttributeBuffer>(target);
            for (int i = 0; i < attributeIds.Length; i++)
            {
                int attributeId = attributeIds[i];
                if (!source.HasAttribute(attributeId))
                {
                    throw new MorphExecutionException(
                        $"Entity morph failed because source is missing inherited attribute id '{attributeId}'.");
                }

                if (!targetAttributes.HasAttribute(attributeId))
                {
                    string attributeName = AttributeRegistry.GetName(attributeId) ?? attributeId.ToString();
                    throw new MorphExecutionException(
                        $"Entity morph failed because target template is missing inherited attribute '{attributeName}'.");
                }

                AttributeMutationOps.SetBase(world, target, attributeId, ReadAttributeValue(in source, attributeId, valueSource));
            }
        }

        private static float ReadAttributeValue(in AttributeBuffer source, int attributeId, MorphAttributeValueSource valueSource)
        {
            return valueSource switch
            {
                MorphAttributeValueSource.Base => source.GetBase(attributeId),
                MorphAttributeValueSource.Current => source.GetCurrent(attributeId),
                _ => throw new InvalidOperationException($"Unsupported morph attribute value source '{valueSource}'."),
            };
        }

        private static void ApplyTags(ref GameplayTagContainer target, in GameplayTagContainer source, in MorphProfileDescriptor profile)
        {
            switch (profile.TagInheritMode)
            {
                case MorphTagInheritMode.StripListed:
                    StripTags(ref target, profile.StripTagIds);
                    break;
                case MorphTagInheritMode.CarryListed:
                    CarryTags(ref target, in source, profile.CarryTagIds);
                    break;
                case MorphTagInheritMode.StripListedAndCarryListed:
                    StripTags(ref target, profile.StripTagIds);
                    CarryTags(ref target, in source, profile.CarryTagIds);
                    break;
            }
        }

        private static void StripTags(ref GameplayTagContainer target, int[] stripTagIds)
        {
            for (int i = 0; i < stripTagIds.Length; i++)
            {
                target.RemoveTag(stripTagIds[i]);
            }
        }

        private static void CarryTags(ref GameplayTagContainer target, in GameplayTagContainer source, int[] carryTagIds)
        {
            for (int i = 0; i < carryTagIds.Length; i++)
            {
                int tagId = carryTagIds[i];
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
