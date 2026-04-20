using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    /// <summary>
    /// Fixed-capacity slot buffer for persistent performer instances.
    /// Supports Scope-based grouping and imperative parameter overrides.
    ///
    /// Modeled after <see cref="Indicators.IndicatorRequestBuffer"/> but with
    /// Scope lifecycle management and per-instance param overrides.
    /// </summary>
    public sealed class PerformerInstanceBuffer
    {
        private readonly PerformerInstance[] _slots;
        private int _highWaterMark;
        private readonly PerformerParamBlackboard _blackboard;
        private int _activeCount;
        private int _structureVersion;
        private readonly System.Collections.Generic.Dictionary<int, int> _ownerPayloadRefCounts = new System.Collections.Generic.Dictionary<int, int>();

        // Free-list: O(1) allocation by reusing released slots.
        // Array-based stack – no heap allocation on push/pop.
        private readonly int[] _freeStack;
        private int _freeCount;

        public int Capacity => _slots.Length;
        public PerformerParamBlackboard Blackboard => _blackboard;

        /// <summary>
        /// Number of currently active instances.
        /// </summary>
        public int ActiveCount
        {
            get => _activeCount;
        }

        public int StructureVersion
        {
            get => _structureVersion;
        }

        public PerformerInstanceBuffer(int capacity = 256)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "PerformerInstanceBuffer capacity must be positive.");

            _slots = new PerformerInstance[capacity];
            _blackboard = new PerformerParamBlackboard(capacity);
            _freeStack = new int[capacity];
        }

        /// <summary>
        /// Allocate a new performer instance. Returns false if the buffer is full.
        /// The returned handle is the slot index.
        /// </summary>
        public bool TryAllocate(
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition,
            int stableId,
            int parentHandle,
            out int handle)
        {
            if (parentHandle >= 0 && !IsActive(parentHandle))
            {
                handle = -1;
                return false;
            }

            // 1. Try free-list first – O(1)
            if (_freeCount > 0)
            {
                int idx = _freeStack[--_freeCount];
                InitSlot(idx, defId, owner, scopeId, anchorKind, worldPosition, stableId, parentHandle);
                handle = idx;
                return true;
            }

            // 2. Append beyond high-water mark – O(1)
            if (_highWaterMark < _slots.Length)
            {
                int idx = _highWaterMark++;
                InitSlot(idx, defId, owner, scopeId, anchorKind, worldPosition, stableId, parentHandle);
                handle = idx;
                return true;
            }

            handle = -1;
            return false;
        }

        public bool TryAllocate(int defId, Entity owner, int scopeId, out int handle)
        {
            return TryAllocate(defId, owner, scopeId, PresentationAnchorKind.Entity, Vector3.Zero, 0, -1, out handle);
        }

        public bool TryAllocate(
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition,
            int stableId,
            out int handle)
        {
            return TryAllocate(defId, owner, scopeId, anchorKind, worldPosition, stableId, -1, out handle);
        }

        /// <summary>
        /// Release a single instance by handle.
        /// </summary>
        public bool Release(int handle)
        {
            if (handle < 0 || handle >= _highWaterMark) return false;
            if (!_slots[handle].Active) return false; // guard against double-free
            ReleaseRecursive(handle, null);
            return true;
        }

        public bool Release(int handle, Action<int, PerformerInstance>? onReleased)
        {
            if (handle < 0 || handle >= _highWaterMark) return false;
            if (!_slots[handle].Active) return false;
            ReleaseRecursive(handle, onReleased);
            return true;
        }

        /// <summary>
        /// Release all instances belonging to the given scope. This is an internal
        /// built-in behavior — callers do not need to write rules for cascade destroy.
        /// </summary>
        public int ReleaseScope(int scopeId)
        {
            return ReleaseScope(scopeId, null);
        }

        public int ReleaseScope(int scopeId, Action<int, PerformerInstance>? onReleased)
        {
            int released = 0;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (_slots[i].Active && _slots[i].ScopeId == scopeId)
                {
                    released += ReleaseRecursive(i, onReleased);
                }
            }

            return released;
        }

        private void PushFree(int handle)
        {
            if (_freeCount < _freeStack.Length)
                _freeStack[_freeCount++] = handle;
        }

        /// <summary>
        /// Get a reference to the instance at the given handle.
        /// </summary>
        public ref PerformerInstance Get(int handle) => ref _slots[handle];

        /// <summary>
        /// Returns true if the handle points to an active instance.
        /// </summary>
        public bool IsActive(int handle)
        {
            return handle >= 0 && handle < _highWaterMark && _slots[handle].Active;
        }

        public bool TryGetActive(int handle, out PerformerInstance instance)
        {
            if (IsActive(handle))
            {
                instance = _slots[handle];
                return true;
            }

            instance = default;
            return false;
        }

        public bool HasOwnerPayload(Entity owner)
        {
            return owner != Entity.Null &&
                   _ownerPayloadRefCounts.TryGetValue(owner.Id, out int count) &&
                   count > 0;
        }

        /// <summary>
        /// Process all active instances. This is used by lifecycle and dirty-sync lanes,
        /// which must still update runtime state for culled owners.
        /// </summary>
        public delegate void ProcessCallback(int handle, ref PerformerInstance instance);

        public int ProcessActive(float dt, ProcessCallback callback)
        {
            int processed = 0;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (!_slots[i].Active) continue;

                // Elapsed always advances (even when dormant)
                _slots[i].Elapsed += dt;

                callback(i, ref _slots[i]);
                processed++;
            }
            return processed;
        }

        /// <summary>
        /// Process only active instances whose culling gate is open. This is the
        /// visible projection lane; do not use it for dirty sync.
        /// </summary>
        public int ProcessVisibleActive(float dt, ProcessCallback callback)
        {
            int processed = 0;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (!_slots[i].Active) continue;

                // Elapsed advances even when projection is skipped.
                _slots[i].Elapsed += dt;

                if (!_slots[i].OwnerCullVisible) continue;

                callback(i, ref _slots[i]);
                processed++;
            }
            return processed;
        }

        public int ReleaseExpired(PerformerDefinitionRegistry definitions, Action<int, PerformerInstance>? onReleased = null)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            int released = 0;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (!_slots[i].Active)
                {
                    continue;
                }

                if (!definitions.TryGet(_slots[i].DefId, out PerformerDefinition definition) ||
                    definition.DefaultLifetime <= 0f ||
                    _slots[i].Elapsed < definition.DefaultLifetime)
                {
                    continue;
                }

                released += ReleaseRecursive(i, onReleased);
            }

            return released;
        }

        /// <summary>
        /// Synchronize the per-instance hot-path culling gate from owner CullState.
        /// CullState remains the SSOT; this cache exists only to avoid repeated ECS
        /// lookups in performer behavior/emit lanes.
        /// </summary>
        public void SyncOwnerCullVisibility(World world)
        {
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (!_slots[i].Active)
                {
                    continue;
                }

                _slots[i].OwnerCullVisible = ResolveOwnerCullVisible(world, in _slots[i]);
            }

            for (int i = 0; i < _highWaterMark; i++)
            {
                if (!_slots[i].Active)
                {
                    continue;
                }

                _slots[i].OwnerCullVisible = _slots[i].OwnerCullVisible && AreParentCullGatesVisible(i);
            }
        }

        /// <summary>
        /// Set an imperative parameter override. Takes priority over declarative bindings.
        /// </summary>
        public void SetParamOverride(int handle, int paramKey, float value)
        {
            _blackboard.SetFloat(handle, paramKey, value);
        }

        /// <summary>
        /// Try to read an imperative override for a parameter key.
        /// </summary>
        public bool TryGetParamOverride(int handle, int paramKey, out float value)
        {
            return _blackboard.TryGetFloat(handle, paramKey, out value);
        }

        public bool TryResolveParamOverride(int handle, int paramKey, out float value)
        {
            return _blackboard.TryResolveFloat(handle, paramKey, out value);
        }

        /// <summary>
        /// Remove an imperative override for a specific parameter key.
        /// </summary>
        public void ClearParamOverride(int handle, int paramKey)
        {
            _blackboard.ClearFloat(handle, paramKey);
        }

        public void SetParam(int handle, int paramKey, ParamLane lane, float floatValue, int intValue, in Vector4 vectorValue)
        {
            switch (lane)
            {
                case ParamLane.Float:
                    _blackboard.SetFloat(handle, paramKey, floatValue);
                    break;
                case ParamLane.Int:
                    _blackboard.SetInt(handle, paramKey, intValue);
                    break;
                case ParamLane.Vector:
                    _blackboard.SetVector(handle, paramKey, vectorValue);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lane), lane, "Unsupported performer param lane.");
            }

            if (IsActive(handle))
            {
                _slots[handle].Version++;
            }
        }

        public void SetParamDefault(in PerformerDefinition definition, int handle)
        {
            ParamDefault[] defaults = definition.ParamDefaults;
            for (int i = 0; i < defaults.Length; i++)
            {
                ref readonly ParamDefault entry = ref defaults[i];
                switch (entry.Lane)
                {
                    case ParamLane.Float:
                        _blackboard.SetFloatDefault(handle, entry.ParamKey, entry.FloatValue);
                        break;
                    case ParamLane.Int:
                        _blackboard.SetIntDefault(handle, entry.ParamKey, entry.IntValue);
                        break;
                    case ParamLane.Vector:
                        _blackboard.SetVectorDefault(handle, entry.ParamKey, entry.VectorValue);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(entry.Lane), entry.Lane, "Unsupported performer param lane.");
                }
            }
        }

        public float ResolveFloat(int handle, int paramKey, float defaultValue = 0f)
        {
            return _blackboard.ResolveFloat(handle, paramKey, defaultValue);
        }

        public int ResolveInt(int handle, int paramKey, int defaultValue = 0)
        {
            return _blackboard.ResolveInt(handle, paramKey, defaultValue);
        }

        public Vector4 ResolveVector(int handle, int paramKey, Vector4 defaultValue)
        {
            return _blackboard.ResolveVector(handle, paramKey, defaultValue);
        }

        public int GetParentHandle(int handle)
        {
            return _blackboard.GetParent(handle);
        }

        /// <summary>
        /// Remove all active instances.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _highWaterMark; i++)
                _slots[i].Active = false;
            _blackboard.ClearAll();
            _highWaterMark = 0;
            _freeCount = 0;
            _activeCount = 0;
            _structureVersion++;
            _ownerPayloadRefCounts.Clear();
        }

        /// <summary>
        /// Release active instances whose entity anchor owner is no longer alive.
        /// This prevents dead-map/session performer instances from holding slots
        /// until a later emit pass happens to observe them.
        /// </summary>
        public int ReleaseDeadEntityAnchors(World world)
        {
            return ReleaseDeadEntityAnchors(world, null);
        }

        public int ReleaseDeadEntityAnchors(World world, Action<int, PerformerInstance>? onReleased)
        {
            int released = 0;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (!_slots[i].Active || _slots[i].AnchorKind != PresentationAnchorKind.Entity)
                    continue;

                if (world.IsAlive(_slots[i].Owner))
                    continue;

                released += ReleaseRecursive(i, onReleased);
            }

            return released;
        }

        public bool HasActiveScopedInstance(
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition)
        {
            for (int i = 0; i < _highWaterMark; i++)
            {
                ref readonly var slot = ref _slots[i];
                if (!slot.Active ||
                    slot.DefId != defId ||
                    slot.ScopeId != scopeId ||
                    slot.Owner != owner ||
                    slot.AnchorKind != anchorKind)
                {
                    continue;
                }

                if (anchorKind != PresentationAnchorKind.WorldPosition || slot.WorldPosition == worldPosition)
                    return true;
            }

            return false;
        }

        private void InitSlot(
            int idx,
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition,
            int stableId,
            int parentHandle)
        {
            _slots[idx] = new PerformerInstance
            {
                DefId = defId,
                Owner = owner,
                ScopeId = scopeId,
                StableId = stableId,
                AnchorKind = anchorKind,
                WorldPosition = worldPosition,
                WorldRotation = Quaternion.Identity,
                WorldScale = Vector3.One,
                Elapsed = 0f,
                TransformSource = parentHandle >= 0
                    ? TransformSource.InheritParent
                    : anchorKind == PresentationAnchorKind.Entity
                    ? TransformSource.EntityTransform
                    : TransformSource.WorldFixed,
                ParentHandle = parentHandle,
                FirstChildHandle = -1,
                NextSiblingHandle = -1,
                BehaviorActiveMask = 0u,
                OwnerCullVisible = true,
                Version = 1,
                Active = true
            };
            _activeCount++;
            _structureVersion++;
            AddOwnerPayloadRef(owner);
            _blackboard.ClearAll(idx);
            _blackboard.SetParent(idx, parentHandle);

            if (parentHandle >= 0)
            {
                ref PerformerInstance parent = ref _slots[parentHandle];
                _slots[idx].NextSiblingHandle = parent.FirstChildHandle;
                parent.FirstChildHandle = idx;
            }
        }

        private int ReleaseRecursive(int handle, Action<int, PerformerInstance>? onReleased)
        {
            if (!IsActive(handle))
            {
                return 0;
            }

            int released = 0;
            int child = _slots[handle].FirstChildHandle;
            while (child >= 0)
            {
                int nextChild = _slots[child].NextSiblingHandle;
                released += ReleaseRecursive(child, onReleased);
                child = nextChild;
            }

            UnlinkFromParent(handle);

            PerformerInstance snapshot = _slots[handle];
            onReleased?.Invoke(handle, snapshot);

            RemoveOwnerPayloadRef(snapshot.Owner);
            _activeCount--;
            _structureVersion++;
            _slots[handle].Active = false;
            _slots[handle].ParentHandle = -1;
            _slots[handle].FirstChildHandle = -1;
            _slots[handle].NextSiblingHandle = -1;
            _slots[handle].BehaviorActiveMask = 0u;
            _slots[handle].Version++;
            _blackboard.ClearAll(handle);
            PushFree(handle);
            return released + 1;
        }

        private void UnlinkFromParent(int handle)
        {
            int parentHandle = _slots[handle].ParentHandle;
            if (parentHandle < 0 || !IsActive(parentHandle))
            {
                return;
            }

            ref PerformerInstance parent = ref _slots[parentHandle];
            if (parent.FirstChildHandle == handle)
            {
                parent.FirstChildHandle = _slots[handle].NextSiblingHandle;
                return;
            }

            int current = parent.FirstChildHandle;
            while (current >= 0)
            {
                if (_slots[current].NextSiblingHandle == handle)
                {
                    _slots[current].NextSiblingHandle = _slots[handle].NextSiblingHandle;
                    return;
                }

                current = _slots[current].NextSiblingHandle;
            }
        }

        private void AddOwnerPayloadRef(Entity owner)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            if (_ownerPayloadRefCounts.TryGetValue(owner.Id, out int count))
            {
                _ownerPayloadRefCounts[owner.Id] = count + 1;
                return;
            }

            _ownerPayloadRefCounts.Add(owner.Id, 1);
        }

        private void RemoveOwnerPayloadRef(Entity owner)
        {
            if (owner == Entity.Null)
            {
                return;
            }

            if (!_ownerPayloadRefCounts.TryGetValue(owner.Id, out int count))
            {
                return;
            }

            if (count <= 1)
            {
                _ownerPayloadRefCounts.Remove(owner.Id);
                return;
            }

            _ownerPayloadRefCounts[owner.Id] = count - 1;
        }

        private static bool ResolveOwnerCullVisible(World world, in PerformerInstance instance)
        {
            if (instance.AnchorKind != PresentationAnchorKind.Entity)
            {
                return true;
            }

            if (!world.IsAlive(instance.Owner))
            {
                return false;
            }

            return !world.Has<CullState>(instance.Owner) || world.Get<CullState>(instance.Owner).IsVisible;
        }

        private bool AreParentCullGatesVisible(int handle)
        {
            int current = _slots[handle].ParentHandle;
            while (current >= 0)
            {
                if (!IsActive(current))
                {
                    return false;
                }

                if (!_slots[current].OwnerCullVisible)
                {
                    return false;
                }

                current = _slots[current].ParentHandle;
            }

            return true;
        }
    }
}
