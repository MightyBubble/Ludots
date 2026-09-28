using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships.Config;

namespace Ludots.Core.Gameplay.Relationships
{
    public sealed partial class RelationshipRuntime
    {
        private const int MaxWalkDepth = 1024;

        private Entity[] _ruleScratch = new Entity[4];
        private Entity[] _singleSourceScratch = new Entity[2];
        private Entity[] _walkEdgeScratch = new Entity[8];
        private readonly List<Entity> _upstreamQueue = new(16);
        private readonly HashSet<Entity> _upstreamVisited = new();
        private readonly List<Entity> _downstreamQueue = new(32);
        private readonly HashSet<Entity> _downstreamVisited = new();
        private readonly List<Entity> _projectionScratch = new(32);

        /// <summary>
        /// The single entity linking to <paramref name="target"/> with <paramref name="typeId"/>.
        /// Fails loudly when several do: walking up a relationship needs the type to declare <c>maxIncoming: 1</c>.
        /// </summary>
        public bool TryGetSingleSource(Entity target, int typeId, out Entity source)
        {
            source = Entity.Null;
            if (!IsAliveInRuntimeWorld(target))
            {
                return false;
            }

            int count = CollectIncoming(target, typeId, _singleSourceScratch, out int dropped);
            if (count + dropped > 1)
            {
                throw new InvalidOperationException(
                    $"Entity {Describe(target)} has {count + dropped} incoming '{_types.Get(typeId).Name}' links; " +
                    "walking up this relationship needs a single source per entity (declare rules.maxIncoming: 1).");
            }

            if (count == 0)
            {
                return false;
            }

            source = _singleSourceScratch[0];
            return true;
        }

        /// <summary>Walks single incoming links of <paramref name="typeId"/> to the top; false when <paramref name="entity"/> has none.</summary>
        public bool TryResolveRootSource(Entity entity, int typeId, out Entity root)
        {
            root = Entity.Null;
            Entity current = entity;
            int depth = 0;
            while (TryGetSingleSource(current, typeId, out Entity source))
            {
                root = source;
                current = source;
                if (++depth > MaxWalkDepth)
                {
                    throw new InvalidOperationException(
                        $"Walking up '{_types.Get(typeId).Name}' from {Describe(entity)} exceeded {MaxWalkDepth} steps; the relationship has a cycle (declare rules.acyclic).");
                }
            }

            return root != Entity.Null;
        }

        /// <summary>True when <paramref name="candidate"/> reaches <paramref name="entity"/> through one or more links of <paramref name="typeId"/>.</summary>
        public bool IsUpstreamOf(Entity candidate, Entity entity, int typeId)
        {
            if (candidate == Entity.Null || entity == Entity.Null || candidate == entity)
            {
                return false;
            }

            if (!IsAliveInRuntimeWorld(candidate) || !IsAliveInRuntimeWorld(entity))
            {
                return false;
            }

            _upstreamQueue.Clear();
            _upstreamVisited.Clear();
            _upstreamQueue.Add(entity);
            _upstreamVisited.Add(entity);
            for (int head = 0; head < _upstreamQueue.Count; head++)
            {
                int count = CollectIncomingInto(_upstreamQueue[head], typeId, ref _walkEdgeScratch);
                for (int i = 0; i < count; i++)
                {
                    Entity source = _walkEdgeScratch[i];
                    if (source == candidate)
                    {
                        return true;
                    }

                    if (_upstreamVisited.Add(source))
                    {
                        _upstreamQueue.Add(source);
                    }
                }
            }

            return false;
        }

        /// <summary>Appends every entity reachable from <paramref name="root"/> through outgoing links of <paramref name="typeId"/>, excluding the root.</summary>
        public void CollectDownstream(Entity root, int typeId, List<Entity> output)
        {
            ArgumentNullException.ThrowIfNull(output);
            if (!IsAliveInRuntimeWorld(root))
            {
                return;
            }

            _downstreamQueue.Clear();
            _downstreamVisited.Clear();
            _downstreamQueue.Add(root);
            _downstreamVisited.Add(root);
            for (int head = 0; head < _downstreamQueue.Count; head++)
            {
                int count = CollectOutgoingInto(_downstreamQueue[head], typeId, ref _walkEdgeScratch);
                for (int i = 0; i < count; i++)
                {
                    Entity child = _walkEdgeScratch[i];
                    if (_downstreamVisited.Add(child))
                    {
                        _downstreamQueue.Add(child);
                        output.Add(child);
                    }
                }
            }
        }

