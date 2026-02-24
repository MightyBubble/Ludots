using Arch.Buffer;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class DeferredTriggerCollectionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _dirtyQuery = new QueryDescription()
            .WithAll<DirtyFlags>();

        private readonly DeferredTriggerQueue _triggerQueue;
        private readonly TagOps _tagOps;
        private readonly CommandBuffer _commandBuffer = new CommandBuffer();

        public DeferredTriggerCollectionSystem(World world, DeferredTriggerQueue triggerQueue, TagOps tagOps = null) : base(world)
        {
            _triggerQueue = triggerQueue;
            _tagOps = tagOps ?? new TagOps();
        }

        public override void Update(in float dt)
        {
            var job = new CollectionJob
            {
                World = World,
                TriggerQueue = _triggerQueue,
                CommandBuffer = _commandBuffer,
                TagOps = _tagOps
            };

            World.InlineEntityQuery<CollectionJob, DirtyFlags>(in _dirtyQuery, ref job);
            _commandBuffer.Playback(World, dispose: true);
        }

        unsafe struct CollectionJob : IForEachWithEntity<DirtyFlags>
        {
            public World World;
            public DeferredTriggerQueue TriggerQueue;
            public TagOps TagOps;
            public CommandBuffer CommandBuffer;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref DirtyFlags dirtyFlags)
            {
                if (World.Has<AttributeBuffer>(entity))
                {
                    ref var attrBuffer = ref World.Get<AttributeBuffer>(entity);
                    bool hasSnapshot = World.Has<AttributeLastSnapshot>(entity);
                    if (!hasSnapshot)
                    {
                        var snap = default(AttributeLastSnapshot);
                        for (int i = 0; i < DirtyFlags.MAX_ATTRS; i++)
                        {
                            float newValue = attrBuffer.GetCurrent(i);
                            snap.Values[i] = newValue;
                            if (!dirtyFlags.IsAttributeDirty(i)) continue;

                            TriggerQueue.EnqueueAttributeChanged(new AttributeChangedTrigger
                            {
                                Target = entity,
                                AttributeId = i,
                                OldValue = 0f,
                                NewValue = newValue
                            });
                            dirtyFlags.ClearAttributeDirty(i);
                        }
                        CommandBuffer.Add(entity, snap);
                    }
                    else
                    {
                        ref var snap = ref World.Get<AttributeLastSnapshot>(entity);
                        for (int i = 0; i < DirtyFlags.MAX_ATTRS; i++)
                        {
                            if (!dirtyFlags.IsAttributeDirty(i)) continue;
                            float oldValue = snap.Values[i];
                            float newValue = attrBuffer.GetCurrent(i);
                            snap.Values[i] = newValue;
                            if (oldValue != newValue)
                            {
                                TriggerQueue.EnqueueAttributeChanged(new AttributeChangedTrigger
                                {
                                    Target = entity,
                                    AttributeId = i,
                                    OldValue = oldValue,
                                    NewValue = newValue
                                });
                            }
                            dirtyFlags.ClearAttributeDirty(i);
                        }
                    }
                }

                bool hasTags = World.Has<GameplayTagContainer>(entity);
                bool hasCounts = World.Has<TagCountContainer>(entity);
                if (!hasTags && !hasCounts) return;

                bool anyDirty = false;
                for (int i = 0; i < DirtyFlags.TAG_DIRTY_BYTES; i++)
                {
                    if (dirtyFlags.TagDirty[i] != 0)
                    {
                        anyDirty = true;
                        break;
                    }
                }
                if (!anyDirty) return;

                ref var counts = ref World.TryGetRef<TagCountContainer>(entity, out bool hasCountsRef);

                bool hasTagSnapshot = hasTags && World.Has<GameplayTagSnapshot>(entity);
                var tagSnapLocal = default(GameplayTagSnapshot);
                if (hasTags && !hasTagSnapshot)
                {
                    ref var tagsInit = ref World.Get<GameplayTagContainer>(entity);
                    tagSnapLocal.Bits[0] = tagsInit.Bits[0];
                    tagSnapLocal.Bits[1] = tagsInit.Bits[1];
                    tagSnapLocal.Bits[2] = tagsInit.Bits[2];
                    tagSnapLocal.Bits[3] = tagsInit.Bits[3];
                }

                bool hasCountSnapshot = hasCountsRef && World.Has<TagCountSnapshot>(entity);
                TagCountSnapshot countSnapLocal = default;
                if (hasCountsRef && !hasCountSnapshot)
                {
                    countSnapLocal = TagCountSnapshot.From(ref counts);
                }

                bool hasEffectiveCache = hasTags && World.Has<GameplayTagEffectiveCache>(entity);
                GameplayTagEffectiveCache effCacheLocal = default;

                bool hasEffectiveChanged = hasTags && World.Has<GameplayTagEffectiveChangedBits>(entity);
                GameplayTagEffectiveChangedBits effChangedLocal = default;

                for (int tagId = 1; tagId < 256; tagId++)
                {
                    if (!dirtyFlags.IsTagDirty(tagId)) continue;

                    if (hasTags)
                    {
                        ref var tags = ref World.Get<GameplayTagContainer>(entity);
                        bool isPresent = tags.HasTag(tagId);
                        if (!hasTagSnapshot)
                        {
                            if (isPresent)
                            {
                                TriggerQueue.EnqueueTagChanged(new TagChangedTrigger
                                {
                                    Target = entity,
                                    TagId = tagId,
                                    WasPresent = false,
                                    IsPresent = true
                                });
                            }
                        }
                        else
                        {
                            ref var snap = ref World.Get<GameplayTagSnapshot>(entity);
                            int word = tagId >> 6;
                            int bit = tagId & 63;
                            ulong mask = 1UL << bit;
                            ulong value = snap.Bits[word];
                            bool wasPresent = (value & mask) != 0;
                            if (wasPresent != isPresent)
                            {
                                TriggerQueue.EnqueueTagChanged(new TagChangedTrigger
                                {
                                    Target = entity,
                                    TagId = tagId,
                                    WasPresent = wasPresent,
                                    IsPresent = isPresent
                                });
                            }
                            snap.Bits[word] = isPresent ? (value | mask) : (value & ~mask);
                        }
                    }

                    if (hasCountsRef)
                    {
                        ushort newCount = counts.GetCount(tagId);
                        if (!hasCountSnapshot)
                        {
                            if (newCount != 0)
                            {
                                TriggerQueue.EnqueueTagCountChanged(new TagCountChangedTrigger
                                {
                                    Target = entity,
                                    TagId = tagId,
                                    OldCount = 0,
                                    NewCount = newCount
                                });
                            }
                            countSnapLocal.SetCount(tagId, newCount);
                        }
                        else
                        {
                            ref var snap = ref World.Get<TagCountSnapshot>(entity);
                            ushort oldCount = snap.GetCount(tagId);
                            snap.SetCount(tagId, newCount);
                            if (oldCount != newCount)
                            {
                                TriggerQueue.EnqueueTagCountChanged(new TagCountChangedTrigger
                                {
                                    Target = entity,
                                    TagId = tagId,
                                    OldCount = oldCount,
                                    NewCount = newCount
                                });
                            }
                        }
                    }

                    dirtyFlags.ClearTagDirty(tagId);
                }

                if (hasTags)
                {
                    ref var tags = ref World.Get<GameplayTagContainer>(entity);
                    if (hasEffectiveCache)
                    {
                        ref var cache = ref World.Get<GameplayTagEffectiveCache>(entity);
                        for (int tagId = 1; tagId < 256; tagId++)
                        {
                            bool newEff = TagOps.HasTag(ref tags, tagId, TagSense.Effective);
                            bool oldEff = cache.Has(tagId);
                            if (oldEff != newEff)
                            {
                                if (hasEffectiveChanged)
                                {
                                    ref var changed = ref World.Get<GameplayTagEffectiveChangedBits>(entity);
                                    changed.Mark(tagId);
                                }
                                else
                                {
                                    effChangedLocal.Mark(tagId);
                                }
                                cache.Set(tagId, newEff);
                            }
                        }
                    }
                    else
                    {
                        for (int tagId = 1; tagId < 256; tagId++)
                        {
                            bool newEff = TagOps.HasTag(ref tags, tagId, TagSense.Effective);
                            bool oldEff = effCacheLocal.Has(tagId);
                            if (oldEff != newEff)
                            {
                                if (hasEffectiveChanged)
                                {
                                    ref var changed = ref World.Get<GameplayTagEffectiveChangedBits>(entity);
                                    changed.Mark(tagId);
                                }
                                else
                                {
                                    effChangedLocal.Mark(tagId);
                                }
                                effCacheLocal.Set(tagId, newEff);
                            }
                        }
                    }
                }

                if (hasTags && !hasTagSnapshot)
                {
                    CommandBuffer.Add(entity, tagSnapLocal);
                }
                if (hasCountsRef && !hasCountSnapshot)
                {
                    CommandBuffer.Add(entity, countSnapLocal);
                }
                if (hasTags && !hasEffectiveCache)
                {
                    CommandBuffer.Add(entity, effCacheLocal);
                }
                if (hasTags && !hasEffectiveChanged && (effChangedLocal.Bits[0] | effChangedLocal.Bits[1] | effChangedLocal.Bits[2] | effChangedLocal.Bits[3]) != 0)
                {
                    CommandBuffer.Add(entity, effChangedLocal);
                }
            }
        }
    }
}
