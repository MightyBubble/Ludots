using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Knowledge;
using Ludots.Core.Spatial;

namespace Ludots.Core.ParticipantVisibility
{
    public sealed class DynamicParticipantVisibilityPublisher
    {
        private const int InitialScratchCapacity = 64;

        private readonly World _world;
        private readonly EntityCollectionStore _collections;
        private readonly KnowledgeProjectionStore _knowledge;
        private readonly TagOps? _tagOps;
        private readonly BindingState[] _states;
        private Entity[] _candidateScratch = new Entity[InitialScratchCapacity];

        public DynamicParticipantVisibilityPublisher(
            World world,
            EntityCollectionStore collections,
            KnowledgeProjectionStore knowledge,
            DynamicParticipantVisibilityBinding[] bindings,
            TagOps? tagOps = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            _tagOps = tagOps;
            _states = new BindingState[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                _states[i] = new BindingState(bindings[i], InitialScratchCapacity);
            }
        }

        public int BindingCount => _states.Length;

        public DynamicParticipantVisibilityPublishResult Publish(int currentTick)
        {
            int changedCollections = 0;
            int upsertedKnowledge = 0;
            int removedKnowledge = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                ref BindingState state = ref _states[i];
                DynamicParticipantVisibilityBinding binding = state.Binding;
                if (!CanPublish(in binding))
                {
                    removedKnowledge += ClearState(ref state);
                    continue;
                }

                QueryDescription query = binding.Query;
                int candidateCount = _world.CountEntities(in query);
                EnsureCandidateCapacity(candidateCount);
                if (candidateCount > 0)
                {
                    _world.GetEntities(in query, _candidateScratch.AsSpan(0, candidateCount));
                }

                int memberCount = FilterCandidates(in binding, _candidateScratch.AsSpan(0, candidateCount));
                Span<Entity> members = _candidateScratch.AsSpan(0, memberCount);
                memberCount = SpatialQueryPostProcessor.SortStableDedup(members);
                members = _candidateScratch.AsSpan(0, memberCount);
                if (!state.MatchesPrevious(members, out ulong signature))
                {
                    _collections.Replace(
                        binding.Viewer,
                        binding.CollectionDescriptor,
                        members);
                    changedCollections++;

                    removedKnowledge += RemoveStaleKnowledge(ref state, members);
                    upsertedKnowledge += UpsertKnowledge(in binding, members, currentTick);
                    state.ReplacePrevious(members, signature);
                }
            }

            return new DynamicParticipantVisibilityPublishResult(
                changedCollections,
                upsertedKnowledge,
                removedKnowledge);
        }

        public int Clear()
        {
            int removed = 0;
            for (int i = 0; i < _states.Length; i++)
            {
                removed += ClearState(ref _states[i]);
            }

            return removed;
        }

        private bool CanPublish(in DynamicParticipantVisibilityBinding binding)
        {
            return binding.Viewer != Entity.Null &&
                   _world.IsAlive(binding.Viewer) &&
                   (binding.Source == Entity.Null || _world.IsAlive(binding.Source));
        }

        private int FilterCandidates(in DynamicParticipantVisibilityBinding binding, Span<Entity> candidates)
        {
            int written = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                Entity candidate = candidates[i];
                if (!MatchesBinding(in binding, candidate))
                {
                    continue;
                }

                candidates[written++] = candidate;
            }

