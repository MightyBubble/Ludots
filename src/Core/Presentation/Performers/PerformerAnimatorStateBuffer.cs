using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public sealed class PerformerAnimatorStateBuffer
    {
        private int[] _slotByEntityId;
        private AnimatorPackedState[] _packedStates;
        private AnimatorRuntimeState[] _runtimeStates;
        private AnimatorFeedbackBuffer[] _feedbackBuffers;
        private AnimationOverlayRequest[] _overlays;
        private readonly Stack<int> _freeSlots = new();
        private int _highWaterMark;

        public PerformerAnimatorStateBuffer(int capacity = 256)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _packedStates = new AnimatorPackedState[capacity];
            _runtimeStates = new AnimatorRuntimeState[capacity];
            _feedbackBuffers = new AnimatorFeedbackBuffer[capacity];
            _overlays = new AnimationOverlayRequest[capacity];
            _slotByEntityId = new int[capacity];
        }

        public bool IsAllocated(Entity entity)
        {
            return entity != Entity.Null &&
                   entity.Id >= 0 &&
                   entity.Id < _slotByEntityId.Length &&
                   _slotByEntityId[entity.Id] != 0;
        }

        public int Allocate(Entity entity, int controllerId)
        {
            return EnsureAndResolveSlot(entity, controllerId);
        }

        public void Ensure(Entity entity, int controllerId)
        {
            if (entity == Entity.Null) return;
            EnsureEntityCapacity(entity.Id);
            int encodedSlot = _slotByEntityId[entity.Id];
            if (encodedSlot != 0)
            {
                int existingSlot = encodedSlot - 1;
                if (_packedStates[existingSlot].GetControllerId() != controllerId)
                {
                    _packedStates[existingSlot] = AnimatorPackedState.Create(controllerId);
                    _runtimeStates[existingSlot] = AnimatorRuntimeState.Create(controllerId);
                    _feedbackBuffers[existingSlot] = default;
                    _overlays[existingSlot] = default;
                }
                return;
            }
            int slot = AllocateSlot();
            _slotByEntityId[entity.Id] = slot + 1;
            _packedStates[slot] = AnimatorPackedState.Create(controllerId);
            _runtimeStates[slot] = AnimatorRuntimeState.Create(controllerId);
            _feedbackBuffers[slot] = default;
            _overlays[slot] = default;
        }

        public void Clear(Entity entity)
        {
            if (entity == Entity.Null) return;
            if (entity.Id < 0 || entity.Id >= _slotByEntityId.Length) return;
            int encodedSlot = _slotByEntityId[entity.Id];
            if (encodedSlot == 0) return;
            int slot = encodedSlot - 1;
            _packedStates[slot] = default;
            _runtimeStates[slot] = default;
            _feedbackBuffers[slot] = default;
            _overlays[slot] = default;
            _freeSlots.Push(slot);
            _slotByEntityId[entity.Id] = 0;
        }

        public ref AnimatorPackedState GetPackedState(Entity entity)
        {
            return ref _packedStates[ResolveSlot(entity)];
        }

        public ref AnimatorRuntimeState GetRuntimeState(Entity entity)
        {
            return ref _runtimeStates[ResolveSlot(entity)];
        }

        public ref AnimatorFeedbackBuffer GetFeedbackBuffer(Entity entity)
        {
            return ref _feedbackBuffers[ResolveSlot(entity)];
        }

        public ref AnimationOverlayRequest GetOverlay(Entity entity)
        {
            return ref _overlays[ResolveSlot(entity)];
        }

        public ref AnimationOverlayRequest GetOverlayBySlot(int slot)
        {
            return ref _overlays[slot];
        }

        public int EnsureAndResolveSlot(Entity entity, int controllerId)
        {
            if (entity == Entity.Null)
            {
                throw new InvalidOperationException("Performer animator state cannot be allocated for a null entity.");
            }

            EnsureEntityCapacity(entity.Id);
            int encodedSlot = _slotByEntityId[entity.Id];
            if (encodedSlot != 0)
            {
                int existingSlot = encodedSlot - 1;
                if (_packedStates[existingSlot].GetControllerId() != controllerId)
                {
                    _packedStates[existingSlot] = AnimatorPackedState.Create(controllerId);
                    _runtimeStates[existingSlot] = AnimatorRuntimeState.Create(controllerId);
                    _feedbackBuffers[existingSlot] = default;
                    _overlays[existingSlot] = default;
                }

                return existingSlot;
            }

            int slot = AllocateSlot();
            _slotByEntityId[entity.Id] = slot + 1;
            _packedStates[slot] = AnimatorPackedState.Create(controllerId);
            _runtimeStates[slot] = AnimatorRuntimeState.Create(controllerId);
            _feedbackBuffers[slot] = default;
            _overlays[slot] = default;
            return slot;
        }

        public bool TryGetPackedState(Entity entity, out AnimatorPackedState state)
        {
            if (entity == Entity.Null ||
                entity.Id < 0 ||
                entity.Id >= _slotByEntityId.Length)
            {
                state = default;
                return false;
            }

            int encodedSlot = _slotByEntityId[entity.Id];
            if (encodedSlot == 0)
            {
                state = default;
                return false;
            }

            state = _packedStates[encodedSlot - 1];
            return true;
        }

        public ref AnimatorPackedState GetPackedStateBySlot(int slot)
        {
            return ref _packedStates[slot];
        }

        public ref AnimatorRuntimeState GetRuntimeStateBySlot(int slot)
        {
            return ref _runtimeStates[slot];
        }

        public ref AnimatorFeedbackBuffer GetFeedbackBufferBySlot(int slot)
        {
            return ref _feedbackBuffers[slot];
        }

        private int ResolveSlot(Entity entity)
        {
            if (entity == Entity.Null ||
                entity.Id < 0 ||
                entity.Id >= _slotByEntityId.Length ||
                _slotByEntityId[entity.Id] == 0)
            {
                throw new InvalidOperationException($"Performer animator state for entity {entity.Id} is not allocated.");
            }

            return _slotByEntityId[entity.Id] - 1;
        }

        private int AllocateSlot()
        {
            if (_freeSlots.Count > 0) return _freeSlots.Pop();
            if (_highWaterMark >= _packedStates.Length) Grow();
            return _highWaterMark++;
        }

        private void Grow()
        {
            int newCapacity = _packedStates.Length * 2;
            Array.Resize(ref _packedStates, newCapacity);
            Array.Resize(ref _runtimeStates, newCapacity);
            Array.Resize(ref _feedbackBuffers, newCapacity);
            Array.Resize(ref _overlays, newCapacity);
        }

        private void EnsureEntityCapacity(int entityId)
        {
            if (entityId < _slotByEntityId.Length)
            {
                return;
            }

            int next = _slotByEntityId.Length * 2;
            if (next <= entityId)
            {
                next = entityId + 1;
            }

            Array.Resize(ref _slotByEntityId, next);
        }
    }
}
