using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public sealed class PerformerAnimatorStateBuffer
    {
        private readonly Dictionary<int, int> _entityToSlot = new();
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
        }

        public bool IsAllocated(Entity entity)
        {
            return entity != Entity.Null && _entityToSlot.ContainsKey(entity.Id);
        }

        public void Ensure(Entity entity, int controllerId)
        {
            if (entity == Entity.Null) return;
            if (_entityToSlot.TryGetValue(entity.Id, out int slot))
            {
                if (_packedStates[slot].GetControllerId() != controllerId)
                {
                    _packedStates[slot] = AnimatorPackedState.Create(controllerId);
                    _runtimeStates[slot] = AnimatorRuntimeState.Create(controllerId);
                    _feedbackBuffers[slot] = default;
                    _overlays[slot] = default;
                }
                return;
            }
            slot = AllocateSlot();
            _entityToSlot[entity.Id] = slot;
            _packedStates[slot] = AnimatorPackedState.Create(controllerId);
            _runtimeStates[slot] = AnimatorRuntimeState.Create(controllerId);
            _feedbackBuffers[slot] = default;
            _overlays[slot] = default;
        }

        public void Clear(Entity entity)
        {
            if (entity == Entity.Null) return;
            if (!_entityToSlot.TryGetValue(entity.Id, out int slot)) return;
            _packedStates[slot] = default;
            _runtimeStates[slot] = default;
            _feedbackBuffers[slot] = default;
            _overlays[slot] = default;
            _freeSlots.Push(slot);
            _entityToSlot.Remove(entity.Id);
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

        private int ResolveSlot(Entity entity)
        {
            if (entity == Entity.Null || !_entityToSlot.TryGetValue(entity.Id, out int slot))
                throw new InvalidOperationException($"Performer animator state for entity {entity.Id} is not allocated.");
            return slot;
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
    }
}
