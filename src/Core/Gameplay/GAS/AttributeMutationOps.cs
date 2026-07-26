using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Keeps direct attribute mutations on the same dirty/refresh path as the
    /// rest of GAS so presentation and deferred triggers do not miss updates.
    /// </summary>
    public static class AttributeMutationOps
    {
        public static void AddCurrent(World world, Entity target, int attributeId, float delta, TagOps tagOps)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            SetCurrent(world, target, attributeId, attributes.GetCurrent(attributeId) + delta, tagOps);
        }

        public static void SetCurrent(World world, Entity target, int attributeId, float value, TagOps tagOps)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            RequireTagOps(tagOps);
            EnsureDirtyFlags(world, target);
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            AttributeBuffer attributesBefore = attributes;
            DirtyFlags dirtyBefore = world.Get<DirtyFlags>(target);
            float before = attributes.GetCurrent(attributeId);
            attributes.SetCurrent(attributeId, value);
            float after = attributes.GetCurrent(attributeId);
            if (before == after)
            {
                return;
            }

            try
            {
                world.Get<DirtyFlags>(target).MarkAttributeDirty(attributeId);
                tagOps?.MarkDirtyEntity(world, target);
            }
            catch
            {
                attributes = attributesBefore;
                world.Get<DirtyFlags>(target) = dirtyBefore;
                throw;
            }

            MarkPresentationChanged(world, target, attributeId);
        }

        public static void SetBase(World world, Entity target, int attributeId, float value, TagOps tagOps)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            RequireTagOps(tagOps);
            EnsureDirtyFlags(world, target);
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            AttributeBuffer attributesBefore = attributes;
            DirtyFlags dirtyBefore = world.Get<DirtyFlags>(target);
            float beforeBase = attributes.GetBase(attributeId);
            float beforeCurrent = attributes.GetCurrent(attributeId);
            attributes.SetBase(attributeId, value);
            float afterBase = attributes.GetBase(attributeId);
            float afterCurrent = attributes.GetCurrent(attributeId);
            if (beforeBase == afterBase && beforeCurrent == afterCurrent)
            {
                return;
            }

            try
            {
                world.Get<DirtyFlags>(target).MarkAttributeDirty(attributeId);
                tagOps?.MarkDirtyEntity(world, target);
            }
            catch
            {
                attributes = attributesBefore;
                world.Get<DirtyFlags>(target) = dirtyBefore;
                throw;
            }

            MarkPresentationChanged(world, target, attributeId);
        }

        public static void ApplyModifiers(World world, Entity target, in EffectModifiers modifiers, TagOps tagOps)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            RequireTagOps(tagOps);
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            Span<float> beforeValues = stackalloc float[AttributeBuffer.MAX_ATTRS];
            ulong touchedMask = 0UL;
            for (int i = 0; i < modifiers.Count; i++)
            {
                int attributeId = modifiers.Get(i).AttributeId;
                if (attributeId < 0 || attributeId >= AttributeBuffer.MAX_ATTRS)
                {
                    continue;
                }

                ulong bit = 1UL << attributeId;
                if ((touchedMask & bit) != 0UL)
                {
                    continue;
                }

                touchedMask |= bit;
                beforeValues[attributeId] = attributes.GetCurrent(attributeId);
            }

            if (touchedMask == 0UL)
            {
                return;
            }

            EnsureDirtyFlags(world, target);
            AttributeBuffer attributesBefore = attributes;
            DirtyFlags dirtyBefore = world.Get<DirtyFlags>(target);
            EffectModifierOps.Apply(in modifiers, ref attributes);

            bool hasDirty = false;
            ulong changedMask = 0UL;
            for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
            {
                ulong bit = 1UL << attributeId;
                if ((touchedMask & bit) == 0UL)
                {
                    continue;
                }

                if (beforeValues[attributeId] == attributes.GetCurrent(attributeId))
                {
                    continue;
                }

                if (!hasDirty)
                {
                    hasDirty = true;
                }

                world.Get<DirtyFlags>(target).MarkAttributeDirty(attributeId);
                changedMask |= bit;
            }

            if (hasDirty)
            {
                try
                {
                    tagOps?.MarkDirtyEntity(world, target);
                }
                catch
                {
                    attributes = attributesBefore;
                    world.Get<DirtyFlags>(target) = dirtyBefore;
                    throw;
                }

                for (int attributeId = 0; attributeId < AttributeBuffer.MAX_ATTRS; attributeId++)
                {
                    if ((changedMask & (1UL << attributeId)) != 0UL)
                    {
                        MarkPresentationChanged(world, target, attributeId);
                    }
                }
            }
        }

        private static void EnsureDirtyFlags(World world, Entity target)
        {
            if (!world.Has<DirtyFlags>(target))
            {
                throw new InvalidOperationException(
                    $"{TagOps.MissingDirtyFlagsError}: entity={target.Id}, operation=AttributeMutationOps.");
            }
        }

        private static void RequireTagOps(TagOps tagOps)
        {
            if (tagOps == null)
            {
                throw new InvalidOperationException(TagOps.MissingTagOpsError);
            }
        }

        private static void MarkPresentationChanged(World world, Entity target, int attributeId)
        {
            if (!world.Has<GameplayAttributeChangedBits>(target))
            {
                world.Add(target, new GameplayAttributeChangedBits());
            }

            world.Get<GameplayAttributeChangedBits>(target).Mark(attributeId);
        }
    }
}
