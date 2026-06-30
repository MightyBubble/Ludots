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
        public static void AddCurrent(World world, Entity target, int attributeId, float delta)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            SetCurrent(world, target, attributeId, attributes.GetCurrent(attributeId) + delta);
        }

        public static void SetCurrent(World world, Entity target, int attributeId, float value)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            float before = attributes.GetCurrent(attributeId);
            attributes.SetCurrent(attributeId, value);
            float after = attributes.GetCurrent(attributeId);
            if (before == after)
            {
                return;
            }

            MarkAttributeChanged(world, target, attributeId);
        }

        public static void SetBase(World world, Entity target, int attributeId, float value)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            float beforeBase = attributes.GetBase(attributeId);
            float beforeCurrent = attributes.GetCurrent(attributeId);
            attributes.SetBase(attributeId, value);
            float afterBase = attributes.GetBase(attributeId);
            float afterCurrent = attributes.GetCurrent(attributeId);
            if (beforeBase == afterBase && beforeCurrent == afterCurrent)
            {
                return;
            }

            MarkAttributeChanged(world, target, attributeId);
        }

        private static void MarkAttributeChanged(World world, Entity target, int attributeId)
        {
            EnsureDirtyFlags(world, target);
            world.Get<DirtyFlags>(target).MarkAttributeDirty(attributeId);
            MarkPresentationChanged(world, target, attributeId);
        }

        public static void ApplyModifiers(World world, Entity target, in EffectModifiers modifiers)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

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

            EffectModifierOps.Apply(in modifiers, ref attributes);

            if (touchedMask == 0UL)
            {
                return;
            }

            bool hasDirty = false;
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
                    EnsureDirtyFlags(world, target);
                    hasDirty = true;
                }

                world.Get<DirtyFlags>(target).MarkAttributeDirty(attributeId);
                MarkPresentationChanged(world, target, attributeId);
            }
        }

        private static void EnsureDirtyFlags(World world, Entity target)
        {
            if (!world.Has<DirtyFlags>(target))
            {
                world.Add(target, new DirtyFlags());
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
