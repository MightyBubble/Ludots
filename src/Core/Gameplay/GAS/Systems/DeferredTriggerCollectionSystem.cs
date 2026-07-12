using Arch.Buffer;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class DeferredTriggerCollectionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _dirtyQuery = new QueryDescription()
            .WithAll<DirtyFlags>();

        private readonly DeferredTriggerQueue _triggerQueue;
        private readonly TagOps _tagOps;
        private readonly CommandBuffer _commandBuffer = new();

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
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
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
                        ulong dirtyMask = dirtyFlags.AttributeDirtyMask;
                        while (dirtyMask != 0UL)
                        {
                            int i = BitOperations.TrailingZeroCount(dirtyMask);
                            dirtyMask &= dirtyMask - 1UL;
                            float newValue = attrBuffer.GetCurrent(i);
                            snap.Values[i] = newValue;

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
                        ulong dirtyMask = dirtyFlags.AttributeDirtyMask;
                        while (dirtyMask != 0UL)
                        {
                            int i = BitOperations.TrailingZeroCount(dirtyMask);
                            dirtyMask &= dirtyMask - 1UL;
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
                if (!hasTags && !hasCounts)
                {
                    ClearDirtyFlagsIfClean(entity, ref dirtyFlags);
                    return;
                }

                var dirtyTagMask = default(GameplayTagContainer);
                bool anyDirty = false;
                for (int byteIndex = 0; byteIndex < DirtyFlags.TAG_DIRTY_BYTES; byteIndex++)
                {
                    byte dirtyByte = dirtyFlags.TagDirty[byteIndex];
                    if (dirtyByte == 0)
                    {
                        continue;
                    }

                    anyDirty = true;
                    while (dirtyByte != 0)
                    {
                        int bit = BitOperations.TrailingZeroCount(dirtyByte);
                        dirtyByte = (byte)(dirtyByte & (dirtyByte - 1));
                        int tagId = (byteIndex << 3) + bit;
                        if (tagId == 0)
                        {
                            dirtyFlags.ClearTagDirty(tagId);
                            continue;
                        }
                        dirtyTagMask.AddTag(tagId);
                    }
                }
                if (!anyDirty)
                {
                    ClearDirtyFlagsIfClean(entity, ref dirtyFlags);
                    return;
                }

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

                for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                {
                    ulong dirtyBits = dirtyTagMask.Bits[wordIndex];
                    while (dirtyBits != 0UL)
                    {
                        int bit = BitOperations.TrailingZeroCount(dirtyBits);
                        dirtyBits &= dirtyBits - 1UL;
                        int tagId = (wordIndex << 6) + bit;

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
                                int tagBit = tagId & 63;
                                ulong mask = 1UL << tagBit;
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
                }

                if (hasTags)
                {
                    ref var tags = ref World.Get<GameplayTagContainer>(entity);
                    var effectiveCandidateMask = dirtyTagMask;
                    for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                    {
                        ulong presentBits = tags.Bits[wordIndex];
                        while (presentBits != 0UL)
                        {
                            int bit = BitOperations.TrailingZeroCount(presentBits);
                            presentBits &= presentBits - 1UL;
                            int tagId = (wordIndex << 6) + bit;
                            if (!hasEffectiveCache || TagOps.EffectiveMayChangeForDirtyTags(tagId, in dirtyTagMask))
                            {
                                effectiveCandidateMask.Bits[wordIndex] |= 1UL << bit;
                            }
                        }
                    }

                    if (hasEffectiveCache)
                    {
                        ref var cache = ref World.Get<GameplayTagEffectiveCache>(entity);
                        ApplyEffectiveCandidateChanges(entity, ref tags, in effectiveCandidateMask, ref cache, hasEffectiveChanged, ref effChangedLocal);
                    }
                    else
                    {
                        ApplyEffectiveCandidateChanges(entity, ref tags, in effectiveCandidateMask, ref effCacheLocal, hasEffectiveChanged, ref effChangedLocal);
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

                ClearDirtyFlagsIfClean(entity, ref dirtyFlags);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ClearDirtyFlagsIfClean(Entity entity, ref DirtyFlags dirtyFlags)
            {
                if (!dirtyFlags.IsAnyAttributeDirty() && !dirtyFlags.IsAnyTagDirty())
                {
                    dirtyFlags.Clear();
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void ApplyEffectiveCandidateChanges(
                Entity entity,
                ref GameplayTagContainer tags,
                in GameplayTagContainer candidates,
                ref GameplayTagEffectiveCache cache,
                bool hasEffectiveChanged,
                ref GameplayTagEffectiveChangedBits effChangedLocal)
            {
                for (int wordIndex = 0; wordIndex < 4; wordIndex++)
                {
                    ulong candidateBits = candidates.Bits[wordIndex];
                    while (candidateBits != 0UL)
                    {
                        int bit = BitOperations.TrailingZeroCount(candidateBits);
                        candidateBits &= candidateBits - 1UL;
                        int tagId = (wordIndex << 6) + bit;
                        if (tagId == 0)
                        {
                            continue;
                        }

                        bool newEff = TagOps.HasTag(ref tags, tagId, TagSense.Effective);
                        bool oldEff = cache.Has(tagId);
                        if (oldEff == newEff)
                        {
                            continue;
                        }

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
        }
    }
}
