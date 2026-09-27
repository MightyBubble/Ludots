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

            float current = (uint)attributeId < (uint)Components.AttributeBuffer.MAX_ATTRS
                ? world.Get<Components.AttributeBuffer>(target).GetCurrent(attributeId)
                : AttributeReads.Current(world, target, attributeId);
            SetCurrent(world, target, attributeId, current + delta, tagOps);
        }

        public static void SetCurrent(World world, Entity target, int attributeId, float value, TagOps tagOps)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            RequireTagOps(tagOps);
            if (attributeId < 0 || attributeId >= Registry.AttributeRegistry.MaxAttributeIds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attributeId), attributeId,
                    $"attributeId must be in [0, {Registry.AttributeRegistry.MaxAttributeIds}).");
            }

            EnsureDirtyFlags(world, target);
            if ((uint)attributeId >= (uint)Components.AttributeBuffer.MAX_ATTRS)
            {
                SetCurrentHigh(world, target, attributeId, value, tagOps);
                return;
            }

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
                tagOps.MarkDirtyEntity(world, target);
                MarkAttributeAggregateDirty(world, target, tagOps);
                MirrorToStore(world, target, attributeId);
            }
            catch
            {
                attributes = attributesBefore;
                world.Get<DirtyFlags>(target) = dirtyBefore;
                throw;
            }

            MarkPresentationChanged(world, target, attributeId);
        }

        public static void ReplaceCurrentFromCap(World world, Entity target, int attributeId, TagOps tagOps)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            float cap = (uint)attributeId < (uint)Components.AttributeBuffer.MAX_ATTRS
                ? world.Get<Components.AttributeBuffer>(target).GetCap(attributeId)
                : AttributeReads.Cap(world, target, attributeId);
            SetCurrent(world, target, attributeId, cap, tagOps);
        }

        public static void SetBase(World world, Entity target, int attributeId, float value, TagOps tagOps)
        {
            if (!world.IsAlive(target) || !world.Has<AttributeBuffer>(target))
            {
                return;
            }

            RequireTagOps(tagOps);
            if (attributeId < 0 || attributeId >= Registry.AttributeRegistry.MaxAttributeIds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(attributeId), attributeId,
                    $"attributeId must be in [0, {Registry.AttributeRegistry.MaxAttributeIds}).");
            }

            EnsureDirtyFlags(world, target);
            if ((uint)attributeId >= (uint)Components.AttributeBuffer.MAX_ATTRS)
            {
                SetBaseHigh(world, target, attributeId, value, tagOps);
                return;
            }

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
                tagOps.MarkDirtyEntity(world, target);
                MarkAttributeAggregateDirty(world, target, tagOps);
                MirrorToStore(world, target, attributeId);
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
            ApplyModifiersHigh(world, target, in modifiers, tagOps);
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            Span<float> beforeValues = stackalloc float[AttributeBuffer.MAX_ATTRS];
            ulong touchedMask = 0UL;
            for (int i = 0; i < modifiers.Count; i++)
            {
                int attributeId = modifiers.Get(i).AttributeId;
                if ((uint)attributeId >= (uint)Components.AttributeBuffer.MAX_ATTRS)
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
                    tagOps.MarkDirtyEntity(world, target);
                    MarkAttributeAggregateDirty(world, target, tagOps);
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

        private static void SetCurrentHigh(World world, Entity target, int attributeId, float value, TagOps tagOps)
        {
            WorldAttributeStore store = WorldAttributeStoreAmbient.Current
                ?? throw new InvalidOperationException(
                    "GAS.CAPACITY.ERR.HighLaneUnavailable: attributeId >= 64 需要世界列存（RFC-0067 P1），但 WorldAttributeStore 未绑定。");
            int row = store.EnsureRow(target);
            float before = store.GetCurrent(row, attributeId);
            store.SetCurrentHigh(row, attributeId, value);
            if (before == store.GetCurrent(row, attributeId))
            {
                return;
            }

            store.MarkAttributeDirtyHigh(row, attributeId);
            store.MarkAggregateDirty(row);
            tagOps.MarkDirtyEntity(world, target);
            MarkAttributeAggregateDirty(world, target, tagOps);
        }

        private static void SetBaseHigh(World world, Entity target, int attributeId, float value, TagOps tagOps)
        {
            WorldAttributeStore store = WorldAttributeStoreAmbient.Current
                ?? throw new InvalidOperationException(
                    "GAS.CAPACITY.ERR.HighLaneUnavailable: attributeId >= 64 需要世界列存（RFC-0067 P1），但 WorldAttributeStore 未绑定。");
            int row = store.EnsureRow(target);
            float beforeBase = store.GetBase(row, attributeId);
            float beforeCurrent = store.GetCurrent(row, attributeId);
            store.SetBase(row, attributeId, value);
            if (beforeBase == store.GetBase(row, attributeId) && beforeCurrent == store.GetCurrent(row, attributeId))
            {
                return;
            }

            store.MarkAttributeDirtyHigh(row, attributeId);
            store.MarkAggregateDirty(row);
            tagOps.MarkDirtyEntity(world, target);
            MarkAttributeAggregateDirty(world, target, tagOps);
        }

        /// <summary>ApplyModifiers 的高槽位前置补丁：内嵌车道由本方法与 EffectModifierOps 共同跳过 ≥64。</summary>
        private static void ApplyModifiersHigh(World world, Entity target, in EffectModifiers modifiers, TagOps tagOps)
        {
            bool hasHigh = false;
            for (int i = 0; i < modifiers.Count; i++)
            {
                if (modifiers.Get(i).AttributeId >= Components.AttributeBuffer.MAX_ATTRS)
                {
                    hasHigh = true;
                    break;
                }
            }

            if (!hasHigh)
            {
                return;
            }

            WorldAttributeStore store = WorldAttributeStoreAmbient.Current
                ?? throw new InvalidOperationException(
                    "GAS.CAPACITY.ERR.HighLaneUnavailable: modifier 引用 attributeId >= 64 需要世界列存（RFC-0067 P1）。");
            int row = store.EnsureRow(target);
            bool changed = false;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var mod = modifiers.Get(i);
                if (mod.AttributeId < Components.AttributeBuffer.MAX_ATTRS)
                {
                    continue;
                }

                float before = store.GetCurrent(row, mod.AttributeId);
                float value = mod.Operation switch
                {
                    Components.ModifierOp.Add => before + mod.Value,
                    Components.ModifierOp.Multiply => before * mod.Value,
                    _ => mod.Value,
                };
                store.SetCurrentHigh(row, mod.AttributeId, value);
                if (before != store.GetCurrent(row, mod.AttributeId))
                {
                    store.MarkAttributeDirtyHigh(row, mod.AttributeId);
                    changed = true;
                }
            }

            if (changed)
            {
                store.MarkAggregateDirty(row);
                tagOps.MarkDirtyEntity(world, target);
                MarkAttributeAggregateDirty(world, target, tagOps);
            }
        }

        /// <summary>内嵌车道 settle 后把最终值（含钳制）镜像进列存；未建行（无种子来源）则跳过——
        /// 列存在 P1 是 ≥64 槽位的运行真相与 <64 槽位的种子期镜像，不做无源镜像。</summary>
        private static void MirrorToStore(World world, Entity target, int attributeId)
        {
            WorldAttributeStore store = WorldAttributeStoreAmbient.Current;
            if (store == null || !store.TryGetRow(target, out int row))
            {
                return;
            }

            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(target);
            store.MirrorCurrent(row, attributeId, attributes.GetBase(attributeId), attributes.GetCap(attributeId), attributes.GetCurrent(attributeId));
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

        private static void MarkAttributeAggregateDirty(World world, Entity target, TagOps tagOps)
        {
            if (!world.Has<ActiveEffectContainer>(target))
            {
                return;
            }

            (tagOps.AggregateDirty ?? throw new InvalidOperationException(AttributeAggregateDirtyRegistry.MissingRegistryError)).MarkDirty(target);
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
