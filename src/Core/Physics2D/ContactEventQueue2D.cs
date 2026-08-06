using System;
using Arch.Core;
using Ludots.Core.Layers;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D
{
    public enum ContactEventType2D : byte
    {
        Begin = 0,
        End = 1
    }

    /// <summary>
    /// Contact event payload (issue #732). EntityA/EntityB follow the collision pair's
    /// canonical order (lower entity id first); Normal points from A to B as produced by
    /// the narrow phase. Begin carries the first-frame penetration; End carries the last
    /// known normal and zero penetration. Layers are captured at Begin time so End events
    /// stay meaningful even when an entity died.
    /// </summary>
    public struct ContactEvent2D
    {
        public ContactEventType2D Type;
        public Entity EntityA;
        public Entity EntityB;
        public byte ShapeSlotA;
        public byte ShapeSlotB;
        public Fix64Vec2 Normal;
        public Fix64 Penetration;
        public LayerMask LayerA;
        public LayerMask LayerB;
    }

    /// <summary>
    /// Preallocated contact event queue. Physics writes during the fixed step; gameplay
    /// reads after the physics step in the same frame and must drain (read + clear).
    /// Events never accumulate across frames by contract; an undrained queue eventually
    /// overflows and throws instead of silently dropping events.
    /// </summary>
    public sealed class ContactEventQueue2D
    {
        private readonly ContactEvent2D[] _events;
        private int _count;

        public ContactEventQueue2D(int contactEventQueueCapacity)
        {
            if (contactEventQueueCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contactEventQueueCapacity),
                    contactEventQueueCapacity,
                    "contactEventQueueCapacity must be > 0.");
            }

            Capacity = contactEventQueueCapacity;
            _events = new ContactEvent2D[contactEventQueueCapacity];
        }

        public int Capacity { get; }

        public int Count => _count;

        public ReadOnlySpan<ContactEvent2D> Events => _events.AsSpan(0, _count);

        public void Enqueue(in ContactEvent2D contactEvent)
        {
            if (_count >= Capacity)
            {
                throw new InvalidOperationException(
                    $"Contact event queue overflow: contactEventQueueCapacity={Capacity} reached. Raise 'Physics2D/kinematic.json' contactEventQueueCapacity or drain the queue every frame.");
            }

            _events[_count++] = contactEvent;
        }

        /// <summary>
        /// Drain contract: returns every queued event and clears the queue. The returned
        /// span stays valid until the next physics step writes new events.
        /// </summary>
        public ReadOnlySpan<ContactEvent2D> DrainEvents()
        {
            var drained = _events.AsSpan(0, _count);
            _count = 0;
            return drained;
        }

        public void Clear()
        {
            _count = 0;
        }
    }
}
