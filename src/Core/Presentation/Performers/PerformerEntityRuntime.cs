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
            return entity;
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
            bool found = false;
            var query = new QueryDescription().WithAll<PerformerState, PerformerWorldPosition>();
            _world.Query(in query, (Entity entity, ref PerformerState state, ref PerformerWorldPosition pos) =>
            {
                if (found) return;
                if (state.DefId != defId || state.ScopeId != scopeId ||
                    state.OwnerEntity != owner || state.AnchorKind != anchorKind)
                    return;
                if (anchorKind != PresentationAnchorKind.WorldPosition || pos.Value == worldPosition)
                    found = true;
            });
            return found;
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
    }
}
