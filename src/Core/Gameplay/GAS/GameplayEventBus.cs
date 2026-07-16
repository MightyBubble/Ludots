using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// A lightweight, double-buffered EventBus for GameplayEvents to avoid Entity churn.
    /// Uses arrays instead of List to eliminate GC allocations.
    /// </summary>
    public class GameplayEventBus
    {
        private GameplayEvent[] _currentEvents = new GameplayEvent[GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME];
        private GameplayEvent[] _nextEvents = new GameplayEvent[GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME];
        private int _currentCount = 0;
        private int _nextCount = 0;
        private int _droppedInNext;
        private int _droppedLastSwap;
        private bool _nextBudgetFused;

        public int Capacity => _nextEvents.Length;
        public int AvailableNextCapacity => _nextEvents.Length - _nextCount;

        internal readonly struct WriteCheckpoint
        {
            internal readonly int NextCount;
            internal readonly int DroppedInNext;
            internal readonly bool NextBudgetFused;

            internal WriteCheckpoint(int nextCount, int droppedInNext, bool nextBudgetFused)
            {
                NextCount = nextCount;
                DroppedInNext = droppedInNext;
                NextBudgetFused = nextBudgetFused;
            }
        }

        internal WriteCheckpoint CaptureWriteCheckpoint()
        {
            return new WriteCheckpoint(_nextCount, _droppedInNext, _nextBudgetFused);
        }

        internal void RollbackWrites(in WriteCheckpoint checkpoint)
        {
            if (_nextCount < checkpoint.NextCount)
            {
                throw new InvalidOperationException("GAS.GAMEPLAY_EVENT.ERR.InvalidWriteRollback");
            }

            _nextCount = checkpoint.NextCount;
            _droppedInNext = checkpoint.DroppedInNext;
            _nextBudgetFused = checkpoint.NextBudgetFused;
        }

        public void Publish(GameplayEvent evt)
        {
            if (_nextCount >= _nextEvents.Length)
            {
                if (!_nextBudgetFused)
                {
                    _nextBudgetFused = true;
                }
                _droppedInNext++;
                return;
            }
            
            _nextEvents[_nextCount++] = evt;
        }

        public void Update()
        {
            // Swap buffers and reset counts
            var temp = _currentEvents;
            _currentEvents = _nextEvents;
            _nextEvents = temp;
            
            _currentCount = _nextCount;
            _nextCount = 0;
            _droppedLastSwap = _droppedInNext;
            _droppedInNext = 0;
            _nextBudgetFused = false;
        }

        public EventList Events => new EventList(_currentEvents, _currentCount);
        public int DroppedEventsLastUpdate => _droppedLastSwap;
        
        /// <summary>
        /// Lightweight wrapper to provide array slice access.
        /// </summary>
        public readonly struct EventList
        {
            private readonly GameplayEvent[] _array;
            private readonly int _count;
            
            public EventList(GameplayEvent[] array, int count)
            {
                _array = array;
                _count = count;
            }
            
            public GameplayEvent this[int index] => _array[index];
            public int Count => _count;
        }
    }
}
