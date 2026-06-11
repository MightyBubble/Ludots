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

        // Free-list: O(1) allocation by reusing released slots.
        // Array-based stack — no heap allocation on push/pop.
        private readonly int[] _freeStack;
        private int _freeCount;

        // Per-instance parameter overrides: flat [handle * MaxOverridesPerInstance + offset]
        private const int MaxOverridesPerInstance = 8;
        private readonly int[] _overrideKeys;    // -1 = unused
        private readonly float[] _overrideValues;

        private readonly int _animatorSlotsPerInstance;
        private readonly int[] _animatorBehaviorSlots;
        private readonly AnimatorPackedState[] _animatorPackedStates;
        private readonly AnimatorRuntimeState[] _animatorRuntimeStates;
        private readonly AnimatorParameterBuffer[] _animatorParameterBuffers;
        private readonly AnimationOverlayRequest[] _animationOverlayRequests;
        private readonly AnimatorFeedbackBuffer[] _animatorFeedbackBuffers;

        public int Capacity => _slots.Length;

        public int AnimatorSlotsPerInstance => _animatorSlotsPerInstance;

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

        public PerformerInstanceBuffer(int capacity = 256, int animatorSlotsPerInstance = 1)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "PerformerInstanceBuffer capacity must be positive.");
            if (animatorSlotsPerInstance <= 0)
                throw new ArgumentOutOfRangeException(nameof(animatorSlotsPerInstance), "Performer animator slots per instance must be positive.");

            _slots = new PerformerInstance[capacity];
            _freeStack = new int[capacity];
            _overrideKeys = new int[capacity * MaxOverridesPerInstance];
            _overrideValues = new float[capacity * MaxOverridesPerInstance];
            _animatorSlotsPerInstance = animatorSlotsPerInstance;
            int animatorCapacity = capacity * animatorSlotsPerInstance;
            _animatorBehaviorSlots = new int[animatorCapacity];
            _animatorPackedStates = new AnimatorPackedState[animatorCapacity];
            _animatorRuntimeStates = new AnimatorRuntimeState[animatorCapacity];
            _animatorParameterBuffers = new AnimatorParameterBuffer[animatorCapacity];
            _animationOverlayRequests = new AnimationOverlayRequest[animatorCapacity];
            _animatorFeedbackBuffers = new AnimatorFeedbackBuffer[animatorCapacity];
            Array.Fill(_overrideKeys, -1);
            Array.Fill(_animatorBehaviorSlots, -1);
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
            out int handle)
        {
            // 1. Try free-list first — O(1)
            if (_freeCount > 0)
            {
                int idx = _freeStack[--_freeCount];
                InitSlot(idx, defId, owner, scopeId, anchorKind, worldPosition, stableId);
                handle = idx;
                return true;
            }

            // 2. Append beyond high-water mark — O(1)
            if (_highWaterMark < _slots.Length)
            {
                int idx = _highWaterMark++;
                InitSlot(idx, defId, owner, scopeId, anchorKind, worldPosition, stableId);
                handle = idx;
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
            if (handle < 0 || handle >= _highWaterMark) return;
            if (!_slots[handle].Active) return; // guard against double-free
            _slots[handle].Active = false;
            ClearAllOverrides(handle);
            ClearAnimatorSlots(handle);
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
                    ClearAnimatorSlots(i);
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

        public delegate void AnimatorSlotProcessCallback(
            int handle,
            ref PerformerInstance instance,
            int behaviorSlot,
            ref AnimatorPackedState packed,
            ref AnimatorRuntimeState runtime,
            ref AnimatorParameterBuffer parameters,
            ref AnimationOverlayRequest overlay,
            ref AnimatorFeedbackBuffer feedback,
            float dt);

        public int ProcessAnimatorSlots(float dt, AnimatorSlotProcessCallback callback)
        {
            int processed = 0;
            for (int handle = 0; handle < _highWaterMark; handle++)
            {
                if (!_slots[handle].Active)
                    continue;

                int baseIdx = GetAnimatorBaseIndex(handle);
                for (int slot = 0; slot < _animatorSlotsPerInstance; slot++)
                {
                    int idx = baseIdx + slot;
                    int behaviorSlot = _animatorBehaviorSlots[idx];
                    if (behaviorSlot < 0)
                        continue;

                    callback(
                        handle,
                        ref _slots[handle],
                        behaviorSlot,
                        ref _animatorPackedStates[idx],
                        ref _animatorRuntimeStates[idx],
                        ref _animatorParameterBuffers[idx],
                        ref _animationOverlayRequests[idx],
                        ref _animatorFeedbackBuffers[idx],
                        dt);
                    processed++;
                }
            }

            return processed;
        }

        public void InitializeAnimatorSlots(int handle, PerformerDefinition definition)
        {
            if (!IsActive(handle))
                throw new ArgumentOutOfRangeException(nameof(handle), "Cannot initialize animator slots for an inactive performer instance.");
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            ClearAnimatorSlots(handle);

            int count = 0;
            for (int i = 0; i < definition.Behaviors.Length; i++)
            {
                ref readonly BehaviorSlot behavior = ref definition.Behaviors[i];
                if (behavior.Kind != BehaviorKind.Animator)
                    continue;

                if (count >= _animatorSlotsPerInstance)
                {
                    throw new InvalidOperationException(
                        $"Performer '{definition.Key}' declares more Animator behavior slots than PerformerInstanceBuffer capacity per instance ({_animatorSlotsPerInstance}). Increase presentation.performerAnimatorSlotsPerInstance.");
                }

                int idx = GetAnimatorBaseIndex(handle) + count;
                _animatorBehaviorSlots[idx] = behavior.SlotIndex;
                _animatorPackedStates[idx] = AnimatorPackedState.Create(behavior.Animator.AnimatorControllerId);
                _animatorRuntimeStates[idx] = AnimatorRuntimeState.Create(behavior.Animator.AnimatorControllerId);
                _animatorParameterBuffers[idx] = default;
                _animationOverlayRequests[idx] = default;
                _animatorFeedbackBuffers[idx] = default;
                count++;
            }
        }

        public bool TryReadAnimatorSlot(
            int handle,
            int behaviorSlot,
            out AnimatorPackedState packed,
            out AnimationOverlayRequest overlay)
        {
            if (handle < 0 || handle >= _highWaterMark || !_slots[handle].Active)
            {
                packed = default;
                overlay = default;
                return false;
            }

            int idx = FindAnimatorSlotIndex(handle, behaviorSlot);
            if (idx < 0)
            {
                packed = default;
                overlay = default;
                return false;
            }

            packed = _animatorPackedStates[idx];
            overlay = _animationOverlayRequests[idx];
            return true;
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
                ClearAnimatorSlots(i);
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
            ClearAnimatorSlots(idx);
        }

        private void ClearAllOverrides(int handle)
        {
            int baseIdx = handle * MaxOverridesPerInstance;
            for (int i = 0; i < MaxOverridesPerInstance; i++)
                _overrideKeys[baseIdx + i] = -1;
        }

        private int GetAnimatorBaseIndex(int handle)
        {
            return handle * _animatorSlotsPerInstance;
        }

        private int FindAnimatorSlotIndex(int handle, int behaviorSlot)
        {
            int baseIdx = GetAnimatorBaseIndex(handle);
            for (int i = 0; i < _animatorSlotsPerInstance; i++)
            {
                int idx = baseIdx + i;
                if (_animatorBehaviorSlots[idx] == behaviorSlot)
                    return idx;
            }

            return -1;
        }

        private void ClearAnimatorSlots(int handle)
        {
            int baseIdx = GetAnimatorBaseIndex(handle);
            for (int i = 0; i < _animatorSlotsPerInstance; i++)
            {
                int idx = baseIdx + i;
                _animatorBehaviorSlots[idx] = -1;
                _animatorPackedStates[idx] = default;
                _animatorRuntimeStates[idx] = default;
                _animatorParameterBuffers[idx] = default;
                _animationOverlayRequests[idx] = default;
                _animatorFeedbackBuffers[idx] = default;
            }
        }
    }
}
