using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Rendering;

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
        private readonly int[] _slotGenerations;
        private int _highWaterMark;

        // Free-list: O(1) allocation by reusing released slots.
        // Array-based stack — no heap allocation on push/pop.
        private readonly int[] _freeStack;
        private int _freeCount;

        // Per-instance parameter overrides: flat [handle * MaxOverridesPerInstance + offset]
        private const int MaxOverridesPerInstance = 8;
        private readonly int[] _overrideKeys;    // -1 = unused
        private readonly float[] _overrideValues;
        private const int MaxTypedFieldOverridesPerInstance = 8;
        private readonly string[] _typedFieldOverrideNames;
        private readonly PresentationTypedValue[] _typedFieldOverrideValues;

        public int Capacity => _slots.Length;

        /// <summary>
        /// Number of currently active instances.
        /// </summary>
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _highWaterMark; i++)
                    if (_slots[i].Active) count++;
                return count;
            }
        }

        public PerformerInstanceBuffer(int capacity = 256)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "PerformerInstanceBuffer capacity must be positive.");

            _slots = new PerformerInstance[capacity];
            _slotGenerations = new int[capacity];
            _freeStack = new int[capacity];
            _overrideKeys = new int[capacity * MaxOverridesPerInstance];
            _overrideValues = new float[capacity * MaxOverridesPerInstance];
            _typedFieldOverrideNames = new string[capacity * MaxTypedFieldOverridesPerInstance];
            _typedFieldOverrideValues = new PresentationTypedValue[capacity * MaxTypedFieldOverridesPerInstance];
            Array.Fill(_overrideKeys, -1);
            Array.Fill(_typedFieldOverrideNames, string.Empty);
        }

        /// <summary>
        /// Allocate a new performer instance. Returns false if the buffer is full.
        /// The returned handle is an opaque runtime handle that remains valid only
        /// for the currently active occupant of the slot.
        /// </summary>
        public bool TryAllocate(
            int defId,
            Entity owner,
            int scopeId,
            PresentationAnchorKind anchorKind,
            in Vector3 worldPosition,
            int stableId,
            out int handle)
        {
            // 1. Try free-list first — O(1)
            if (_freeCount > 0)
            {
                int idx = _freeStack[--_freeCount];
                InitSlot(idx, defId, owner, scopeId, anchorKind, worldPosition, stableId);
                handle = EncodeOpaqueHandle(idx);
                return true;
            }

            // 2. Append beyond high-water mark — O(1)
            if (_highWaterMark < _slots.Length)
            {
                int idx = _highWaterMark++;
                InitSlot(idx, defId, owner, scopeId, anchorKind, worldPosition, stableId);
                handle = EncodeOpaqueHandle(idx);
                return true;
            }

            handle = -1;
            return false;
        }

        public bool TryAllocate(int defId, Entity owner, int scopeId, out int handle)
        {
            return TryAllocate(defId, owner, scopeId, PresentationAnchorKind.Entity, Vector3.Zero, 0, out handle);
        }

        /// <summary>
        /// Release a single instance by handle.
        /// </summary>
        public void Release(int handle)
        {
            if (!TryResolveActiveSlot(handle, out int slot))
            {
                return;
            }

            _slots[slot].Active = false;
            ClearAllOverrides(slot);
            PushFree(slot);
        }

        /// <summary>
        /// Release all instances belonging to the given scope. This is an internal
        /// built-in behavior — callers do not need to write rules for cascade destroy.
        /// </summary>
        public void ReleaseScope(int scopeId)
        {
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (_slots[i].Active && _slots[i].ScopeId == scopeId)
                {
                    _slots[i].Active = false;
                    ClearAllOverrides(i);
                    PushFree(i);
                }
            }
        }

        private void PushFree(int handle)
        {
            if (_freeCount < _freeStack.Length)
                _freeStack[_freeCount++] = handle;
        }

        /// <summary>
        /// Get a reference to the instance at the given handle.
        /// </summary>
        public ref PerformerInstance Get(int handle)
        {
            if (!TryResolveActiveSlot(handle, out int slot))
            {
                throw new InvalidOperationException($"Performer handle {handle} does not resolve to an active instance.");
            }

            return ref _slots[slot];
        }

        /// <summary>
        /// Returns true if the handle points to an active instance.
        /// </summary>
        public bool IsActive(int handle)
        {
            return TryResolveActiveSlot(handle, out _);
        }

        /// <summary>
        /// Process all active instances: advance elapsed time, invoke callback,
        /// and auto-expire duration-based instances.
        /// Returns the number of active instances processed.
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

                callback(EncodeOpaqueHandle(i), ref _slots[i]);
                processed++;
            }
            return processed;
        }

        // ── Imperative Parameter Overrides ──

        /// <summary>
        /// Set an imperative parameter override. Takes priority over declarative bindings.
        /// </summary>
        public void SetParamOverride(int handle, int paramKey, float value)
        {
            if (!TryResolveActiveSlot(handle, out int slot))
            {
                return;
            }

            int baseIdx = slot * MaxOverridesPerInstance;

            // Try to find existing override for this key
            for (int i = 0; i < MaxOverridesPerInstance; i++)
            {
                if (_overrideKeys[baseIdx + i] == paramKey)
                {
                    _overrideValues[baseIdx + i] = value;
                    return;
                }
            }

            // Find free slot
            for (int i = 0; i < MaxOverridesPerInstance; i++)
            {
                if (_overrideKeys[baseIdx + i] < 0)
                {
                    _overrideKeys[baseIdx + i] = paramKey;
                    _overrideValues[baseIdx + i] = value;
                    return;
                }
            }

            // Override slots full — silently ignore (could log warning)
        }

        /// <summary>
        /// Set an imperative typed field override. Takes priority over typed field bindings.
        /// </summary>
        public void SetFieldOverride(int handle, string fieldName, in PresentationTypedValue value)
        {
            if (!TryResolveActiveSlot(handle, out int slot))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Typed performer field override requires a non-empty field name.", nameof(fieldName));
            }

            int baseIdx = slot * MaxTypedFieldOverridesPerInstance;
            for (int i = 0; i < MaxTypedFieldOverridesPerInstance; i++)
            {
                if (string.Equals(_typedFieldOverrideNames[baseIdx + i], fieldName, StringComparison.Ordinal))
                {
                    _typedFieldOverrideValues[baseIdx + i] = value;
                    return;
                }
            }

            for (int i = 0; i < MaxTypedFieldOverridesPerInstance; i++)
            {
                if (string.IsNullOrEmpty(_typedFieldOverrideNames[baseIdx + i]))
                {
                    _typedFieldOverrideNames[baseIdx + i] = fieldName;
                    _typedFieldOverrideValues[baseIdx + i] = value;
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Typed performer field override buffer is full for handle={handle}; cannot set '{fieldName}'.");
        }

        /// <summary>
        /// Try to read an imperative override for a parameter key.
        /// </summary>
        public bool TryGetParamOverride(int handle, int paramKey, out float value)
        {
            if (!TryResolveActiveSlot(handle, out int slot))
            {
                value = 0f;
                return false;
            }

            int baseIdx = slot * MaxOverridesPerInstance;
            for (int i = 0; i < MaxOverridesPerInstance; i++)
            {
                if (_overrideKeys[baseIdx + i] == paramKey)
                {
                    value = _overrideValues[baseIdx + i];
                    return true;
                }
            }
            value = 0f;
            return false;
        }

        /// <summary>
        /// Try to read an imperative typed field override.
        /// </summary>
        public bool TryGetFieldOverride(int handle, string fieldName, out PresentationTypedValue value)
        {
            if (string.IsNullOrWhiteSpace(fieldName) || !TryResolveActiveSlot(handle, out int slot))
            {
                value = default;
                return false;
            }

            int baseIdx = slot * MaxTypedFieldOverridesPerInstance;
            for (int i = 0; i < MaxTypedFieldOverridesPerInstance; i++)
            {
                if (string.Equals(_typedFieldOverrideNames[baseIdx + i], fieldName, StringComparison.Ordinal))
                {
                    value = _typedFieldOverrideValues[baseIdx + i];
                    return true;
                }
            }

            value = default;
            return false;
        }

        public int GetFieldOverrideCount(int handle)
        {
            if (!TryResolveActiveSlot(handle, out int slot))
            {
                return 0;
            }

            int count = 0;
            int baseIdx = slot * MaxTypedFieldOverridesPerInstance;
            for (int i = 0; i < MaxTypedFieldOverridesPerInstance; i++)
            {
                if (!string.IsNullOrEmpty(_typedFieldOverrideNames[baseIdx + i]))
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetFieldOverrideAt(int handle, int index, out string fieldName, out PresentationTypedValue value)
        {
            if (index < 0 || !TryResolveActiveSlot(handle, out int slot))
            {
                fieldName = string.Empty;
                value = default;
                return false;
            }

            int seen = 0;
            int baseIdx = slot * MaxTypedFieldOverridesPerInstance;
            for (int i = 0; i < MaxTypedFieldOverridesPerInstance; i++)
            {
                string name = _typedFieldOverrideNames[baseIdx + i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (seen == index)
                {
                    fieldName = name;
                    value = _typedFieldOverrideValues[baseIdx + i];
                    return true;
                }

                seen++;
            }

            fieldName = string.Empty;
            value = default;
            return false;
        }

        /// <summary>
        /// Remove an imperative override for a specific parameter key.
        /// </summary>
        public void ClearParamOverride(int handle, int paramKey)
        {
            if (!TryResolveActiveSlot(handle, out int slot))
            {
                return;
            }

            int baseIdx = slot * MaxOverridesPerInstance;
            for (int i = 0; i < MaxOverridesPerInstance; i++)
            {
                if (_overrideKeys[baseIdx + i] == paramKey)
                {
                    _overrideKeys[baseIdx + i] = -1;
                    return;
                }
            }
        }

        /// <summary>
        /// Remove an imperative typed field override.
        /// </summary>
        public void ClearFieldOverride(int handle, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName) || !TryResolveActiveSlot(handle, out int slot))
            {
                return;
            }

            int baseIdx = slot * MaxTypedFieldOverridesPerInstance;
            for (int i = 0; i < MaxTypedFieldOverridesPerInstance; i++)
            {
                if (string.Equals(_typedFieldOverrideNames[baseIdx + i], fieldName, StringComparison.Ordinal))
                {
                    _typedFieldOverrideNames[baseIdx + i] = string.Empty;
                    _typedFieldOverrideValues[baseIdx + i] = default;
                    return;
                }
            }
        }

        /// <summary>
        /// Remove all active instances.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _highWaterMark; i++)
                _slots[i].Active = false;
            Array.Fill(_overrideKeys, -1);
            Array.Fill(_typedFieldOverrideNames, string.Empty);
            Array.Clear(_typedFieldOverrideValues, 0, _typedFieldOverrideValues.Length);
            _highWaterMark = 0;
            _freeCount = 0;
        }

        /// <summary>
        /// Release active instances whose entity anchor owner is no longer alive.
        /// This prevents dead-map/session performer instances from holding slots
        /// until a later emit pass happens to observe them.
        /// </summary>
        public int ReleaseDeadEntityAnchors(World world)
        {
            int released = 0;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (!_slots[i].Active || _slots[i].AnchorKind != PresentationAnchorKind.Entity)
                    continue;

                if (world.IsAlive(_slots[i].Owner))
                    continue;

                _slots[i].Active = false;
                ClearAllOverrides(i);
                PushFree(i);
                released++;
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
            int stableId)
        {
            _slotGenerations[idx] = checked(_slotGenerations[idx] + 1);
            _slots[idx] = new PerformerInstance
            {
                DefId = defId,
                Owner = owner,
                ScopeId = scopeId,
                StableId = stableId,
                AnchorKind = anchorKind,
                WorldPosition = worldPosition,
                Elapsed = 0f,
                Active = true
            };
            ClearAllOverrides(idx);
        }

        private int EncodeOpaqueHandle(int slot)
        {
            return checked(_slotGenerations[slot] * _slots.Length + slot);
        }

        private bool TryResolveAnySlot(int handle, out int slot)
        {
            if ((uint)handle < (uint)_slots.Length)
            {
                slot = handle;
                return slot < _highWaterMark;
            }

            if (_slots.Length <= 0 || handle < _slots.Length)
            {
                slot = -1;
                return false;
            }

            int resolvedSlot = handle % _slots.Length;
            int generation = handle / _slots.Length;
            if ((uint)resolvedSlot >= (uint)_highWaterMark ||
                generation < 0 ||
                _slotGenerations[resolvedSlot] != generation)
            {
                slot = -1;
                return false;
            }

            slot = resolvedSlot;
            return true;
        }

        private bool TryResolveActiveSlot(int handle, out int slot)
        {
            if (!TryResolveAnySlot(handle, out slot))
            {
                return false;
            }

            return _slots[slot].Active;
        }

        private void ClearAllOverrides(int handle)
        {
            int baseIdx = handle * MaxOverridesPerInstance;
            for (int i = 0; i < MaxOverridesPerInstance; i++)
                _overrideKeys[baseIdx + i] = -1;

            int typedBaseIdx = handle * MaxTypedFieldOverridesPerInstance;
            for (int i = 0; i < MaxTypedFieldOverridesPerInstance; i++)
            {
                _typedFieldOverrideNames[typedBaseIdx + i] = string.Empty;
                _typedFieldOverrideValues[typedBaseIdx + i] = default;
            }
        }
    }
}
