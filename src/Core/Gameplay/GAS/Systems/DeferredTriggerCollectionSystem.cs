using Arch.Buffer;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Capacity;
using Ludots.Core.Gameplay.GAS.Components;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class DeferredTriggerCollectionSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _bootstrapQuery = new QueryDescription()
            .WithAll<DirtyFlags>();

        private readonly DeferredTriggerQueue _triggerQueue;
        private readonly TagOps _tagOps;
        private readonly DirtyEntityQueue _dirtyEntities;
        private readonly CommandBuffer _commandBuffer = new();
        private bool _bootstrapPending = true;

        public DeferredTriggerCollectionSystem(
            World world,
            DeferredTriggerQueue triggerQueue,
            TagOps tagOps = null,
            DirtyEntityQueue dirtyEntities = null) : base(world)
        {
            _triggerQueue = triggerQueue;
            _tagOps = tagOps ?? throw new InvalidOperationException(TagOps.MissingTagOpsError);
            _dirtyEntities = dirtyEntities ?? _tagOps.DirtyEntities;
            if (!ReferenceEquals(_dirtyEntities, _tagOps.DirtyEntities))
            {
                throw new InvalidOperationException("GAS.DIRTY_ENTITY.ERR.MismatchedQueue");
            }
        }

        public int VisitedEntityCountLastUpdate { get; private set; }

        public override void Update(in float dt)
        {
            if (_bootstrapPending)
            {
                _bootstrapPending = false;
                var bootstrap = new BootstrapJob { World = World, DirtyEntities = _dirtyEntities };
                World.InlineEntityQuery<BootstrapJob, DirtyFlags>(in _bootstrapQuery, ref bootstrap);
            }

            var job = new CollectionJob
            {
                World = World,
                TriggerQueue = _triggerQueue,
                CommandBuffer = _commandBuffer,
                TagOps = _tagOps
            };

            VisitedEntityCountLastUpdate = 0;
            int activeCount = _dirtyEntities.Count;
            for (int i = 0; i < activeCount && _dirtyEntities.TryDequeue(out Entity entity); i++)
            {
                if (!World.IsAlive(entity) || !World.Has<DirtyFlags>(entity))
                {
                    continue;
                }

                ref DirtyFlags dirty = ref World.Get<DirtyFlags>(entity);
                dirty.DeferredTriggerQueued = 0;
                VisitedEntityCountLastUpdate++;
                job.Update(entity, ref dirty);
                if (dirty.IsAnyAttributeDirty() || dirty.IsAnyTagDirty())
                {
                    _dirtyEntities.Track(World, entity);
                }
            }
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private struct BootstrapJob : IForEachWithEntity<DirtyFlags>
        {
            public World World;
            public DirtyEntityQueue DirtyEntities;

            public void Update(Entity entity, ref DirtyFlags dirty)
            {
                if (dirty.DeferredTriggerQueued == 0 &&
                    (dirty.IsAnyAttributeDirty() || dirty.IsAnyTagDirty()))
                {
                    DirtyEntities.Track(World, entity);
                }
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
                        CollectAttributeDirtyTriggers(
                            entity,
                            ref attrBuffer,
                            ref dirtyFlags,
                            ref snap,
                            hasExistingSnapshot: false);
                        CommandBuffer.Add(entity, snap);
                    }
                    else
                    {
                        ref var snap = ref World.Get<AttributeLastSnapshot>(entity);
                        CollectAttributeDirtyTriggers(
                            entity,
                            ref attrBuffer,
                            ref dirtyFlags,
                            ref snap,
                            hasExistingSnapshot: true);
                    }
                }

                bool hasTags = World.Has<GameplayTagContainer>(entity);
                bool hasCounts = World.Has<TagCountContainer>(entity);
                if (!hasTags && !hasCounts)
                {
                    ClearDirtyFlagsIfClean(entity, ref dirtyFlags);
                    return;
                }

                var dirtyTagMask = default(GameplayTagBitSet);
                bool anyDirty = false;
                int tagDirtyWords = DirtyFlags.ActiveTagDirtyWordCount();
                for (int wordIndex = 0; wordIndex < tagDirtyWords; wordIndex++)
                {
                    ulong dirtyWord = dirtyFlags.TagDirtyWords[wordIndex];
                    if (dirtyWord == 0UL)
                    {
                        continue;
                    }

                    anyDirty = true;
                    while (dirtyWord != 0UL)
                    {
                        int bit = BitOperations.TrailingZeroCount(dirtyWord);
                        dirtyWord &= dirtyWord - 1UL;
                        int tagId = (wordIndex << 6) + bit;
                        if (tagId == 0)
                        {
                            throw new InvalidOperationException(
                                "DirtyFlags must not mark reserved tag id 0.");
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
                    tagSnapLocal.CopyFromContainer(in tagsInit);
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

                int dirtyWords = GameplayTagBitSet.ActiveWordCount();
                for (int wordIndex = 0; wordIndex < dirtyWords; wordIndex++)
                {
                    ulong dirtyBits = dirtyTagMask.WordAt(wordIndex);
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
                                bool wasPresent = snap.Has(tagId);
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
                                snap.Set(tagId, isPresent);
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
                    int presentWords = tags.WordCount;
                    Span<ulong> presentScratch = stackalloc ulong[GameplayTagBitSet.MaxWords];
                    tags.CopyWordsTo(presentScratch.Slice(0, presentWords));
                    for (int wordIndex = 0; wordIndex < presentWords; wordIndex++)
                    {
                        ulong presentBits = presentScratch[wordIndex];
                        while (presentBits != 0UL)
                        {
                            int bit = BitOperations.TrailingZeroCount(presentBits);
                            presentBits &= presentBits - 1UL;
                            int tagId = (wordIndex << 6) + bit;
                            if (!hasEffectiveCache || TagOps.EffectiveMayChangeForDirtyTags(tagId, in dirtyTagMask))
                            {
                                effectiveCandidateMask.AddTag(tagId);
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
                if (hasTags && !hasEffectiveChanged && effChangedLocal.IsAnyBitSet())
                {
                    CommandBuffer.Add(entity, effChangedLocal);
                }

                ClearDirtyFlagsIfClean(entity, ref dirtyFlags);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void CollectAttributeDirtyTriggers(
                Entity entity,
                ref AttributeBuffer attrBuffer,
                ref DirtyFlags dirtyFlags,
                ref AttributeLastSnapshot snap,
                bool hasExistingSnapshot)
            {
                int words = GasWorldColumnStore.AttrDirtyWordCount(
                    GasLoadTimeCapacitySession.Plan.AttributeSlotCount);
                for (int wordIndex = 0; wordIndex < words; wordIndex++)
                {
                    ulong dirtyMask = dirtyFlags.AttributeDirtyWords[wordIndex];
                    while (dirtyMask != 0UL)
                    {
                        int bit = BitOperations.TrailingZeroCount(dirtyMask);
                        dirtyMask &= dirtyMask - 1UL;
                        int attributeId = (wordIndex << 6) + bit;
                        float newValue = attrBuffer.GetCurrent(attributeId);
                        if (!hasExistingSnapshot)
                        {
                            snap.Values[attributeId] = newValue;
                            TriggerQueue.EnqueueAttributeChanged(new AttributeChangedTrigger
                            {
                                Target = entity,
                                AttributeId = attributeId,
                                OldValue = 0f,
                                NewValue = newValue
                            });
                        }
                        else
                        {
                            float oldValue = snap.Values[attributeId];
                            snap.Values[attributeId] = newValue;
                            if (oldValue != newValue)
                            {
                                TriggerQueue.EnqueueAttributeChanged(new AttributeChangedTrigger
                                {
                                    Target = entity,
                                    AttributeId = attributeId,
                                    OldValue = oldValue,
                                    NewValue = newValue
                                });
                            }
                        }

                        dirtyFlags.ClearAttributeDirty(attributeId);
                    }
                }
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
                in GameplayTagBitSet candidates,
                ref GameplayTagEffectiveCache cache,
                bool hasEffectiveChanged,
                ref GameplayTagEffectiveChangedBits effChangedLocal)
            {
                int words = GameplayTagBitSet.ActiveWordCount();
                for (int wordIndex = 0; wordIndex < words; wordIndex++)
                {
                    ulong candidateBits = candidates.WordAt(wordIndex);
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
