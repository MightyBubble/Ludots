using System;

namespace Ludots.Core.Presentation.Requests
{
    public sealed class SoundRequestBuffer
    {
        private readonly SoundRequest[] _buffer;
        private int _count;

        public SoundRequestBuffer(int capacity = 2048)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new SoundRequest[capacity];
        }

        public int Count => _count;

        public ReadOnlySpan<SoundRequest> GetSpan()
        {
            return _buffer.AsSpan(0, _count);
        }

        public bool TryAdd(in SoundRequest request)
        {
            if (_count >= _buffer.Length)
            {
                return false;
            }

            _buffer[_count++] = request;
            return true;
        }

        public void Add(in SoundRequest request)
        {
            if (!TryAdd(in request))
            {
                throw new InvalidOperationException(
                    $"SoundRequestBuffer overflowed while adding kind={request.Kind}, stableId={request.StableId}, soundAssetId={request.SoundAssetId}.");
            }
        }

        public void Clear()
        {
            _count = 0;
        }
    }
}
