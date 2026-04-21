using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public sealed class PerformerEntityRuntime
    {
        private readonly World _world;
        private int _activeCount;
        private int _structureVersion;
        private readonly Dictionary<int, int> _ownerPayloadRefCounts = new();
        private readonly Dictionary<int, List<Entity>> _byDefinition = new();
        private readonly Dictionary<OwnerDefinitionKey, List<Entity>> _byOwnerDefinition = new();
        private readonly Dictionary<ScopedOwnerKey, Entity> _scopedInstances = new();

        public int ActiveCount => _activeCount;
        public int StructureVersion => _structureVersion;
        public World World => _world;

        public PerformerEntityRuntime(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public Entity Create(
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition,
            int stableId,
            Entity parent,
            PerformerDefinition definition)
        {
            var state = new PerformerState
            {
                DefId = defId,
                StableId = stableId,
                ScopeId = scopeId,
                OwnerEntity = owner,
                AnchorKind = anchorKind,
                BehaviorActiveMask = 0u,
                Elapsed = 0f,
                Version = 1,
                DefaultLifetime = definition.DefaultLifetime,
            };

            var transformSource = parent != Entity.Null
                ? TransformSource.InheritParent
                : anchorKind == PresentationAnchorKind.Entity
                    ? TransformSource.EntityTransform
                    : TransformSource.WorldFixed;
            var entity = _world.Create(
                state,
                new PerformerWorldPosition { Value = worldPosition },
                new PerformerWorldRotation { Value = Quaternion.Identity },
                new PerformerWorldScale { Value = Vector3.One },
                new PerformerTransformSource { Value = transformSource },
                new PerformerParent { Parent = parent },
                new PerformerChildren(),
                new PerformerCullState { OwnerCullVisible = true, LOD = LODLevel.High },
                new PerformerFloatParams(),
                new PerformerIntParams(),
                new PerformerVectorParams(),
                new PerformerFloatDefaults(),
                new PerformerIntDefaults(),
                new PerformerVectorDefaults(),
                new PerformerEmitCache());

            AddBehaviorMarkers(entity, definition);

            if (parent != Entity.Null && _world.IsAlive(parent))
            {
                ref var parentChildren = ref _world.Get<PerformerChildren>(parent);
                parentChildren.Add(entity);
            }

            _activeCount++;
            _structureVersion++;
            AddOwnerPayloadRef(owner);
            AddIndexes(entity, in state);
            return entity;
        }

        public Entity CreateHierarchy(
            PerformerDefinitionRegistry definitions,
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition,
            int stableId,
            Entity parent,
            PerformerDefinition definition,
            Func<int>? allocateStableId = null)
        {
            Entity entity = Create(defId, owner, scopeId, anchorKind, in worldPosition, stableId, parent, definition);
            SetParamDefault(definition, entity);
            InitializeTransform(entity, definition);
            CreateChildrenRecursive(definitions, entity, owner, scopeId, anchorKind, allocateStableId);
            return entity;
        }

        public void InitializeTransform(Entity performer, PerformerDefinition definition)
        {
            if (!_world.IsAlive(performer) || !_world.Has<PerformerState>(performer))
            {
                return;
            }

            ref readonly PerformerState state = ref _world.Get<PerformerState>(performer);
            Entity owner = state.OwnerEntity;
            bool hasOwnerTransform = _world.IsAlive(owner) && _world.Has<VisualTransform>(owner);
            VisualTransform ownerTransform = hasOwnerTransform ? _world.Get<VisualTransform>(owner) : VisualTransform.Default;

            Vector3 position = hasOwnerTransform ? ownerTransform.Position : _world.Get<PerformerWorldPosition>(performer).Value;
            Quaternion rotation = hasOwnerTransform ? ownerTransform.Rotation : Quaternion.Identity;
            Vector3 scale = hasOwnerTransform ? ownerTransform.Scale : Vector3.One;

            position += definition.PositionOffset;

            _world.Get<PerformerWorldPosition>(performer).Value = position;
            if (_world.Has<PerformerWorldRotation>(performer))
            {
                _world.Get<PerformerWorldRotation>(performer).Value = rotation;
            }

            if (_world.Has<PerformerWorldScale>(performer))
            {
                _world.Get<PerformerWorldScale>(performer).Value = scale;
            }
        }

        public Entity Create(int defId, Entity owner, int scopeId)
        {
            return Create(defId, owner, scopeId, PresentationAnchorKind.Entity,
                Vector3.Zero, 0, Entity.Null, default);
        }

        public void Destroy(Entity performer, Action<Entity, PerformerState>? onDestroyed = null)
        {
            if (!_world.IsAlive(performer) || !_world.Has<PerformerState>(performer))
                return;

            ref var children = ref _world.Get<PerformerChildren>(performer);
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var childEntity = children.Get(i);
                if (_world.IsAlive(childEntity))
                    Destroy(childEntity, onDestroyed);
            }

            UnlinkFromParent(performer);

            var snapshot = _world.Get<PerformerState>(performer);
            RemoveIndexes(performer, in snapshot);
            onDestroyed?.Invoke(performer, snapshot);

            RemoveOwnerPayloadRef(snapshot.OwnerEntity);
            _activeCount--;
            _structureVersion++;
            _world.Destroy(performer);
        }

        public int DestroyScope(int scopeId, Action<Entity, PerformerState>? onDestroyed = null)
        {
            if (scopeId <= 0)
                throw new InvalidOperationException($"DestroyPerformerScope requires ScopeTag > 0, got {scopeId}.");

            var toDestroy = new List<Entity>();
            var query = new QueryDescription().WithAll<PerformerState>();
            _world.Query(in query, (Entity entity, ref PerformerState state) =>
            {
                if (state.ScopeId == scopeId)
                    toDestroy.Add(entity);
            });

            int destroyed = 0;
            foreach (var entity in toDestroy)
            {
                if (_world.IsAlive(entity))
                {
                    Destroy(entity, onDestroyed);
                    destroyed++;
                }
            }
            return destroyed;
        }
        public void SetParam(Entity performer, int paramKey, ParamLane lane,
            float floatValue, int intValue, in Vector4 vectorValue)
        {
            if (!_world.IsAlive(performer)) return;

            switch (lane)
            {
                case ParamLane.Float:
                    ref var fp = ref _world.Get<PerformerFloatParams>(performer);
                    fp.Set(paramKey, floatValue);
                    break;
                case ParamLane.Int:
                    ref var ip = ref _world.Get<PerformerIntParams>(performer);
                    ip.Set(paramKey, intValue);
                    break;
                case ParamLane.Vector:
                    ref var vp = ref _world.Get<PerformerVectorParams>(performer);
                    vp.Set(paramKey, in vectorValue);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lane));
            }

            ref var state = ref _world.Get<PerformerState>(performer);
            state.Version++;
        }

        public void SetParamDefault(in PerformerDefinition definition, Entity performer)
        {
            ParamDefault[] defaults = definition.ParamDefaults;
            for (int i = 0; i < defaults.Length; i++)
            {
                ref readonly ParamDefault entry = ref defaults[i];
                switch (entry.Lane)
                {
                    case ParamLane.Float:
                        ref var fd = ref _world.Get<PerformerFloatDefaults>(performer);
                        fd.Set(entry.ParamKey, entry.FloatValue);
                        break;
                    case ParamLane.Int:
                        ref var id = ref _world.Get<PerformerIntDefaults>(performer);
                        id.Set(entry.ParamKey, entry.IntValue);
                        break;
                    case ParamLane.Vector:
                        ref var vd = ref _world.Get<PerformerVectorDefaults>(performer);
                        vd.Set(entry.ParamKey, entry.VectorValue);
                        break;
                }
            }
        }

        public float ResolveFloat(Entity performer, int paramKey, float defaultValue = 0f)
        {
            return PerformerParamResolver.ResolveFloat(_world, performer, paramKey, defaultValue);
        }

        public int ResolveInt(Entity performer, int paramKey, int defaultValue = 0)
        {
            return PerformerParamResolver.ResolveInt(_world, performer, paramKey, defaultValue);
        }

        public Vector4 ResolveVector(Entity performer, int paramKey, Vector4 defaultValue)
        {
            return PerformerParamResolver.ResolveVector(_world, performer, paramKey, defaultValue);
        }
        public bool HasOwnerPayload(Entity owner)
        {
            return owner != Entity.Null &&
                   _ownerPayloadRefCounts.TryGetValue(owner.Id, out int count) &&
                   count > 0;
        }

        public bool HasActiveScopedInstance(
            int defId, Entity owner, int scopeId,
            PresentationAnchorKind anchorKind, Vector3 worldPosition)
        {
            var key = new ScopedOwnerKey(defId, owner, scopeId, anchorKind, worldPosition);
            if (!_scopedInstances.TryGetValue(key, out Entity entity))
            {
                return false;
            }

            if (_world.IsAlive(entity) && _world.Has<PerformerState>(entity))
            {
                return true;
            }

            _scopedInstances.Remove(key);
            return false;
        }

        public IReadOnlyList<Entity> GetActiveByDefinition(int defId)
        {
            return _byDefinition.TryGetValue(defId, out var entities)
                ? entities
                : Array.Empty<Entity>();
        }

        public IReadOnlyList<Entity> GetActiveByOwnerDefinition(int defId, Entity owner)
        {
            return _byOwnerDefinition.TryGetValue(new OwnerDefinitionKey(owner, defId), out var entities)
                ? entities
                : Array.Empty<Entity>();
        }

        public void SyncCullVisibility()
        {
            var query = new QueryDescription().WithAll<PerformerState, PerformerCullState>();
            _world.Query(in query, (Entity entity, ref PerformerState state, ref PerformerCullState cull) =>
            {
                cull.OwnerCullVisible = ResolveOwnerCullVisible(state);
                cull.LOD = ResolveOwnerLod(state);
            });

            _world.Query(in query, (Entity entity, ref PerformerState state, ref PerformerCullState cull) =>
            {
                if (!_world.Has<PerformerParent>(entity)) return;
                var parent = _world.Get<PerformerParent>(entity).Parent;
                if (parent == Entity.Null || !_world.IsAlive(parent)) return;
                if (!_world.Has<PerformerCullState>(parent)) return;
                var parentCull = _world.Get<PerformerCullState>(parent);
                cull.OwnerCullVisible = cull.OwnerCullVisible && parentCull.OwnerCullVisible;
            });
        }

        public int ReleaseDeadEntityAnchors(Action<Entity, PerformerState>? onDestroyed = null)
        {
            var toDestroy = new List<Entity>();
            var query = new QueryDescription().WithAll<PerformerState>();
            _world.Query(in query, (Entity entity, ref PerformerState state) =>
            {
                if (state.AnchorKind != PresentationAnchorKind.Entity) return;
                if (!_world.IsAlive(state.OwnerEntity))
                    toDestroy.Add(entity);
            });

            int released = 0;
            foreach (var entity in toDestroy)
            {
                if (_world.IsAlive(entity))
                {
                    Destroy(entity, onDestroyed);
                    released++;
                }
            }
            return released;
        }

        public int ReleaseExpired(PerformerDefinitionRegistry definitions,
            Action<Entity, PerformerState>? onReleased = null)
        {
            var toRelease = new List<Entity>();
            var query = new QueryDescription().WithAll<PerformerState>();
            _world.Query(in query, (Entity entity, ref PerformerState state) =>
            {
                if (state.DefaultLifetime > 0f && state.Elapsed >= state.DefaultLifetime)
                    toRelease.Add(entity);
            });

            int released = 0;
            foreach (var entity in toRelease)
            {
                if (_world.IsAlive(entity))
                {
                    Destroy(entity, onReleased);
                    released++;
                }
            }
            return released;
        }

        public void AdvanceElapsed(float dt)
        {
            var query = new QueryDescription().WithAll<PerformerState>();
            _world.Query(in query, (Entity entity, ref PerformerState state) =>
            {
                state.Elapsed += dt;
            });
        }

        public void Clear()
        {
            var toDestroy = new List<Entity>();
            var query = new QueryDescription().WithAll<PerformerState>();
            _world.Query(in query, (Entity entity) => toDestroy.Add(entity));
            foreach (var entity in toDestroy)
            {
                if (_world.IsAlive(entity))
                    _world.Destroy(entity);
            }
            _activeCount = 0;
            _structureVersion++;
            _ownerPayloadRefCounts.Clear();
            _byDefinition.Clear();
            _byOwnerDefinition.Clear();
            _scopedInstances.Clear();
        }

        private void AddBehaviorMarkers(Entity entity, PerformerDefinition definition)
        {
            BehaviorSlot[] behaviors = definition.Behaviors;
            if (behaviors == null) return;

            bool hasAsset = false, hasAttr = false, hasTag = false;
            bool hasAnimator = false, hasMaterial = false, hasSound = false;
            bool hasSpline = false, hasAttachment = false;

            for (int i = 0; i < behaviors.Length; i++)
            {
                switch (behaviors[i].Kind)
                {
                    case BehaviorKind.AssetBinding: hasAsset = true; break;
                    case BehaviorKind.AttributeBinding: hasAttr = true; break;
                    case BehaviorKind.TagBinding: hasTag = true; break;
                    case BehaviorKind.Animator: hasAnimator = true; break;
                    case BehaviorKind.Material: hasMaterial = true; break;
                    case BehaviorKind.Sound: hasSound = true; break;
                    case BehaviorKind.Spline: hasSpline = true; break;
                    case BehaviorKind.Attachment: hasAttachment = true; break;
                }
            }

            if (hasAsset) _world.Add<PerfHasAssetBinding>(entity);
            if (hasAttr) _world.Add<PerfHasAttributeBinding>(entity);
            if (hasTag) _world.Add<PerfHasTagBinding>(entity);
            if (hasAnimator) _world.Add<PerfHasAnimator>(entity);
            if (hasMaterial) _world.Add<PerfHasMaterial>(entity);
            if (hasSound) _world.Add<PerfHasSound>(entity);
            if (hasSpline) _world.Add<PerfHasSpline>(entity);
            if (hasAttachment) _world.Add<PerfHasAttachment>(entity);
        }

        private void CreateChildrenRecursive(
            PerformerDefinitionRegistry definitions,
            Entity parentEntity,
            Entity owner,
            int parentScopeId,
            PresentationAnchorKind anchorKind,
            Func<int>? allocateStableId)
        {
            if (!definitions.TryGet(_world.Get<PerformerState>(parentEntity).DefId, out PerformerDefinition parentDefinition))
            {
                return;
            }

            ChildPerformerRef[] children = parentDefinition.Children;
            if (children == null || children.Length == 0)
            {
                return;
            }

            for (int i = 0; i < children.Length; i++)
            {
                ref readonly ChildPerformerRef child = ref children[i];
                if (!definitions.TryGet(child.DefinitionId, out PerformerDefinition childDefinition))
                {
                    throw new InvalidOperationException($"Performer child definition id={child.DefinitionId} is not registered.");
                }

                int childScopeId = child.ScopeTag > 0 ? child.ScopeTag : parentScopeId;
                Entity childEntity = Create(
                    child.DefinitionId,
                    owner,
                    childScopeId,
                    anchorKind,
                    Vector3.Zero,
                    allocateStableId != null ? allocateStableId() : 0,
                    parentEntity,
                    childDefinition);

                ref PerformerState childState = ref _world.Get<PerformerState>(childEntity);
                childState.BehaviorActiveMask = BuildDefaultBehaviorMask(childDefinition);
                SetParamDefault(childDefinition, childEntity);
                ApplyParamOverrides(childEntity, child.ParamOverrides);
                InitializeTransform(childEntity, childDefinition);
                CreateChildrenRecursive(definitions, childEntity, owner, childScopeId, anchorKind, allocateStableId);
            }
        }

        private static uint BuildDefaultBehaviorMask(PerformerDefinition definition)
        {
            if (definition.Behaviors == null || definition.Behaviors.Length == 0)
            {
                return 0u;
            }

            uint mask = 0u;
            for (int i = 0; i < definition.Behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref definition.Behaviors[i];
                if (!slot.ActiveByDefault || slot.SlotIndex < 0 || slot.SlotIndex >= 32)
                {
                    continue;
                }

                mask |= 1u << slot.SlotIndex;
            }

            return mask;
        }

        private void ApplyParamOverrides(Entity performer, ParamDefault[] overrides)
        {
            if (overrides == null || overrides.Length == 0)
            {
                return;
            }

            for (int i = 0; i < overrides.Length; i++)
            {
                ref readonly ParamDefault entry = ref overrides[i];
                switch (entry.Lane)
                {
                    case ParamLane.Float:
                        SetParam(performer, entry.ParamKey, ParamLane.Float, entry.FloatValue, 0, Vector4.Zero);
                        break;
                    case ParamLane.Int:
                        SetParam(performer, entry.ParamKey, ParamLane.Int, 0f, entry.IntValue, Vector4.Zero);
                        break;
                    case ParamLane.Vector:
                        SetParam(performer, entry.ParamKey, ParamLane.Vector, 0f, 0, entry.VectorValue);
                        break;
                }
            }
        }

        private void UnlinkFromParent(Entity performer)
        {
            if (!_world.Has<PerformerParent>(performer)) return;
            var parent = _world.Get<PerformerParent>(performer).Parent;
            if (parent == Entity.Null || !_world.IsAlive(parent)) return;
            if (!_world.Has<PerformerChildren>(parent)) return;
            ref var parentChildren = ref _world.Get<PerformerChildren>(parent);
            parentChildren.Remove(performer);
        }

        private bool ResolveOwnerCullVisible(in PerformerState state)
        {
            if (state.AnchorKind != PresentationAnchorKind.Entity) return true;
            if (!_world.IsAlive(state.OwnerEntity)) return false;
            return !_world.Has<CullState>(state.OwnerEntity) ||
                   _world.Get<CullState>(state.OwnerEntity).IsVisible;
        }

        private LODLevel ResolveOwnerLod(in PerformerState state)
        {
            if (!_world.IsAlive(state.OwnerEntity) ||
                !_world.Has<CullState>(state.OwnerEntity))
                return LODLevel.High;
            return _world.Get<CullState>(state.OwnerEntity).LOD;
        }

        private void AddOwnerPayloadRef(Entity owner)
        {
            if (owner == Entity.Null) return;
            if (_ownerPayloadRefCounts.TryGetValue(owner.Id, out int count))
                _ownerPayloadRefCounts[owner.Id] = count + 1;
            else
                _ownerPayloadRefCounts.Add(owner.Id, 1);
        }

        private void RemoveOwnerPayloadRef(Entity owner)
        {
            if (owner == Entity.Null) return;
            if (!_ownerPayloadRefCounts.TryGetValue(owner.Id, out int count)) return;
            if (count <= 1)
                _ownerPayloadRefCounts.Remove(owner.Id);
            else
                _ownerPayloadRefCounts[owner.Id] = count - 1;
        }

        private void AddIndexes(Entity performer, in PerformerState state)
        {
            AddToIndex(_byDefinition, state.DefId, performer);
            AddToIndex(_byOwnerDefinition, new OwnerDefinitionKey(state.OwnerEntity, state.DefId), performer);
            if (state.ScopeId > 0 && state.DefaultLifetime <= 0f)
            {
                Vector3 position = _world.Get<PerformerWorldPosition>(performer).Value;
                _scopedInstances[new ScopedOwnerKey(
                    state.DefId,
                    state.OwnerEntity,
                    state.ScopeId,
                    state.AnchorKind,
                    position)] = performer;
            }
        }

        private void RemoveIndexes(Entity performer, in PerformerState state)
        {
            RemoveFromIndex(_byDefinition, state.DefId, performer);
            RemoveFromIndex(_byOwnerDefinition, new OwnerDefinitionKey(state.OwnerEntity, state.DefId), performer);
            if (state.ScopeId > 0 && state.DefaultLifetime <= 0f)
            {
                Vector3 position = _world.Has<PerformerWorldPosition>(performer)
                    ? _world.Get<PerformerWorldPosition>(performer).Value
                    : Vector3.Zero;
                _scopedInstances.Remove(new ScopedOwnerKey(
                    state.DefId,
                    state.OwnerEntity,
                    state.ScopeId,
                    state.AnchorKind,
                    position));
            }
        }

        private static void AddToIndex<TKey>(Dictionary<TKey, List<Entity>> index, TKey key, Entity performer)
            where TKey : notnull
        {
            if (!index.TryGetValue(key, out var entities))
            {
                entities = new List<Entity>(4);
                index[key] = entities;
            }

            entities.Add(performer);
        }

        private static void RemoveFromIndex<TKey>(Dictionary<TKey, List<Entity>> index, TKey key, Entity performer)
            where TKey : notnull
        {
            if (!index.TryGetValue(key, out var entities))
            {
                return;
            }

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                if (entities[i] != performer)
                {
                    continue;
                }

                int last = entities.Count - 1;
                entities[i] = entities[last];
                entities.RemoveAt(last);
                break;
            }

            if (entities.Count == 0)
            {
                index.Remove(key);
            }
        }

        private readonly struct OwnerDefinitionKey : IEquatable<OwnerDefinitionKey>
        {
            private readonly int _ownerId;
            private readonly int _ownerWorldId;
            private readonly int _ownerVersion;
            private readonly int _defId;

            public OwnerDefinitionKey(Entity owner, int defId)
            {
                _ownerId = owner.Id;
                _ownerWorldId = owner.WorldId;
                _ownerVersion = owner.Version;
                _defId = defId;
            }

            public bool Equals(OwnerDefinitionKey other)
            {
                return _ownerId == other._ownerId &&
                       _ownerWorldId == other._ownerWorldId &&
                       _ownerVersion == other._ownerVersion &&
                       _defId == other._defId;
            }

            public override bool Equals(object? obj)
            {
                return obj is OwnerDefinitionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_ownerId, _ownerWorldId, _ownerVersion, _defId);
            }
        }

        private readonly struct ScopedOwnerKey : IEquatable<ScopedOwnerKey>
        {
            private readonly int _defId;
            private readonly int _ownerId;
            private readonly int _ownerWorldId;
            private readonly int _ownerVersion;
            private readonly int _scopeId;
            private readonly PresentationAnchorKind _anchorKind;
            private readonly Vector3 _worldPosition;

            public ScopedOwnerKey(
                int defId,
                Entity owner,
                int scopeId,
                PresentationAnchorKind anchorKind,
                in Vector3 worldPosition)
            {
                _defId = defId;
                _ownerId = owner.Id;
                _ownerWorldId = owner.WorldId;
                _ownerVersion = owner.Version;
                _scopeId = scopeId;
                _anchorKind = anchorKind;
                _worldPosition = anchorKind == PresentationAnchorKind.WorldPosition ? worldPosition : Vector3.Zero;
            }

            public bool Equals(ScopedOwnerKey other)
            {
                return _defId == other._defId &&
                       _ownerId == other._ownerId &&
                       _ownerWorldId == other._ownerWorldId &&
                       _ownerVersion == other._ownerVersion &&
                       _scopeId == other._scopeId &&
                       _anchorKind == other._anchorKind &&
                       _worldPosition == other._worldPosition;
            }

            public override bool Equals(object? obj)
            {
                return obj is ScopedOwnerKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_defId, _ownerId, _ownerWorldId, _ownerVersion, _scopeId, _anchorKind, _worldPosition);
            }
        }
    }
}
