using System;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class PrimitiveDrawBuffer
    {
        private readonly PrimitiveDrawItem[] _buffer;
        private int _count;
        private int _revision;
        private int _staticMeshGeometryRevision;
        private int _staticMeshLaneItemCount;
        private int _skinnedLaneItemCount;

        public int Count => _count;
        public int Capacity => _buffer.Length;
        public int Revision => _revision;
        public int StaticMeshGeometryRevision => _staticMeshGeometryRevision;
        public int StaticMeshLaneItemCount => _staticMeshLaneItemCount;
        public int SkinnedLaneItemCount => _skinnedLaneItemCount;
        public int DroppedSinceClear { get; private set; }
        public int DroppedTotal { get; private set; }

        public PrimitiveDrawBuffer(int capacity = 8192)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new PrimitiveDrawItem[capacity];
        }

        public bool TryAdd(in PrimitiveDrawItem item)
        {
            if (_count >= _buffer.Length)
            {
                DroppedSinceClear++;
                DroppedTotal++;
                return false;
            }

            _buffer[_count++] = item;
            if (item.RenderPath.IsStaticInstanceLane())
            {
                _staticMeshLaneItemCount++;
            }
            else if (item.RenderPath.IsSkinnedLane())
            {
                _skinnedLaneItemCount++;
            }

            return true;
        }

        public ReadOnlySpan<PrimitiveDrawItem> GetSpan() => new ReadOnlySpan<PrimitiveDrawItem>(_buffer, 0, _count);

        public void SetRevision(int revision)
        {
            _revision = revision;
        }

        public void SetStaticMeshGeometryRevision(int revision)
        {
            _staticMeshGeometryRevision = revision;
        }

        public void Clear()
        {
            _count = 0;
            _staticMeshLaneItemCount = 0;
            _skinnedLaneItemCount = 0;
            DroppedSinceClear = 0;
        }
    }
}
