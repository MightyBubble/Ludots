using System;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Performers
{
    public sealed class PerformerAnimatorStateBuffer
    {
        private readonly AnimatorPackedState[] _packedStates;
        private readonly AnimatorRuntimeState[] _runtimeStates;
        private readonly AnimatorFeedbackBuffer[] _feedbackBuffers;
        private readonly AnimationOverlayRequest[] _overlays;
        private readonly bool[] _allocated;

        public PerformerAnimatorStateBuffer(int capacity = 256)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _packedStates = new AnimatorPackedState[capacity];
            _runtimeStates = new AnimatorRuntimeState[capacity];
            _feedbackBuffers = new AnimatorFeedbackBuffer[capacity];
            _overlays = new AnimationOverlayRequest[capacity];
            _allocated = new bool[capacity];
        }

        public int Capacity => _packedStates.Length;

        public bool IsAllocated(int handle)
        {
            ValidateHandle(handle);
            return _allocated[handle];
        }

        public void Ensure(int handle, int controllerId)
        {
            ValidateHandle(handle);
            if (!_allocated[handle] || _packedStates[handle].GetControllerId() != controllerId)
            {
                _packedStates[handle] = AnimatorPackedState.Create(controllerId);
                _runtimeStates[handle] = AnimatorRuntimeState.Create(controllerId);
                _feedbackBuffers[handle] = default;
                _overlays[handle] = default;
                _allocated[handle] = true;
            }
        }

        public void Clear(int handle)
        {
            ValidateHandle(handle);
            _packedStates[handle] = default;
            _runtimeStates[handle] = default;
            _feedbackBuffers[handle] = default;
            _overlays[handle] = default;
            _allocated[handle] = false;
        }

        public ref AnimatorPackedState GetPackedState(int handle)
        {
            ValidateAllocated(handle);
            return ref _packedStates[handle];
        }

        public ref AnimatorRuntimeState GetRuntimeState(int handle)
        {
            ValidateAllocated(handle);
            return ref _runtimeStates[handle];
        }

        public ref AnimatorFeedbackBuffer GetFeedbackBuffer(int handle)
        {
            ValidateAllocated(handle);
            return ref _feedbackBuffers[handle];
        }

        public ref AnimationOverlayRequest GetOverlay(int handle)
        {
            ValidateAllocated(handle);
            return ref _overlays[handle];
        }

        private void ValidateAllocated(int handle)
        {
            ValidateHandle(handle);
            if (!_allocated[handle])
            {
                throw new InvalidOperationException($"Performer animator state handle {handle} is not allocated.");
            }
        }

        private void ValidateHandle(int handle)
        {
            if ((uint)handle >= (uint)_packedStates.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(handle));
            }
        }
    }
}
