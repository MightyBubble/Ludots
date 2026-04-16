using System;

namespace Ludots.Core.Presentation.Requests
{
    public sealed class PresentationRequestBuffer
    {
        private PresentationRequest[] _buffer;
        private int _count;

        public PresentationRequestBuffer(int capacity = 131072)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new PresentationRequest[capacity];
        }

        public int Count => _count;

        public int Capacity => _buffer.Length;

        public void Add(in PresentationRequest request)
        {
            if (_count >= _buffer.Length)
            {
                throw new InvalidOperationException(
                    $"PresentationRequestBuffer overflowed while adding kind={request.Kind}, stableId={request.StableId}.");
            }

            _buffer[_count++] = request;
        }

        public ReadOnlySpan<PresentationRequest> GetSpan()
        {
            return _buffer.AsSpan(0, _count);
        }

        public void Clear()
        {
            _count = 0;
        }
    }
}
