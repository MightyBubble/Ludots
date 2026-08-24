using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using Arch.Buffer;
using Arch.Core;
using Arch.Core.Extensions.Dangerous;
using Arch.Core.Utils;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Presenters
{
    public sealed class PresenterEntityRuntime
    {
        private static readonly QueryDescription _presenterCullQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterCullState>();
        private static readonly QueryDescription _presenterStateQuery = new QueryDescription()
            .WithAll<PresenterState>();
        private static readonly QueryDescription _rootCullFastPathQuery = new QueryDescription()
            .WithAll<PresenterState, PresenterCullState, PresenterEmitCache>();
        private static readonly QueryDescription _ownerPayloadMarkerQuery = new QueryDescription()
            .WithAll<PresentationOwnerHasPresenterPayload>();

        private readonly World _world;
        private PresenterDefinitionRegistry? _definitions;
        private int _activeCount;
        private int _structureVersion;
        private int _nonRootCount;
        private int _dirtyStaticVisualCount;
        private int _dirtyRetainedPresentationRequestCount;
        private PresenterAnimatorStateBuffer? _animatorStates;
        private readonly Dictionary<int, List<Entity>> _byDefinition = new();
        private readonly Dictionary<OwnerKey, OwnerPresenterBucket> _byOwner = new();
        private readonly Dictionary<OwnerDefinitionKey, OwnerPresenterBucket> _byOwnerDefinition = new();
        private readonly Dictionary<int, EntityBucket> _byScope = new();
        private readonly Dictionary<ScopedOwnerKey, Entity> _scopedInstances = new();
        private readonly List<OwnerKey> _deadOwnerKeys = new(256);
        private readonly List<OwnerDefinitionKey> _ownerDefinitionScratch = new(256);
        private readonly List<Entity> _entityScratch = new(256);
        private readonly CommandBuffer _structuralCommands = new();
        private Entity[] _childBatchCreated = Array.Empty<Entity>();
        private int[] _childBatchScopeIds = Array.Empty<int>();
        private int[] _childBatchStableIds = Array.Empty<int>();
        private Entity[] _scopeDestroyBuffer = Array.Empty<Entity>();
        private Entity[] _retainedPresentationDirtyBuffer = Array.Empty<Entity>();
        private int _retainedPresentationDirtyBufferCount;
        private CommandBuffer? _deferredStructuralCommands;
        private bool _suppressOwnerPayloadMarkerWrites;

        public int ActiveCount => _activeCount;
        public int StructureVersion => _structureVersion;
        public bool HasNonRootPresenters => _nonRootCount != 0;
        public bool HasDirtyStaticVisuals => _dirtyStaticVisualCount != 0;
        public bool HasDirtyRetainedPresentationRequests => _dirtyRetainedPresentationRequestCount != 0;
        public ReadOnlySpan<Entity> RetainedPresentationDirtyEntities =>
            _retainedPresentationDirtyBuffer.AsSpan(0, _retainedPresentationDirtyBufferCount);
        public World World => _world;
        public double LastRootBatchSetupMs { get; private set; }
        public double LastRootBatchWorldCreateMs { get; private set; }
        public double LastRootBatchComponentFillMs { get; private set; }
        public double LastRootBatchIndexWriteMs { get; private set; }
        public double LastRootBatchOwnerPayloadMs { get; private set; }
        public int LastRootBatchOwnerPayloadCount { get; private set; }
        public double LastRootBatchPostCreateMs { get; private set; }
        public double LastChildBatchSetupMs { get; private set; }
        public double LastChildBatchWorldCreateMs { get; private set; }
        public double LastChildBatchComponentFillMs { get; private set; }
        public double LastChildBatchIndexWriteMs { get; private set; }
        public double LastChildBatchStableIdMs { get; private set; }

        public string BuildActiveDefinitionSummary(int maxEntries)
        {
            if (maxEntries <= 0 || _activeCount == 0)
            {
                return string.Empty;
            }

            var counts = new Dictionary<int, int>(Math.Min(_activeCount, 64));
            var query = _world.Query(in _presenterStateQuery);
            foreach (ref readonly Chunk chunk in query)
            {
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    int defId = states[i].DefId;
                    counts.TryGetValue(defId, out int count);
                    counts[defId] = count + 1;
                }
            }

            if (counts.Count == 0)
            {
                return string.Empty;
            }

            Span<DefinitionCount> top = maxEntries <= 16
                ? stackalloc DefinitionCount[maxEntries]
                : new DefinitionCount[maxEntries];
            int topCount = 0;
            foreach (KeyValuePair<int, int> pair in counts)
            {
                int count = pair.Value;
                if (count <= 0)
                {
                    continue;
                }

                int insertAt = topCount;
                while (insertAt > 0 && top[insertAt - 1].Count < count)
                {
                    if (insertAt < top.Length)
                    {
                        top[insertAt] = top[insertAt - 1];
                    }

                    insertAt--;
                }

                if (insertAt >= top.Length)
                {
                    continue;
                }

                top[insertAt] = new DefinitionCount(pair.Key, count);
                if (topCount < top.Length)
                {
                    topCount++;
                }
            }

            if (topCount == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(topCount * 48);
            for (int i = 0; i < topCount; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                int defId = top[i].DefinitionId;
                string name = _definitions != null ? _definitions.GetName(defId) : defId.ToString();
                builder.Append(name);
                builder.Append(':');
                builder.Append(top[i].Count);
            }

            return builder.ToString();
        }

        public PresenterEntityRuntime(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        private void ResetLastRootBatchTiming()
        {
            LastRootBatchSetupMs = 0d;
            LastRootBatchWorldCreateMs = 0d;
            LastRootBatchComponentFillMs = 0d;
            LastRootBatchIndexWriteMs = 0d;
            LastRootBatchOwnerPayloadMs = 0d;
            LastRootBatchOwnerPayloadCount = 0;
            LastRootBatchPostCreateMs = 0d;
            LastChildBatchSetupMs = 0d;
            LastChildBatchWorldCreateMs = 0d;
            LastChildBatchComponentFillMs = 0d;
            LastChildBatchIndexWriteMs = 0d;
            LastChildBatchStableIdMs = 0d;
        }

        private static double ElapsedMs(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
        }

        private readonly struct DefinitionCount
        {
            public readonly int DefinitionId;
            public readonly int Count;

            public DefinitionCount(int definitionId, int count)
            {
                DefinitionId = definitionId;
                Count = count;
            }
        }

        public void BindDefinitions(PresenterDefinitionRegistry definitions)
        {
            if (ReferenceEquals(_definitions, definitions))
            {
                return;
            }

            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            ReconcileBoundDefinitions();
        }

        public void BindAnimatorStates(PresenterAnimatorStateBuffer animatorStates)
        {
            _animatorStates = animatorStates ?? throw new ArgumentNullException(nameof(animatorStates));
        }

        public void BeginDeferredStructuralChanges(CommandBuffer commandBuffer)
        {
            if (_deferredStructuralCommands != null && !ReferenceEquals(_deferredStructuralCommands, commandBuffer))
            {
                throw new InvalidOperationException("PresenterEntityRuntime already has an active deferred structural command sink.");
            }

            _deferredStructuralCommands = commandBuffer ?? throw new ArgumentNullException(nameof(commandBuffer));
        }

        public void EndDeferredStructuralChanges(CommandBuffer commandBuffer)
        {
            if (!ReferenceEquals(_deferredStructuralCommands, commandBuffer))
            {
                throw new InvalidOperationException("PresenterEntityRuntime deferred structural command sink mismatch.");
            }

            _deferredStructuralCommands = null;
        }

        public Entity Create(
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition,
            int stableId,
            Entity parent,
            PresenterDefinition? definition)
        {
            definition = ResolveDefinition(defId, definition);
            EnsureParentCanAcceptChild(parent, defId);

            var state = new PresenterState
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
            var cullState = new PresenterCullState
            {
                OwnerCullVisible = ResolveOwnerCullVisible(in state),
                LOD = ResolveOwnerLod(in state),
            };
            var entity = _world.Create(
                state,
                new PresenterWorldPosition { Value = worldPosition },
                new PresenterWorldPlanePosition { ValueCm = WorldPlane2D.VisualMetersToLogicCm(in worldPosition) },
                new PresenterWorldRotation { Value = Quaternion.Identity },
                ResolvePresenterWorldFacing(owner),
                new PresenterWorldScale { Value = Vector3.One },
                new PresenterTransformSource { Value = transformSource },
                new PresenterParent { Parent = parent },
                new PresenterChildren(),
                cullState,
                new PresenterFloatParams(),
                new PresenterIntParams(),
                new PresenterVectorParams(),
                new PresenterFloatDefaults(),
                new PresenterIntDefaults(),
                new PresenterVectorDefaults(),
                new PresenterEmitCache());

            InitializeAnimatorSlotIfPresent(entity, definition);
            AddIndexes(entity, in state, definition);
            AddBehaviorMarkers(entity, definition, state.BehaviorActiveMask);
            AddEventDrivenStaticEmitMarkers(entity, definition);
            AddRetainedPresentationRequestMarkers(entity, definition);

            if (parent != Entity.Null && _world.IsAlive(parent))
            {
                if (AttachChildToParent(parent, entity, defId))
                {
                    _nonRootCount++;
                }
            }

            _activeCount++;
            _structureVersion++;
            return entity;
        }

        public int CreateEntityAnchoredRootBatch(
            PresenterDefinitionRegistry definitions,
            int defId,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> scopeIds,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            PresenterDefinition? definition,
            Span<Entity> created,
            Func<int>? allocateStableId = null,
            ReadOnlySpan<ParamDefault[]> rootParamOverrides = default)
        {
            if (owners.Length != scopeIds.Length ||
                owners.Length != stableIds.Length ||
                owners.Length != ownerTransforms.Length ||
                owners.Length != ownerCulls.Length ||
                owners.Length > created.Length ||
                (!rootParamOverrides.IsEmpty && rootParamOverrides.Length != owners.Length))
            {
                throw new ArgumentException("Presenter batch create spans must have matching lengths.");
            }

            if (owners.Length == 0)
            {
                return 0;
            }

            definition = ResolveDefinition(defId, definition);
            ResetLastRootBatchTiming();
            long setupStart = Stopwatch.GetTimestamp();
            ReserveBatchIndexCapacity(owners.Length, definition);
            uint defaultBehaviorMask = BuildDefaultBehaviorMask(definition);
            bool includeTransformSyncTick = BatchRequiresTransformSyncTick(definition, defaultBehaviorMask, owners);
            bool includeOwnerPayloadTransformSync = includeTransformSyncTick &&
                CanBatchUseOwnerPayloadTransformSync(definition, defaultBehaviorMask, owners);
            bool includeGroundingTick = BatchRequiresGroundingTick(definition, defaultBehaviorMask, owners);
            Signature signature = BuildBatchSignature(
                definition,
                defaultBehaviorMask,
                includeTransformSyncTick,
                includeOwnerPayloadTransformSync,
                includeOwnerPayloadAttachedTransformSync: false,
                includeAttachmentTick: true,
                includeGroundingTick);
            LastRootBatchSetupMs = ElapsedMs(setupStart);
            long worldCreateStart = Stopwatch.GetTimestamp();
            _world.Create(created, signature, owners.Length);
            LastRootBatchWorldCreateMs = ElapsedMs(worldCreateStart);
            bool hasParamDefaults = definition.ParamDefaults != null && definition.ParamDefaults.Length > 0;
            bool hasChildren = definition.Children != null && definition.Children.Length > 0;
            bool writeOwnerPayloadBatch = OwnersHavePreseededPayloadMarkers(owners);
            bool previousSuppressOwnerPayloadMarkerWrites = _suppressOwnerPayloadMarkerWrites;
            if (writeOwnerPayloadBatch)
            {
                _suppressOwnerPayloadMarkerWrites = true;
            }

            try
            {
                long fillStart = Stopwatch.GetTimestamp();
                FillEntityAnchoredRootBatch(
                    created.Slice(0, owners.Length),
                    owners,
                    scopeIds,
                    stableIds,
                    ownerTransforms,
                    ownerCulls,
                    defId,
                    definition,
                    defaultBehaviorMask,
                    out double indexWriteMs);
                LastRootBatchIndexWriteMs = indexWriteMs;
                LastRootBatchComponentFillMs = Math.Max(0d, ElapsedMs(fillStart) - indexWriteMs);
            }
            finally
            {
                _suppressOwnerPayloadMarkerWrites = previousSuppressOwnerPayloadMarkerWrites;
            }

            if (writeOwnerPayloadBatch)
            {
                long payloadStart = Stopwatch.GetTimestamp();
                WriteSingleRootOwnerPayloadMarkersBatch(
                    owners,
                    created.Slice(0, owners.Length),
                    includeOwnerPayloadTransformSync);
                LastRootBatchOwnerPayloadCount = owners.Length;
                LastRootBatchOwnerPayloadMs = ElapsedMs(payloadStart);
            }

            long postCreateStart = Stopwatch.GetTimestamp();
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

            if (!rootParamOverrides.IsEmpty)
            {
                for (int i = 0; i < owners.Length; i++)
                {
                    ApplyParamOverrides(created[i], rootParamOverrides[i]);
                }
            }

            if (hasChildren)
            {
                CreateChildrenRecursiveBatch(
                    definitions,
                    created.Slice(0, owners.Length),
                    owners,
                    scopeIds,
                    ownerTransforms,
                    ownerCulls,
                    PresentationAnchorKind.Entity,
                    allocateStableId);
            }

            LastRootBatchPostCreateMs = ElapsedMs(postCreateStart);
            return owners.Length;
        }

        public Entity CreateHierarchy(
            PresenterDefinitionRegistry definitions,
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition,
            int stableId,
            Entity parent,
            PresenterDefinition? definition,
            Func<int>? allocateStableId = null)
        {
            definition = ResolveDefinition(defId, definition);

            Entity entity = Create(defId, owner, scopeId, anchorKind, in worldPosition, stableId, parent, definition);
            ref PresenterState state = ref _world.Get<PresenterState>(entity);
            state.BehaviorActiveMask = BuildDefaultBehaviorMask(definition);
            SetParamDefault(definition, entity);
            InitializeTransform(entity, definition);
            CreateChildrenRecursive(definitions, entity, owner, scopeId, anchorKind, allocateStableId);
            return entity;
        }

        public void InitializeTransform(Entity presenter, PresenterDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition), "Presenter transform initialization requires a resolved definition.");
            }

            if (!_world.IsAlive(presenter) || !_world.Has<PresenterState>(presenter))
            {
                return;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            Entity owner = state.OwnerEntity;
            bool hasOwnerTransform = _world.IsAlive(owner) && _world.Has<VisualTransform>(owner);
            VisualTransform ownerTransform = hasOwnerTransform ? _world.Get<VisualTransform>(owner) : VisualTransform.Default;

            Vector3 position = hasOwnerTransform ? ownerTransform.Position : _world.Get<PresenterWorldPosition>(presenter).Value;
            Quaternion rotation = hasOwnerTransform ? ownerTransform.Rotation : Quaternion.Identity;
            Vector3 scale = hasOwnerTransform ? ownerTransform.Scale : Vector3.One;

            position += definition.PositionOffset;

            _world.Get<PresenterWorldPosition>(presenter).Value = position;
            if (_world.Has<PresenterWorldPlanePosition>(presenter))
            {
                _world.Get<PresenterWorldPlanePosition>(presenter).ValueCm = WorldPlane2D.VisualMetersToLogicCm(in position);
            }

            if (_world.Has<PresenterWorldRotation>(presenter))
            {
                _world.Get<PresenterWorldRotation>(presenter).Value = rotation;
            }

            if (_world.Has<PresenterWorldScale>(presenter))
            {
                _world.Get<PresenterWorldScale>(presenter).Value = scale;
            }
        }

        public Entity Create(int defId, Entity owner, int scopeId)
        {
            return Create(defId, owner, scopeId, PresentationAnchorKind.Entity,
                Vector3.Zero, 0, Entity.Null, ResolveDefinition(defId, null));
        }

        public void Destroy(Entity presenter, Action<Entity, PresenterState>? onDestroyed = null)
        {
            if (!_world.IsAlive(presenter) || !_world.Has<PresenterState>(presenter))
                return;

            ref var children = ref _world.Get<PresenterChildren>(presenter);
            for (int i = children.Count - 1; i >= 0; i--)
            {
                var childEntity = children.Get(i);
                if (_world.IsAlive(childEntity))
                    Destroy(childEntity, onDestroyed);
            }

            UnlinkFromParent(presenter);

            var snapshot = _world.Get<PresenterState>(presenter);
            if (_world.Has<PresenterEmitCache>(presenter))
            {
                ref PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(presenter);
                if (emitCache.StaticDirty != 0 && _dirtyStaticVisualCount > 0)
                {
                    _dirtyStaticVisualCount--;
                }
            if (emitCache.RetainedDirty != 0 && _dirtyRetainedPresentationRequestCount > 0)
            {
                _dirtyRetainedPresentationRequestCount--;
            }
            }
            RemoveIndexes(presenter, in snapshot);
            onDestroyed?.Invoke(presenter, snapshot);

            RemoveOwnerPayloadRef(snapshot.OwnerEntity);
            _activeCount--;
            _structureVersion++;
            _world.Destroy(presenter);
        }

        public int DestroyScope(int scopeId, Action<Entity, PresenterState>? onDestroyed = null)
        {
            if (scopeId <= 0)
                throw new InvalidOperationException($"DestroyPresenterScope requires ScopeTag > 0, got {scopeId}.");

            if (!_byScope.TryGetValue(scopeId, out EntityBucket scoped) || scoped.Count == 0)
            {
                return 0;
            }

            int destroyed = 0;
            int count = scoped.Count;
            EnsureScopeDestroyBufferCapacity(count);
            scoped.CopyTo(_scopeDestroyBuffer);
            for (int i = 0; i < count; i++)
            {
                Entity entity = _scopeDestroyBuffer[i];
                _scopeDestroyBuffer[i] = Entity.Null;
                if (_world.IsAlive(entity))
                {
                    Destroy(entity, onDestroyed);
                    destroyed++;
                }
            }
            return destroyed;
        }
        public void SetParam(Entity presenter, int paramKey, ParamLane lane,
            float floatValue, int intValue, in Vector4 vectorValue)
        {
            SetParamInternal(presenter, paramKey, lane, floatValue, intValue, in vectorValue, propagateToChildren: false);
        }

        public void SetParamAndPropagateToAffectedChildren(Entity presenter, int paramKey, ParamLane lane,
            float floatValue, int intValue, in Vector4 vectorValue)
        {
            if (!_world.IsAlive(presenter) || !_world.Has<PresenterState>(presenter))
            {
                return;
            }

            SetParamInternal(presenter, paramKey, lane, floatValue, intValue, in vectorValue, propagateToChildren: false);
            PropagateParamToAffectedChildren(presenter, paramKey, lane, floatValue, intValue, in vectorValue);
        }

        /// <summary>
        /// SinkParamToAsset：不写值、只强制把已存在的 param 重新 sink 到 asset 属性。
        /// paramKey=0 表示不做 key 过滤（刷新全部 lane）；指定 key 时按定义的
        /// static visual / material source 影响面过滤，避免无意义重 emit。
        /// </summary>
        public void SinkParamsToAssets(Entity presenter, int paramKey, ParamLane lane)
        {
            if (!_world.IsAlive(presenter))
            {
                return;
            }

            SinkParamsToAssetsSingle(presenter, paramKey, lane);
            PropagateSinkToChildren(presenter, paramKey, lane);
        }

        private void SinkParamsToAssetsSingle(Entity presenter, int paramKey, ParamLane lane)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterState>(presenter) ||
                !_world.Has<PresenterEmitCache>(presenter))
            {
                return;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            if (paramKey != 0)
            {
                if (_definitions == null ||
                    !_definitions.TryGet(state.DefId, out PresenterDefinition definition))
                {
                    return;
                }

                bool affectsStaticVisual = definition.AffectsStaticVisualParam(paramKey, lane);
                bool affectsMaterial = definition.AffectsMaterialSourceParam(paramKey, lane);
                if (affectsMaterial)
                {
                    MarkMaterialDirtyIfMaterialSourceParamChanged(presenter, in state, definition, paramKey, lane);
                }

                if (!affectsStaticVisual)
                {
                    return;
                }
            }

            MarkTransformDrivenEmitDirty(presenter);
        }

        private void PropagateSinkToChildren(Entity presenter, int paramKey, ParamLane lane)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterChildren>(presenter))
            {
                return;
            }

            ref PresenterChildren children = ref _world.Get<PresenterChildren>(presenter);
            for (int i = 0; i < children.Count; i++)
            {
                Entity child = children.Get(i);
                SinkParamsToAssetsSingle(child, paramKey, lane);
                PropagateSinkToChildren(child, paramKey, lane);
            }
        }

        private bool SetParamInternal(Entity presenter, int paramKey, ParamLane lane,
            float floatValue, int intValue, in Vector4 vectorValue, bool propagateToChildren)
        {
            if (!_world.IsAlive(presenter)) return false;

            bool changed = false;
            switch (lane)
            {
                case ParamLane.Float:
                    ref var fp = ref _world.Get<PresenterFloatParams>(presenter);
                    changed = !fp.TryGet(paramKey, out float existingFloat) || existingFloat != floatValue;
                    fp.Set(paramKey, floatValue);
                    break;
                case ParamLane.Int:
                    ref var ip = ref _world.Get<PresenterIntParams>(presenter);
                    changed = !ip.TryGet(paramKey, out int existingInt) || existingInt != intValue;
                    ip.Set(paramKey, intValue);
                    break;
                case ParamLane.Vector:
                    ref var vp = ref _world.Get<PresenterVectorParams>(presenter);
                    changed = !vp.TryGet(paramKey, out Vector4 existingVector) || existingVector != vectorValue;
                    vp.Set(paramKey, in vectorValue);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lane));
            }

            if (!changed)
            {
                return false;
            }

            ref var state = ref _world.Get<PresenterState>(presenter);
            state.Version++;
            MarkStaticDirtyIfVisualParamChanged(presenter, in state, paramKey, lane);
            if (propagateToChildren)
            {
                PropagateParamToAffectedChildren(presenter, paramKey, lane, floatValue, intValue, in vectorValue);
            }

            return true;
        }

        public void SetParamDefault(in PresenterDefinition definition, Entity presenter)
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
                        ref var fd = ref _world.Get<PresenterFloatDefaults>(presenter);
                        fd.Set(entry.ParamKey, entry.FloatValue);
                        break;
                    case ParamLane.Int:
                        ref var id = ref _world.Get<PresenterIntDefaults>(presenter);
                        id.Set(entry.ParamKey, entry.IntValue);
                        break;
                    case ParamLane.Vector:
                        ref var vd = ref _world.Get<PresenterVectorDefaults>(presenter);
                        vd.Set(entry.ParamKey, entry.VectorValue);
                        break;
                }
            }
        }

        public float ResolveFloat(Entity presenter, int paramKey, float defaultValue = 0f)
        {
            return PresenterParamResolver.ResolveFloat(_world, presenter, paramKey, defaultValue);
        }

        public bool TryResolveFloat(Entity presenter, int paramKey, out float value)
        {
            return PresenterParamResolver.TryResolveFloat(_world, presenter, paramKey, out value);
        }

        public int ResolveInt(Entity presenter, int paramKey, int defaultValue = 0)
        {
            return PresenterParamResolver.ResolveInt(_world, presenter, paramKey, defaultValue);
        }

        public bool TryResolveInt(Entity presenter, int paramKey, out int value)
        {
            return PresenterParamResolver.TryResolveInt(_world, presenter, paramKey, out value);
        }

        public Vector4 ResolveVector(Entity presenter, int paramKey, Vector4 defaultValue)
        {
            return PresenterParamResolver.ResolveVector(_world, presenter, paramKey, defaultValue);
        }

        public bool TryResolveVector(Entity presenter, int paramKey, out Vector4 value)
        {
            return PresenterParamResolver.TryResolveVector(_world, presenter, paramKey, out value);
        }

        public void ClearParam(Entity presenter, int paramKey, ParamLane lane)
        {
            ClearParamInternal(presenter, paramKey, lane, propagateToChildren: false);
        }

        public void ClearParamAndPropagateToAffectedChildren(Entity presenter, int paramKey, ParamLane lane)
        {
            ClearParamInternal(presenter, paramKey, lane, propagateToChildren: true);
        }

        private bool ClearParamInternal(Entity presenter, int paramKey, ParamLane lane, bool propagateToChildren)
        {
            if (!_world.IsAlive(presenter))
            {
                return false;
            }

            bool changed = false;
            switch (lane)
            {
                case ParamLane.Float:
                    if (_world.Has<PresenterFloatParams>(presenter))
                    {
                        ref PresenterFloatParams floats = ref _world.Get<PresenterFloatParams>(presenter);
                        changed = floats.Clear(paramKey);
                    }
                    break;
                case ParamLane.Int:
                    if (_world.Has<PresenterIntParams>(presenter))
                    {
                        ref PresenterIntParams ints = ref _world.Get<PresenterIntParams>(presenter);
                        changed = ints.Clear(paramKey);
                    }
                    break;
                case ParamLane.Vector:
                    if (_world.Has<PresenterVectorParams>(presenter))
                    {
                        ref PresenterVectorParams vectors = ref _world.Get<PresenterVectorParams>(presenter);
                        changed = vectors.Clear(paramKey);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lane));
            }

            if (!changed)
            {
                if (propagateToChildren)
                {
                    PropagateClearParamToAffectedChildren(presenter, paramKey, lane);
                }

                return false;
            }

            if (!_world.Has<PresenterState>(presenter))
            {
                return false;
            }

            ref PresenterState state = ref _world.Get<PresenterState>(presenter);
            state.Version++;
            MarkStaticDirtyIfVisualParamChanged(presenter, in state, paramKey, lane);
            if (propagateToChildren)
            {
                PropagateClearParamToAffectedChildren(presenter, paramKey, lane);
            }

            return true;
        }
        public bool HasOwnerPayload(Entity owner)
        {
            return owner != Entity.Null &&
                   _byOwner.TryGetValue(new OwnerKey(owner), out OwnerPresenterBucket bucket) &&
                   bucket.Count > 0;
        }

        public bool HasActiveScopedInstance(
            int defId, Entity owner, int scopeId,
            PresentationAnchorKind anchorKind, Vector3 worldPosition)
        {
            return TryGetActiveScopedInstance(defId, owner, scopeId, anchorKind, worldPosition, out _);
        }

        public bool TryGetActiveScopedInstance(
            int defId, Entity owner, int scopeId,
            PresentationAnchorKind anchorKind, Vector3 worldPosition,
            out Entity entity)
        {
            if (anchorKind == PresentationAnchorKind.Entity &&
                TryGetEntityAnchoredScopedInstance(defId, owner, scopeId, out entity))
            {
                return true;
            }

            var key = new ScopedOwnerKey(defId, owner, scopeId, anchorKind, default);
            if (!_scopedInstances.TryGetValue(key, out entity))
            {
                return false;
            }

            if (_world.IsAlive(entity) && _world.Has<PresenterState>(entity))
            {
                return true;
            }

            _scopedInstances.Remove(key);
            entity = Entity.Null;
            return false;
        }

        public bool TryGetUniqueActiveScopedInstanceByScope(
            int defId,
            Entity owner,
            int scopeId,
            out Entity entity)
        {
            entity = Entity.Null;
            if (scopeId <= 0 ||
                !_byScope.TryGetValue(scopeId, out EntityBucket scoped) ||
                scoped.Count == 0)
            {
                return false;
            }

            Entity match = Entity.Null;
            int matchCount = 0;
            bool requireOwner = owner != Entity.Null;
            for (int i = 0; i < scoped.Count; i++)
            {
                if (!TryMatchScopedInstance(scoped.GetAt(i), defId, owner, scopeId, out Entity candidate, requireOwner))
                {
                    continue;
                }

                match = candidate;
                matchCount++;
            }

            if (matchCount == 0)
            {
                return false;
            }

            if (matchCount > 1)
            {
                throw new InvalidOperationException(
                    $"DestroyScopedPresenter matched {matchCount} active presenters for defId={defId}, owner={owner.Id}, scopeTag={scopeId}; scoped destroy requires a unique def/owner/scope match.");
            }

            entity = match;
            return true;
        }

        public bool UpdateWorldPosition(Entity presenter, in Vector3 worldPosition)
        {
            if (!(_world.IsAlive(presenter) &&
                  _world.Has<PresenterState>(presenter) &&
                  _world.Has<PresenterWorldPosition>(presenter)))
            {
                return false;
            }

            ref PresenterWorldPosition position = ref _world.Get<PresenterWorldPosition>(presenter);
            if (position.Value == worldPosition)
            {
                return false;
            }

            position.Value = worldPosition;
            if (_world.Has<PresenterWorldPlanePosition>(presenter))
            {
                _world.Get<PresenterWorldPlanePosition>(presenter).ValueCm = WorldPlane2D.VisualMetersToLogicCm(in worldPosition);
            }

            ref PresenterState state = ref _world.Get<PresenterState>(presenter);
            state.Version++;
            MarkTransformDrivenEmitDirty(presenter);
            return true;
        }

        private bool TryGetEntityAnchoredScopedInstance(int defId, Entity owner, int scopeId, out Entity entity)
        {
            entity = Entity.Null;
            if (owner == Entity.Null ||
                !_byOwner.TryGetValue(new OwnerKey(owner), out OwnerPresenterBucket bucket) ||
                bucket.Count == 0)
            {
                return false;
            }

            if (bucket.TryGetSingle(out Entity single))
            {
                return TryMatchEntityAnchoredScopedInstance(single, defId, scopeId, out entity);
            }

            for (int i = 0; i < bucket.Count; i++)
            {
                if (TryMatchEntityAnchoredScopedInstance(bucket.GetAt(i), defId, scopeId, out entity))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryMatchEntityAnchoredScopedInstance(Entity presenter, int defId, int scopeId, out Entity entity)
        {
            if (!TryMatchScopedInstance(presenter, defId, Entity.Null, scopeId, out entity, requireOwner: false))
            {
                return false;
            }

            return _world.Get<PresenterState>(entity).AnchorKind == PresentationAnchorKind.Entity;
        }

        private bool TryMatchScopedInstance(Entity presenter, int defId, Entity owner, int scopeId, out Entity entity, bool requireOwner = true)
        {
            entity = Entity.Null;
            if (!_world.IsAlive(presenter) || !_world.Has<PresenterState>(presenter))
            {
                return false;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            if (state.DefId != defId ||
                state.ScopeId != scopeId ||
                (requireOwner && state.OwnerEntity != owner) ||
                state.DefaultLifetime > 0f)
            {
                return false;
            }

            entity = presenter;
            return true;
        }

        public IReadOnlyList<Entity> GetActiveByDefinition(int defId)
        {
            return _byDefinition.TryGetValue(defId, out var entities)
                ? entities
                : Array.Empty<Entity>();
        }

        public IReadOnlyList<Entity> GetActiveByOwnerDefinition(int defId, Entity owner)
        {
            return _byOwnerDefinition.TryGetValue(new OwnerDefinitionKey(owner, defId), out OwnerPresenterBucket entities)
                ? entities
                : Array.Empty<Entity>();
        }

        internal bool TryGetActiveByOwnerDefinition(int defId, Entity owner, out OwnerPresenterBucket entities)
        {
            return _byOwnerDefinition.TryGetValue(new OwnerDefinitionKey(owner, defId), out entities!);
        }

        internal bool TryGetActiveByOwner(Entity owner, out OwnerPresenterBucket presenters)
        {
            presenters = default;
            if (owner == Entity.Null ||
                !_byOwner.TryGetValue(new OwnerKey(owner), out presenters) ||
                presenters.Count == 0)
            {
                return false;
            }

            return true;
        }

        internal int ReleaseDeadOwners(Action<Entity, PresenterState>? onDestroyed = null)
        {
            if (_byOwner.Count == 0)
            {
                return 0;
            }

            _deadOwnerKeys.Clear();
            foreach (OwnerKey ownerKey in _byOwner.Keys)
            {
                Entity owner = ownerKey.ToEntity();
                if (!_world.IsAlive(owner))
                {
                    _deadOwnerKeys.Add(ownerKey);
                }
            }

            int released = 0;
            _entityScratch.Clear();
            for (int i = 0; i < _deadOwnerKeys.Count; i++)
            {
                OwnerKey ownerKey = _deadOwnerKeys[i];
                if (!_byOwner.TryGetValue(ownerKey, out OwnerPresenterBucket bucket) || bucket.Count == 0)
                {
                    continue;
                }

                if (bucket.TryGetSingle(out Entity single))
                {
                    if (_world.IsAlive(single) && _world.Has<PresenterState>(single))
                    {
                        _entityScratch.Add(single);
                    }

                    continue;
                }

                for (int presenterIndex = 0; presenterIndex < bucket.Count; presenterIndex++)
                {
                    Entity presenter = bucket.GetAt(presenterIndex);
                    if (_world.IsAlive(presenter) && _world.Has<PresenterState>(presenter))
                    {
                        _entityScratch.Add(presenter);
                    }
                }
            }

            for (int i = 0; i < _entityScratch.Count; i++)
            {
                Entity presenter = _entityScratch[i];
                if (_world.IsAlive(presenter) && _world.Has<PresenterState>(presenter))
                {
                    Destroy(presenter, onDestroyed);
                    released++;
                }
            }

            _entityScratch.Clear();
            _deadOwnerKeys.Clear();
            return released;
        }

        public void SyncCullVisibility()
        {
            foreach (ref var chunk in _world.Query(in _presenterCullQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var states = chunk.GetSpan<PresenterState>();
                var culls = chunk.GetSpan<PresenterCullState>();
                foreach (var index in chunk)
                {
                    ref PresenterState state = ref states[index];
                    ref PresenterCullState cull = ref culls[index];
                    ResolveOwnerCull(in state, out bool ownerCullVisible, out LODLevel ownerLod);
                    bool changed = cull.OwnerCullVisible != ownerCullVisible || cull.LOD != ownerLod;
                    cull.OwnerCullVisible = ownerCullVisible;
                    cull.LOD = ownerLod;
                    if (changed)
                    {
                        MarkStaticDirty(Unsafe.Add(ref entityFirst, index));
                    }
                }
            }

            if (_nonRootCount == 0)
            {
                return;
            }

            foreach (ref var chunk in _world.Query(in _presenterCullQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var culls = chunk.GetSpan<PresenterCullState>();
                foreach (var index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    if (!_world.Has<PresenterParent>(entity))
                    {
                        continue;
                    }

                    Entity parent = _world.Get<PresenterParent>(entity).Parent;
                    if (parent == Entity.Null || !_world.IsAlive(parent) || !_world.Has<PresenterCullState>(parent))
                    {
                        continue;
                    }

                    ref PresenterCullState cull = ref culls[index];
                    bool ownerCullVisible = cull.OwnerCullVisible && _world.Get<PresenterCullState>(parent).OwnerCullVisible;
                    bool changed = cull.OwnerCullVisible != ownerCullVisible;
                    cull.OwnerCullVisible = ownerCullVisible;
                    if (changed)
                    {
                        MarkStaticDirty(entity);
                    }
                }
            }
        }

        public void SyncCullVisibility(ReadOnlySpan<Entity> owners)
        {
            for (int i = 0; i < owners.Length; i++)
            {
                Entity owner = owners[i];
                if (!_byOwner.TryGetValue(new OwnerKey(owner), out OwnerPresenterBucket presenters))
                {
                    continue;
                }

                bool ownerVisible = ResolveOwnerCullVisible(owner);
                LODLevel ownerLod = ResolveOwnerLod(owner);
                if (presenters.TryGetSingle(out Entity single))
                {
                    SyncOwnerRootCull(single, ownerVisible, ownerLod);
                    continue;
                }

                for (int presenterIndex = 0; presenterIndex < presenters.Count; presenterIndex++)
                {
                    SyncOwnerRootCull(presenters.GetAt(presenterIndex), ownerVisible, ownerLod);
                }
            }
        }

        public void MarkEventDrivenStaticEmitDirty(ReadOnlySpan<Entity> owners)
        {
            for (int i = 0; i < owners.Length; i++)
            {
                if (!_byOwner.TryGetValue(new OwnerKey(owners[i]), out OwnerPresenterBucket presenters))
                {
                    continue;
                }

                if (presenters.TryGetSingle(out Entity single))
                {
                    MarkStaticDirty(single);
                    continue;
                }

                for (int presenterIndex = 0; presenterIndex < presenters.Count; presenterIndex++)
                {
                    MarkStaticDirty(presenters.GetAt(presenterIndex));
                }
            }
        }

        public void SyncRootCullVisibilityAndMarkEventDrivenStaticEmitDirty(ReadOnlySpan<Entity> owners)
        {
            if (_nonRootCount != 0)
            {
                throw new InvalidOperationException("Root-only cull sync fast path cannot run while non-root presenters exist.");
            }

            if (owners.Length > 1024)
            {
                SyncAllRootCullVisibilityAndMarkChangedEventDrivenStaticEmitDirty();
                return;
            }

            for (int i = 0; i < owners.Length; i++)
            {
                Entity owner = owners[i];
                if (!_byOwner.TryGetValue(new OwnerKey(owner), out OwnerPresenterBucket presenters))
                {
                    continue;
                }

                ResolveOwnerCull(owner, out bool ownerVisible, out LODLevel ownerLod);
                if (presenters.TryGetSingle(out Entity single))
                {
                    SyncRootCullAndMaybeMarkStaticDirty(single, ownerVisible, ownerLod);
                    continue;
                }

                for (int presenterIndex = 0; presenterIndex < presenters.Count; presenterIndex++)
                {
                    SyncRootCullAndMaybeMarkStaticDirty(presenters.GetAt(presenterIndex), ownerVisible, ownerLod);
                }
            }
        }

        public bool TrySyncSingleRootCullVisibilityAndMarkEventDrivenStaticEmitDirty(
            Entity presenter,
            bool ownerVisible,
            LODLevel ownerLod)
        {
            if (presenter == Entity.Null ||
                !_world.IsAlive(presenter) ||
                !_world.Has<PresenterCullState>(presenter))
            {
                return false;
            }

            if (_world.Has<PresenterParent>(presenter) &&
                _world.Get<PresenterParent>(presenter).Parent != Entity.Null)
            {
                return false;
            }

            if (_world.Has<PresenterChildren>(presenter) &&
                _world.Get<PresenterChildren>(presenter).Count > 0)
            {
                SyncCullHierarchyAndMarkDirty(presenter, ownerVisible, ownerLod, parentVisible: true);
                return true;
            }

            ref PresenterCullState cull = ref _world.Get<PresenterCullState>(presenter);
            bool changed = cull.OwnerCullVisible != ownerVisible || cull.LOD != ownerLod;
            cull.OwnerCullVisible = ownerVisible;
            cull.LOD = ownerLod;
            if (changed)
            {
                MarkStaticDirty(presenter);
            }

            if (ownerVisible && (changed || !_world.Has<PerfHasEmitWork>(presenter)))
            {
                EnsureRequestBackedEmitWorkScheduled(presenter);
            }

            return true;
        }

        private void SyncAllRootCullVisibilityAndMarkChangedEventDrivenStaticEmitDirty()
        {
            foreach (ref var chunk in _world.Query(in _rootCullFastPathQuery))
            {
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                Span<PresenterCullState> culls = chunk.GetSpan<PresenterCullState>();
                Span<PresenterEmitCache> emitCaches = chunk.GetSpan<PresenterEmitCache>();
                ref Entity entityFirst = ref chunk.Entity(0);
                bool hasStaticStableVisual = chunk.Has<PerfStaticStableVisual>();
                bool hasRetainedPresentationRequest = chunk.Has<PerfRetainedPresentationRequest>();

                foreach (int index in chunk)
                {
                    ResolveOwnerCull(states[index].OwnerEntity, out bool ownerVisible, out LODLevel ownerLod);
                    ref PresenterCullState cull = ref culls[index];
                    bool changed = cull.OwnerCullVisible != ownerVisible || cull.LOD != ownerLod;
                    cull.OwnerCullVisible = ownerVisible;
                    cull.LOD = ownerLod;

                    if (!changed)
                    {
                        continue;
                    }

                    ref PresenterEmitCache emitCache = ref emitCaches[index];
                    if (hasStaticStableVisual)
                    {
                        MarkStaticDirty(ref emitCache);
                    }

                    if (hasRetainedPresentationRequest)
                    {
                        Entity entity = Unsafe.Add(ref entityFirst, index);
                        if (MarkRetainedPresentationRequestDirty(ref emitCache))
                        {
                            AppendRetainedPresentationDirtyEntity(entity);
                        }
                    }
                }
            }
        }

        public int ReleaseDeadEntityAnchors(Action<Entity, PresenterState>? onDestroyed = null)
        {
            _entityScratch.Clear();
            foreach (ref Chunk chunk in _world.Query(in _presenterStateQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                foreach (int index in chunk)
                {
                    ref readonly PresenterState state = ref states[index];
                    if (state.AnchorKind != PresentationAnchorKind.Entity || _world.IsAlive(state.OwnerEntity))
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    _entityScratch.Add(entity);
                }
            }

            int released = 0;
            for (int i = 0; i < _entityScratch.Count; i++)
            {
                Entity entity = _entityScratch[i];
                if (_world.IsAlive(entity))
                {
                    Destroy(entity, onDestroyed);
                    released++;
                }
            }

            _entityScratch.Clear();
            return released;
        }

        public int ReleaseExpired(PresenterDefinitionRegistry definitions,
            Action<Entity, PresenterState>? onReleased = null)
        {
            _entityScratch.Clear();
            foreach (ref Chunk chunk in _world.Query(in _presenterStateQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                foreach (int index in chunk)
                {
                    ref readonly PresenterState state = ref states[index];
                    if (state.DefaultLifetime <= 0f || state.Elapsed < state.DefaultLifetime)
                    {
                        continue;
                    }

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    _entityScratch.Add(entity);
                }
            }

            int released = 0;
            for (int i = 0; i < _entityScratch.Count; i++)
            {
                Entity entity = _entityScratch[i];
                if (_world.IsAlive(entity))
                {
                    Destroy(entity, onReleased);
                    released++;
                }
            }

            _entityScratch.Clear();
            return released;
        }

        public void AdvanceElapsed(float dt)
        {
            foreach (ref Chunk chunk in _world.Query(in _presenterStateQuery))
            {
                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                foreach (int index in chunk)
                {
                    states[index].Elapsed += dt;
                }
            }
        }

        public void Clear()
        {
            _entityScratch.Clear();
            foreach (ref Chunk chunk in _world.Query(in _presenterStateQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    _entityScratch.Add(entity);
                }
            }
            for (int i = 0; i < _entityScratch.Count; i++)
            {
                Entity entity = _entityScratch[i];
                if (_world.IsAlive(entity))
                    _world.Destroy(entity);
            }

            _entityScratch.Clear();
            ClearOwnerPayloadMarkers();
            _activeCount = 0;
            _structureVersion++;
            _nonRootCount = 0;
            _byDefinition.Clear();
            _byOwner.Clear();
            _byOwnerDefinition.Clear();
            _byScope.Clear();
            _scopedInstances.Clear();
        }

        public void SyncTickBehaviorMarkers(Entity entity, PresenterDefinition definition, uint activeBehaviorMask)
        {
            if (!_world.IsAlive(entity))
            {
                return;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            if (behaviors == null ||
                !_world.Has<PresenterState>(entity))
            {
                RemoveTickBehaviorMarkers(entity);
                return;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(entity);
            bool hasSound = false;
            bool hasSpline = false;
            bool hasAttachment = false;
            bool hasAttachmentTick = false;
            bool hasGrounding = false;
            bool hasAnimator = false;
            bool hasOwnerFacingBinding = definition.HasOwnerFacingBindingWork;
            bool hasMinimapMarker = false;
            bool hasExtensionBehavior = false;

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
                    case BehaviorKind.Grounding:
                        hasGrounding |= GroundingRequiresPresenterTick(entity, in state, in slot.Grounding);
                        break;
                    case BehaviorKind.Animator: hasAnimator = true; break;
                    case BehaviorKind.Attachment:
                        hasAttachment = true;
                        if (AttachmentRequiresTick(entity, definition, in slot.Attachment))
                        {
                            hasAttachmentTick = true;
                        }

                        break;
                    case BehaviorKind.MinimapMarker: hasMinimapMarker = true; break;
                    case BehaviorKind.Extension:
                        hasExtensionBehavior |=
                            (slot.KindId != 0 ? slot.KindId : (byte)slot.Kind) >= PerformerBehaviorKindRegistry.FirstModBehaviorKindId &&
                            slot.ExtensionLane == PerformerBehaviorExecutionLane.ContinuousTick;
                        break;
                }
            }

            bool canUseOwnerPayloadAttachedTransformSync =
                CanUseOwnerPayloadAttachedTransformSync(entity, definition, activeBehaviorMask);
            bool needsTransformSync = !canUseOwnerPayloadAttachedTransformSync &&
                PresenterTransformRequiresTick(entity, depth: 0);
            hasAttachmentTick &= !canUseOwnerPayloadAttachedTransformSync;

            SyncTickBehaviorMarker<PerfHasSound>(entity, hasSound);
            SyncTickBehaviorMarker<PerfHasSpline>(entity, hasSpline);
            SyncTickBehaviorMarker<PerfHasAttachment>(entity, hasAttachment);
            SyncTickBehaviorMarker<PerfHasAttachmentTick>(entity, hasAttachmentTick);
            SyncTickBehaviorMarker<PerfHasGrounding>(entity, hasGrounding);
            SyncTickBehaviorMarker<PerfHasOwnerFacingBinding>(entity, hasOwnerFacingBinding);
            SyncTickBehaviorMarker<PerfHasMinimapMarker>(entity, hasMinimapMarker);
            SyncTickBehaviorMarker<PerfHasExtensionBehavior>(entity, hasExtensionBehavior);
            SyncTickBehaviorMarker<PerfTransformSyncTick>(entity, needsTransformSync);
            SyncTickBehaviorMarker<PerfOwnerPayloadTransformSync>(entity, needsTransformSync && CanUseOwnerPayloadTransformSync(entity));
            SyncTickBehaviorMarker<PerfOwnerPayloadAttachedTransformSync>(entity, canUseOwnerPayloadAttachedTransformSync);
            SyncTickBehaviorMarker<PerfHasAnimator>(entity, hasAnimator);
        }

        public void SyncEmitWorkMarkers(Entity entity, PresenterDefinition definition, uint activeBehaviorMask)
        {
            if (!_world.IsAlive(entity))
            {
                return;
            }

            bool hasEmitWork = false;
            bool retainedPresentationRequest = definition.UsesRetainedPresentationRequest;
            bool retainedPresentationLifecycleTick = definition.NeedsRetainedPresentationRequestLifecycleTick;
            bool needsInactiveVisualRemoval = false;
            if (!hasEmitWork && !definition.UsesEventDrivenStaticEmit && !retainedPresentationRequest)
            {
                BehaviorSlot[] behaviors = definition.Behaviors;
                if (behaviors != null)
                {
                    for (int i = 0; i < behaviors.Length; i++)
                    {
                        ref readonly BehaviorSlot slot = ref behaviors[i];
                        if (!IsAssetOutputBehavior(slot.Kind) ||
                            slot.SlotIndex is < 0 or >= 32)
                        {
                            continue;
                        }

                        uint bit = 1u << slot.SlotIndex;
                        if ((activeBehaviorMask & bit) != 0)
                        {
                            hasEmitWork = true;
                            break;
                        }

                        needsInactiveVisualRemoval |= IsRequestBackedVisual(slot.AssetBinding.AssetKind);
                    }
                }
            }

            if (!hasEmitWork && needsInactiveVisualRemoval && _world.Has<PresenterEmitCache>(entity))
            {
                ref readonly PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(entity);
                hasEmitWork = emitCache.CachedVersion != 0;
            }

            SyncTickBehaviorMarker<PerfHasEmitWork>(entity, hasEmitWork);
            SyncTickBehaviorMarker<PerfRetainedPresentationRequest>(entity, retainedPresentationRequest);
            SyncTickBehaviorMarker<PerfRetainedPresentationRequestLifecycleTick>(entity, retainedPresentationLifecycleTick);
            if (retainedPresentationRequest)
            {
                MarkRetainedPresentationRequestDirty(entity);
            }

            bool canUseOwnerPayloadAttachedTransformSync =
                CanUseOwnerPayloadAttachedTransformSync(entity, definition, activeBehaviorMask);
            bool needsTransformSync = !canUseOwnerPayloadAttachedTransformSync &&
                PresenterTransformRequiresTick(entity, depth: 0);
            SyncTickBehaviorMarker<PerfTransformSyncTick>(entity, needsTransformSync);
            SyncTickBehaviorMarker<PerfOwnerPayloadTransformSync>(entity, needsTransformSync && CanUseOwnerPayloadTransformSync(entity));
            SyncTickBehaviorMarker<PerfOwnerPayloadAttachedTransformSync>(entity, canUseOwnerPayloadAttachedTransformSync);
        }

        public bool SetBehaviorActive(Entity entity, PresenterDefinition definition, int slotIndex, bool active)
        {
            if (!_world.IsAlive(entity) ||
                !_world.Has<PresenterState>(entity) ||
                slotIndex is < 0 or >= 32)
            {
                return false;
            }

            uint bit = 1u << slotIndex;
            if ((definition.BehaviorPresenceMask & bit) == 0)
            {
                return false;
            }

            ref PresenterState state = ref _world.Get<PresenterState>(entity);
            uint nextMask = active
                ? state.BehaviorActiveMask | bit
                : state.BehaviorActiveMask & ~bit;
            if (nextMask == state.BehaviorActiveMask)
            {
                return false;
            }

            state.BehaviorActiveMask = nextMask;
            state.Version++;
            RefreshOwnerPayloadMarker(state.OwnerEntity);
            SyncTickBehaviorMarkers(entity, definition, nextMask);
            SyncEmitWorkMarkers(entity, definition, nextMask);
            MarkStaticDirty(entity);
            return true;
        }

        private void RefreshOwnerPayloadMarker(Entity owner)
        {
            if (owner == Entity.Null ||
                !_byOwner.TryGetValue(new OwnerKey(owner), out OwnerPresenterBucket bucket) ||
                bucket.Count == 0)
            {
                return;
            }

            WriteOwnerPayloadMarker(owner, in bucket);
        }

        private static bool IsRequestBackedVisual(AssetKind kind)
        {
            return kind is AssetKind.Mesh
                or AssetKind.SkinnedMesh
                or AssetKind.Decal
                or AssetKind.VFX
                or AssetKind.Surface
                or AssetKind.WorldHud
                or AssetKind.WorldText
                or AssetKind.Spline
                or AssetKind.GroundOverlay;
        }

        private void AddBehaviorMarkers(Entity entity, PresenterDefinition definition, uint activeBehaviorMask)
        {
            SyncTickBehaviorMarkers(entity, definition, activeBehaviorMask);
            SyncEmitWorkMarkers(entity, definition, activeBehaviorMask);
        }

        private void RemoveTickBehaviorMarkers(Entity entity)
        {
            if (_world.Has<PerfHasSound>(entity))
            {
                RemoveMarker<PerfHasSound>(entity);
            }

            if (_world.Has<PerfHasSpline>(entity))
            {
                RemoveMarker<PerfHasSpline>(entity);
            }

            if (_world.Has<PerfHasAttachment>(entity))
            {
                RemoveMarker<PerfHasAttachment>(entity);
            }

            if (_world.Has<PerfHasAttachmentTick>(entity))
            {
                RemoveMarker<PerfHasAttachmentTick>(entity);
            }

            if (_world.Has<PerfHasGrounding>(entity))
            {
                RemoveMarker<PerfHasGrounding>(entity);
            }

            if (_world.Has<PerfHasOwnerFacingBinding>(entity))
            {
                RemoveMarker<PerfHasOwnerFacingBinding>(entity);
            }

            if (_world.Has<PerfHasMinimapMarker>(entity))
            {
                RemoveMarker<PerfHasMinimapMarker>(entity);
            }

            if (_world.Has<PerfHasExtensionBehavior>(entity))
            {
                RemoveMarker<PerfHasExtensionBehavior>(entity);
            }

            if (_world.Has<PerfTransformSyncTick>(entity))
            {
                RemoveMarker<PerfTransformSyncTick>(entity);
            }

            if (_world.Has<PerfOwnerPayloadTransformSync>(entity))
            {
                RemoveMarker<PerfOwnerPayloadTransformSync>(entity);
            }

            if (_world.Has<PerfOwnerPayloadAttachedTransformSync>(entity))
            {
                RemoveMarker<PerfOwnerPayloadAttachedTransformSync>(entity);
            }

            if (_world.Has<PerfHasAnimator>(entity))
            {
                RemoveMarker<PerfHasAnimator>(entity);
            }

            if (_world.Has<PerfHasEmitWork>(entity))
            {
                RemoveMarker<PerfHasEmitWork>(entity);
            }

            if (_world.Has<PerfRetainedPresentationRequest>(entity))
            {
                RemoveMarker<PerfRetainedPresentationRequest>(entity);
            }

            if (_world.Has<PerfRetainedPresentationRequestLifecycleTick>(entity))
            {
                RemoveMarker<PerfRetainedPresentationRequestLifecycleTick>(entity);
            }
        }

        private void SyncTickBehaviorMarker<TMarker>(Entity entity, bool shouldHave)
        {
            bool hasMarker = _world.Has<TMarker>(entity);
            if (shouldHave)
            {
                if (!hasMarker)
                {
                    AddMarker<TMarker>(entity);
                }

                return;
            }

            if (hasMarker)
            {
                RemoveMarker<TMarker>(entity);
            }
        }

        private void AddMarker<TMarker>(Entity entity)
        {
            if (_deferredStructuralCommands != null)
            {
                _deferredStructuralCommands.Add<TMarker>(in entity);
                return;
            }

            _world.Add<TMarker>(entity);
        }

        private void RemoveMarker<TMarker>(Entity entity)
        {
            if (_deferredStructuralCommands != null)
            {
                _deferredStructuralCommands.Remove<TMarker>(in entity);
                return;
            }

            _world.Remove<TMarker>(entity);
        }

        private bool AttachmentRequiresTick(Entity entity, PresenterDefinition definition, in AttachmentConfig config)
        {
            if (config.Target == AttachmentTarget.Bone)
            {
                return true;
            }

            if (config.Target != AttachmentTarget.Parent)
            {
                return true;
            }

            if (!DefinitionHasAttachmentTransformConsumers(definition))
            {
                return true;
            }

            if (!_world.IsAlive(entity) || !_world.Has<PresenterState>(entity))
            {
                return true;
            }

            ref PresenterState state = ref _world.Get<PresenterState>(entity);
            if (!OwnerTransformIsStatic(in state))
            {
                return true;
            }

            if (!_world.Has<PresenterParent>(entity))
            {
                return false;
            }

            Entity parent = _world.Get<PresenterParent>(entity).Parent;
            return PresenterTransformRequiresTick(parent, depth: 0);
        }

        private bool CanUseOwnerPayloadAttachedTransformSync(
            Entity entity,
            PresenterDefinition definition,
            uint activeMask)
        {
            if (entity == Entity.Null ||
                !_world.IsAlive(entity) ||
                !_world.Has<PresenterState>(entity) ||
                !_world.Has<PresenterParent>(entity) ||
                !_world.Has<PresenterTransformSource>(entity) ||
                !DefinitionHasAttachmentTransformConsumers(definition) ||
                !definition.SupportsFastParentAttachmentTick ||
                !TryGetInlineParentAttachment(definition, activeMask, out _))
            {
                return false;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(entity);
            if (state.AnchorKind != PresentationAnchorKind.Entity)
            {
                return false;
            }

            Entity parent = _world.Get<PresenterParent>(entity).Parent;
            return parent != Entity.Null &&
                   _world.IsAlive(parent) &&
                   _world.Has<PerfOwnerPayloadTransformSync>(parent);
        }

        private bool GroundingRequiresPresenterTick(
            Entity entity,
            in PresenterState state,
            in GroundingConfig config)
        {
            if (config.Mode == GroundingMode.None ||
                config.UpdatePolicy != GroundingUpdatePolicy.EveryFrame)
            {
                return false;
            }

            if (CanUseOwnerHeightmapSampleForSnapToGround(in state, entity, in config))
            {
                return false;
            }

            return true;
        }

        private bool CanUseOwnerHeightmapSampleForSnapToGround(
            in PresenterState state,
            Entity presenter,
            in GroundingConfig config)
        {
            if (!CanUseOwnerHeightmapSampleForSnapToGroundDefinition(definition: null, in config) ||
                state.AnchorKind != PresentationAnchorKind.Entity ||
                state.OwnerEntity == Entity.Null ||
                !_world.IsAlive(state.OwnerEntity) ||
                !_world.Has<VisualTransform>(state.OwnerEntity) ||
                !_world.Has<VisualHeightmapSampleState>(state.OwnerEntity) ||
                _world.Get<VisualHeightmapSampleState>(state.OwnerEntity).Sampled == 0 ||
                !_world.Has<PresenterTransformSource>(presenter) ||
                _world.Get<PresenterTransformSource>(presenter).Value != TransformSource.EntityTransform)
            {
                return false;
            }

            return true;
        }

        private static bool CanUseOwnerHeightmapSampleForSnapToGroundDefinition(
            PresenterDefinition? definition,
            in GroundingConfig config)
        {
            return config.Mode == GroundingMode.SnapToGround &&
                   config.Offset == 0f &&
                   (definition == null || definition.PositionOffset == Vector3.Zero);
        }

        private static bool DefinitionHasAttachmentTransformConsumers(PresenterDefinition definition)
        {
            return definition.HasAssetBindingBehavior ||
                   definition.HasSurfaceAuthoring ||
                   definition.UsesRetainedPresentationRequest ||
                   (definition.Children != null && definition.Children.Length != 0);
        }

        private bool BatchRequiresTransformSyncTick(
            PresenterDefinition definition,
            uint activeMask,
            ReadOnlySpan<Entity> owners)
        {
            if (owners.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < owners.Length; i++)
            {
                Entity owner = owners[i];
                if (owner == Entity.Null ||
                    !_world.IsAlive(owner) ||
                    !_world.Has<PresentationStaticTransform>(owner))
                {
                    return true;
                }
            }

            return DefinitionRequiresTransformSyncForStaticOwner(definition, activeMask, depth: 0);
        }

        private bool BatchRequiresGroundingTick(
            PresenterDefinition definition,
            uint activeMask,
            ReadOnlySpan<Entity> owners)
        {
            if (definition == null ||
                definition.Behaviors == null ||
                definition.Behaviors.Length == 0 ||
                owners.Length == 0)
            {
                return false;
            }

            bool hasGroundingWork = false;
            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.SlotIndex is < 0 or >= 32 ||
                    (activeMask & (1u << slot.SlotIndex)) == 0 ||
                    slot.Kind != BehaviorKind.Grounding)
                {
                    continue;
                }

                if (slot.Grounding.Mode == GroundingMode.None ||
                    slot.Grounding.UpdatePolicy != GroundingUpdatePolicy.EveryFrame)
                {
                    continue;
                }

                hasGroundingWork = true;
                if (!CanUseOwnerHeightmapSampleForSnapToGroundDefinition(definition, in slot.Grounding))
                {
                    return true;
                }
            }

            if (!hasGroundingWork)
            {
                return false;
            }

            for (int i = 0; i < owners.Length; i++)
            {
                Entity owner = owners[i];
                if (owner == Entity.Null ||
                    !_world.IsAlive(owner) ||
                    !_world.Has<VisualTransform>(owner) ||
                    !_world.Has<VisualHeightmapSampleState>(owner) ||
                    _world.Get<VisualHeightmapSampleState>(owner).Sampled == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanBatchUseOwnerPayloadTransformSync(
            PresenterDefinition definition,
            uint activeMask,
            ReadOnlySpan<Entity> owners)
        {
            if (owners.Length == 0 ||
                !DefinitionCanUseOwnerPayloadTransformSync(definition, activeMask, depth: 0))
            {
                return false;
            }

            for (int i = 0; i < owners.Length; i++)
            {
                Entity owner = owners[i];
                if (owner == Entity.Null ||
                    !_world.IsAlive(owner) ||
                    !_world.Has<WorldPositionCm>(owner) ||
                    !_world.Has<VisualTransform>(owner) ||
                    _world.Has<PresentationStaticTransform>(owner))
                {
                    return false;
                }
            }

            return true;
        }

        private bool DefinitionCanUseOwnerPayloadTransformSync(
            PresenterDefinition definition,
            uint activeMask,
            int depth)
        {
            if (depth >= 8)
            {
                return false;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
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
                    case BehaviorKind.AssetBinding:
                    case BehaviorKind.WorldText:
                    case BehaviorKind.Grounding:
                    case BehaviorKind.MinimapMarker:
                    case BehaviorKind.Animator:
                    case BehaviorKind.Material:
                        break;

                    default:
                        return false;
                }
            }

            return true;
        }

        private bool DefinitionRequiresTransformSyncForStaticOwner(
            PresenterDefinition definition,
            uint activeMask,
            int depth)
        {
            if (depth >= 8)
            {
                return true;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            if (behaviors != null)
            {
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
                            if (slot.Attachment.Target != AttachmentTarget.Parent)
                            {
                                return true;
                            }
                            break;
                    }
                }
            }

            ChildPresenterRef[] children = definition.Children;
            if (children == null || children.Length == 0 || _definitions == null)
            {
                return false;
            }

            for (int i = 0; i < children.Length; i++)
            {
                if (!_definitions.TryGet(children[i].DefinitionId, out PresenterDefinition childDefinition))
                {
                    return true;
                }

                uint childMask = BuildDefaultBehaviorMask(childDefinition);
                if (DefinitionRequiresTransformSyncForStaticOwner(childDefinition, childMask, depth + 1))
                {
                    return true;
                }
            }

            return false;
        }

        private bool PresenterTransformRequiresTick(Entity presenter, int depth)
        {
            if (presenter == Entity.Null ||
                !_world.IsAlive(presenter) ||
                !_world.Has<PresenterState>(presenter))
            {
                return false;
            }

            if (depth >= 8)
            {
                return true;
            }

            ref PresenterState state = ref _world.Get<PresenterState>(presenter);
            if (!OwnerTransformIsStatic(in state))
            {
                return true;
            }

            if (_world.Has<PresenterTransformSource>(presenter))
            {
                TransformSource source = _world.Get<PresenterTransformSource>(presenter).Value;
                if (source is TransformSource.SplineDriven or TransformSource.BoneAttached)
                {
                    return true;
                }
            }

            if (_definitions == null ||
                !_definitions.TryGet(state.DefId, out PresenterDefinition definition))
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

                        Entity parent = _world.Has<PresenterParent>(presenter)
                            ? _world.Get<PresenterParent>(presenter).Parent
                            : Entity.Null;
                        return PresenterTransformRequiresTick(parent, depth + 1);
                }
            }

            return false;
        }

        private bool OwnerTransformIsStatic(in PresenterState state)
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

        private void AddEventDrivenStaticEmitMarkers(Entity entity, PresenterDefinition definition)
        {
            if (!definition.UsesEventDrivenStaticEmit)
            {
                return;
            }

            if (!_world.Has<PerfStaticStableVisual>(entity))
            {
                AddMarker<PerfStaticStableVisual>(entity);
            }

            ref PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(entity);
            MarkStaticDirty(ref emitCache);
        }

        private void AddRetainedPresentationRequestMarkers(Entity entity, PresenterDefinition definition)
        {
            if (!definition.UsesRetainedPresentationRequest)
            {
                return;
            }

            if (!_world.Has<PerfRetainedPresentationRequest>(entity))
            {
                AddMarker<PerfRetainedPresentationRequest>(entity);
            }

            SyncTickBehaviorMarker<PerfRetainedPresentationRequestLifecycleTick>(
                entity,
                definition.NeedsRetainedPresentationRequestLifecycleTick);

            MarkRetainedPresentationRequestDirty(entity);
        }

        private void CreateChildrenRecursive(
            PresenterDefinitionRegistry definitions,
            Entity parentEntity,
            Entity owner,
            int parentScopeId,
            PresentationAnchorKind anchorKind,
            Func<int>? allocateStableId)
        {
            if (!definitions.TryGet(_world.Get<PresenterState>(parentEntity).DefId, out PresenterDefinition parentDefinition))
            {
                return;
            }

            ChildPresenterRef[] children = parentDefinition.Children;
            if (children == null || children.Length == 0)
            {
                return;
            }

            for (int i = 0; i < children.Length; i++)
            {
                ref readonly ChildPresenterRef child = ref children[i];
                if (!definitions.TryGet(child.DefinitionId, out PresenterDefinition childDefinition))
                {
                    throw new InvalidOperationException($"Presenter child definition id={child.DefinitionId} is not registered.");
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

                ref PresenterState childState = ref _world.Get<PresenterState>(childEntity);
                childState.BehaviorActiveMask = BuildDefaultBehaviorMask(childDefinition);
                SyncTickBehaviorMarkers(childEntity, childDefinition, childState.BehaviorActiveMask);
                SyncEmitWorkMarkers(childEntity, childDefinition, childState.BehaviorActiveMask);
                SetParamDefault(childDefinition, childEntity);
                ApplyParamOverrides(childEntity, child.ParamOverrides);
                if (child.TransformOverride.HasOverride)
                {
                    _world.Add(childEntity, child.TransformOverride);
                }
                InitializeTransform(childEntity, childDefinition);
                if (childDefinition.RequiresBootstrapProcessing && !_world.Has<PresenterBootstrapPending>(childEntity))
                {
                    AddMarker<PresenterBootstrapPending>(childEntity);
                }

                CreateChildrenRecursive(definitions, childEntity, owner, childScopeId, anchorKind, allocateStableId);
            }
        }

        private void CreateChildrenRecursiveBatch(
            PresenterDefinitionRegistry definitions,
            ReadOnlySpan<Entity> parentEntities,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> parentScopeIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            PresentationAnchorKind anchorKind,
            Func<int>? allocateStableId)
        {
            if (parentEntities.Length == 0)
            {
                return;
            }

            if (parentEntities.Length != owners.Length ||
                parentEntities.Length != parentScopeIds.Length ||
                parentEntities.Length != ownerTransforms.Length ||
                parentEntities.Length != ownerCulls.Length)
            {
                throw new ArgumentException("Presenter child batch spans must have matching lengths.");
            }

            if (!definitions.TryGet(_world.Get<PresenterState>(parentEntities[0]).DefId, out PresenterDefinition parentDefinition))
            {
                return;
            }

            ChildPresenterRef[] children = parentDefinition.Children;
            if (children == null || children.Length == 0)
            {
                return;
            }

            if (!CanBatchCreateDirectChildren(definitions, children))
            {
                for (int i = 0; i < parentEntities.Length; i++)
                {
                    CreateChildrenRecursive(
                        definitions,
                        parentEntities[i],
                        owners[i],
                        parentScopeIds[i],
                        anchorKind,
                        allocateStableId);
                }

                return;
            }

            EnsureChildBatchCapacity(parentEntities.Length);
            for (int childIndex = 0; childIndex < children.Length; childIndex++)
            {
                ref readonly ChildPresenterRef child = ref children[childIndex];
                if (!definitions.TryGet(child.DefinitionId, out PresenterDefinition childDefinition))
                {
                    throw new InvalidOperationException($"Presenter child definition id={child.DefinitionId} is not registered.");
                }

                EnsureParentsCanAcceptChild(parentEntities, child.DefinitionId);

                bool hasParamDefaults = childDefinition.ParamDefaults != null && childDefinition.ParamDefaults.Length != 0;
                long stableIdStart = Stopwatch.GetTimestamp();
                int childScopeCount = 0;
                for (int i = 0; i < parentEntities.Length; i++)
                {
                    _childBatchScopeIds[i] = child.ScopeTag > 0 ? child.ScopeTag : parentScopeIds[i];
                    _childBatchStableIds[i] = allocateStableId != null ? allocateStableId() : 0;
                    childScopeCount++;
                }
                LastChildBatchStableIdMs += ElapsedMs(stableIdStart);

                long childSetupStart = Stopwatch.GetTimestamp();
                uint defaultBehaviorMask = BuildDefaultBehaviorMask(childDefinition);
                bool needsTransformSync = !BatchCanInlineParentAttachmentWithoutTick(
                    childDefinition,
                    defaultBehaviorMask,
                    parentEntities,
                    depth: 0);
                bool includeOwnerPayloadAttachedTransformSync = needsTransformSync &&
                    BatchCanUseOwnerPayloadAttachedTransformSync(
                        childDefinition,
                        defaultBehaviorMask,
                        parentEntities);
                bool includeTransformSyncTick = needsTransformSync && !includeOwnerPayloadAttachedTransformSync;
                bool includeOwnerPayloadTransformSync = false;
                bool includeAttachmentTick = includeTransformSyncTick;
                bool includeGroundingTick = BatchRequiresGroundingTick(childDefinition, defaultBehaviorMask, owners);
                Signature signature = BuildBatchSignature(
                    childDefinition,
                    defaultBehaviorMask,
                    includeTransformSyncTick,
                    includeOwnerPayloadTransformSync,
                    includeOwnerPayloadAttachedTransformSync,
                    includeAttachmentTick,
                    includeGroundingTick);
                LastChildBatchSetupMs += ElapsedMs(childSetupStart);

                long childWorldCreateStart = Stopwatch.GetTimestamp();
                _world.Create(_childBatchCreated.AsSpan(0, childScopeCount), signature, childScopeCount);
                LastChildBatchWorldCreateMs += ElapsedMs(childWorldCreateStart);
                ReserveBatchIndexCapacity(childScopeCount, childDefinition);

                long childFillStart = Stopwatch.GetTimestamp();
                FillEntityAnchoredBatch(
                    _childBatchCreated.AsSpan(0, childScopeCount),
                    owners,
                    _childBatchScopeIds.AsSpan(0, childScopeCount),
                    _childBatchStableIds.AsSpan(0, childScopeCount),
                    ownerTransforms,
                    ownerCulls,
                    parentEntities,
                    child.DefinitionId,
                    childDefinition,
                    defaultBehaviorMask,
                    anchorKind,
                    out double childIndexWriteMs);
                LastChildBatchIndexWriteMs += childIndexWriteMs;
                LastChildBatchComponentFillMs += Math.Max(0d, ElapsedMs(childFillStart) - childIndexWriteMs);

                if (childDefinition.UsesEventDrivenStaticEmit)
                {
                    _dirtyStaticVisualCount += childScopeCount;
                }

                if (childDefinition.UsesRetainedPresentationRequest)
                {
                    _dirtyRetainedPresentationRequestCount += childScopeCount;
                }

                if (hasParamDefaults)
                {
                    for (int i = 0; i < childScopeCount; i++)
                    {
                        SetParamDefault(childDefinition, _childBatchCreated[i]);
                    }
                }

                _activeCount += childScopeCount;
                _nonRootCount += childScopeCount;
                _structureVersion++;
            }
        }

        private bool BatchCanInlineParentAttachmentWithoutTick(
            PresenterDefinition definition,
            uint activeMask,
            ReadOnlySpan<Entity> parents,
            int depth)
        {
            if (!TryGetInlineParentAttachment(definition, activeMask, out _) ||
                !DefinitionHasAttachmentTransformConsumers(definition) ||
                parents.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < parents.Length; i++)
            {
                Entity parent = parents[i];
                if (parent == Entity.Null ||
                    PresenterTransformRequiresTick(parent, depth))
                {
                    return false;
                }
            }

            return true;
        }

        private bool BatchCanUseOwnerPayloadAttachedTransformSync(
            PresenterDefinition definition,
            uint activeMask,
            ReadOnlySpan<Entity> parents)
        {
            if (parents.Length == 0 ||
                !DefinitionHasAttachmentTransformConsumers(definition) ||
                !definition.SupportsFastParentAttachmentTick ||
                !TryGetInlineParentAttachment(definition, activeMask, out _))
            {
                return false;
            }

            for (int i = 0; i < parents.Length; i++)
            {
                Entity parent = parents[i];
                if (parent == Entity.Null ||
                    !_world.IsAlive(parent) ||
                    !_world.Has<PerfOwnerPayloadTransformSync>(parent))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CanBatchCreateDirectChildren(
            PresenterDefinitionRegistry definitions,
            ChildPresenterRef[] children)
        {
            for (int i = 0; i < children.Length; i++)
            {
                ref readonly ChildPresenterRef child = ref children[i];
                if ((child.ParamOverrides != null && child.ParamOverrides.Length != 0) ||
                    child.TransformOverride.HasOverride ||
                    !definitions.TryGet(child.DefinitionId, out PresenterDefinition childDefinition) ||
                    RequiresDeferredBootstrapAfterBatchCreate(childDefinition) ||
                    (childDefinition.Children != null && childDefinition.Children.Length != 0))
                {
                    return false;
                }
            }

            return true;
        }

        private void EnsureChildBatchCapacity(int required)
        {
            if (required <= _childBatchCreated.Length)
            {
                return;
            }

            int capacity = Math.Max(required, Math.Max(256, _childBatchCreated.Length * 2));
            Array.Resize(ref _childBatchCreated, capacity);
            Array.Resize(ref _childBatchScopeIds, capacity);
            Array.Resize(ref _childBatchStableIds, capacity);
        }

        private static uint BuildDefaultBehaviorMask(PresenterDefinition definition)
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

        private static Signature BuildBatchSignature(PresenterDefinition definition, uint defaultBehaviorMask)
        {
            return BuildBatchSignature(
                definition,
                defaultBehaviorMask,
                includeTransformSyncTick: true,
                includeOwnerPayloadTransformSync: false,
                includeOwnerPayloadAttachedTransformSync: false,
                includeAttachmentTick: true,
                includeGroundingTick: true);
        }

        private static Signature BuildBatchSignature(
            PresenterDefinition definition,
            uint defaultBehaviorMask,
            bool includeTransformSyncTick,
            bool includeOwnerPayloadTransformSync,
            bool includeOwnerPayloadAttachedTransformSync,
            bool includeAttachmentTick,
            bool includeGroundingTick)
        {
            Signature signature =
                Component<PresenterState>.Signature +
                Component<PresenterWorldPosition>.Signature +
                Component<PresenterWorldPlanePosition>.Signature +
                Component<PresenterWorldRotation>.Signature +
                Component<PresenterWorldFacing>.Signature +
                Component<PresenterWorldScale>.Signature +
                Component<PresenterTransformSource>.Signature +
                Component<PresenterParent>.Signature +
                Component<PresenterChildren>.Signature +
                Component<PresenterCullState>.Signature +
                Component<PresenterFloatParams>.Signature +
                Component<PresenterIntParams>.Signature +
                Component<PresenterVectorParams>.Signature +
                Component<PresenterFloatDefaults>.Signature +
                Component<PresenterIntDefaults>.Signature +
                Component<PresenterVectorDefaults>.Signature +
                Component<PresenterEmitCache>.Signature;

            BehaviorSlot[] behaviors = definition.Behaviors;
            if (behaviors != null)
            {
                bool hasSound = false;
                bool hasSpline = false;
                bool hasAttachment = false;
                bool hasAttachmentTick = false;
                bool hasGrounding = false;
                bool hasAnimator = false;
                bool hasOwnerFacingBinding = definition.HasOwnerFacingBindingWork;
                bool hasMinimapMarker = false;
                bool hasExtensionBehavior = false;
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
                        case BehaviorKind.Grounding:
                            hasGrounding |= includeGroundingTick &&
                                            slot.Grounding.Mode != GroundingMode.None &&
                                            slot.Grounding.UpdatePolicy == GroundingUpdatePolicy.EveryFrame;
                            break;
                        case BehaviorKind.Animator: hasAnimator = true; break;
                        case BehaviorKind.Attachment:
                            hasAttachment = true;
                            hasAttachmentTick |= includeAttachmentTick &&
                                                 DefinitionHasAttachmentTransformConsumers(definition);
                            break;
                        case BehaviorKind.MinimapMarker: hasMinimapMarker = true; break;
                        case BehaviorKind.Extension:
                            hasExtensionBehavior |=
                                (slot.KindId != 0 ? slot.KindId : (byte)slot.Kind) >= PerformerBehaviorKindRegistry.FirstModBehaviorKindId &&
                                slot.ExtensionLane == PerformerBehaviorExecutionLane.ContinuousTick;
                            break;
                    }
                }

                if (hasSound) signature += Component<PerfHasSound>.Signature;
                if (hasSpline) signature += Component<PerfHasSpline>.Signature;
                if (hasAnimator)
                {
                    signature += Component<PerfHasAnimator>.Signature;
                    signature += Component<PresenterAnimatorSlot>.Signature;
                }
                if (hasAttachment) signature += Component<PerfHasAttachment>.Signature;
                if (hasAttachmentTick) signature += Component<PerfHasAttachmentTick>.Signature;
                if (hasGrounding) signature += Component<PerfHasGrounding>.Signature;
                if (hasOwnerFacingBinding) signature += Component<PerfHasOwnerFacingBinding>.Signature;
                if (hasMinimapMarker) signature += Component<PerfHasMinimapMarker>.Signature;
                if (hasExtensionBehavior) signature += Component<PerfHasExtensionBehavior>.Signature;
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
                if (definition.NeedsRetainedPresentationRequestLifecycleTick)
                {
                    signature += Component<PerfRetainedPresentationRequestLifecycleTick>.Signature;
                }
            }

            if (includeTransformSyncTick)
            {
                signature += Component<PerfTransformSyncTick>.Signature;
            }

            if (includeOwnerPayloadTransformSync)
            {
                signature += Component<PerfOwnerPayloadTransformSync>.Signature;
            }

            if (includeOwnerPayloadAttachedTransformSync)
            {
                signature += Component<PerfOwnerPayloadAttachedTransformSync>.Signature;
            }

            return signature;
        }

        public static bool RequiresDeferredBootstrapAfterBatchCreate(PresenterDefinition definition)
        {
            if (definition == null || !definition.RequiresBootstrapProcessing)
            {
                return false;
            }

            if (definition.HasSurfaceAuthoring ||
                definition.HasOwnerTagBindingWork ||
                definition.MaterialBehaviorIndices.Length != 0 ||
                definition.ExtensionBootstrapBehaviorIndices.Length != 0 ||
                definition.BootstrapGroundingBehaviorIndices.Length != 0 ||
                !CanInlineInitialParamBindings(definition) ||
                !CanInlineInitialAttributeBehaviors(definition) ||
                ActiveAssetBindingNeedsDeferredBootstrap(definition) ||
                ActiveAttachmentNeedsDeferredBootstrap(definition))
            {
                return true;
            }

            return false;
        }

        public static bool RequiresDeferredBootstrapAfterBatchCreateHierarchy(
            PresenterDefinition definition,
            PresenterDefinitionRegistry definitions)
        {
            if (definition == null)
            {
                return false;
            }

            if (RequiresDeferredBootstrapAfterBatchCreate(definition))
            {
                return true;
            }

            ChildPresenterRef[] children = definition.Children;
            if (children == null || children.Length == 0)
            {
                return false;
            }

            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            for (int i = 0; i < children.Length; i++)
            {
                ref readonly ChildPresenterRef child = ref children[i];
                if (!definitions.TryGet(child.DefinitionId, out PresenterDefinition childDefinition))
                {
                    throw new InvalidOperationException($"Presenter child definition id={child.DefinitionId} is not registered.");
                }

                if (RequiresDeferredBootstrapAfterBatchCreateHierarchy(childDefinition, definitions))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanInlineInitialParamBindings(PresenterDefinition definition)
        {
            PresenterParamBinding[] bindings = definition.Bindings;
            for (int i = 0; i < bindings.Length; i++)
            {
                switch (bindings[i].Value.Source)
                {
                    case ValueSourceKind.Constant:
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static bool CanInlineInitialAttributeBehaviors(PresenterDefinition definition)
        {
            if (!definition.HasOwnerAttributeBindingWork)
            {
                return true;
            }

            CompiledBinding[] compiled = definition.CompiledBindings;
            uint activeMask = BuildDefaultBehaviorMask(definition);
            for (int i = 0; i < compiled.Length; i++)
            {
                ref readonly CompiledBinding binding = ref compiled[i];
                if (!binding.IsAttributeBound ||
                    binding.SlotIndex is < 0 or >= 32 ||
                    (activeMask & (1u << binding.SlotIndex)) == 0)
                {
                    continue;
                }

                if (binding.SourceAttributeId < 0 ||
                    binding.TargetParamKey < 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ActiveAssetBindingNeedsDeferredBootstrap(PresenterDefinition definition)
        {
            BehaviorSlot[] behaviors = definition.Behaviors;
            uint activeMask = BuildDefaultBehaviorMask(definition);
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (!IsAssetOutputBehavior(slot.Kind) ||
                    slot.SlotIndex is < 0 or >= 32 ||
                    (activeMask & (1u << slot.SlotIndex)) == 0)
                {
                    continue;
                }

                if (slot.AssetBinding.LocalOffset != Vector3.Zero ||
                    slot.AssetBinding.LocalRotation != Quaternion.Identity ||
                    (slot.AssetBinding.LocalScale != Vector3.One &&
                     !AssetBindingUsesRetainedPresentationScale(slot.AssetBinding.AssetKind)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AssetBindingUsesRetainedPresentationScale(AssetKind kind)
        {
            return kind is AssetKind.WorldHud or AssetKind.WorldText or AssetKind.Spline or AssetKind.GroundOverlay;
        }

        private static bool ActiveAttachmentNeedsDeferredBootstrap(PresenterDefinition definition)
        {
            BehaviorSlot[] behaviors = definition.Behaviors;
            uint activeMask = BuildDefaultBehaviorMask(definition);
            bool foundParentAttachment = false;
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.Attachment ||
                    slot.SlotIndex is < 0 or >= 32 ||
                    (activeMask & (1u << slot.SlotIndex)) == 0)
                {
                    continue;
                }

                if (slot.Attachment.Target != AttachmentTarget.Parent)
                {
                    return true;
                }

                if (foundParentAttachment)
                {
                    return true;
                }

                foundParentAttachment = true;
            }

            return false;
        }

        private PresenterDefinition ResolveDefinition(int defId, PresenterDefinition? definition)
        {
            if (definition != null)
            {
                return definition;
            }

            return _definitions != null && _definitions.TryGet(defId, out PresenterDefinition resolved)
                ? resolved
                : throw new InvalidOperationException($"Presenter entity references unknown definition id={defId}.");
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
            _byScope.Clear();
            _scopedInstances.Clear();
            var addStatic = new List<Entity>();
            var removeStatic = new List<Entity>();
            var dirtyStatic = new List<Entity>();
            var addRetained = new List<Entity>();
            var removeRetained = new List<Entity>();
            var dirtyRetained = new List<Entity>();
            var syncTickWork = new List<ReconcileDefinitionWork>();
            var syncEmitWork = new List<ReconcileDefinitionWork>();
            var addBootstrap = new List<Entity>();
            var removeUnknownTickMarkers = new List<Entity>();
            var query = new QueryDescription().WithAll<PresenterState>();
            _suppressOwnerPayloadMarkerWrites = true;
            try
            {
                _world.Query(in query, (Entity entity, ref PresenterState state) =>
                {
                    if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition))
                    {
                        if (_world.Has<PerfStaticStableVisual>(entity))
                        {
                            removeStatic.Add(entity);
                        }

                        removeUnknownTickMarkers.Add(entity);
                        AddToOwnerIndex(state.OwnerEntity, entity);
                        if (state.ScopeId > 0)
                        {
                AddToScopeIndex(state.ScopeId, entity);
                        }

                        return;
                    }

                    AddIndexes(entity, in state, definition);
                    syncTickWork.Add(new ReconcileDefinitionWork(entity, definition, state.BehaviorActiveMask));
                    syncEmitWork.Add(new ReconcileDefinitionWork(entity, definition, state.BehaviorActiveMask));
                    if (definition.RequiresBootstrapProcessing && !_world.Has<PresenterBootstrapPending>(entity))
                    {
                        addBootstrap.Add(entity);
                    }

                    if (definition.UsesEventDrivenStaticEmit)
                    {
                        if (!_world.Has<PerfStaticStableVisual>(entity))
                        {
                            addStatic.Add(entity);
                        }

                        if (_world.Has<PresenterEmitCache>(entity))
                        {
                            dirtyStatic.Add(entity);
                        }
                    }
                    else if (_world.Has<PerfStaticStableVisual>(entity))
                    {
                        removeStatic.Add(entity);
                    }

                    if (definition.UsesRetainedPresentationRequest)
                    {
                        if (!_world.Has<PerfRetainedPresentationRequest>(entity))
                        {
                            addRetained.Add(entity);
                        }

                        dirtyRetained.Add(entity);
                    }
                    else if (_world.Has<PerfRetainedPresentationRequest>(entity))
                    {
                        removeRetained.Add(entity);
                    }
                });
            }
            finally
            {
                _suppressOwnerPayloadMarkerWrites = false;
            }

            for (int i = 0; i < removeStatic.Count; i++)
            {
                Entity entity = removeStatic[i];
                if (_world.IsAlive(entity) && _world.Has<PerfStaticStableVisual>(entity))
                {
                    RemoveMarker<PerfStaticStableVisual>(entity);
                }
            }

            for (int i = 0; i < removeRetained.Count; i++)
            {
                Entity entity = removeRetained[i];
                if (_world.IsAlive(entity) && _world.Has<PerfRetainedPresentationRequest>(entity))
                {
                    RemoveMarker<PerfRetainedPresentationRequest>(entity);
                }
            }

            for (int i = 0; i < removeUnknownTickMarkers.Count; i++)
            {
                Entity entity = removeUnknownTickMarkers[i];
                if (_world.IsAlive(entity))
                {
                    RemoveTickBehaviorMarkers(entity);
                }
            }

            for (int i = 0; i < addStatic.Count; i++)
            {
                Entity entity = addStatic[i];
                if (_world.IsAlive(entity) && !_world.Has<PerfStaticStableVisual>(entity))
                {
                    AddMarker<PerfStaticStableVisual>(entity);
                }
            }

            for (int i = 0; i < addRetained.Count; i++)
            {
                Entity entity = addRetained[i];
                if (_world.IsAlive(entity) && !_world.Has<PerfRetainedPresentationRequest>(entity))
                {
                    AddMarker<PerfRetainedPresentationRequest>(entity);
                }
            }

            for (int i = 0; i < dirtyStatic.Count; i++)
            {
                Entity entity = dirtyStatic[i];
                if (_world.IsAlive(entity) && _world.Has<PresenterEmitCache>(entity))
                {
                    ref PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(entity);
                    MarkStaticDirty(ref emitCache);
                }
            }

            for (int i = 0; i < dirtyRetained.Count; i++)
            {
                Entity entity = dirtyRetained[i];
                MarkRetainedPresentationRequestDirty(entity);
            }

            RebuildOwnerPayloadMarkersFromIndex();

            for (int i = 0; i < syncTickWork.Count; i++)
            {
                ReconcileDefinitionWork work = syncTickWork[i];
                SyncTickBehaviorMarkers(work.Entity, work.Definition, work.ActiveBehaviorMask);
            }

            for (int i = 0; i < syncEmitWork.Count; i++)
            {
                ReconcileDefinitionWork work = syncEmitWork[i];
                SyncEmitWorkMarkers(work.Entity, work.Definition, work.ActiveBehaviorMask);
            }

            for (int i = 0; i < addBootstrap.Count; i++)
            {
                Entity entity = addBootstrap[i];
                if (_world.IsAlive(entity) && !_world.Has<PresenterBootstrapPending>(entity))
                {
                    AddMarker<PresenterBootstrapPending>(entity);
                }
            }

            SortEntityIndexesByStableId(_byDefinition);
            SortOwnerDefinitionIndexesByStableId();
        }

        private readonly struct ReconcileDefinitionWork
        {
            public readonly Entity Entity;
            public readonly PresenterDefinition Definition;
            public readonly uint ActiveBehaviorMask;

            public ReconcileDefinitionWork(Entity entity, PresenterDefinition definition, uint activeBehaviorMask)
            {
                Entity = entity;
                Definition = definition;
                ActiveBehaviorMask = activeBehaviorMask;
            }
        }

        private void SortEntityIndexesByStableId<TKey>(Dictionary<TKey, List<Entity>> index)
            where TKey : notnull
        {
            foreach (List<Entity> entities in index.Values)
            {
                entities.Sort(CompareByStableIdThenEntityId);
            }
        }

        private void SortOwnerDefinitionIndexesByStableId()
        {
            _ownerDefinitionScratch.Clear();
            foreach (OwnerDefinitionKey key in _byOwnerDefinition.Keys)
            {
                _ownerDefinitionScratch.Add(key);
            }

            for (int i = 0; i < _ownerDefinitionScratch.Count; i++)
            {
                OwnerDefinitionKey key = _ownerDefinitionScratch[i];
                ref OwnerPresenterBucket bucket = ref CollectionsMarshal.GetValueRefOrNullRef(_byOwnerDefinition, key);
                if (!Unsafe.IsNullRef(ref bucket))
                {
                    bucket.Sort(CompareByStableIdThenEntityId);
                }
            }

            _ownerDefinitionScratch.Clear();
        }

        private int CompareByStableIdThenEntityId(Entity left, Entity right)
        {
            int leftStableId = _world.IsAlive(left) && _world.Has<PresenterState>(left)
                ? _world.Get<PresenterState>(left).StableId
                : int.MaxValue;
            int rightStableId = _world.IsAlive(right) && _world.Has<PresenterState>(right)
                ? _world.Get<PresenterState>(right).StableId
                : int.MaxValue;
            int stableCompare = leftStableId.CompareTo(rightStableId);
            return stableCompare != 0 ? stableCompare : left.Id.CompareTo(right.Id);
        }

        private void FillEntityAnchoredRootBatch(
            ReadOnlySpan<Entity> created,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> scopeIds,
            ReadOnlySpan<int> stableIds,
            ReadOnlySpan<VisualTransform> ownerTransforms,
            ReadOnlySpan<CullState> ownerCulls,
            int defId,
            PresenterDefinition definition,
            uint defaultBehaviorMask,
            out double indexWriteMs)
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
                PresentationAnchorKind.Entity,
                out indexWriteMs);
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
            PresenterDefinition definition,
            uint defaultBehaviorMask,
            PresentationAnchorKind anchorKind,
            out double indexWriteMs)
        {
            bool hasInlineParentAttachment = TryGetInlineParentAttachment(
                definition,
                defaultBehaviorMask,
                out AttachmentConfig parentAttachment);
            bool hasInlineParamBindings = definition.Bindings.Length != 0 && CanInlineInitialParamBindings(definition);
            bool hasInlineAttributeBehaviors = definition.HasOwnerAttributeBindingWork && CanInlineInitialAttributeBehaviors(definition);
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

                Span<PresenterState> states = chunk.GetSpan<PresenterState>();
                Span<PresenterWorldPosition> positions = chunk.GetSpan<PresenterWorldPosition>();
                Span<PresenterWorldPlanePosition> planePositions = chunk.GetSpan<PresenterWorldPlanePosition>();
                Span<PresenterWorldRotation> rotations = chunk.GetSpan<PresenterWorldRotation>();
                Span<PresenterWorldFacing> facings = chunk.GetSpan<PresenterWorldFacing>();
                Span<PresenterWorldScale> scales = chunk.GetSpan<PresenterWorldScale>();
                Span<PresenterTransformSource> transformSources = chunk.GetSpan<PresenterTransformSource>();
                Span<PresenterParent> parentComponents = chunk.GetSpan<PresenterParent>();
                Span<PresenterChildren> children = chunk.GetSpan<PresenterChildren>();
                Span<PresenterCullState> culls = chunk.GetSpan<PresenterCullState>();
                Span<PresenterFloatParams> floatParams = chunk.GetSpan<PresenterFloatParams>();
                Span<PresenterIntParams> intParams = chunk.GetSpan<PresenterIntParams>();
                Span<PresenterVectorParams> vectorParams = chunk.GetSpan<PresenterVectorParams>();
                Span<PresenterFloatDefaults> floatDefaults = chunk.GetSpan<PresenterFloatDefaults>();
                Span<PresenterIntDefaults> intDefaults = chunk.GetSpan<PresenterIntDefaults>();
                Span<PresenterVectorDefaults> vectorDefaults = chunk.GetSpan<PresenterVectorDefaults>();
                Span<PresenterEmitCache> emitCaches = chunk.GetSpan<PresenterEmitCache>();
                bool hasAnimatorSlots = chunk.Has<PresenterAnimatorSlot>();
                Span<PresenterAnimatorSlot> animatorSlots = hasAnimatorSlots
                    ? chunk.GetSpan<PresenterAnimatorSlot>()
                    : default;

                for (int offset = 0; offset < run; offset++)
                {
                    int ownerIndex = batchIndex + offset;
                    int componentIndex = row + offset;
                    Entity owner = owners[ownerIndex];
                    Entity presenter = created[ownerIndex];
                    Entity parent = parentEntities.IsEmpty ? Entity.Null : parentEntities[ownerIndex];
                    var state = new PresenterState
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
                    if (hasInlineParentAttachment &&
                        parent != Entity.Null &&
                        _world.IsAlive(parent))
                    {
                        Vector3 parentPosition = _world.Has<PresenterWorldPosition>(parent)
                            ? _world.Get<PresenterWorldPosition>(parent).Value
                            : Vector3.Zero;
                        Quaternion parentRotation = _world.Has<PresenterWorldRotation>(parent)
                            ? _world.Get<PresenterWorldRotation>(parent).Value
                            : Quaternion.Identity;
                        Vector3 parentScale = _world.Has<PresenterWorldScale>(parent)
                            ? _world.Get<PresenterWorldScale>(parent).Value
                            : Vector3.One;
                        Quaternion normalizedParentRotation = VisualMath.NormalizeOrIdentity(parentRotation);
                        Vector3 normalizedParentScale = VisualMath.NormalizeScale(parentScale);
                        Vector3 scaledOffset = parentAttachment.InheritScale
                            ? normalizedParentScale * parentAttachment.Offset
                            : parentAttachment.Offset;
                        position = parentPosition + Vector3.Transform(scaledOffset, normalizedParentRotation);
                        rotation = VisualMath.NormalizeOrIdentity(normalizedParentRotation * VisualMath.NormalizeOrIdentity(parentAttachment.RotationOffset));
                        scale = parentAttachment.InheritScale ? normalizedParentScale : Vector3.One;
                    }

                    states[componentIndex] = state;
                    positions[componentIndex] = new PresenterWorldPosition { Value = position };
                    planePositions[componentIndex] = new PresenterWorldPlanePosition
                    {
                        ValueCm = WorldPlane2D.VisualMetersToLogicCm(in position),
                    };
                    rotations[componentIndex] = new PresenterWorldRotation { Value = rotation };
                    facings[componentIndex] = hasInlineParentAttachment &&
                        parent != Entity.Null &&
                        _world.IsAlive(parent) &&
                        _world.Has<PresenterWorldFacing>(parent)
                            ? _world.Get<PresenterWorldFacing>(parent)
                            : ResolvePresenterWorldFacing(owner);
                    scales[componentIndex] = new PresenterWorldScale { Value = scale };
                    transformSources[componentIndex] = new PresenterTransformSource
                    {
                        Value = hasInlineParentAttachment && parent != Entity.Null
                            ? TransformSource.AttachedToParent
                            : parent != Entity.Null
                                ? TransformSource.InheritParent
                                : TransformSource.EntityTransform,
                    };
                    parentComponents[componentIndex] = new PresenterParent { Parent = parent };
                    children[componentIndex] = default;
                    culls[componentIndex] = new PresenterCullState
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
                    emitCaches[componentIndex] = new PresenterEmitCache
                    {
                        StaticDirty = definition.UsesEventDrivenStaticEmit ? (byte)1 : (byte)0,
                        RetainedDirty = definition.UsesRetainedPresentationRequest ? (byte)1 : (byte)0,
                    };
                    if (hasAnimatorSlots)
                    {
                        animatorSlots[componentIndex] = new PresenterAnimatorSlot
                        {
                            Value = AllocateAnimatorSlot(presenter, definition),
                        };
                    }

                    if (hasInlineParamBindings)
                    {
                        ApplyInlineInitialParamBindings(
                            ref floatParams[componentIndex],
                            definition.Bindings);
                    }

                    if (hasInlineAttributeBehaviors &&
                        _world.IsAlive(owner) &&
                        _world.Has<AttributeBuffer>(owner))
                    {
                        ref AttributeBuffer attributes = ref _world.Get<AttributeBuffer>(owner);
                        ApplyInlineInitialAttributeBehaviors(
                            ref floatParams[componentIndex],
                            ref intParams[componentIndex],
                            definition.CompiledBindings,
                            defaultBehaviorMask,
                            ref attributes);
                    }
                }

                batchIndex += run;
                chunkIndex++;
                row = 0;
            }

            long indexStart = Stopwatch.GetTimestamp();
            AddEntityAnchoredBatchIndexes(created, owners, scopeIds, parentEntities, defId, definition, anchorKind);
            indexWriteMs = ElapsedMs(indexStart);
        }

        private static bool TryGetInlineParentAttachment(
            PresenterDefinition definition,
            uint activeMask,
            out AttachmentConfig attachment)
        {
            attachment = default;
            bool found = false;
            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.Attachment ||
                    slot.SlotIndex is < 0 or >= 32 ||
                    (activeMask & (1u << slot.SlotIndex)) == 0)
                {
                    continue;
                }

                if (slot.Attachment.Target != AttachmentTarget.Parent || found)
                {
                    attachment = default;
                    return false;
                }

                attachment = slot.Attachment;
                found = true;
            }

            return found;
        }

        private static void ApplyInlineInitialParamBindings(
            ref PresenterFloatParams floatParams,
            PresenterParamBinding[] bindings)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                ref readonly PresenterParamBinding binding = ref bindings[i];
                float value;
                switch (binding.Value.Source)
                {
                    case ValueSourceKind.Constant:
                        value = binding.Value.ConstantValue;
                        break;
                    default:
                        continue;
                }

                floatParams.Set(binding.ParamKey, value);
            }
        }

        private static void ApplyInlineInitialAttributeBehaviors(
            ref PresenterFloatParams floatParams,
            ref PresenterIntParams intParams,
            CompiledBinding[] compiled,
            uint activeMask,
            ref AttributeBuffer attributes)
        {
            for (int i = 0; i < compiled.Length; i++)
            {
                ref readonly CompiledBinding binding = ref compiled[i];
                if (!binding.IsAttributeBound ||
                    binding.SlotIndex is < 0 or >= 32 ||
                    (activeMask & (1u << binding.SlotIndex)) == 0)
                {
                    continue;
                }

                ApplyInlineInitialAttributeBehavior(
                    ref floatParams,
                    ref intParams,
                    in binding,
                    ref attributes);
            }
        }

        private static void ApplyInlineInitialAttributeBehavior(
            ref PresenterFloatParams floatParams,
            ref PresenterIntParams intParams,
            in CompiledBinding binding,
            ref AttributeBuffer attributes)
        {
            float value = ResolveAttributeValue(ref attributes, binding.SourceAttributeId, binding.Mode);
            floatParams.Set(binding.TargetParamKey, value);

            if (!binding.TrySelectThreshold(value, out ThresholdMapping threshold))
            {
                return;
            }

            int thresholdIntValue = (int)threshold.OutputValue;
            floatParams.Set(threshold.OutputParamKey, threshold.OutputValue);
            intParams.Set(threshold.OutputParamKey, thresholdIntValue);
        }

        private static float ResolveAttributeValue(ref AttributeBuffer attributes, int attributeId, ValueSourceKind mode)
        {
            return mode switch
            {
                ValueSourceKind.Attribute => attributes.GetCurrent(attributeId),
                ValueSourceKind.AttributeRatio => ResolveAttributeRatio(ref attributes, attributeId),
                ValueSourceKind.AttributeBase => attributes.GetBase(attributeId),
                _ => attributes.GetCurrent(attributeId),
            };
        }

        private static float ResolveAttributeRatio(ref AttributeBuffer attributes, int attributeId)
        {
            float current = attributes.GetCurrent(attributeId);
            float max = attributes.GetBase(attributeId);
            return max <= 0f ? 0f : Math.Clamp(current / max, 0f, 1f);
        }

        private void AddEntityAnchoredBatchIndexes(
            ReadOnlySpan<Entity> created,
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<int> scopeIds,
            ReadOnlySpan<Entity> parentEntities,
            int defId,
            PresenterDefinition definition,
            PresentationAnchorKind anchorKind)
        {
            bool needsByDefinitionIndex = definition.NeedsByDefinitionIndex;
            bool needsByOwnerDefinitionIndex = definition.NeedsByOwnerDefinitionIndex;
            bool needsScopedInstances = definition.DefaultLifetime <= 0f &&
                                        anchorKind == PresentationAnchorKind.WorldPosition;

            for (int i = 0; i < created.Length; i++)
            {
                Entity owner = owners[i];
                Entity presenter = created[i];
                int scopeId = scopeIds[i];

                AddToOwnerIndex(owner, presenter);
                if (needsByDefinitionIndex)
                {
                    AddToIndex(_byDefinition, defId, presenter, initialListCapacity: 4);
                }

                if (needsByOwnerDefinitionIndex)
                {
                    AddToOwnerDefinitionIndex(owner, defId, presenter);
                }

                if (scopeId > 0)
                {
                    AddToScopeIndex(scopeId, presenter);
                }

                if (scopeId > 0 && needsScopedInstances)
                {
                    _scopedInstances[new ScopedOwnerKey(
                        defId,
                        owner,
                        scopeId,
                        anchorKind,
                        Vector3.Zero)] = presenter;
                }

                Entity parent = parentEntities.IsEmpty ? Entity.Null : parentEntities[i];
                if (parent != Entity.Null && _world.IsAlive(parent))
                {
                    AttachChildToParent(parent, presenter, defId);
                }
            }
        }

        private void EnsureParentsCanAcceptChild(ReadOnlySpan<Entity> parents, int childDefinitionId)
        {
            for (int i = 0; i < parents.Length; i++)
            {
                EnsureParentCanAcceptChild(parents[i], childDefinitionId);
            }
        }

        private void EnsureParentCanAcceptChild(Entity parent, int childDefinitionId)
        {
            if (parent == Entity.Null || !_world.IsAlive(parent))
            {
                return;
            }

            if (!_world.Has<PresenterChildren>(parent))
            {
                throw new InvalidOperationException(
                    $"Presenter parent entity {parent.Id} cannot attach child definition id={childDefinitionId} because it has no PresenterChildren component.");
            }

            ref readonly PresenterChildren children = ref _world.Get<PresenterChildren>(parent);
            if (children.Count >= PresenterChildren.MAX_CHILDREN)
            {
                throw new InvalidOperationException(
                    $"Presenter parent entity {parent.Id} exceeded child capacity while attaching child definition id={childDefinitionId}; capacity={PresenterChildren.MAX_CHILDREN}.");
            }
        }

        private bool AttachChildToParent(Entity parent, Entity child, int childDefinitionId)
        {
            if (parent == Entity.Null || !_world.IsAlive(parent))
            {
                return false;
            }

            if (!_world.Has<PresenterChildren>(parent))
            {
                throw new InvalidOperationException(
                    $"Presenter parent entity {parent.Id} cannot attach child definition id={childDefinitionId} because it has no PresenterChildren component.");
            }

            ref PresenterChildren parentChildren = ref _world.Get<PresenterChildren>(parent);
            if (!parentChildren.Add(child))
            {
                throw new InvalidOperationException(
                    $"Presenter parent entity {parent.Id} exceeded child capacity while attaching child definition id={childDefinitionId}; capacity={PresenterChildren.MAX_CHILDREN}.");
            }

            return true;
        }

        private bool OwnersHavePreseededPayloadMarkers(ReadOnlySpan<Entity> owners)
        {
            if (owners.Length == 0)
            {
                return false;
            }

            return _world.IsAlive(owners[0]) && _world.Has<PresentationOwnerHasPresenterPayload>(owners[0]);
        }

        private void WriteSingleRootOwnerPayloadMarkersBatch(
            ReadOnlySpan<Entity> owners,
            ReadOnlySpan<Entity> presenters,
            bool singleRootTransformSync)
        {
            if (owners.Length != presenters.Length || owners.Length == 0)
            {
                return;
            }

            Entity first = owners[0];
            Archetype archetype = _world.GetEntityDataArray()[first.Id].Archetype;
            Slot slot = _world.GetSlot(first);
            int batchIndex = 0;
            int chunkIndex = slot.ChunkIndex;
            int row = slot.Index;
            while (batchIndex < owners.Length)
            {
                ref Chunk chunk = ref archetype.GetChunk(chunkIndex);
                int run = Math.Min(owners.Length - batchIndex, chunk.Count - row);
                Span<PresentationOwnerHasPresenterPayload> payloads = chunk.GetSpan<PresentationOwnerHasPresenterPayload>();
                for (int offset = 0; offset < run; offset++)
                {
                    int index = batchIndex + offset;
                    payloads[row + offset] = new PresentationOwnerHasPresenterPayload
                    {
                        Count = 1,
                        RootCount = 1,
                        SingleRootPresenter = presenters[index],
                        SingleRootTransformSync = singleRootTransformSync ? (byte)1 : (byte)0,
                    };
                }

                batchIndex += run;
                chunkIndex++;
                row = 0;
            }
        }

        private void ApplyParamOverrides(Entity presenter, ParamDefault[] overrides)
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
                        SetParam(presenter, entry.ParamKey, ParamLane.Float, entry.FloatValue, 0, Vector4.Zero);
                        break;
                    case ParamLane.Int:
                        SetParam(presenter, entry.ParamKey, ParamLane.Int, 0f, entry.IntValue, Vector4.Zero);
                        break;
                    case ParamLane.Vector:
                        SetParam(presenter, entry.ParamKey, ParamLane.Vector, 0f, 0, entry.VectorValue);
                        break;
                }
            }
        }

        private void UnlinkFromParent(Entity presenter)
        {
            if (!_world.Has<PresenterParent>(presenter)) return;
            var parent = _world.Get<PresenterParent>(presenter).Parent;
            if (parent != Entity.Null && _nonRootCount > 0)
            {
                _nonRootCount--;
            }

            if (parent == Entity.Null || !_world.IsAlive(parent)) return;
            if (!_world.Has<PresenterChildren>(parent)) return;
            ref var parentChildren = ref _world.Get<PresenterChildren>(parent);
            parentChildren.Remove(presenter);
        }

        private bool ResolveOwnerCullVisible(in PresenterState state)
        {
            return ResolveOwnerCullVisible(state.OwnerEntity);
        }

        private LODLevel ResolveOwnerLod(in PresenterState state)
        {
            return ResolveOwnerLod(state.OwnerEntity);
        }

        private void ResolveOwnerCull(in PresenterState state, out bool ownerCullVisible, out LODLevel ownerLod)
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
            // Owner payload presence is now derived from the owner -> presenter index.
        }

        private PresenterWorldFacing ResolvePresenterWorldFacing(Entity owner)
        {
            if (owner == Entity.Null ||
                !_world.IsAlive(owner) ||
                !_world.Has<FacingDirection>(owner))
            {
                return default;
            }

            return new PresenterWorldFacing
            {
                AngleRad = _world.Get<FacingDirection>(owner).AngleRad,
                HasValue = 1,
            };
        }

        private void AddIndexes(Entity presenter, in PresenterState state, PresenterDefinition definition)
        {
            if (definition.NeedsByDefinitionIndex)
            {
                AddToIndex(_byDefinition, state.DefId, presenter, initialListCapacity: 4);
            }

            AddToOwnerIndex(state.OwnerEntity, presenter);
            if (definition.NeedsByOwnerDefinitionIndex)
            {
                AddToOwnerDefinitionIndex(state.OwnerEntity, state.DefId, presenter);
            }

            if (state.ScopeId > 0)
            {
                AddToScopeIndex(state.ScopeId, presenter);
            }

            if (state.ScopeId > 0 && state.DefaultLifetime <= 0f)
            {
                _scopedInstances[new ScopedOwnerKey(
                    state.DefId,
                    state.OwnerEntity,
                    state.ScopeId,
                    state.AnchorKind,
                    default)] = presenter;
            }
        }

        private void RemoveIndexes(Entity presenter, in PresenterState state)
        {
            RemoveFromIndex(_byDefinition, state.DefId, presenter);
            RemoveFromOwnerIndex(state.OwnerEntity, presenter);
            RemoveFromOwnerDefinitionIndex(state.OwnerEntity, state.DefId, presenter);
            if (state.ScopeId > 0)
            {
                RemoveFromScopeIndex(state.ScopeId, presenter);
            }

            if (state.ScopeId > 0 && state.DefaultLifetime <= 0f)
            {
                _scopedInstances.Remove(new ScopedOwnerKey(
                    state.DefId,
                    state.OwnerEntity,
                    state.ScopeId,
                    state.AnchorKind,
                    default));
            }
        }

        private void InitializeAnimatorSlotIfPresent(Entity presenter, PresenterDefinition definition)
        {
            if (!definition.HasAnimatorBehavior ||
                !_world.IsAlive(presenter) ||
                _world.Has<PresenterAnimatorSlot>(presenter))
            {
                return;
            }

            AddMarker<PresenterAnimatorSlot>(presenter);
            if (_world.Has<PresenterAnimatorSlot>(presenter))
            {
                _world.Get<PresenterAnimatorSlot>(presenter).Value = AllocateAnimatorSlot(presenter, definition);
            }
        }

        private int AllocateAnimatorSlot(Entity presenter, PresenterDefinition definition)
        {
            if (_animatorStates == null)
            {
                return -1;
            }

            int controllerId = ResolvePrimaryAnimatorControllerId(definition);
            return controllerId > 0
                ? _animatorStates.Allocate(presenter, controllerId)
                : -1;
        }

        private static int ResolvePrimaryAnimatorControllerId(PresenterDefinition definition)
        {
            if (!definition.HasAnimatorBehavior || definition.Behaviors == null)
            {
                return 0;
            }

            if (definition.SupportsSingleAnimatorFastUpdate &&
                definition.SingleAnimatorFastBehaviorIndex >= 0 &&
                definition.SingleAnimatorFastBehaviorIndex < definition.Behaviors.Length)
            {
                return definition.Behaviors[definition.SingleAnimatorFastBehaviorIndex].Animator.AnimatorControllerId;
            }

            for (int i = 0; i < definition.Behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref definition.Behaviors[i];
                if (slot.Kind == BehaviorKind.Animator && slot.Animator.AnimatorControllerId > 0)
                {
                    return slot.Animator.AnimatorControllerId;
                }
            }

            return 0;
        }

        private void ReserveBatchIndexCapacity(int count, PresenterDefinition definition)
        {
            _byOwner.EnsureCapacity(_byOwner.Count + count);
            _byScope.EnsureCapacity(_byScope.Count + count);
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

        private void EnsureScopeDestroyBufferCapacity(int count)
        {
            if (_scopeDestroyBuffer.Length >= count)
            {
                return;
            }

            int capacity = _scopeDestroyBuffer.Length == 0 ? 256 : _scopeDestroyBuffer.Length;
            while (capacity < count)
            {
                capacity *= 2;
            }

            _scopeDestroyBuffer = new Entity[capacity];
        }

        private static void AddToIndex<TKey>(Dictionary<TKey, List<Entity>> index, TKey key, Entity presenter, int initialListCapacity)
            where TKey : notnull
        {
            if (!index.TryGetValue(key, out var entities))
            {
                entities = new List<Entity>(initialListCapacity);
                index[key] = entities;
            }

            entities.Add(presenter);
        }

        private static void RemoveFromIndex<TKey>(Dictionary<TKey, List<Entity>> index, TKey key, Entity presenter)
            where TKey : notnull
        {
            if (!index.TryGetValue(key, out var entities))
            {
                return;
            }

            for (int i = entities.Count - 1; i >= 0; i--)
            {
                if (entities[i] != presenter)
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

        private void AddToScopeIndex(int scopeId, Entity presenter)
        {
            ref EntityBucket bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(_byScope, scopeId, out _);
            bucket.Add(presenter);
        }

        private void RemoveFromScopeIndex(int scopeId, Entity presenter)
        {
            ref EntityBucket bucket = ref CollectionsMarshal.GetValueRefOrNullRef(_byScope, scopeId);
            if (Unsafe.IsNullRef(ref bucket) || !bucket.Remove(presenter))
            {
                return;
            }

            if (bucket.Count == 0)
            {
                _byScope.Remove(scopeId);
            }
        }

        private void AddToOwnerIndex(Entity owner, Entity presenter)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            ref OwnerPresenterBucket bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(_byOwner, new OwnerKey(owner), out _);
            bucket.Add(presenter);
            if (!_suppressOwnerPayloadMarkerWrites)
            {
                WriteOwnerPayloadMarker(owner, in bucket);
            }
        }

        private void RebuildOwnerPayloadMarkersFromIndex()
        {
            foreach (KeyValuePair<OwnerKey, OwnerPresenterBucket> entry in _byOwner)
            {
                Entity owner = entry.Key.ToEntity();
                if (owner == Entity.Null || !_world.IsAlive(owner))
                {
                    continue;
                }

                OwnerPresenterBucket bucket = entry.Value;
                WriteOwnerPayloadMarker(owner, in bucket);
            }
        }

        private void RemoveFromOwnerIndex(Entity owner, Entity presenter)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            OwnerKey key = new(owner);
            ref OwnerPresenterBucket bucket = ref CollectionsMarshal.GetValueRefOrNullRef(_byOwner, key);
            if (Unsafe.IsNullRef(ref bucket) || !bucket.Remove(presenter))
            {
                return;
            }

            if (bucket.Count == 0)
            {
                _byOwner.Remove(key);
                RemoveOwnerPayloadMarker(owner);
                return;
            }

            WriteOwnerPayloadMarker(owner, in bucket, excludedPresenter: presenter);
        }

        private void AddToOwnerDefinitionIndex(Entity owner, int defId, Entity presenter)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            ref OwnerPresenterBucket bucket = ref CollectionsMarshal.GetValueRefOrAddDefault(
                _byOwnerDefinition,
                new OwnerDefinitionKey(owner, defId),
                out _);
            bucket.Add(presenter);
        }

        private void RemoveFromOwnerDefinitionIndex(Entity owner, int defId, Entity presenter)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            OwnerDefinitionKey key = new(owner, defId);
            ref OwnerPresenterBucket bucket = ref CollectionsMarshal.GetValueRefOrNullRef(_byOwnerDefinition, key);
            if (Unsafe.IsNullRef(ref bucket) || !bucket.Remove(presenter))
            {
                return;
            }

            if (bucket.Count == 0)
            {
                _byOwnerDefinition.Remove(key);
            }
        }

        private void WriteOwnerPayloadMarker(Entity owner, in OwnerPresenterBucket bucket, Entity excludedPresenter = default)
        {
            if (owner == Entity.Null || !_world.IsAlive(owner))
            {
                return;
            }

            bool singleRoot = TryGetSingleRoot(in bucket, out Entity singleRootPresenter);
            bool hasPreviousPayload = _world.TryGet(owner, out PresentationOwnerHasPresenterPayload previousPayload);
            var next = new PresentationOwnerHasPresenterPayload
            {
                Count = bucket.Count,
                RootCount = singleRoot ? 1 : CountRoots(in bucket),
                SingleRootPresenter = singleRootPresenter,
                SingleRootTransformSync = singleRoot && OwnerPayloadTransformEligible(singleRootPresenter) ? (byte)1 : (byte)0,
            };

            if (hasPreviousPayload)
            {
                _world.Get<PresentationOwnerHasPresenterPayload>(owner) = next;
            }
            else
            {
                _world.Add(owner, next);
            }

            if (!hasPreviousPayload ||
                previousPayload.RootCount != next.RootCount ||
                previousPayload.SingleRootPresenter != next.SingleRootPresenter ||
                previousPayload.SingleRootTransformSync != next.SingleRootTransformSync)
            {
                SyncSingleRootTransformSyncMarker(previousPayload.SingleRootPresenter, excludedPresenter);
                SyncSingleRootTransformSyncMarker(next.SingleRootPresenter, excludedPresenter);
            }
        }

        private void SyncSingleRootTransformSyncMarker(Entity presenter, Entity excludedPresenter)
        {
            if (presenter == Entity.Null ||
                presenter == excludedPresenter ||
                !_world.IsAlive(presenter) ||
                !_world.Has<PresenterState>(presenter))
            {
                return;
            }

            SyncTickBehaviorMarker<PerfOwnerPayloadTransformSync>(presenter, CanUseOwnerPayloadTransformSync(presenter));
        }

        private void RemoveOwnerPayloadMarker(Entity owner)
        {
            if (owner == Entity.Null || !_world.IsAlive(owner) || !_world.Has<PresentationOwnerHasPresenterPayload>(owner))
            {
                return;
            }

            _world.Remove<PresentationOwnerHasPresenterPayload>(owner);
        }

        private bool CanUseOwnerPayloadTransformSync(Entity presenter)
        {
            if (presenter == Entity.Null ||
                !_world.IsAlive(presenter) ||
                !_world.Has<PresenterState>(presenter))
            {
                return false;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            return _world.TryGet(state.OwnerEntity, out PresentationOwnerHasPresenterPayload payload) &&
                payload.RootCount == 1 &&
                payload.SingleRootPresenter == presenter &&
                payload.SingleRootTransformSync != 0;
        }

        private bool OwnerPayloadTransformEligible(Entity presenter)
        {
            if (presenter == Entity.Null ||
                !_world.IsAlive(presenter) ||
                !_world.Has<PresenterState>(presenter) ||
                !_world.Has<PresenterTransformSource>(presenter))
            {
                return false;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            if (state.AnchorKind != PresentationAnchorKind.Entity ||
                OwnerTransformIsStatic(in state) ||
                state.OwnerEntity == Entity.Null ||
                !_world.IsAlive(state.OwnerEntity) ||
                !_world.Has<WorldPositionCm>(state.OwnerEntity) ||
                !_world.Has<VisualTransform>(state.OwnerEntity) ||
                _world.Get<PresenterTransformSource>(presenter).Value != TransformSource.EntityTransform)
            {
                return false;
            }

            return _definitions == null ||
                (_definitions.TryGet(state.DefId, out PresenterDefinition definition) &&
                 DefinitionCanUseOwnerPayloadTransformSync(definition, state.BehaviorActiveMask, depth: 0));
        }

        private bool TryGetSingleRoot(in OwnerPresenterBucket bucket, out Entity root)
        {
            root = Entity.Null;
            int rootCount = 0;
            for (int i = 0; i < bucket.Count; i++)
            {
                Entity presenter = bucket.GetAt(i);
                if (!IsEntityAnchoredRootPresenter(presenter))
                {
                    continue;
                }

                root = presenter;
                rootCount++;
                if (rootCount > 1)
                {
                    root = Entity.Null;
                    return false;
                }
            }

            return rootCount == 1;
        }

        private int CountRoots(in OwnerPresenterBucket bucket)
        {
            int count = 0;
            for (int i = 0; i < bucket.Count; i++)
            {
                if (IsEntityAnchoredRootPresenter(bucket.GetAt(i)))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsEntityAnchoredRootPresenter(Entity presenter)
        {
            if (presenter == Entity.Null ||
                !_world.IsAlive(presenter) ||
                !_world.Has<PresenterState>(presenter) ||
                _world.Get<PresenterState>(presenter).AnchorKind != PresentationAnchorKind.Entity)
            {
                return false;
            }

            return !_world.Has<PresenterParent>(presenter) ||
                   _world.Get<PresenterParent>(presenter).Parent == Entity.Null;
        }

        private void ClearOwnerPayloadMarkers()
        {
            foreach (ref Chunk chunk in _world.Query(in _ownerPayloadMarkerQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    _structuralCommands.Remove<PresentationOwnerHasPresenterPayload>(in entity);
                }
            }

            if (_structuralCommands.Size > 0)
            {
                _structuralCommands.Playback(_world);
            }
        }

        private void SyncOwnerRootCull(Entity presenter, bool ownerVisible, LODLevel ownerLod)
        {
            if (!_world.IsAlive(presenter) || !_world.Has<PresenterState>(presenter))
            {
                return;
            }

            if (_world.Has<PresenterParent>(presenter))
            {
                Entity parent = _world.Get<PresenterParent>(presenter).Parent;
                if (parent != Entity.Null && _world.IsAlive(parent) && _world.Has<PresenterCullState>(parent))
                {
                    return;
                }
            }

            SyncCullHierarchy(presenter, ownerVisible, ownerLod, parentVisible: true);
        }

        private void SyncRootCullAndMaybeMarkStaticDirty(Entity presenter, bool ownerVisible, LODLevel ownerLod)
        {
            if (!_world.IsAlive(presenter) || !_world.Has<PresenterCullState>(presenter))
            {
                return;
            }

            ref PresenterCullState cull = ref _world.Get<PresenterCullState>(presenter);
            bool changed = cull.OwnerCullVisible != ownerVisible || cull.LOD != ownerLod;
            cull.OwnerCullVisible = ownerVisible;
            cull.LOD = ownerLod;
            if (changed)
            {
                MarkStaticDirty(presenter);
            }

            if (ownerVisible && (changed || !_world.Has<PerfHasEmitWork>(presenter)))
            {
                EnsureRequestBackedEmitWorkScheduled(presenter);
            }
        }

        private void MarkStaticDirty(Entity presenter)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterEmitCache>(presenter))
            {
                return;
            }

            ref PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(presenter);
            if (_world.Has<PerfStaticStableVisual>(presenter))
            {
                MarkStaticDirty(ref emitCache);
            }

            if (_world.Has<PerfRetainedPresentationRequest>(presenter))
            {
                if (MarkRetainedPresentationRequestDirty(ref emitCache))
                {
                    AppendRetainedPresentationDirtyEntity(presenter);
                }
            }
        }

        private void MarkMaterialDirtyIfMaterialSourceParamChanged(
            Entity presenter,
            in PresenterState state,
            PresenterDefinition definition,
            int paramKey,
            ParamLane lane)
        {
            if (!definition.AffectsMaterialSourceParam(paramKey, lane) ||
                (state.BehaviorActiveMask & GetBehaviorMask(definition.MaterialBehaviorIndices, definition.Behaviors)) == 0u)
            {
                return;
            }

            if (!_world.Has<PerfMaterialDirty>(presenter))
            {
                AddMarker<PerfMaterialDirty>(presenter);
            }
        }

        private static uint GetBehaviorMask(int[] behaviorIndices, BehaviorSlot[] behaviors)
        {
            uint mask = 0u;
            if (behaviorIndices == null || behaviors == null)
            {
                return mask;
            }

            for (int i = 0; i < behaviorIndices.Length; i++)
            {
                int behaviorIndex = behaviorIndices[i];
                if ((uint)behaviorIndex >= (uint)behaviors.Length)
                {
                    continue;
                }

                int slotIndex = behaviors[behaviorIndex].SlotIndex;
                if (slotIndex is >= 0 and < 32)
                {
                    mask |= 1u << slotIndex;
                }
            }

            return mask;
        }

        private void MarkStaticDirtyIfVisualParamChanged(Entity presenter, in PresenterState state, int paramKey, ParamLane lane)
        {
            if (!_world.Has<PresenterEmitCache>(presenter) ||
                !_world.IsAlive(presenter))
            {
                return;
            }

            if (_definitions == null ||
                !_definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                return;
            }

            bool affectsStaticVisual = definition.AffectsStaticVisualParam(paramKey, lane);
            bool affectsMaterial = definition.AffectsMaterialSourceParam(paramKey, lane);
            if (!affectsStaticVisual && !affectsMaterial)
            {
                return;
            }

            if (affectsMaterial)
            {
                MarkMaterialDirtyIfMaterialSourceParamChanged(presenter, in state, definition, paramKey, lane);
            }

            if (affectsStaticVisual)
            {
                ref PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(presenter);
                if (_world.Has<PerfStaticStableVisual>(presenter))
                {
                    MarkStaticDirty(ref emitCache);
                }

                if (_world.Has<PerfRetainedPresentationRequest>(presenter))
                {
                    if (MarkRetainedPresentationRequestDirty(ref emitCache))
                    {
                        AppendRetainedPresentationDirtyEntity(presenter);
                    }
                }
            }
        }

        private void PropagateParamToAffectedChildren(Entity presenter, int paramKey, ParamLane lane,
            float floatValue, int intValue, in Vector4 vectorValue)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterChildren>(presenter) ||
                _definitions == null)
            {
                return;
            }

            ref PresenterChildren children = ref _world.Get<PresenterChildren>(presenter);
            for (int i = 0; i < children.Count; i++)
            {
                PropagateParamToAffectedChild(children.Get(i), paramKey, lane, floatValue, intValue, in vectorValue);
            }
        }

        private void PropagateParamToAffectedChild(Entity child, int paramKey, ParamLane lane,
            float floatValue, int intValue, in Vector4 vectorValue)
        {
            if (!_world.IsAlive(child) ||
                !_world.Has<PresenterState>(child) ||
                _definitions == null)
            {
                return;
            }

            ref PresenterState childState = ref _world.Get<PresenterState>(child);
            if (_definitions.TryGet(childState.DefId, out PresenterDefinition childDefinition) &&
                (childDefinition.AffectsStaticVisualParam(paramKey, lane) ||
                 childDefinition.AffectsMaterialSourceParam(paramKey, lane)))
            {
                SetParamInternal(child, paramKey, lane, floatValue, intValue, in vectorValue, propagateToChildren: false);
            }

            PropagateParamToAffectedChildren(child, paramKey, lane, floatValue, intValue, in vectorValue);
        }

        private void PropagateClearParamToAffectedChildren(Entity presenter, int paramKey, ParamLane lane)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterChildren>(presenter) ||
                _definitions == null)
            {
                return;
            }

            ref PresenterChildren children = ref _world.Get<PresenterChildren>(presenter);
            for (int i = 0; i < children.Count; i++)
            {
                PropagateClearParamToAffectedChild(children.Get(i), paramKey, lane);
            }
        }

        private void PropagateClearParamToAffectedChild(Entity child, int paramKey, ParamLane lane)
        {
            if (!_world.IsAlive(child) ||
                !_world.Has<PresenterState>(child) ||
                _definitions == null)
            {
                return;
            }

            ref PresenterState childState = ref _world.Get<PresenterState>(child);
            if (_definitions.TryGet(childState.DefId, out PresenterDefinition childDefinition) &&
                (childDefinition.AffectsStaticVisualParam(paramKey, lane) ||
                 childDefinition.AffectsMaterialSourceParam(paramKey, lane)))
            {
                ClearParamInternal(child, paramKey, lane, propagateToChildren: false);
            }

            PropagateClearParamToAffectedChildren(child, paramKey, lane);
        }

        public void ClearStaticDirty(Entity presenter)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterEmitCache>(presenter))
            {
                return;
            }

            ref PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(presenter);
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

        public void ClearConsumedRetainedPresentationDirtyEntities()
        {
            if (_retainedPresentationDirtyBufferCount == 0)
            {
                return;
            }

            Array.Clear(_retainedPresentationDirtyBuffer, 0, _retainedPresentationDirtyBufferCount);
            _retainedPresentationDirtyBufferCount = 0;
        }

        private void SyncCullHierarchy(Entity presenter, bool ownerVisible, LODLevel ownerLod, bool parentVisible)
        {
            if (!_world.IsAlive(presenter) || !_world.Has<PresenterCullState>(presenter))
            {
                return;
            }

            ref PresenterCullState cull = ref _world.Get<PresenterCullState>(presenter);
            cull.OwnerCullVisible = ownerVisible && parentVisible;
            cull.LOD = ownerLod;

            if (!_world.Has<PresenterChildren>(presenter))
            {
                return;
            }

            ref PresenterChildren children = ref _world.Get<PresenterChildren>(presenter);
            for (int i = 0; i < children.Count; i++)
            {
                SyncCullHierarchy(children.Get(i), ownerVisible, ownerLod, cull.OwnerCullVisible);
            }
        }

        private void SyncCullHierarchyAndMarkDirty(Entity presenter, bool ownerVisible, LODLevel ownerLod, bool parentVisible)
        {
            if (!_world.IsAlive(presenter) || !_world.Has<PresenterCullState>(presenter))
            {
                return;
            }

            ref PresenterCullState cull = ref _world.Get<PresenterCullState>(presenter);
            bool nextVisible = ownerVisible && parentVisible;
            bool changed = cull.OwnerCullVisible != nextVisible || cull.LOD != ownerLod;
            cull.OwnerCullVisible = nextVisible;
            cull.LOD = ownerLod;
            if (changed)
            {
                MarkStaticDirty(presenter);
            }

            if (nextVisible && (changed || !_world.Has<PerfHasEmitWork>(presenter)))
            {
                EnsureRequestBackedEmitWorkScheduled(presenter);
            }

            if (!_world.Has<PresenterChildren>(presenter))
            {
                return;
            }

            ref PresenterChildren children = ref _world.Get<PresenterChildren>(presenter);
            for (int i = 0; i < children.Count; i++)
            {
                SyncCullHierarchyAndMarkDirty(children.Get(i), ownerVisible, ownerLod, nextVisible);
            }
        }

        private void EnsureRequestBackedEmitWorkScheduled(Entity presenter)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterState>(presenter) ||
                _definitions == null)
            {
                return;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition) ||
                !DefinitionUsesRequestBackedEmitWork(definition, state.BehaviorActiveMask))
            {
                return;
            }

            if (!_world.Has<PerfHasEmitWork>(presenter))
            {
                AddMarker<PerfHasEmitWork>(presenter);
            }
        }

        public bool EnsureRequestBackedEmitWorkScheduledIfNeeded(Entity presenter)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterState>(presenter) ||
                _world.Has<PerfHasEmitWork>(presenter) ||
                _definitions == null)
            {
                return false;
            }

            ref readonly PresenterState state = ref _world.Get<PresenterState>(presenter);
            if (!_definitions.TryGet(state.DefId, out PresenterDefinition definition) ||
                !DefinitionUsesRequestBackedEmitWork(definition, state.BehaviorActiveMask))
            {
                return false;
            }

            AddMarker<PerfHasEmitWork>(presenter);
            return true;
        }

        public void MarkTransformDrivenEmitDirty(Entity presenter)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PresenterEmitCache>(presenter))
            {
                return;
            }

            ref PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(presenter);
            if (_world.Has<PerfStaticStableVisual>(presenter))
            {
                MarkStaticDirty(ref emitCache);
            }

            if (_world.Has<PerfRetainedPresentationRequest>(presenter) &&
                MarkRetainedPresentationRequestDirty(ref emitCache))
            {
                AppendRetainedPresentationDirtyEntity(presenter);
            }

            if (_world.Has<PresenterState>(presenter))
            {
                EnsureRequestBackedEmitWorkScheduled(presenter);
            }
        }

        private static bool DefinitionUsesRequestBackedEmitWork(PresenterDefinition definition, uint activeBehaviorMask)
        {
            if (definition == null ||
                definition.UsesEventDrivenStaticEmit ||
                definition.UsesRetainedPresentationRequest ||
                definition.AssetBehaviorIndices.Length == 0)
            {
                return false;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            int[] assetBehaviorIndices = definition.AssetBehaviorIndices;
            for (int i = 0; i < assetBehaviorIndices.Length; i++)
            {
                int behaviorIndex = assetBehaviorIndices[i];
                if ((uint)behaviorIndex >= (uint)behaviors.Length)
                {
                    continue;
                }

                int slotIndex = behaviors[behaviorIndex].SlotIndex;
                if (slotIndex is >= 0 and < 32 &&
                    (activeBehaviorMask & (1u << slotIndex)) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAssetOutputBehavior(BehaviorKind kind)
        {
            return kind is BehaviorKind.AssetBinding or BehaviorKind.WorldText;
        }

        private void MarkStaticDirty(ref PresenterEmitCache emitCache)
        {
            if (emitCache.StaticDirty != 0)
            {
                return;
            }

            emitCache.StaticDirty = 1;
            _dirtyStaticVisualCount++;
        }

        private void MarkRetainedPresentationRequestDirty(Entity presenter)
        {
            if (!_world.IsAlive(presenter) ||
                !_world.Has<PerfRetainedPresentationRequest>(presenter) ||
                !_world.Has<PresenterEmitCache>(presenter))
            {
                return;
            }

            ref PresenterEmitCache emitCache = ref _world.Get<PresenterEmitCache>(presenter);
            if (MarkRetainedPresentationRequestDirty(ref emitCache))
            {
                AppendRetainedPresentationDirtyEntity(presenter);
            }
        }

        private bool MarkRetainedPresentationRequestDirty(ref PresenterEmitCache emitCache)
        {
            if (emitCache.RetainedDirty != 0)
            {
                return false;
            }

            emitCache.RetainedDirty = 1;
            _dirtyRetainedPresentationRequestCount++;
            return true;
        }

        private void AppendRetainedPresentationDirtyEntity(Entity presenter)
        {
            if (_retainedPresentationDirtyBufferCount == _retainedPresentationDirtyBuffer.Length)
            {
                int nextCapacity = _retainedPresentationDirtyBuffer.Length == 0
                    ? 256
                    : _retainedPresentationDirtyBuffer.Length * 2;
                Array.Resize(ref _retainedPresentationDirtyBuffer, nextCapacity);
            }

            _retainedPresentationDirtyBuffer[_retainedPresentationDirtyBufferCount++] = presenter;
        }

        private readonly struct OwnerKey : IEquatable<OwnerKey>
        {
            private readonly int _ownerId;
            private readonly int _ownerWorldId;
            private readonly int _ownerVersion;

            public OwnerKey(Entity owner)
            {
                _ownerId = owner.Id;
                _ownerWorldId = owner.WorldId;
                _ownerVersion = owner.Version;
            }

            public Entity ToEntity()
            {
                return EntityUtil.Reconstruct(_ownerId, _ownerWorldId, _ownerVersion);
            }

            public bool Equals(OwnerKey other)
            {
                return _ownerId == other._ownerId &&
                       _ownerWorldId == other._ownerWorldId &&
                       _ownerVersion == other._ownerVersion;
            }

            public override bool Equals(object? obj)
            {
                return obj is OwnerKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(_ownerId, _ownerWorldId, _ownerVersion);
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
                _worldPosition = Vector3.Zero;
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

        internal struct OwnerPresenterBucket : IReadOnlyList<Entity>
        {
            private const int InlineCapacity = 4;
            public Entity Single;
            public Entity Inline1;
            public Entity Inline2;
            public Entity Inline3;
            public List<Entity>? Many;
            public int Count;

            readonly int IReadOnlyCollection<Entity>.Count => Count;

            public void Add(Entity presenter)
            {
                if (Count == 0)
                {
                    Single = presenter;
                    Count = 1;
                    return;
                }

                if (Count == 1)
                {
                    Inline1 = presenter;
                    Count = 2;
                    return;
                }

                if (Count == 2)
                {
                    Inline2 = presenter;
                    Count = 3;
                    return;
                }

                if (Count == 3)
                {
                    Inline3 = presenter;
                    Count = 4;
                    return;
                }

                Many ??= new List<Entity>(InlineCapacity * 2)
                {
                    Single,
                    Inline1,
                    Inline2,
                    Inline3,
                };
                Many.Add(presenter);
                Count = Many.Count;
            }

            public bool Remove(Entity presenter)
            {
                if (Count == 0)
                {
                    return false;
                }

                if (Count == 1)
                {
                    if (Single != presenter)
                    {
                        return false;
                    }

                    Single = Entity.Null;
                    Count = 0;
                    return true;
                }

                if (Many == null)
                {
                    for (int i = 0; i < Count; i++)
                    {
                        if (GetAt(i) != presenter)
                        {
                            continue;
                        }

                        RemoveInlineAt(i);
                        return true;
                    }

                    return false;
                }

                List<Entity> many = Many;
                for (int i = many.Count - 1; i >= 0; i--)
                {
                    if (many[i] != presenter)
                    {
                        continue;
                    }

                    int last = many.Count - 1;
                    many[i] = many[last];
                    many.RemoveAt(last);
                    Count--;
                    if (Count <= InlineCapacity)
                    {
                        CopyManyToInline(many);
                        Many = null;
                    }

                    return true;
                }

                return false;
            }

            public readonly bool TryGetSingle(out Entity presenter)
            {
                presenter = Single;
                return Count == 1;
            }

            public readonly Entity this[int index] => GetAt(index);

            public readonly Entity GetAt(int index)
            {
                if ((uint)index >= (uint)Count)
                {
                    return Entity.Null;
                }

                if (Many != null)
                {
                    return Many[index];
                }

                return index switch
                {
                    0 => Single,
                    1 => Inline1,
                    2 => Inline2,
                    3 => Inline3,
                    _ => Entity.Null,
                };
            }

            public void Sort(Comparison<Entity> comparison)
            {
                if (Count <= 1)
                {
                    return;
                }

                if (Many != null)
                {
                    Many.Sort(comparison);
                    return;
                }

                Span<Entity> values = stackalloc Entity[InlineCapacity];
                for (int i = 0; i < Count; i++)
                {
                    values[i] = GetAt(i);
                }

                values.Slice(0, Count).Sort(comparison);
                for (int i = 0; i < Count; i++)
                {
                    SetInline(i, values[i]);
                }
            }

            public readonly Enumerator GetEnumerator()
            {
                return new Enumerator(this);
            }

            readonly IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                {
                    yield return GetAt(i);
                }
            }

            readonly System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return ((IEnumerable<Entity>)this).GetEnumerator();
            }

            private void RemoveInlineAt(int index)
            {
                int last = Count - 1;
                Entity replacement = GetAt(last);
                SetInline(index, replacement);
                SetInline(last, Entity.Null);
                Count--;
                if (Count == 1)
                {
                    Inline1 = Entity.Null;
                    Inline2 = Entity.Null;
                    Inline3 = Entity.Null;
                }
            }

            private void CopyManyToInline(List<Entity> many)
            {
                Count = many.Count;
                Single = Count > 0 ? many[0] : Entity.Null;
                Inline1 = Count > 1 ? many[1] : Entity.Null;
                Inline2 = Count > 2 ? many[2] : Entity.Null;
                Inline3 = Count > 3 ? many[3] : Entity.Null;
                many.Clear();
            }

            private void SetInline(int index, Entity value)
            {
                switch (index)
                {
                    case 0: Single = value; break;
                    case 1: Inline1 = value; break;
                    case 2: Inline2 = value; break;
                    case 3: Inline3 = value; break;
                }
            }

            public struct Enumerator
            {
                private readonly OwnerPresenterBucket _bucket;
                private int _index;

                internal Enumerator(OwnerPresenterBucket bucket)
                {
                    _bucket = bucket;
                    _index = -1;
                    Current = Entity.Null;
                }

                public Entity Current { get; private set; }

                public bool MoveNext()
                {
                    int next = _index + 1;
                    if (next >= _bucket.Count)
                    {
                        Current = Entity.Null;
                        return false;
                    }

                    _index = next;
                    Current = _bucket.GetAt(next);
                    return true;
                }
            }
        }

        private struct EntityBucket
        {
            public Entity Single;
            public List<Entity>? Many;
            public int Count;

            public void Add(Entity entity)
            {
                if (Count == 0)
                {
                    Single = entity;
                    Count = 1;
                    return;
                }

                if (Count == 1)
                {
                    Many = new List<Entity>(4) { Single, entity };
                    Single = Entity.Null;
                    Count = 2;
                    return;
                }

                Many!.Add(entity);
                Count++;
            }

            public bool Remove(Entity entity)
            {
                if (Count == 0)
                {
                    return false;
                }

                if (Count == 1)
                {
                    if (Single != entity)
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
                    if (many[i] != entity)
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

            public readonly void CopyTo(Entity[] target)
            {
                if (Count == 0)
                {
                    return;
                }

                if (Count == 1)
                {
                    target[0] = Single;
                    return;
                }

                Many!.CopyTo(target, 0);
            }

            public readonly Entity GetAt(int index)
            {
                if ((uint)index >= (uint)Count)
                {
                    return Entity.Null;
                }

                return Count == 1 ? Single : Many![index];
            }
        }
    }
}
