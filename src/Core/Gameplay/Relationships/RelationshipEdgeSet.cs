using System;

namespace Ludots.Core.Gameplay.Relationships
{
    public struct RelationshipEdgeSet
    {
        private int[]? _typeIds;
        private RelationshipEdge[]? _edges;
        private int _count;

        public int Count => _count;

        public bool TryGetAt(int index, out int typeId, out RelationshipEdge edge)
        {
            if ((uint)index >= (uint)_count || _typeIds == null || _edges == null)
            {
                typeId = default;
                edge = default;
                return false;
            }

            typeId = _typeIds[index];
            edge = _edges[index];
            return true;
        }

        public bool HasType(int typeId)
        {
            return FindIndex(typeId) >= 0;
        }

        public bool TryGet(int typeId, out RelationshipEdge edge)
        {
            int index = FindIndex(typeId);
            if (index < 0 || _edges == null)
            {
                edge = default;
                return false;
            }

            edge = _edges[index];
            return true;
        }

        public RelationshipEdge GetOrAdd(int typeId, RelationshipMetricRegistry metrics, out bool added)
        {
            int index = FindIndex(typeId);
            if (index >= 0 && _edges != null)
            {
                added = false;
                return _edges[index];
            }

            EnsureCapacity(_count + 1);
            added = true;
            _typeIds![_count] = typeId;
            _edges![_count] = RelationshipEdge.CreateDefault(metrics);
            _count++;
            return _edges[_count - 1];
        }

        public void Set(int typeId, RelationshipEdge edge)
        {
            int index = FindIndex(typeId);
            if (index < 0)
            {
                EnsureCapacity(_count + 1);
                _typeIds![_count] = typeId;
                _edges![_count] = edge;
                _count++;
                return;
            }

            _edges![index] = edge;
        }

        public bool Remove(int typeId)
        {
            int index = FindIndex(typeId);
            if (index < 0 || _typeIds == null || _edges == null)
            {
                return false;
            }

            int lastIndex = _count - 1;
            if (index != lastIndex)
            {
                _typeIds[index] = _typeIds[lastIndex];
                _edges[index] = _edges[lastIndex];
            }

            _typeIds[lastIndex] = default;
            _edges[lastIndex] = default;
            _count--;
            return true;
        }

        public int CountMatching(int typeId)
        {
            if (_typeIds == null || _count == 0)
            {
                return 0;
            }

            if (typeId == RelationshipTypeRegistry.AnyTypeId)
            {
                return _count;
            }

            return FindIndex(typeId) >= 0 ? 1 : 0;
        }

        private int FindIndex(int typeId)
        {
            if (_typeIds == null)
            {
                return -1;
            }

            for (int i = 0; i < _count; i++)
            {
                if (_typeIds[i] == typeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureCapacity(int requiredCount)
        {
            if (requiredCount <= 0)
            {
                return;
            }

            if (_typeIds == null || _edges == null)
            {
                int length = Math.Max(4, requiredCount);
                _typeIds = new int[length];
                _edges = new RelationshipEdge[length];
                return;
            }

            if (_typeIds.Length >= requiredCount)
            {
                return;
            }

            int newLength = Math.Max(_typeIds.Length * 2, requiredCount);
            Array.Resize(ref _typeIds, newLength);
            Array.Resize(ref _edges, newLength);
        }
    }
}