        public void RemoveIncoming(Entity target, int typeId)
        {
            if (!IsAliveInRuntimeWorld(target))
            {
                return;
            }

            int count = CollectIncomingInto(target, typeId, ref _ruleScratch);
            for (int i = 0; i < count; i++)
            {
                RemoveLink(_ruleScratch[i], target, typeId);
            }
        }

        private void RejectLinkBreakingRule(Entity source, Entity target, int typeId, in RelationshipTypeRule rule)
        {
            for (int i = 0; i < rule.BlockedAnyTypeIds.Length; i++)
            {
                int blocked = rule.BlockedAnyTypeIds[i];
                if (HasLink(source, target, blocked))
                {
                    throw new InvalidOperationException(
                        $"Cannot link {Describe(source)} -> {Describe(target)} as '{_types.Get(typeId).Name}': " +
                        $"the pair already has '{_types.Get(blocked).Name}' (rules.blockedAny).");
                }
            }

            if (rule.Acyclic && (source == target || IsUpstreamOf(target, source, typeId)))
            {
                throw new InvalidOperationException(
                    $"Cannot link {Describe(source)} -> {Describe(target)} as '{_types.Get(typeId).Name}': " +
                    "the link would close a cycle (rules.acyclic).");
            }

            if (rule.OnFull != RelationshipCapacityPolicy.Reject)
            {
                return;
            }

            if (rule.MaxIncoming > 0 && CountIncoming(target, typeId) >= rule.MaxIncoming)
            {
                throw new InvalidOperationException(
                    $"Cannot link {Describe(source)} -> {Describe(target)} as '{_types.Get(typeId).Name}': " +
                    $"the target already has {rule.MaxIncoming} incoming link(s) (rules.maxIncoming, onFull Reject).");
            }

            if (rule.MaxOutgoing > 0 && CountOutgoing(source, typeId) >= rule.MaxOutgoing)
            {
                throw new InvalidOperationException(
                    $"Cannot link {Describe(source)} -> {Describe(target)} as '{_types.Get(typeId).Name}': " +
                    $"the source already has {rule.MaxOutgoing} outgoing link(s) (rules.maxOutgoing, onFull Reject).");
            }
        }

        private void MakeRoomForLink(Entity source, Entity target, int typeId, in RelationshipTypeRule rule)
        {
            if (rule.OnFull == RelationshipCapacityPolicy.Replace)
            {
                if (rule.MaxIncoming == 1)
                {
                    RemoveIncoming(target, typeId);
                }

                if (rule.MaxOutgoing == 1)
                {
                    int count = CollectOutgoingInto(source, typeId, ref _ruleScratch);
                    for (int i = 0; i < count; i++)
                    {
                        RemoveLink(source, _ruleScratch[i], typeId);
                    }
                }
            }

            for (int i = 0; i < rule.RemovedTypeIds.Length; i++)
            {
                RemoveLink(source, target, rule.RemovedTypeIds[i]);
            }
        }

        private void ProjectPlayerOwnerSubtree(Entity root, int ownsTypeId)
        {
            if (!IsAliveInRuntimeWorld(root))
            {
                return;
            }

            ParticipantIdentityProjector.SyncPlayerOwner(_world, root, this, ownsTypeId);
            _projectionScratch.Clear();
            CollectDownstream(root, ownsTypeId, _projectionScratch);
            for (int i = 0; i < _projectionScratch.Count; i++)
            {
                ParticipantIdentityProjector.SyncPlayerOwner(_world, _projectionScratch[i], this, ownsTypeId);
            }
        }

        private int CountIncoming(Entity target, int typeId)
        {
            int count = CollectIncoming(target, typeId, Span<Entity>.Empty, out int dropped);
            return count + dropped;
        }

        private int CountOutgoing(Entity source, int typeId)
        {
            int count = CollectOutgoing(source, typeId, Span<Entity>.Empty, out int dropped);
            return count + dropped;
        }

        private int CollectIncomingInto(Entity target, int typeId, ref Entity[] buffer)
        {
            int count = CollectIncoming(target, typeId, buffer, out int dropped);
            if (dropped == 0)
            {
                return count;
            }

            Array.Resize(ref buffer, count + dropped);
            return CollectIncoming(target, typeId, buffer, out _);
        }

        private int CollectOutgoingInto(Entity source, int typeId, ref Entity[] buffer)
        {
            int count = CollectOutgoing(source, typeId, buffer, out int dropped);
            if (dropped == 0)
            {
                return count;
            }

            Array.Resize(ref buffer, count + dropped);
            return CollectOutgoing(source, typeId, buffer, out _);
        }

        private static string Describe(Entity entity) => $"{entity.Id}:{entity.Version}";
    }
}
