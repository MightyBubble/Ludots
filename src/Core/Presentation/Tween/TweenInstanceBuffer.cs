using System;

namespace Ludots.Core.Presentation.Tween
{
    public struct TweenInstance
    {
        public bool Active;
        public int ScopeId;
        public TweenTarget Target;
        public float From;
        public float To;
        public float Duration;
        public float DelayRemaining;
        public float Elapsed;
        public TweenEase Ease;
    }

    public sealed class TweenInstanceBuffer
    {
        private readonly TweenInstance[] _slots;
        private readonly int[] _freeStack;
        private int _freeCount;
        private int _highWaterMark;

        public int Capacity => _slots.Length;
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _highWaterMark; i++)
                {
                    if (_slots[i].Active)
                        count++;
                }

                return count;
            }
        }

        public TweenInstanceBuffer(int capacity = 256)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _slots = new TweenInstance[capacity];
            _freeStack = new int[capacity];
        }

        public delegate void ProcessCallback(int handle, ref TweenInstance instance);

        public bool TryAllocate(in TweenCommand command, out int handle)
        {
            if (_freeCount > 0)
            {
                int idx = _freeStack[--_freeCount];
                InitSlot(idx, in command);
                handle = idx;
                return true;
            }

            if (_highWaterMark < _slots.Length)
            {
                int idx = _highWaterMark++;
                InitSlot(idx, in command);
                handle = idx;
                return true;
            }

            handle = -1;
            return false;
        }

        public void Release(int handle)
        {
            if (handle < 0 || handle >= _highWaterMark) return;
            if (!_slots[handle].Active) return;
            _slots[handle].Active = false;
            if (_freeCount < _freeStack.Length)
                _freeStack[_freeCount++] = handle;
        }

        public void ReleaseScope(int scopeId)
        {
            if (scopeId == 0) return;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (_slots[i].Active && _slots[i].ScopeId == scopeId)
                    Release(i);
            }
        }

        public void ProcessScope(int scopeId, ProcessCallback callback)
        {
            if (scopeId == 0) return;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (_slots[i].Active && _slots[i].ScopeId == scopeId)
                    callback(i, ref _slots[i]);
            }
        }

        public int ProcessActive(float dt, ProcessCallback callback)
        {
            int processed = 0;
            for (int i = 0; i < _highWaterMark; i++)
            {
                if (!_slots[i].Active) continue;
                callback(i, ref _slots[i]);
                processed++;
            }

            return processed;
        }

        public bool IsActive(int handle)
        {
            return handle >= 0 && handle < _highWaterMark && _slots[handle].Active;
        }

        public ref TweenInstance Get(int handle) => ref _slots[handle];

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            _freeCount = 0;
            _highWaterMark = 0;
        }

        private void InitSlot(int idx, in TweenCommand command)
        {
            _slots[idx] = new TweenInstance
            {
                Active = true,
                ScopeId = command.ScopeId,
                Target = command.Target,
                From = command.From,
                To = command.To,
                Duration = command.Duration,
                DelayRemaining = command.Delay,
                Elapsed = 0f,
                Ease = command.Ease
            };
        }
    }
}
