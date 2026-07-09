using System;

namespace Ludots.Core.Gameplay.Relationships
{
    public struct RelationshipEdgeSet
    {
        private int _firstTypeId;
        private RelationshipEdge _firstEdge;
        private int[]? _extraTypeIds;
        private RelationshipEdge[]? _extraEdges;
        private int _count;

        public int Count => _count;

        public bool TryGetAt(int index, out int typeId, out RelationshipEdge edge)
        {
            if ((uint)index >= (uint)_count)
            {
                typeId = default;
                edge = default;
                return false;
            }

            if (index == 0)
            {
                typeId = _firstTypeId;
                edge = _firstEdge;
                return true;
            }

            typeId = _extraTypeIds![index - 1];
            edge = _extraEdges![index - 1];
            return true;
        }

        public bool HasType(int typeId)
        {
            return FindIndex(typeId) >= 0;
        }

        public bool TryGet(int typeId, out RelationshipEdge edge)
        {
            int index = FindIndex(typeId);
            if (index < 0)
            {
                edge = default;
                return false;
            }

            edge = index == 0 ? _firstEdge : _extraEdges![index - 1];
            return true;
        }

        public RelationshipEdge GetOrAdd(int typeId, RelationshipMetricRegistry metrics, out bool added)
        {
            int index = FindIndex(typeId);
            if (index >= 0)
            {
                added = false;
                return index == 0 ? _firstEdge : _extraEdges![index - 1];
            }

            EnsureCapacity(_count + 1);
            added = true;
            RelationshipEdge edge = RelationshipEdge.CreateDefault(metrics);
            if (_count == 0)
            {
                _firstTypeId = typeId;
                _firstEdge = edge;
            }
            else
            {
                int extraIndex = _count - 1;
                _extraTypeIds![extraIndex] = typeId;
                _extraEdges![extraIndex] = edge;
            }

            _count++;
            return edge;
        }

        public void Set(int typeId, RelationshipEdge edge)
        {
            int index = FindIndex(typeId);
            if (index < 0)
            {
                EnsureCapacity(_count + 1);
                if (_count == 0)
                {
                    _firstTypeId = typeId;
                    _firstEdge = edge;
                }
                else
                {
                    int extraIndex = _count - 1;
                    _extraTypeIds![extraIndex] = typeId;
                    _extraEdges![extraIndex] = edge;
                }

                _count++;
                return;
            }

            if (index == 0)
            {
                _firstEdge = edge;
            }
            else
            {
                _extraEdges![index - 1] = edge;
            }
        }

        public bool Remove(int typeId)
        {
            int index = FindIndex(typeId);
            if (index < 0)
            {
                return false;
            }

            int lastIndex = _count - 1;
            if (lastIndex == 0)
            {
                _firstTypeId = default;
                _firstEdge = default;
                _count = 0;
                return true;
            }

            int lastExtraIndex = lastIndex - 1;
            if (index == 0)
            {
                _firstTypeId = _extraTypeIds![lastExtraIndex];
                _firstEdge = _extraEdges![lastExtraIndex];
            }
            else if (index != lastIndex)
            {
                int extraIndex = index - 1;
                _extraTypeIds![extraIndex] = _extraTypeIds![lastExtraIndex];
                _extraEdges![extraIndex] = _extraEdges![lastExtraIndex];
            }

            _extraTypeIds![lastExtraIndex] = default;
            _extraEdges![lastExtraIndex] = default;
            _count--;
            return true;
        }

        public int CountMatching(int typeId)
        {
            if (_count == 0)
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
            if (_count == 0)
            {
                return -1;
            }

            if (_firstTypeId == typeId)
            {
                return 0;
            }

            for (int i = 1; i < _count; i++)
            {
                if (_extraTypeIds![i - 1] == typeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureCapacity(int requiredCount)
        {
            if (requiredCount <= 1)
            {
                return;
            }

            int requiredExtraCount = requiredCount - 1;
            if (_extraTypeIds == null || _extraEdges == null)
            {
                int length = Math.Max(3, requiredExtraCount);
                _extraTypeIds = new int[length];
                _extraEdges = new RelationshipEdge[length];
                return;
            }

            if (_extraTypeIds.Length >= requiredExtraCount)
            {
                return;
            }

            int newLength = Math.Max(_extraTypeIds.Length * 2, requiredExtraCount);
            Array.Resize(ref _extraTypeIds, newLength);
            Array.Resize(ref _extraEdges, newLength);
        }
    }
}