            return written;
        }

        private bool MatchesBinding(in DynamicParticipantVisibilityBinding binding, Entity candidate)
        {
            if (!_world.IsAlive(candidate))
            {
                return false;
            }

            DynamicParticipantQueryFlags flags = binding.Flags;
            if ((flags & DynamicParticipantQueryFlags.RequireSelectable) != 0 &&
                !CommandSourceEligibility.IsSelectableNow(_world, candidate))
            {
                return false;
            }

            if ((flags & DynamicParticipantQueryFlags.ExcludePlayerIdentity) != 0 &&
                _world.Has<PlayerIdentity>(candidate))
            {
                return false;
            }

            if ((flags & DynamicParticipantQueryFlags.ExcludeTeamIdentity) != 0 &&
                _world.Has<TeamIdentity>(candidate))
            {
                return false;
            }

            if ((flags & DynamicParticipantQueryFlags.RequireMapMatch) != 0 &&
                (!_world.TryGet(candidate, out MapEntity mapEntity) || mapEntity.MapId != binding.MapId))
            {
                return false;
            }

            if (!MatchesOwner(binding.Viewer, candidate))
            {
                return false;
            }

            return binding.RequiredTagId <= 0 || MatchesRequiredTag(candidate, binding.RequiredTagId);
        }

        private bool MatchesOwner(Entity viewer, Entity candidate)
        {
            if (_world.Has<PlayerIdentity>(viewer))
            {
                int playerId = _world.Get<PlayerIdentity>(viewer).PlayerId;
                return _world.TryGet(candidate, out PlayerOwner owner) &&
                       owner.PlayerId == playerId;
            }

            if (_world.Has<TeamIdentity>(viewer))
            {
                int teamId = _world.Get<TeamIdentity>(viewer).TeamId;
                return _world.TryGet(candidate, out Team team) &&
                       team.Id == teamId;
            }

            return true;
        }

        private bool MatchesRequiredTag(Entity candidate, int tagId)
        {
            if (!_world.TryGet(candidate, out GameplayTagContainer tags))
            {
                return false;
            }

            return _tagOps == null
                ? tags.HasTag(tagId)
                : _tagOps.HasTag(ref tags, tagId, TagSense.Effective);
        }

        private int UpsertKnowledge(
            in DynamicParticipantVisibilityBinding binding,
            ReadOnlySpan<Entity> members,
            int currentTick)
        {
            int upserted = 0;
            for (int i = 0; i < members.Length; i++)
            {
                Entity target = members[i];
                Entity source = ResolveSource(in binding, target);
                _knowledge.Upsert(
                    binding.Viewer,
                    target,
                    new KnowledgeDisclosureRecord(
                        binding.Presence,
                        binding.Position,
                        binding.AttributeMask,
                        binding.RelationshipTypeMask,
                        binding.TagMask,
                        source,
                        currentTick <= 0 ? 1 : currentTick,
                        binding.ExpiryTick,
                        binding.ConfidencePermille <= 0 ? 1000 : binding.ConfidencePermille,
                        revision: 0));
                upserted++;
            }

            return upserted;
        }

        private static Entity ResolveSource(in DynamicParticipantVisibilityBinding binding, Entity target)
        {
            return binding.SourceKind switch
            {
                DynamicParticipantSourceKind.Entity => binding.Source,
                DynamicParticipantSourceKind.Target => target,
                _ => binding.Viewer,
            };
        }

        private int RemoveStaleKnowledge(ref BindingState state, ReadOnlySpan<Entity> members)
        {
            int removed = 0;
            ReadOnlySpan<Entity> previous = state.Previous.AsSpan(0, state.PreviousCount);
            int memberIndex = 0;
            for (int i = 0; i < previous.Length; i++)
            {
                Entity old = previous[i];
                while (memberIndex < members.Length && CompareEntity(members[memberIndex], old) < 0)
                {
                    memberIndex++;
                }

                if (memberIndex >= members.Length || members[memberIndex] != old)
                {
                    if (_knowledge.Remove(state.Binding.Viewer, old))
                    {
                        removed++;
                    }
                }
            }

            return removed;
        }

        private int ClearState(ref BindingState state)
        {
            int removed = 0;
            for (int i = 0; i < state.PreviousCount; i++)
            {
                if (_knowledge.Remove(state.Binding.Viewer, state.Previous[i]))
                {
                    removed++;
                }
            }

            if (state.Binding.Viewer != Entity.Null)
            {
                _collections.Remove(state.Binding.Viewer, state.Binding.CollectionDescriptor.Key);
            }

            state.PreviousCount = 0;
            state.Signature = 0;
            return removed;
        }

        private void EnsureCandidateCapacity(int required)
        {
            if (required <= _candidateScratch.Length)
            {
                return;
            }

            int next = _candidateScratch.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _candidateScratch, next);
        }

        private static ulong ComputeSignature(ReadOnlySpan<Entity> members)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < members.Length; i++)
            {
                hash = HashEntity(hash, members[i]);
            }

            return hash == 0 ? 1UL : hash;
        }

        private static ulong HashEntity(ulong hash, Entity entity)
        {
            hash = HashCombine(hash, (uint)entity.WorldId);
            hash = HashCombine(hash, (uint)entity.Id);
            hash = HashCombine(hash, (uint)entity.Version);
            return hash;
        }

        private static ulong HashCombine(ulong hash, uint value)
        {
            unchecked
            {
                hash ^= value;
                return hash * 1099511628211UL;
            }
        }

        private static int CompareEntity(Entity left, Entity right)
        {
            int c = left.WorldId.CompareTo(right.WorldId);
            if (c != 0)
            {
                return c;
            }

            c = left.Id.CompareTo(right.Id);
            return c != 0 ? c : left.Version.CompareTo(right.Version);
        }

        private sealed class BindingState
        {
            public BindingState(in DynamicParticipantVisibilityBinding binding, int initialCapacity)
            {
                Binding = binding;
                Previous = new Entity[initialCapacity];
            }

            public DynamicParticipantVisibilityBinding Binding { get; }
            public Entity[] Previous;
            public int PreviousCount;
            public ulong Signature;

            public bool MatchesPrevious(ReadOnlySpan<Entity> members, out ulong signature)
            {
                signature = ComputeSignature(members);
                if (PreviousCount != members.Length || Signature != signature)
                {
                    return false;
                }

                for (int i = 0; i < members.Length; i++)
                {
                    if (Previous[i] != members[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            public void ReplacePrevious(ReadOnlySpan<Entity> members, ulong signature)
            {
                if (members.Length > Previous.Length)
                {
                    int next = Previous.Length;
                    while (next < members.Length)
                    {
                        next *= 2;
                    }

                    Array.Resize(ref Previous, next);
                }

                members.CopyTo(Previous);
                PreviousCount = members.Length;
                Signature = signature;
            }
        }
    }

    public readonly record struct DynamicParticipantVisibilityPublishResult(
        int ChangedCollections,
        int UpsertedKnowledgeRecords,
        int RemovedKnowledgeRecords);
}
