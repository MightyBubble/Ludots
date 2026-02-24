using System;
using System.Collections.Generic;

namespace Ludots.Physics.Broadphase.Strategies
{
    public sealed class BruteForceStrategy : ISpatialPartitionStrategy
    {
        private readonly List<RigidBodyDesc> _bodies = new();

        public void Build(ReadOnlySpan<RigidBodyDesc> bodies)
        {
            _bodies.Clear();
            _bodies.EnsureCapacity(bodies.Length);
            for (int i = 0; i < bodies.Length; i++)
            {
                _bodies.Add(bodies[i]);
            }
        }

        public void QueryPotentialCollisions(List<(int, int)> bodyPairs)
        {
            bodyPairs.Clear();

            for (int i = 0; i < _bodies.Count; i++)
            {
                for (int j = i + 1; j < _bodies.Count; j++)
                {
                    var aabbA = _bodies[i].BoundingBox;
                    var aabbB = _bodies[j].BoundingBox;
                    if (aabbA.Overlaps(in aabbB))
                    {
                        bodyPairs.Add((i, j));
                    }
                }
            }
        }

        public void QueryAABB(in Aabb queryArea, List<int> results)
        {
            results.Clear();
            for (int i = 0; i < _bodies.Count; i++)
            {
                var aabb = _bodies[i].BoundingBox;
                if (aabb.Overlaps(in queryArea))
                {
                    results.Add(i);
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
                TotalDynamicEntities = _bodies.Count
            };
        }

        public void Clear()
        {
            _bodies.Clear();
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
