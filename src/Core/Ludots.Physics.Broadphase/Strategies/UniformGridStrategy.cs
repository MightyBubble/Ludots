using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Physics.Broadphase.Strategies
{
    public sealed class UniformGridStrategy : ISpatialPartitionStrategy
    {
        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly int X;
            public readonly int Y;

            public CellKey(int x, int y)
            {
                X = x;
                Y = y;
            }

            public bool Equals(CellKey other) => X == other.X && Y == other.Y;

            public override bool Equals(object? obj) => obj is CellKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(X, Y);
        }

        private readonly int _cellSizeCm;
        private readonly List<RigidBodyDesc> _dynamicBodies = new();
        private readonly List<RigidBodyDesc> _staticBodies = new();
        private readonly Dictionary<CellKey, List<int>> _dynamicCells = new();
        private readonly Dictionary<CellKey, List<int>> _staticCells = new();
        private readonly Stack<List<int>> _bucketPool = new();
        private readonly HashSet<long> _pairKeys = new();
        private readonly HashSet<int> _queryBodyKeys = new();
        private int _lastPotentialPairCount;

        public UniformGridStrategy(int cellSizeCm)
        {
            if (cellSizeCm < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm), "UniformGrid cell size must be >= 1 cm.");
            }

            _cellSizeCm = cellSizeCm;
        }

        public void Build(
            ReadOnlySpan<RigidBodyDesc> dynamicBodies,
            ReadOnlySpan<RigidBodyDesc> staticBodies,
            bool rebuildStatic)
        {
            ClearCells(_dynamicCells);
            _dynamicBodies.Clear();
            _dynamicBodies.EnsureCapacity(dynamicBodies.Length);

            for (int i = 0; i < dynamicBodies.Length; i++)
            {
                _dynamicBodies.Add(dynamicBodies[i]);
                AddBodyToCells(_dynamicCells, i, in dynamicBodies[i].BoundingBox);
            }

            if (!rebuildStatic)
            {
                return;
            }

            ClearCells(_staticCells);
            _staticBodies.Clear();
            _staticBodies.EnsureCapacity(staticBodies.Length);

            for (int i = 0; i < staticBodies.Length; i++)
            {
                _staticBodies.Add(staticBodies[i]);
                AddBodyToCells(_staticCells, i, in staticBodies[i].BoundingBox);
            }
        }

        public void QueryPotentialCollisions(List<(int, int)> bodyPairs)
        {
            bodyPairs.Clear();
            _pairKeys.Clear();
            _lastPotentialPairCount = 0;

            if (_dynamicBodies.Count == 0)
            {
                return;
            }

            int staticOffset = _dynamicBodies.Count;
            for (int dynamicIndex = 0; dynamicIndex < _dynamicBodies.Count; dynamicIndex++)
            {
                RigidBodyDesc dynamicBody = _dynamicBodies[dynamicIndex];
                GetCellRange(in dynamicBody.BoundingBox, out int minX, out int maxX, out int minY, out int maxY);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        var key = new CellKey(x, y);
                        if (_dynamicCells.TryGetValue(key, out List<int>? dynamicCandidates))
                        {
                            for (int i = 0; i < dynamicCandidates.Count; i++)
                            {
                                int otherDynamicIndex = dynamicCandidates[i];
                                if (otherDynamicIndex <= dynamicIndex)
                                {
                                    continue;
                                }

                                RigidBodyDesc other = _dynamicBodies[otherDynamicIndex];
                                if (PotentialPairOverlaps(in dynamicBody.BoundingBox, in other.BoundingBox))
                                {
                                    AddPair(bodyPairs, dynamicIndex, otherDynamicIndex);
                                }
                            }
                        }

                        if (_staticCells.TryGetValue(key, out List<int>? staticCandidates))
                        {
                            for (int i = 0; i < staticCandidates.Count; i++)
                            {
                                int staticIndex = staticCandidates[i];
                                RigidBodyDesc staticBody = _staticBodies[staticIndex];
                                if (PotentialPairOverlaps(in dynamicBody.BoundingBox, in staticBody.BoundingBox))
                                {
                                    AddPair(bodyPairs, dynamicIndex, staticOffset + staticIndex);
                                }
                            }
                        }
                    }
                }
            }

            _lastPotentialPairCount = bodyPairs.Count;
        }

        public void QueryAABB(in Aabb queryArea, List<int> results)
        {
            results.Clear();
            _queryBodyKeys.Clear();

            GetCellRange(in queryArea, out int minX, out int maxX, out int minY, out int maxY);
            int staticOffset = _dynamicBodies.Count;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var key = new CellKey(x, y);
                    if (_dynamicCells.TryGetValue(key, out List<int>? dynamicCandidates))
                    {
                        for (int i = 0; i < dynamicCandidates.Count; i++)
                        {
                            int bodyIndex = dynamicCandidates[i];
                            if (_queryBodyKeys.Add(bodyIndex) &&
                                _dynamicBodies[bodyIndex].BoundingBox.Overlaps(in queryArea))
                            {
                                results.Add(bodyIndex);
                            }
                        }
                    }

                    if (_staticCells.TryGetValue(key, out List<int>? staticCandidates))
                    {
                        for (int i = 0; i < staticCandidates.Count; i++)
                        {
                            int staticBodyIndex = staticCandidates[i];
                            int resultIndex = staticOffset + staticBodyIndex;
                            if (_queryBodyKeys.Add(resultIndex) &&
                                _staticBodies[staticBodyIndex].BoundingBox.Overlaps(in queryArea))
                            {
                                results.Add(resultIndex);
                            }
                        }
                    }
                }
            }
        }

        public void Update(int bodyIndex, in Aabb newAabb)
        {
        }

        public void Remove(int bodyIndex)
        {
        }

        public SpatialMetrics GetMetrics()
        {
            return new SpatialMetrics
            {
                TotalDynamicEntities = _dynamicBodies.Count,
                PotentialPairCount = _lastPotentialPairCount,
                TreeDepth = 0,
                SceneDensity = _dynamicCells.Count + _staticCells.Count
            };
        }

        public void Clear()
        {
            ClearCells(_dynamicCells);
            ClearCells(_staticCells);
            _dynamicBodies.Clear();
            _staticBodies.Clear();
            _pairKeys.Clear();
            _queryBodyKeys.Clear();
            _lastPotentialPairCount = 0;
        }

        public void Dispose()
        {
            Clear();
            while (_bucketPool.Count > 0)
            {
                _bucketPool.Pop().Clear();
            }
        }

        private void AddPair(List<(int, int)> bodyPairs, int indexA, int indexB)
        {
            if (indexB < indexA)
            {
                (indexA, indexB) = (indexB, indexA);
            }

            long key = ((long)indexA << 32) ^ (uint)indexB;
            if (_pairKeys.Add(key))
            {
                bodyPairs.Add((indexA, indexB));
            }
        }

        private void AddBodyToCells(Dictionary<CellKey, List<int>> cells, int bodyIndex, in Aabb aabb)
        {
            GetCellRange(in aabb, out int minX, out int maxX, out int minY, out int maxY);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var key = new CellKey(x, y);
                    if (!cells.TryGetValue(key, out List<int>? bucket))
                    {
                        bucket = RentBucket();
                        cells.Add(key, bucket);
                    }

                    bucket.Add(bodyIndex);
                }
            }
        }

        private List<int> RentBucket()
        {
            return _bucketPool.Count > 0 ? _bucketPool.Pop() : new List<int>(4);
        }

        private void ClearCells(Dictionary<CellKey, List<int>> cells)
        {
            foreach (List<int> bucket in cells.Values)
            {
                bucket.Clear();
                _bucketPool.Push(bucket);
            }

            cells.Clear();
        }

        private void GetCellRange(in Aabb aabb, out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = ToCellCoord(aabb.Min.X);
            maxX = ToCellCoord(aabb.Max.X);
            minY = ToCellCoord(aabb.Min.Y);
            maxY = ToCellCoord(aabb.Max.Y);
        }

        private int ToCellCoord(Fix64 value)
        {
            return FloorDiv(value.FloorToInt(), _cellSizeCm);
        }

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
            {
                quotient--;
            }

            return quotient;
        }

        private static bool PotentialPairOverlaps(in Aabb a, in Aabb b)
        {
            return a.Min.X < b.Max.X && a.Max.X > b.Min.X &&
                   a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
        }
    }
}
