using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// 高槽位（[64, Plan)）属性的延迟触发收集：行级高脏位驱动的实体，
    /// 逐高槽位比较世界列存 LastSnapshot 与 Current，入队 AttributeChangedTrigger
    /// 并前推快照（与内嵌 64 位掩码车道同语义；DirtyFlags 掩码覆盖不到 ≥64）。
    /// </summary>
    public static class AttributeHighLane
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CollectAttributeChanges(
            World world,
            Entity entity,
            DeferredTriggerQueue triggerQueue)
        {
            WorldAttributeStore store = WorldAttributeStoreAmbient.Current;
            if (store == null || store.SlotCount <= AttributeBuffer.MAX_ATTRS)
            {
                return;
            }

            if (!store.TryGetRow(entity, out int row) || !store.HasAttributeDirtyHigh(row))
            {
                return;
            }

            for (int slot = AttributeBuffer.MAX_ATTRS; slot < store.SlotCount; slot++)
            {
                if (!store.IsDefined(row, slot))
                {
                    continue;
                }

                float oldValue = store.GetLastSnapshot(row, slot);
                float newValue = store.GetCurrent(row, slot);
                store.SetLastSnapshot(row, slot, newValue);
                if (oldValue != newValue)
                {
                    triggerQueue.EnqueueAttributeChanged(new AttributeChangedTrigger
                    {
                        Target = entity,
                        AttributeId = slot,
                        OldValue = oldValue,
                        NewValue = newValue,
                    });
                }
            }

            store.ClearAttributeDirtyHigh(row);
        }

        /// <summary>高槽位标签（[256, Plan)）的延迟触发收集：行级高脏位驱动，
        /// 逐位比较列存 LastSnapshot 与当前位，入队 TagChangedTrigger 并前推快照。</summary>
        public static void CollectHighTagChanges(
            World world,
            Entity entity,
            DeferredTriggerQueue triggerQueue)
        {
            WorldAttributeStore store = WorldAttributeStoreAmbient.Current;
            if (store == null || store.TagIdSpace <= GameplayTagContainer.MAX_TAG_ID + 1)
            {
                return;
            }

            if (!store.TryGetRow(entity, out int row) || !store.HasTagDirtyHigh(row))
            {
                return;
            }

            for (int tagId = GameplayTagContainer.MAX_TAG_ID + 1; tagId < store.TagIdSpace; tagId++)
            {
                if (!store.HasTag(row, tagId) && !store.GetTagLastSnapshot(row, tagId))
                {
                    continue;
                }

                bool isPresent = store.HasTag(row, tagId);
                bool wasPresent = store.GetTagLastSnapshot(row, tagId);
                store.SetTagLastSnapshot(row, tagId, isPresent);
                if (isPresent != wasPresent)
                {
                    triggerQueue.EnqueueTagChanged(new TagChangedTrigger
                    {
                        Target = entity,
                        TagId = tagId,
                        WasPresent = wasPresent,
                        IsPresent = isPresent,
                    });
                }
            }

            store.ClearTagDirtyHigh(row);
        }

        /// <summary>种子/建行时初始化行快照（与 ComponentRegistry 内嵌快照播种对齐）。</summary>
        public static void SeedLastSnapshot(WorldAttributeStore store, int row, int attributeId)
        {
            store.SetLastSnapshot(row, attributeId, store.GetCurrent(row, attributeId));
        }
    }
}
