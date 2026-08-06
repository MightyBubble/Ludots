using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Layers;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// Contact begin/end edge detection (issue #732). Consumes the broadphase pairing
    /// results (CollisionPair.ContactCount 0↔>0 transitions) for entities that opted in
    /// via ContactEventEmitter2D and exports events into a preallocated queue.
    ///
    /// Recycling contract: a tracked touching contact whose pair is not visited this step
    /// (pair recycled, entity died, or emitter declaration removed) gets a synthesized End
    /// event from its last known payload — no contact may leak a permanent "begin" state.
    /// Contact truth follows the solver's active pair set: when two dynamic emitters both
    /// fall asleep their pair is recycled and an End is emitted.
    ///
    /// Zero cost when unused: with no ContactEventEmitter2D entities and no tracked
    /// contacts the system returns before touching any collision pair.
    /// </summary>
    public sealed class ContactEventSystem2D : BaseSystem<World, float>
    {
        private readonly struct TrackedContactKey : IEquatable<TrackedContactKey>
        {
            public readonly int EntityAId;
            public readonly int EntityBId;
            public readonly byte ShapeSlotA;
            public readonly byte ShapeSlotB;

            public TrackedContactKey(int entityAId, byte shapeSlotA, int entityBId, byte shapeSlotB)
            {
                EntityAId = entityAId;
                ShapeSlotA = shapeSlotA;
                EntityBId = entityBId;
                ShapeSlotB = shapeSlotB;
            }

            public bool Equals(TrackedContactKey other)
            {
                return EntityAId == other.EntityAId &&
                    EntityBId == other.EntityBId &&
                    ShapeSlotA == other.ShapeSlotA &&
                    ShapeSlotB == other.ShapeSlotB;
            }

            public override bool Equals(object? obj)
            {
                return obj is TrackedContactKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(EntityAId, EntityBId, ShapeSlotA, ShapeSlotB);
            }
        }

        private struct TrackedContact
        {
            public Entity EntityA;
            public Entity EntityB;
            public byte ShapeSlotA;
            public byte ShapeSlotB;
            public Fix64Vec2 LastNormal;
            public LayerMask LayerA;
            public LayerMask LayerB;
            public int LastSeenStep;
        }

        private readonly QueryDescription _activePairsQuery;
        private readonly QueryDescription _emittersQuery;
        private readonly ContactEventQueue2D _queue;
        private readonly IReadOnlyList<string> _allowedLayerNames;
        private readonly HashSet<int> _emitterEntityIds;
        private readonly Dictionary<TrackedContactKey, TrackedContact> _tracked;
        private readonly List<TrackedContactKey> _staleKeys;

        private uint _allowedEmitterCategoryMask;
        private bool _allowedLayersResolved;
        private int _stepIndex;

        public ContactEventSystem2D(
            World world,
            ContactEventQueue2D queue,
            IReadOnlyList<string> allowedEmitterLayerNames,
            int maxTrackedContacts) : base(world)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _allowedLayerNames = allowedEmitterLayerNames ?? throw new ArgumentNullException(nameof(allowedEmitterLayerNames));
            if (maxTrackedContacts < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxTrackedContacts), maxTrackedContacts, "maxTrackedContacts must be > 0.");
            }

            _activePairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            _emittersQuery = new QueryDescription().WithAll<ContactEventEmitter2D>();
            _emitterEntityIds = new HashSet<int>(256);
            _tracked = new Dictionary<TrackedContactKey, TrackedContact>(maxTrackedContacts);
            _staleKeys = new List<TrackedContactKey>(256);
        }

        public override void Update(in float deltaTime)
        {
            _stepIndex = unchecked(_stepIndex + 1);

            _emitterEntityIds.Clear();
            var collectJob = new CollectEmittersJob { EmitterEntityIds = _emitterEntityIds };
            World.InlineEntityQuery<CollectEmittersJob, ContactEventEmitter2D>(in _emittersQuery, ref collectJob);

            if (_emitterEntityIds.Count == 0 && _tracked.Count == 0)
            {
                return;
            }

            if (_emitterEntityIds.Count > 0)
            {
                ResolveAllowedLayersOnce();
                var detectJob = new DetectEdgesJob { Owner = this };
                World.InlineQuery<DetectEdgesJob, CollisionPair>(in _activePairsQuery, ref detectJob);
            }

            SweepStaleTrackedContacts();
        }

        private void ResolveAllowedLayersOnce()
        {
            if (_allowedLayersResolved)
            {
                return;
            }

            uint mask = 0u;
            for (int i = 0; i < _allowedLayerNames.Count; i++)
            {
                mask |= 1u << LayerRegistry.GetIndex(_allowedLayerNames[i]);
            }

            _allowedEmitterCategoryMask = mask;
            _allowedLayersResolved = true;
        }

        private void ProcessPair(ref CollisionPair pair)
        {
            bool emitterA = _emitterEntityIds.Contains(pair.EntityA.Id);
            bool emitterB = _emitterEntityIds.Contains(pair.EntityB.Id);
            if (!emitterA && !emitterB)
            {
                return;
            }

            var key = new TrackedContactKey(pair.EntityA.Id, pair.ShapeSlotA, pair.EntityB.Id, pair.ShapeSlotB);
            bool touching = pair.ContactCount > 0;

            if (touching)
            {
                if (_tracked.TryGetValue(key, out TrackedContact tracked))
                {
                    tracked.LastNormal = pair.Normal;
                    tracked.LastSeenStep = _stepIndex;
                    _tracked[key] = tracked;
                    return;
                }

                BeginContact(ref pair, in key, emitterA, emitterB);
                return;
            }

            if (_tracked.TryGetValue(key, out TrackedContact separated))
            {
                EnqueueEnd(in separated);
                _tracked.Remove(key);
            }
        }

        private void BeginContact(ref CollisionPair pair, in TrackedContactKey key, bool emitterA, bool emitterB)
        {
            LayerMask layerA = RequireEntityLayer(pair.EntityA);
            LayerMask layerB = RequireEntityLayer(pair.EntityB);

            if (emitterA)
            {
                RequireAllowedEmitterLayer(pair.EntityA, in layerA);
            }

            if (emitterB)
            {
                RequireAllowedEmitterLayer(pair.EntityB, in layerB);
            }

            _queue.Enqueue(new ContactEvent2D
            {
                Type = ContactEventType2D.Begin,
                EntityA = pair.EntityA,
                EntityB = pair.EntityB,
                ShapeSlotA = pair.ShapeSlotA,
                ShapeSlotB = pair.ShapeSlotB,
                Normal = pair.Normal,
                Penetration = pair.Penetration,
                LayerA = layerA,
                LayerB = layerB
            });

            _tracked.Add(key, new TrackedContact
            {
                EntityA = pair.EntityA,
                EntityB = pair.EntityB,
                ShapeSlotA = pair.ShapeSlotA,
                ShapeSlotB = pair.ShapeSlotB,
                LastNormal = pair.Normal,
                LayerA = layerA,
                LayerB = layerB,
                LastSeenStep = _stepIndex
            });
        }

        private LayerMask RequireEntityLayer(Entity entity)
        {
            if (!World.TryGet(entity, out EntityLayer layer))
            {
                throw new InvalidOperationException(
                    $"Contact event emission requires EntityLayer on both contact parties, but entity {entity.Id} has none (issue #732 payload contract).");
            }

            return layer.Value;
        }

        private void RequireAllowedEmitterLayer(Entity entity, in LayerMask layer)
        {
            if ((layer.Category & _allowedEmitterCategoryMask) == 0u)
            {
                throw new InvalidOperationException(
                    $"Entity {entity.Id} declares ContactEventEmitter2D but its EntityLayer category 0x{layer.Category:X8} is not covered by the 'Physics2D/kinematic.json' contactEventEmitterLayers allowlist (mask 0x{_allowedEmitterCategoryMask:X8}).");
            }
        }

        private void EnqueueEnd(in TrackedContact tracked)
        {
            _queue.Enqueue(new ContactEvent2D
            {
                Type = ContactEventType2D.End,
                EntityA = tracked.EntityA,
                EntityB = tracked.EntityB,
                ShapeSlotA = tracked.ShapeSlotA,
                ShapeSlotB = tracked.ShapeSlotB,
                Normal = tracked.LastNormal,
                Penetration = Fix64.Zero,
                LayerA = tracked.LayerA,
                LayerB = tracked.LayerB
            });
        }

        private void SweepStaleTrackedContacts()
        {
            if (_tracked.Count == 0)
            {
                return;
            }

            _staleKeys.Clear();
            foreach (var kvp in _tracked)
            {
                if (kvp.Value.LastSeenStep != _stepIndex)
                {
                    _staleKeys.Add(kvp.Key);
                }
            }

            for (int i = 0; i < _staleKeys.Count; i++)
            {
                TrackedContactKey key = _staleKeys[i];
                if (!_tracked.TryGetValue(key, out TrackedContact tracked))
                {
                    continue;
                }

                EnqueueEnd(in tracked);
                _tracked.Remove(key);
            }
        }

        private struct CollectEmittersJob : IForEachWithEntity<ContactEventEmitter2D>
        {
            public HashSet<int> EmitterEntityIds;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref ContactEventEmitter2D emitter)
            {
                EmitterEntityIds.Add(entity.Id);
            }
        }

        private struct DetectEdgesJob : IForEach<CollisionPair>
        {
            public ContactEventSystem2D Owner;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                Owner.ProcessPair(ref pair);
            }
        }
    }
}
