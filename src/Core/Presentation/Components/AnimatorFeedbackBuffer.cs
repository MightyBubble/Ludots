using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Presentation.Components
{
    [InlineArray(8)]
    public struct AnimatorFeedbackEventArray
    {
        private AnimatorFeedbackEvent _element0;
    }

    public struct AnimatorFeedbackBuffer
    {
        public const int MaxEvents = 8;

        private AnimatorFeedbackEventArray _events;
        public int Count;
        public int NextWriteIndex;
        public int DroppedCount;

        public readonly AnimatorFeedbackEvent GetNewest(int newestIndex)
        {
            if ((uint)newestIndex >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(newestIndex));
            }

            int slot = NextWriteIndex - 1 - newestIndex;
            if (slot < 0)
            {
                slot += MaxEvents;
            }

            return _events[slot];
        }

        public void Push(in AnimatorFeedbackEvent feedback)
        {
            if (Count < MaxEvents)
            {
                Count++;
            }
            else
            {
                DroppedCount++;
            }

            _events[NextWriteIndex] = feedback;
            NextWriteIndex++;
            if (NextWriteIndex >= MaxEvents)
            {
                NextWriteIndex = 0;
            }
        }
    }
}
