using System;
using Arch.Core;

namespace Ludots.Core.Presentation.Events
{
    public struct GlobalPresentationEvent
    {
        public PresentationEventKind Kind;
        public int KeyId;
        public Entity Source;
        public Entity Target;
        public float Magnitude;
        public int PayloadA;
        public int PayloadB;
    }

    public sealed class GlobalPresentationEventBuffer
    {
        private readonly GlobalPresentationEvent[] _buffer;
        private int _count;

        public GlobalPresentationEventBuffer(int capacity = 256)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new GlobalPresentationEvent[capacity];
        }

        public int Count => _count;

        public ReadOnlySpan<GlobalPresentationEvent> GetSpan()
        {
            return _buffer.AsSpan(0, _count);
        }

        public bool TryAdd(in GlobalPresentationEvent evt)
        {
            ValidateKind(evt.Kind);
            if (_count >= _buffer.Length)
            {
                return false;
            }

            _buffer[_count++] = evt;
            return true;
        }

        public void AddDayNight(int keyId, float phase01)
        {
            Add(new GlobalPresentationEvent
            {
                Kind = PresentationEventKind.GlobalDayNight,
                KeyId = keyId,
                Magnitude = phase01,
            });
        }

        public void AddRegionChanged(int regionId, int previousRegionId = 0)
        {
            Add(new GlobalPresentationEvent
            {
                Kind = PresentationEventKind.GlobalRegionChanged,
                KeyId = regionId,
                PayloadA = previousRegionId,
            });
        }

        public void AddWeather(int weatherId, float intensity)
        {
            Add(new GlobalPresentationEvent
            {
                Kind = PresentationEventKind.GlobalWeather,
                KeyId = weatherId,
                Magnitude = intensity,
            });
        }

        public void Clear()
        {
            _count = 0;
        }

        private void Add(in GlobalPresentationEvent evt)
        {
            if (!TryAdd(in evt))
            {
                throw new InvalidOperationException($"GlobalPresentationEventBuffer overflowed while adding kind={evt.Kind}, keyId={evt.KeyId}.");
            }
        }

        private static void ValidateKind(PresentationEventKind kind)
        {
            if (kind is not (PresentationEventKind.GlobalDayNight or PresentationEventKind.GlobalRegionChanged or PresentationEventKind.GlobalWeather))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Global presentation events are limited to GlobalDayNight, GlobalRegionChanged, and GlobalWeather.");
            }
        }
    }
}
