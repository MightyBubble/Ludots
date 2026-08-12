using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Capacity;
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
                attributes.SetCurrent(attributeId, before);
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
            DirtyFlags dirtyBefore = world.Get<DirtyFlags>(target);
            float beforeBase = attributes.GetRawBase(attributeId);
            float beforeCap = attributes.GetRawCap(attributeId);
            float beforeCurrent = attributes.GetCurrent(attributeId);
            bool wasDefined = attributes.HasAttribute(attributeId);
            attributes.SetBase(attributeId, value);
            float afterBase = attributes.GetBase(attributeId);
            float afterCurrent = attributes.GetCurrent(attributeId);
            if (beforeBase == afterBase && beforeCurrent == afterCurrent && wasDefined)
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
                if (wasDefined)
                {
                    attributes.SetBase(attributeId, beforeBase);
                    attributes.SetRawCap(attributeId, beforeCap);
                    attributes.SetCurrent(attributeId, beforeCurrent);
                }

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
            int slots = GasLoadTimeCapacitySession.Plan.AttributeSlotCount;
            Span<float> beforeValues = stackalloc float[GasLoadTimeCapacityPlan.AbsoluteMaxAttributeSlots];
            Span<ulong> touchedWords = stackalloc ulong[DirtyFlags.MAX_ATTR_DIRTY_WORDS];
            touchedWords.Clear();
            for (int i = 0; i < modifiers.Count; i++)
            {
                int attributeId = modifiers.Get(i).AttributeId;
                if ((uint)attributeId >= (uint)slots)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(attributeId),
                        attributeId,
                        $"Modifier attributeId exceeds plan slots {slots}.");
                }

                int word = attributeId >> 6;
                ulong bit = 1UL << (attributeId & 63);
                if ((touchedWords[word] & bit) != 0UL)
                {
                    continue;
                }

                touchedWords[word] |= bit;
                beforeValues[attributeId] = attributes.GetCurrent(attributeId);
            }

            bool anyTouched = false;
            for (int w = 0; w < touchedWords.Length; w++)
            {
                if (touchedWords[w] != 0UL)
                {
                    anyTouched = true;
                    break;
                }
            }

            if (!anyTouched)
            {
                return;
            }

            EnsureDirtyFlags(world, target);
            DirtyFlags dirtyBefore = world.Get<DirtyFlags>(target);
            var store = GasLoadTimeCapacitySession.ActiveStore;
            int snapRow = store.AllocateEntityRow();
            store.CopyAttributeRow(attributes.RowId, snapRow);
            EffectModifierOps.Apply(in modifiers, ref attributes);

            bool hasDirty = false;
            Span<ulong> changedWords = stackalloc ulong[DirtyFlags.MAX_ATTR_DIRTY_WORDS];
            changedWords.Clear();
            for (int attributeId = 0; attributeId < slots; attributeId++)
            {
                int word = attributeId >> 6;
                ulong bit = 1UL << (attributeId & 63);
                if ((touchedWords[word] & bit) == 0UL)
                {
                    continue;
                }

                if (beforeValues[attributeId] == attributes.GetCurrent(attributeId))
                {
                    continue;
                }

                hasDirty = true;
                world.Get<DirtyFlags>(target).MarkAttributeDirty(attributeId);
                changedWords[word] |= bit;
            }

            if (hasDirty)
            {
                try
                {
                    tagOps?.MarkDirtyEntity(world, target);
                }
                catch
                {
                    store.CopyAttributeRow(snapRow, attributes.RowId);
                    world.Get<DirtyFlags>(target) = dirtyBefore;
                    store.ReleaseEntityRow(snapRow);
                    throw;
                }

                for (int attributeId = 0; attributeId < slots; attributeId++)
                {
                    if ((changedWords[attributeId >> 6] & (1UL << (attributeId & 63))) != 0UL)
                    {
                        MarkPresentationChanged(world, target, attributeId);
                    }
                }
            }

            store.ReleaseEntityRow(snapRow);
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
