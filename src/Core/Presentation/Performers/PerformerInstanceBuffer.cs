using System;
using Arch.Core;

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

        // Free-list: O(1) allocation by reusing released slots.
        // Array-based stack — no heap allocation on push/pop.
        private readonly int[] _freeStack;
        private int _freeCount;

        // Per-instance parameter overrides: flat [handle * MaxOverridesPerInstance + offset]
        private const int MaxOverridesPerInstance = 8;
        private readonly int[] _overrideKeys;    // -1 = unused
        private readonly float[] _overrideValues;

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
            _slots = new PerformerInstance[capacity];
            _freeStack = new int[capacity];
            _overrideKeys = new int[capacity * MaxOverridesPerInstance];
            _overrideValues = new float[capacity * MaxOverridesPerInstance];
            Array.Fill(_overrideKeys, -1);
        }

        /// <summary>
        /// Allocate a new performer instance. Returns false if the buffer is full.
        /// The returned handle is the slot index.
        /// </summary>
        public bool TryAllocate(int defId, Entity owner, int scopeId, out int handle)
        {
            // 1. Try free-list first — O(1)
            if (_freeCount > 0)
            {
                int idx = _freeStack[--_freeCount];
                InitSlot(idx, defId, owner, scopeId);
                handle = idx;
                return true;
            }

            // 2. Append beyond high-water mark — O(1)
            if (_highWaterMark < _slots.Length)
            {
                int idx = _highWaterMark++;
                InitSlot(idx, defId, owner, scopeId);
                handle = idx;
                return true;
            }

            handle = -1;
            return false;
        }

        /// <summary>
        /// Release a single instance by handle.
        /// </summary>
        public void Release(int handle)
        {
            if (handle < 0 || handle >= _highWaterMark) return;
            if (!_slots[handle].Active) return; // guard against double-free
            _slots[handle].Active = false;
            ClearAllOverrides(handle);
            PushFree(handle);
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
        public ref PerformerInstance Get(int handle) => ref _slots[handle];

        /// <summary>
        /// Returns true if the handle points to an active instance.
        /// </summary>
        public bool IsActive(int handle)
        {
            return handle >= 0 && handle < _highWaterMark && _slots[handle].Active;
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

                callback(i, ref _slots[i]);
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
            int baseIdx = handle * MaxOverridesPerInstance;

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
        /// Try to read an imperative override for a parameter key.
        /// </summary>
        public bool TryGetParamOverride(int handle, int paramKey, out float value)
        {
            int baseIdx = handle * MaxOverridesPerInstance;
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
        /// Remove an imperative override for a specific parameter key.
        /// </summary>
        public void ClearParamOverride(int handle, int paramKey)
        {
            int baseIdx = handle * MaxOverridesPerInstance;
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
        /// Remove all active instances.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _highWaterMark; i++)
                _slots[i].Active = false;
            Array.Fill(_overrideKeys, -1);
            _highWaterMark = 0;
            _freeCount = 0;
        }

        private void InitSlot(int idx, int defId, Entity owner, int scopeId)
        {
            _slots[idx] = new PerformerInstance
            {
                DefId = defId,
                Owner = owner,
                ScopeId = scopeId,
                Elapsed = 0f,
                Active = true
            };
            ClearAllOverrides(idx);
        }

        private void ClearAllOverrides(int handle)
        {
            int baseIdx = handle * MaxOverridesPerInstance;
            for (int i = 0; i < MaxOverridesPerInstance; i++)
                _overrideKeys[baseIdx + i] = -1;
        }
    }
}
