using System;

namespace Ludots.Core.Vision
{
    public sealed class FogFieldStore
    {
        private FogField[] _fields;
        private int _count;
        private readonly int _chunkSizeCells;

        public FogFieldStore(int initialCapacity = 8, int chunkSizeCells = 16)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _fields = new FogField[initialCapacity];
            _chunkSizeCells = chunkSizeCells;
        }

        public int Count => _count;

        public FogField GetOrCreate(int scopeKeyId, in FogLayerDefinition layer)
        {
            for (int i = 0; i < _count; i++)
            {
                FogField field = _fields[i];
                if (field.ScopeKeyId == scopeKeyId && field.LayerId == layer.Id)
                {
                    return field;
                }
            }

            EnsureCapacity(_count + 1);
            FogField next = new(scopeKeyId, in layer, _chunkSizeCells);
            _fields[_count++] = next;
            return next;
        }

        public bool TryGet(int scopeKeyId, FogLayerId layerId, out FogField field)
        {
            for (int i = 0; i < _count; i++)
            {
                FogField current = _fields[i];
                if (current.ScopeKeyId == scopeKeyId && current.LayerId == layerId)
                {
                    field = current;
                    return true;
                }
            }

            field = null!;
            return false;
        }

        public int CopyFields(Span<FogField> destination)
        {
            if (destination.IsEmpty)
            {
                return 0;
            }

            int written = Math.Min(destination.Length, _count);
            for (int i = 0; i < written; i++)
            {
                destination[i] = _fields[i];
            }

            return written;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _fields.Length)
            {
                return;
            }

            int next = _fields.Length;
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref _fields, next);
        }
    }
}
