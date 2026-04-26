using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using Arch.Core.Utils;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public sealed class PerformerEntityRuntime
    {
        private static readonly PerformerDefinition EmptyDefinition = new();
        private static readonly QueryDescription _performerCullQuery = new QueryDescription()
            .WithAll<PerformerState, PerformerCullState>();
        private static readonly QueryDescription _ownerPayloadMarkerQuery = new QueryDescription()
            .WithAll<PresentationOwnerHasPerformerPayload>();

        private readonly World _world;
        private PerformerDefinitionRegistry? _definitions;
        private int _activeCount;
        private int _structureVersion;
        private int _nonRootCount;
        private int _dirtyStaticVisualCount;
        private int _dirtyRetainedPresentationRequestCount;
        private readonly Dictionary<int, List<Entity>> _byDefinition = new();
        private readonly Dictionary<OwnerKey, OwnerPerformerBucket> _byOwner = new();
        private readonly Dictionary<OwnerDefinitionKey, List<Entity>> _byOwnerDefinition = new();
        private readonly Dictionary<ScopedOwnerKey, Entity> _scopedInstances = new();

        public int ActiveCount => _activeCount;
        public int StructureVersion => _structureVersion;
        public bool HasNonRootPerformers => _nonRootCount != 0;
        public bool HasDirtyStaticVisuals => _dirtyStaticVisualCount != 0;
        public bool HasDirtyRetainedPresentationRequests => _dirtyRetainedPresentationRequestCount != 0;
        public World World => _world;

        public PerformerEntityRuntime(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public void BindDefinitions(PerformerDefinitionRegistry definitions)
        {
            if (ReferenceEquals(_definitions, definitions))
            {
                return;
            }

            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            ReconcileBoundDefinitions();
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
            definition = ResolveDefinition(defId, definition);

            var state = new PerformerState
            {
                DefId = defId,
                StableId = stableId,
                ScopeId = scopeId,
                OwnerEntity = owner,
                AnchorKind = anchorKind,
                BehaviorActiveMask = BuildDefaultBehaviorMask(definition),
                Elapsed = 0f,
                Version = 1,
                DefaultLifetime = definition.DefaultLifetime,
            };

            var transformSource = parent != Entity.Null
                ? TransformSource.InheritParent
                : anchorKind == PresentationAnchorKind.Entity
                    ? TransformSource.EntityTransform
                    : TransformSource.WorldFixed;
            var cullState = new PerformerCullState
            {
                OwnerCullVisible = ResolveOwnerCullVisible(in state),
                LOD = ResolveOwnerLod(in state),
            };
            var entity = _world.Create(
                state,
                new PerformerWorldPosition { Value = worldPosition },
                new PerformerWorldRotation { Value = Quaternion.Identity },
                new PerformerWorldScale { Value = Vector3.One },
                new PerformerTransformSource { Value = transformSource },
                new PerformerParent { Parent = parent },
                new PerformerChildren(),
                cullState,
                new PerformerFloatParams(),
                new PerformerIntParams(),
                new PerformerVectorParams(),
                new PerformerFloatDefaults(),
                new PerformerIntDefaults(),
                new PerformerVectorDefaults(),
                new PerformerEmitCache());

            AddBehaviorMarkers(entity, definition, state.BehaviorActiveMask);
            AddEventDrivenStaticEmitMarkers(entity, definition);

            if (parent != Entity.Null && _world.IsAlive(parent))
            {
                ref var parentChildren = ref _world.Get<PerformerChildren>(parent);
                parentChildren.Add(entity);
                _nonRootCount++;
            }

            _activeCount++;
            _structureVersion++;
            AddIndexes(entity, in state, definition);
            return entity;
        }

        public int CreateEntityAnchoredRootBatch(
            PerformerDefinitionRegistry definitions,
            int defId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> scopeIds,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            PerformerDefinition definition,
            Span<Entity> created,
            Func<int>? allocateStableId = null)
        {
            if (owners.Length != scopeIds.Length ||
                owners.Length != stableIds.Length ||
                owners.Length != ownerTransforms.Length ||
                owners.Length != ownerCulls.Length ||
                owners.Length > created.Length)
            {
                throw new ArgumentException("Performer batch create spans must have matching lengths.");
            }

            if (owners.Length == 0)
            {
                return 0;
            }

            definition ??= EmptyDefinition;
            ReserveBatchIndexCapacity(owners.Length, definition);
            uint defaultBehaviorMask = BuildDefaultBehaviorMask(definition);
            Signature signature = BuildBatchSignature(definition, defaultBehaviorMask);
            _world.Create(created, signature, owners.Length);
            bool hasParamDefaults = definition.ParamDefaults != null && definition.ParamDefaults.Length > 0;
            bool hasChildren = definition.Children != null && definition.Children.Length > 0;
            FillEntityAnchoredRootBatch(
                created.Slice(0, owners.Length),
                owners,
                scopeIds,
                stableIds,
                ownerTransforms,
                ownerCulls,
                defId,
                definition,
                defaultBehaviorMask);
            if (definition.UsesEventDrivenStaticEmit)
            {
                _dirtyStaticVisualCount += owners.Length;
            }
            if (definition.UsesRetainedPresentationRequest)
            {
                _dirtyRetainedPresentationRequestCount += owners.Length;
            }
            _activeCount += owners.Length;
            _structureVersion++;

            if (hasParamDefaults)
            {
                for (int i = 0; i < owners.Length; i++)
                {
                    SetParamDefault(definition, created[i]);
                }
            }

            if (hasChildren)
            {
                for (int i = 0; i < owners.Length; i++)
                {
                    CreateChildrenRecursive(
                        definitions,
                        created[i],
                        owners[i],
                        scopeIds[i],
                        PresentationAnchorKind.Entity,
                        allocateStableId);
                }
            }

            return owners.Length;
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
            definition = ResolveDefinition(defId, definition);

            Entity entity = Create(defId, owner, scopeId, anchorKind, in worldPosition, stableId, parent, definition);
            ref PerformerState state = ref _world.Get<PerformerState>(entity);
            state.BehaviorActiveMask = BuildDefaultBehaviorMask(definition);
            SetParamDefault(definition, entity);
            InitializeTransform(entity, definition);
            CreateChildrenRecursive(definitions, entity, owner, scopeId, anchorKind, allocateStableId);
            return entity;
        }

        public void InitializeTransform(Entity performer, PerformerDefinition definition)
        {
            definition ??= EmptyDefinition;

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
            if (_world.Has<PerformerEmitCache>(performer))
            {
                ref PerformerEmitCache emitCache = ref _world.Get<PerformerEmitCache>(performer);
                if (emitCache.StaticDirty != 0 && _dirtyStaticVisualCount > 0)
                {
                    _dirtyStaticVisualCount--;
                }
                if (emitCache.RetainedDirty != 0 && _dirtyRetainedPresentationRequestCount > 0)
                {
                    _dirtyRetainedPresentationRequestCount--;
                }
            }
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

            bool changed = false;
            switch (lane)
            {
                case ParamLane.Float:
                    ref var fp = ref _world.Get<PerformerFloatParams>(performer);
                    changed = !fp.TryGet(paramKey, out float existingFloat) || existingFloat != floatValue;
                    fp.Set(paramKey, floatValue);
                    break;
                case ParamLane.Int:
                    ref var ip = ref _world.Get<PerformerIntParams>(performer);
                    changed = !ip.TryGet(paramKey, out int existingInt) || existingInt != intValue;
                    ip.Set(paramKey, intValue);
                    break;
                case ParamLane.Vector:
                    ref var vp = ref _world.Get<PerformerVectorParams>(performer);
                    changed = !vp.TryGet(paramKey, out Vector4 existingVector) || existingVector != vectorValue;
                    vp.Set(paramKey, in vectorValue);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lane));
            }

            if (!changed)
            {
                return;
            }

            ref var state = ref _world.Get<PerformerState>(performer);
            state.Version++;
            MarkStaticDirtyIfVisualParamChanged(performer, in state, paramKey, lane);
        }

        public void SetParamDefault(in PerformerDefinition definition, Entity performer)
        {
            if (definition == null)
            {
                return;
            }

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

        public void ClearParam(Entity performer, int paramKey, ParamLane lane)
        {
            if (!_world.IsAlive(performer))
            {
                return;
            }

            bool changed = false;
            switch (lane)
            {
                case ParamLane.Float:
                    if (_world.Has<PerformerFloatParams>(performer))
                    {
                        ref PerformerFloatParams floats = ref _world.Get<PerformerFloatParams>(performer);
                        changed = floats.Clear(paramKey);
                    }
                    break;
                case ParamLane.Int:
                    if (_world.Has<PerformerIntParams>(performer))
                    {
                        ref PerformerIntParams ints = ref _world.Get<PerformerIntParams>(performer);
                        changed = ints.Clear(paramKey);
                    }
                    break;
                case ParamLane.Vector:
                    if (_world.Has<PerformerVectorParams>(performer))
                    {
                        ref PerformerVectorParams vectors = ref _world.Get<PerformerVectorParams>(performer);
                        changed = vectors.Clear(paramKey);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lane));
            }

            if (!changed || !_world.Has<PerformerState>(performer))
            {
                return;
            }

            ref PerformerState state = ref _world.Get<PerformerState>(performer);
            state.Version++;
            MarkStaticDirtyIfVisualParamChanged(performer, in state, paramKey, lane);
        }
        public bool HasOwnerPayload(Entity owner)
        {
            return owner != Entity.Null &&
                   _byOwner.TryGetValue(new OwnerKey(owner), out OwnerPerformerBucket bucket) &&
                   bucket.Count > 0;
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

        internal bool TryGetActiveByOwner(Entity owner, out Entity single, out List<Entity>? many)
        {
            single = Entity.Null;
            many = null;
            if (owner == Entity.Null ||
                !_byOwner.TryGetValue(new OwnerKey(owner), out OwnerPerformerBucket bucket) ||
                bucket.Count == 0)
            {
                return false;
            }

            if (bucket.Count == 1)
            {
                single = bucket.Single;
                return true;
            }

            many = bucket.Many;
            return many != null && many.Count != 0;
        }

        public void SyncCullVisibility()
        {
            foreach (ref var chunk in _world.Query(in _performerCullQuery))
            {
                var states = chunk.GetSpan<PerformerState>();
                var culls = chunk.GetSpan<PerformerCullState>();
                foreach (var index in chunk)
                {
                    ref PerformerState state = ref states[index];
                    ref PerformerCullState cull = ref culls[index];
                    ResolveOwnerCull(in state, out bool ownerCullVisible, out LODLevel ownerLod);
                    cull.OwnerCullVisible = ownerCullVisible;
                    cull.LOD = ownerLod;
                }
            }

            if (_nonRootCount == 0)
            {
                return;
            }

            foreach (ref var chunk in _world.Query(in _performerCullQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var culls = chunk.GetSpan<PerformerCullState>();
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (!_world.Has<PerformerParent>(entity))
                    {
                        continue;
                    }

                    Entity parent = _world.Get<PerformerParent>(entity).Parent;
                    if (parent == Entity.Null || !_world.IsAlive(parent) || !_world.Has<PerformerCullState>(parent))
                    {
                        continue;
                    }

                    ref PerformerCullState cull = ref culls[index];
                    cull.OwnerCullVisible = cull.OwnerCullVisible && _world.Get<PerformerCullState>(parent).OwnerCullVisible;
                }
            }
        }

        public void SyncCullVisibility(ReadOnlySpan<Entity> owners)
        {
            for (int i = 0; i < owners.Length; i++)
            {
                Entity owner = owners[i];
                if (!_byOwner.TryGetValue(new OwnerKey(owner), out OwnerPerformerBucket performers))
                {
                    continue;
                }

                bool ownerVisible = ResolveOwnerCullVisible(owner);
                LODLevel ownerLod = ResolveOwnerLod(owner);
                if (performers.TryGetSingle(out Entity single))
                {
                    SyncOwnerRootCull(single, ownerVisible, ownerLod);
                    continue;
                }

                List<Entity>? many = performers.Many;
                if (many == null)
                {
                    continue;
                }

                for (int performerIndex = 0; performerIndex < many.Count; performerIndex++)
                {
                    SyncOwnerRootCull(many[performerIndex], ownerVisible, ownerLod);
                }
            }
        }

        public void MarkEventDrivenStaticEmitDirty(ReadOnlySpan<Entity> owners)
        {
            for (int i = 0; i < owners.Length; i++)
            {
                if (!_byOwner.TryGetValue(new OwnerKey(owners[i]), out OwnerPerformerBucket performers))
                {
                    continue;
                }

                if (performers.TryGetSingle(out Entity single))
                {
                    MarkStaticDirty(single);
                    continue;
                }

                List<Entity>? many = performers.Many;
                if (many == null)
                {
                    continue;
                }

                for (int performerIndex = 0; performerIndex < many.Count; performerIndex++)
                {
                    MarkStaticDirty(many[performerIndex]);
                }
            }
        }

        public void SyncRootCullVisibilityAndMarkEventDrivenStaticEmitDirty(ReadOnlySpan<Entity> owners)
        {
            if (_nonRootCount != 0)
            {
                throw new InvalidOperationException("Root-only cull sync fast path cannot run while non-root performers exist.");
            }

            for (int i = 0; i < owners.Length; i++)
            {
                Entity owner = owners[i];
                if (!_byOwner.TryGetValue(new OwnerKey(owner), out OwnerPerformerBucket performers))
                {
                    continue;
                }

                ResolveOwnerCull(owner, out bool ownerVisible, out LODLevel ownerLod);
                if (performers.TryGetSingle(out Entity single))
                {
                    SyncRootCullAndMaybeMarkStaticDirty(single, ownerVisible, ownerLod);
                    continue;
                }

                List<Entity>? many = performers.Many;
                if (many == null)
                {
                    continue;
                }

                for (int performerIndex = 0; performerIndex < many.Count; performerIndex++)
                {
                    SyncRootCullAndMaybeMarkStaticDirty(many[performerIndex], ownerVisible, ownerLod);
                }
            }
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
            ClearOwnerPayloadMarkers();
            _activeCount = 0;
            _structureVersion++;
            _nonRootCount = 0;
            _byDefinition.Clear();
            _byOwner.Clear();
            _byOwnerDefinition.Clear();
            _scopedInstances.Clear();
        }

        public void SyncTickBehaviorMarkers(Entity entity, PerformerDefinition definition, uint activeBehaviorMask)
        {
            if (!_world.IsAlive(entity))
            {
                return;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            if (behaviors == null)
            {
                RemoveTickBehaviorMarkers(entity);
                return;
            }

            bool hasSound = false;
            bool hasSpline = false;
            bool hasAttachment = false;
            bool hasAnimator = false;

            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.SlotIndex is < 0 or >= 32 ||
                    (activeBehaviorMask & (1u << slot.SlotIndex)) == 0)
                {
                    continue;
                }

                switch (slot.Kind)
                {
                    case BehaviorKind.Sound: hasSound = true; break;
                    case BehaviorKind.Spline: hasSpline = true; break;
                    case BehaviorKind.Animator: hasAnimator = true; break;
                    case BehaviorKind.Attachment:
                        if (AttachmentRequiresTick(entity, in slot.Attachment))
                        {
                            hasAttachment = true;
                        }

                        break;
                }
            }

            SyncTickBehaviorMarker<PerfHasSound>(entity, hasSound);
            SyncTickBehaviorMarker<PerfHasSpline>(entity, hasSpline);
            SyncTickBehaviorMarker<PerfHasAttachment>(entity, hasAttachment);
            SyncTickBehaviorMarker<PerfHasAnimator>(entity, hasAnimator);
        }

        public void SyncEmitWorkMarkers(Entity entity, PerformerDefinition definition, uint activeBehaviorMask)
        {
            if (!_world.IsAlive(entity))
            {
                return;
            }

            bool hasEmitWork = definition.HasSurfaceAuthoring;
            bool retainedPresentationRequest = definition.UsesRetainedPresentationRequest;
            if (!hasEmitWork && !definition.UsesEventDrivenStaticEmit && !retainedPresentationRequest)
            {
                BehaviorSlot[] behaviors = definition.Behaviors;
                if (behaviors != null)
                {
                    for (int i = 0; i < behaviors.Length; i++)
                    {
                        ref readonly BehaviorSlot slot = ref behaviors[i];
                        if (slot.Kind != BehaviorKind.AssetBinding ||
                            slot.SlotIndex is < 0 or >= 32 ||
                            (activeBehaviorMask & (1u << slot.SlotIndex)) == 0)
                        {
                            continue;
                        }

                        hasEmitWork = true;
                        break;
                    }
                }
            }

            SyncTickBehaviorMarker<PerfHasEmitWork>(entity, hasEmitWork);
            SyncTickBehaviorMarker<PerfRetainedPresentationRequest>(entity, retainedPresentationRequest);
            if (retainedPresentationRequest)
            {
                MarkRetainedPresentationRequestDirty(entity);
            }
        }

        public bool SetBehaviorActive(Entity entity, PerformerDefinition definition, int slotIndex, bool active)
        {
            if (!_world.IsAlive(entity) ||
                !_world.Has<PerformerState>(entity) ||
                slotIndex is < 0 or >= 32)
            {
                return false;
            }

            uint bit = 1u << slotIndex;
            ref PerformerState state = ref _world.Get<PerformerState>(entity);
            uint nextMask = active
                ? state.BehaviorActiveMask | bit
                : state.BehaviorActiveMask & ~bit;
            if (nextMask == state.BehaviorActiveMask)
            {
                return false;
            }

            state.BehaviorActiveMask = nextMask;
            state.Version++;
            SyncTickBehaviorMarkers(entity, definition, nextMask);
            SyncEmitWorkMarkers(entity, definition, nextMask);
            MarkStaticDirty(entity);
            return true;
        }

        private void AddBehaviorMarkers(Entity entity, PerformerDefinition definition, uint activeBehaviorMask)
        {
            SyncTickBehaviorMarkers(entity, definition, activeBehaviorMask);
            SyncEmitWorkMarkers(entity, definition, activeBehaviorMask);
        }

        private void RemoveTickBehaviorMarkers(Entity entity)
        {
            if (_world.Has<PerfHasSound>(entity))
            {
                _world.Remove<PerfHasSound>(entity);
            }

            if (_world.Has<PerfHasSpline>(entity))
            {
                _world.Remove<PerfHasSpline>(entity);
            }

            if (_world.Has<PerfHasAttachment>(entity))
            {
                _world.Remove<PerfHasAttachment>(entity);
            }

            if (_world.Has<PerfHasAnimator>(entity))
            {
                _world.Remove<PerfHasAnimator>(entity);
            }

            if (_world.Has<PerfHasEmitWork>(entity))
            {
                _world.Remove<PerfHasEmitWork>(entity);
            }

            if (_world.Has<PerfRetainedPresentationRequest>(entity))
            {
                _world.Remove<PerfRetainedPresentationRequest>(entity);
            }
        }

        private void SyncTickBehaviorMarker<TMarker>(Entity entity, bool shouldHave)
        {
            bool hasMarker = _world.Has<TMarker>(entity);
            if (shouldHave)
            {
                if (!hasMarker)
                {
                    _world.Add<TMarker>(entity);
                }

                return;
            }

            if (hasMarker)
            {
                _world.Remove<TMarker>(entity);
            }
        }

        private bool AttachmentRequiresTick(Entity entity, in AttachmentConfig config)
        {
            if (config.Target == AttachmentTarget.Bone)
            {
                return true;
            }

            if (config.Target != AttachmentTarget.Parent)
            {
                return true;
            }

            if (!_world.IsAlive(entity) || !_world.Has<PerformerState>(entity))
            {
                return true;
            }

            ref PerformerState state = ref _world.Get<PerformerState>(entity);
            if (!OwnerTransformIsStatic(in state))
            {
                return true;
            }

            if (!_world.Has<PerformerParent>(entity))
            {
                return false;
            }

            Entity parent = _world.Get<PerformerParent>(entity).Parent;
            return PerformerTransformRequiresTick(parent, depth: 0);
        }

        private bool PerformerTransformRequiresTick(Entity performer, int depth)
        {
            if (performer == Entity.Null ||
                !_world.IsAlive(performer) ||
                !_world.Has<PerformerState>(performer))
            {
                return false;
            }

            if (depth >= 8)
            {
                return true;
            }

            ref PerformerState state = ref _world.Get<PerformerState>(performer);
            if (!OwnerTransformIsStatic(in state))
            {
                return true;
            }

            if (_world.Has<PerformerTransformSource>(performer))
            {
                TransformSource source = _world.Get<PerformerTransformSource>(performer).Value;
                if (source is TransformSource.SplineDriven or TransformSource.BoneAttached)
                {
                    return true;
                }
            }

            if (_definitions == null ||
                !_definitions.TryGet(state.DefId, out PerformerDefinition definition))
            {
                return true;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            uint activeMask = state.BehaviorActiveMask;
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.SlotIndex is < 0 or >= 32 ||
                    (activeMask & (1u << slot.SlotIndex)) == 0)
                {
                    continue;
                }

                switch (slot.Kind)
                {
                    case BehaviorKind.Spline:
                        return true;

                    case BehaviorKind.Attachment:
                        if (slot.Attachment.Target == AttachmentTarget.Bone)
                        {
                            return true;
                        }

                        if (slot.Attachment.Target != AttachmentTarget.Parent)
                        {
                            return true;
                        }

                        Entity parent = _world.Has<PerformerParent>(performer)
                            ? _world.Get<PerformerParent>(performer).Parent
                            : Entity.Null;
                        return PerformerTransformRequiresTick(parent, depth + 1);
                }
            }

            return false;
        }

        private bool OwnerTransformIsStatic(in PerformerState state)
        {
            if (state.AnchorKind != PresentationAnchorKind.Entity)
            {
                return true;
            }

            Entity owner = state.OwnerEntity;
            return owner != Entity.Null &&
                   _world.IsAlive(owner) &&
                   _world.Has<PresentationStaticTransform>(owner);
        }

        private void AddEventDrivenStaticEmitMarkers(Entity entity, PerformerDefinition definition)
        {
            if (!definition.UsesEventDrivenStaticEmit)
            {
                return;
            }

            _world.Add<PerfStaticStableVisual>(entity);
            ref PerformerEmitCache emitCache = ref _world.Get<PerformerEmitCache>(entity);
            MarkStaticDirty(ref emitCache);
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
                SyncTickBehaviorMarkers(childEntity, childDefinition, childState.BehaviorActiveMask);
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

        private static Signature BuildBatchSignature(PerformerDefinition definition, uint defaultBehaviorMask)
        {
            Signature signature =
                Component<PerformerState>.Signature +
                Component<PerformerWorldPosition>.Signature +
                Component<PerformerWorldRotation>.Signature +
                Component<PerformerWorldScale>.Signature +
                Component<PerformerTransformSource>.Signature +
                Component<PerformerParent>.Signature +
                Component<PerformerChildren>.Signature +
                Component<PerformerCullState>.Signature +
                Component<PerformerFloatParams>.Signature +
                Component<PerformerIntParams>.Signature +
                Component<PerformerVectorParams>.Signature +
                Component<PerformerFloatDefaults>.Signature +
                Component<PerformerIntDefaults>.Signature +
                Component<PerformerVectorDefaults>.Signature +
                Component<PerformerEmitCache>.Signature;

            BehaviorSlot[] behaviors = definition.Behaviors;
            if (behaviors != null)
            {
                bool hasSound = false;
                bool hasSpline = false;
                bool hasAttachment = false;
                bool hasAnimator = false;
                for (int i = 0; i < behaviors.Length; i++)
                {
                    ref readonly BehaviorSlot slot = ref behaviors[i];
                    if (slot.SlotIndex is < 0 or >= 32 ||
                        (defaultBehaviorMask & (1u << slot.SlotIndex)) == 0)
                    {
                        continue;
                    }

                    switch (slot.Kind)
                    {
                        case BehaviorKind.Sound: hasSound = true; break;
                        case BehaviorKind.Spline: hasSpline = true; break;
                        case BehaviorKind.Animator: hasAnimator = true; break;
                        case BehaviorKind.Attachment: hasAttachment = true; break;
                    }
                }

                if (hasSound) signature += Component<PerfHasSound>.Signature;
                if (hasSpline) signature += Component<PerfHasSpline>.Signature;
                if (hasAnimator) signature += Component<PerfHasAnimator>.Signature;
                if (hasAttachment) signature += Component<PerfHasAttachment>.Signature;
                if ((definition.HasSurfaceAuthoring || definition.HasAssetBindingBehavior) &&
                    !definition.UsesEventDrivenStaticEmit &&
                    !definition.UsesRetainedPresentationRequest)
                {
                    signature += Component<PerfHasEmitWork>.Signature;
                }
            }

            if (definition.UsesEventDrivenStaticEmit)
            {
                signature += Component<PerfStaticStableVisual>.Signature;
            }

            if (definition.UsesRetainedPresentationRequest)
            {
                signature += Component<PerfRetainedPresentationRequest>.Signature;
            }

            return signature;
        }

        private PerformerDefinition ResolveDefinition(int defId, PerformerDefinition definition)
        {
            if (definition != null)
            {
                return definition;
            }

            return _definitions != null && _definitions.TryGet(defId, out PerformerDefinition resolved)
                ? resolved
                : EmptyDefinition;
        }

        private void ReconcileBoundDefinitions()
        {
            if (_definitions == null)
            {
                return;
            }

            ClearOwnerPayloadMarkers();
            _byDefinition.Clear();
            _byOwner.Clear();
            _byOwnerDefinition.Clear();
            _scopedInstances.Clear();
            var query = new QueryDescription().WithAll<PerformerState>();
            _world.Query(in query, (Entity entity, ref PerformerState state) =>
            {
                if (!_definitions.TryGet(state.DefId, out PerformerDefinition definition))
                {
                    RemoveTickBehaviorMarkers(entity);
                    if (_world.Has<PerfStaticStableVisual>(entity))
                    {
                        _world.Remove<PerfStaticStableVisual>(entity);
                    }

                    AddToOwnerIndex(state.OwnerEntity, entity);
                    return;
                }

                AddIndexes(entity, in state, definition);
                SyncTickBehaviorMarkers(entity, definition, state.BehaviorActiveMask);
                SyncEmitWorkMarkers(entity, definition, state.BehaviorActiveMask);
                if (definition.RequiresBootstrapProcessing && !_world.Has<PerformerBootstrapPending>(entity))
                {
                    _world.Add<PerformerBootstrapPending>(entity);
                }

                if (definition.UsesEventDrivenStaticEmit)
                {
                    if (!_world.Has<PerfStaticStableVisual>(entity))
                    {
                        _world.Add<PerfStaticStableVisual>(entity);
                    }
                }
                else if (_world.Has<PerfStaticStableVisual>(entity))
                {
                    _world.Remove<PerfStaticStableVisual>(entity);
                }
            });
        }

        private void FillEntityAnchoredRootBatch(
            ReadOnlySpan<Entity> created,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> scopeIds,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            int defId,
            PerformerDefinition definition,
            uint defaultBehaviorMask)
        {
            FillEntityAnchoredBatch(
                created,
                owners,
                scopeIds,
                stableIds,
                ownerTransforms,
                ownerCulls,
                default,
                defId,
                definition,
                defaultBehaviorMask,
                PresentationAnchorKind.Entity);
        }

        private void FillEntityAnchoredBatch(
            ReadOnlySpan<Entity> created,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> scopeIds,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            ReadOnlySpan<Entity> parentEntities,
            int defId,
            PerformerDefinition definition,
            uint defaultBehaviorMask,
            PresentationAnchorKind anchorKind)
        {
            Entity first = created[0];
            Archetype archetype = _world.GetEntityDataArray()[first.Id].Archetype;
            Slot slot = _world.GetSlot(first);
            int batchIndex = 0;
            int chunkIndex = slot.ChunkIndex;
            int row = slot.Index;
            while (batchIndex < created.Length)
            {
                ref Chunk chunk = ref archetype.GetChunk(chunkIndex);
                int run = Math.Min(created.Length - batchIndex, chunk.Count - row);

                Span<PerformerState> states = chunk.GetSpan<PerformerState>();
                Span<PerformerWorldPosition> positions = chunk.GetSpan<PerformerWorldPosition>();
                Span<PerformerWorldRotation> rotations = chunk.GetSpan<PerformerWorldRotation>();
                Span<PerformerWorldScale> scales = chunk.GetSpan<PerformerWorldScale>();
                Span<PerformerTransformSource> transformSources = chunk.GetSpan<PerformerTransformSource>();
                Span<PerformerParent> parentComponents = chunk.GetSpan<PerformerParent>();
                Span<PerformerChildren> children = chunk.GetSpan<PerformerChildren>();
                Span<PerformerCullState> culls = chunk.GetSpan<PerformerCullState>();
                Span<PerformerFloatParams> floatParams = chunk.GetSpan<PerformerFloatParams>();
                Span<PerformerIntParams> intParams = chunk.GetSpan<PerformerIntParams>();
                Span<PerformerVectorParams> vectorParams = chunk.GetSpan<PerformerVectorParams>();
                Span<PerformerFloatDefaults> floatDefaults = chunk.GetSpan<PerformerFloatDefaults>();
                Span<PerformerIntDefaults> intDefaults = chunk.GetSpan<PerformerIntDefaults>();
                Span<PerformerVectorDefaults> vectorDefaults = chunk.GetSpan<PerformerVectorDefaults>();
                Span<PerformerEmitCache> emitCaches = chunk.GetSpan<PerformerEmitCache>();

                for (int offset = 0; offset < run; offset++)
                {
                    int ownerIndex = batchIndex + offset;
                    int componentIndex = row + offset;
                    Entity owner = owners[ownerIndex];
                    Entity performer = created[ownerIndex];
                    Entity parent = parentEntities.IsEmpty ? Entity.Null : parentEntities[ownerIndex];
                    var state = new PerformerState
                    {
                        DefId = defId,
                        StableId = stableIds[ownerIndex],
                        ScopeId = scopeIds[ownerIndex],
                        OwnerEntity = owner,
                        AnchorKind = anchorKind,
                        BehaviorActiveMask = defaultBehaviorMask,
                        Elapsed = 0f,
                        Version = 1,
                        DefaultLifetime = definition.DefaultLifetime,
                    };
                    VisualTransform ownerTransform = ownerTransforms[ownerIndex];
                    CullState ownerCull = ownerCulls[ownerIndex];
                    Vector3 position = ownerTransform.Position + definition.PositionOffset;
                    Quaternion rotation = ownerTransform.Rotation;
                    Vector3 scale = ownerTransform.Scale;

                    states[componentIndex] = state;
                    positions[componentIndex] = new PerformerWorldPosition { Value = position };
                    rotations[componentIndex] = new PerformerWorldRotation { Value = rotation };
                    scales[componentIndex] = new PerformerWorldScale { Value = scale };
                    transformSources[componentIndex] = new PerformerTransformSource
                    {
                        Value = parent != Entity.Null ? TransformSource.InheritParent : TransformSource.EntityTransform,
                    };
                    parentComponents[componentIndex] = new PerformerParent { Parent = parent };
                    children[componentIndex] = default;
                    culls[componentIndex] = new PerformerCullState
                    {
                        OwnerCullVisible = ownerCull.IsVisible,
                        LOD = ownerCull.LOD,
                    };
                    floatParams[componentIndex] = default;
                    intParams[componentIndex] = default;
                    vectorParams[componentIndex] = default;
                    floatDefaults[componentIndex] = default;
                    intDefaults[componentIndex] = default;
                    vectorDefaults[componentIndex] = default;
                    emitCaches[componentIndex] = new PerformerEmitCache
                    {
                        StaticDirty = definition.UsesEventDrivenStaticEmit ? (byte)1 : (byte)0,
                        RetainedDirty = definition.UsesRetainedPresentationRequest ? (byte)1 : (byte)0,
                    };

                    AddToOwnerIndex(owner, performer);
                    if (definition.NeedsByDefinitionIndex)
                    {
                        AddToIndex(_byDefinition, state.DefId, performer, initialListCapacity: 4);
                    }

                    if (definition.NeedsByOwnerDefinitionIndex)
                    {
                        AddToIndex(_byOwnerDefinition, new OwnerDefinitionKey(state.OwnerEntity, state.DefId), performer, initialListCapacity: 1);
                    }

                    if (state.ScopeId > 0 && state.DefaultLifetime <= 0f)
                    {
                        _scopedInstances[new ScopedOwnerKey(
                            state.DefId,
                            state.OwnerEntity,
                            state.ScopeId,
                            state.AnchorKind,
                            Vector3.Zero)] = performer;
                    }

                    if (parent != Entity.Null && _world.IsAlive(parent))
                    {
                        ref PerformerChildren parentChildren = ref _world.Get<PerformerChildren>(parent);
                        parentChildren.Add(performer);
                    }
                }

                batchIndex += run;
                chunkIndex++;
                row = 0;
            }
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
            if (parent != Entity.Null && _nonRootCount > 0)
            {
                _nonRootCount--;
            }

            if (parent == Entity.Null || !_world.IsAlive(parent)) return;
            if (!_world.Has<PerformerChildren>(parent)) return;
            ref var parentChildren = ref _world.Get<PerformerChildren>(parent);
            parentChildren.Remove(performer);
        }

        private bool ResolveOwnerCullVisible(in PerformerState state)
        {
            return ResolveOwnerCullVisible(state.OwnerEntity);
        }

        private LODLevel ResolveOwnerLod(in PerformerState state)
        {
            return ResolveOwnerLod(state.OwnerEntity);
        }

        private void ResolveOwnerCull(in PerformerState state, out bool ownerCullVisible, out LODLevel ownerLod)
        {
            ResolveOwnerCull(state.OwnerEntity, out ownerCullVisible, out ownerLod);
        }

        private bool ResolveOwnerCullVisible(Entity owner)
        {
            ResolveOwnerCull(owner, out bool ownerCullVisible, out _);
            return ownerCullVisible;
        }

        private LODLevel ResolveOwnerLod(Entity owner)
        {
            ResolveOwnerCull(owner, out _, out LODLevel ownerLod);
            return ownerLod;
        }

        private void ResolveOwnerCull(Entity owner, out bool ownerCullVisible, out LODLevel ownerLod)
        {
            if (owner == Entity.Null)
            {
                ownerCullVisible = true;
                ownerLod = LODLevel.High;
                return;
            }

            if (!_world.IsAlive(owner))
            {
                ownerCullVisible = false;
                ownerLod = LODLevel.High;
                return;
            }

            if (!_world.Has<CullState>(owner))
            {
                ownerCullVisible = true;
                ownerLod = LODLevel.High;
                return;
            }

            ref readonly CullState cull = ref _world.Get<CullState>(owner);
            ownerCullVisible = cull.IsVisible;
            ownerLod = cull.LOD;
        }

        private void RemoveOwnerPayloadRef(Entity owner)
        {
            // Owner payload presence is now derived from the owner -> performer index.
        }

        private void AddIndexes(Entity performer, in PerformerState state, PerformerDefinition definition)
        {
            if (definition.NeedsByDefinitionIndex)
            {
                AddToIndex(_byDefinition, state.DefId, performer, initialListCapacity: 4);
            }

            AddToOwnerIndex(state.OwnerEntity, performer);
            if (definition.NeedsByOwnerDefinitionIndex)
            {
                AddToIndex(_byOwnerDefinition, new OwnerDefinitionKey(state.OwnerEntity, state.DefId), performer, initialListCapacity: 1);
            }

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
            RemoveFromOwnerIndex(state.OwnerEntity, performer);
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

        private void ReserveBatchIndexCapacity(int count, PerformerDefinition definition)
        {
            _byOwner.EnsureCapacity(_byOwner.Count + count);
            if (definition.NeedsByDefinitionIndex)
            {
                _byDefinition.EnsureCapacity(_byDefinition.Count + 1);
            }

            if (definition.NeedsByOwnerDefinitionIndex)
            {
                _byOwnerDefinition.EnsureCapacity(_byOwnerDefinition.Count + count);
            }

            if (definition.DefaultLifetime <= 0f)
            {
                _scopedInstances.EnsureCapacity(_scopedInstances.Count + count);
            }
        }

        private static void AddToIndex<TKey>(Dictionary<TKey, List<Entity>> index, TKey key, Entity performer, int initialListCapacity)
            where TKey : notnull
        {
            if (!index.TryGetValue(key, out var entities))
            {
                entities = new List<Entity>(initialListCapacity);
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

        private void AddToOwnerIndex(Entity owner, Entity performer)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            ref OwnerPerformerBucket bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(_byOwner, new OwnerKey(owner), out _);
            bucket.Add(performer);
            IncrementOwnerPayloadMarker(owner);
        }

        private void RemoveFromOwnerIndex(Entity owner, Entity performer)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            OwnerKey key = new(owner);
            ref OwnerPerformerBucket bucket = ref CollectionsMarshal.GetValueRefOrNullRef(_byOwner, key);
            if (Unsafe.IsNullRef(ref bucket) || !bucket.Remove(performer))
            {
                return;
            }

            if (bucket.Count == 0)
            {
                _byOwner.Remove(key);
            }

            DecrementOwnerPayloadMarker(owner);
        }

        private void IncrementOwnerPayloadMarker(Entity owner)
        {
            if (owner == Entity.Null || !_world.IsAlive(owner))
            {
                return;
            }

            if (_world.Has<PresentationOwnerHasPerformerPayload>(owner))
            {
                ref PresentationOwnerHasPerformerPayload marker = ref _world.Get<PresentationOwnerHasPerformerPayload>(owner);
                marker.Count++;
                return;
            }

            _world.Add(owner, new PresentationOwnerHasPerformerPayload { Count = 1 });
        }

        private void DecrementOwnerPayloadMarker(Entity owner)
        {
            if (owner == Entity.Null || !_world.IsAlive(owner) || !_world.Has<PresentationOwnerHasPerformerPayload>(owner))
            {
                return;
            }

            ref PresentationOwnerHasPerformerPayload marker = ref _world.Get<PresentationOwnerHasPerformerPayload>(owner);
            marker.Count--;
            if (marker.Count <= 0)
            {
                _world.Remove<PresentationOwnerHasPerformerPayload>(owner);
            }
        }

        private void ClearOwnerPayloadMarkers()
        {
            _world.Remove<PresentationOwnerHasPerformerPayload>(in _ownerPayloadMarkerQuery);
        }

        private void SyncOwnerRootCull(Entity performer, bool ownerVisible, LODLevel ownerLod)
        {
            if (!_world.IsAlive(performer) || !_world.Has<PerformerState>(performer))
            {
                return;
            }

            if (_world.Has<PerformerParent>(performer))
            {
                Entity parent = _world.Get<PerformerParent>(performer).Parent;
                if (parent != Entity.Null && _world.IsAlive(parent) && _world.Has<PerformerCullState>(parent))
                {
                    return;
                }
            }

            SyncCullHierarchy(performer, ownerVisible, ownerLod, parentVisible: true);
        }

        private void SyncRootCullAndMaybeMarkStaticDirty(Entity performer, bool ownerVisible, LODLevel ownerLod)
        {
            if (!_world.IsAlive(performer) || !_world.Has<PerformerCullState>(performer))
            {
                return;
            }

            ref PerformerCullState cull = ref _world.Get<PerformerCullState>(performer);
            cull.OwnerCullVisible = ownerVisible;
            cull.LOD = ownerLod;
            MarkStaticDirty(performer);
        }

        private void MarkStaticDirty(Entity performer)
        {
            if (!_world.IsAlive(performer) ||
                !_world.Has<PerformerEmitCache>(performer))
            {
                return;
            }

            ref PerformerEmitCache emitCache = ref _world.Get<PerformerEmitCache>(performer);
            if (_world.Has<PerfStaticStableVisual>(performer))
            {
                MarkStaticDirty(ref emitCache);
            }

            if (_world.Has<PerfRetainedPresentationRequest>(performer))
            {
                MarkRetainedPresentationRequestDirty(ref emitCache);
            }
        }

        private void MarkStaticDirtyIfVisualParamChanged(Entity performer, in PerformerState state, int paramKey, ParamLane lane)
        {
            if (!_world.Has<PerformerEmitCache>(performer) ||
                !_world.IsAlive(performer))
            {
                return;
            }

            if (_definitions == null ||
                !_definitions.TryGet(state.DefId, out PerformerDefinition definition) ||
                !definition.AffectsStaticVisualParam(paramKey, lane))
            {
                return;
            }

            ref PerformerEmitCache emitCache = ref _world.Get<PerformerEmitCache>(performer);
            if (_world.Has<PerfStaticStableVisual>(performer))
            {
                MarkStaticDirty(ref emitCache);
            }

            if (_world.Has<PerfRetainedPresentationRequest>(performer))
            {
                MarkRetainedPresentationRequestDirty(ref emitCache);
            }
        }

        public void ClearStaticDirty(Entity performer)
        {
            if (!_world.IsAlive(performer) ||
                !_world.Has<PerformerEmitCache>(performer))
            {
                return;
            }

            ref PerformerEmitCache emitCache = ref _world.Get<PerformerEmitCache>(performer);
            if (emitCache.StaticDirty == 0)
            {
                if (emitCache.RetainedDirty == 0)
                {
                    return;
                }
            }

            if (emitCache.StaticDirty != 0)
            {
                emitCache.StaticDirty = 0;
                if (_dirtyStaticVisualCount > 0)
                {
                    _dirtyStaticVisualCount--;
                }
            }

            if (emitCache.RetainedDirty != 0)
            {
                emitCache.RetainedDirty = 0;
                if (_dirtyRetainedPresentationRequestCount > 0)
                {
                    _dirtyRetainedPresentationRequestCount--;
                }
            }
        }

        private void SyncCullHierarchy(Entity performer, bool ownerVisible, LODLevel ownerLod, bool parentVisible)
        {
            if (!_world.IsAlive(performer) || !_world.Has<PerformerCullState>(performer))
            {
                return;
            }

            ref PerformerCullState cull = ref _world.Get<PerformerCullState>(performer);
            cull.OwnerCullVisible = ownerVisible && parentVisible;
            cull.LOD = ownerLod;

            if (!_world.Has<PerformerChildren>(performer))
            {
                return;
            }

            ref PerformerChildren children = ref _world.Get<PerformerChildren>(performer);
            for (int i = 0; i < children.Count; i++)
            {
                SyncCullHierarchy(children.Get(i), ownerVisible, ownerLod, cull.OwnerCullVisible);
            }
        }

        private void MarkStaticDirty(ref PerformerEmitCache emitCache)
        {
            if (emitCache.StaticDirty != 0)
            {
                return;
            }

            emitCache.StaticDirty = 1;
            _dirtyStaticVisualCount++;
        }

        private void MarkRetainedPresentationRequestDirty(Entity performer)
        {
            if (!_world.IsAlive(performer) ||
                !_world.Has<PerfRetainedPresentationRequest>(performer) ||
                !_world.Has<PerformerEmitCache>(performer))
            {
                return;
            }

            ref PerformerEmitCache emitCache = ref _world.Get<PerformerEmitCache>(performer);
            MarkRetainedPresentationRequestDirty(ref emitCache);
        }

        private void MarkRetainedPresentationRequestDirty(ref PerformerEmitCache emitCache)
        {
            if (emitCache.RetainedDirty != 0)
            {
                return;
            }

            emitCache.RetainedDirty = 1;
            _dirtyRetainedPresentationRequestCount++;
        }

        private readonly struct OwnerKey : IEquatable<OwnerKey>
        {
            private readonly int _ownerId;
            private readonly int _ownerWorldId;

            public OwnerKey(Entity owner)
            {
                _ownerId = owner.Id;
                _ownerWorldId = owner.WorldId;
            }

            public bool Equals(OwnerKey other)
            {
                return _ownerId == other._ownerId && _ownerWorldId == other._ownerWorldId;
            }

            public override bool Equals(object? obj)
            {
                return obj is OwnerKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_ownerId, _ownerWorldId);
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

        private struct OwnerPerformerBucket
        {
            public Entity Single;
            public List<Entity>? Many;
            public int Count;

            public void Add(Entity performer)
            {
                if (Count == 0)
                {
                    Single = performer;
                    Count = 1;
                    return;
                }

                if (Count == 1)
                {
                    Many = new List<Entity>(4) { Single, performer };
                    Single = Entity.Null;
                    Count = 2;
                    return;
                }

                Many!.Add(performer);
                Count++;
            }

            public bool Remove(Entity performer)
            {
                if (Count == 0)
                {
                    return false;
                }

                if (Count == 1)
                {
                    if (Single != performer)
                    {
                        return false;
                    }

                    Single = Entity.Null;
                    Count = 0;
                    return true;
                }

                List<Entity>? many = Many;
                if (many == null)
                {
                    return false;
                }

                for (int i = many.Count - 1; i >= 0; i--)
                {
                    if (many[i] != performer)
                    {
                        continue;
                    }

                    int last = many.Count - 1;
                    many[i] = many[last];
                    many.RemoveAt(last);
                    Count--;
                    if (Count == 1)
                    {
                        Single = many[0];
                        many.Clear();
                        Many = null;
                    }

                    return true;
                }

                return false;
            }

            public readonly bool TryGetSingle(out Entity performer)
            {
                performer = Single;
                return Count == 1;
            }
        }
    }
}
