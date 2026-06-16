using System;
using System.Collections.Generic;

namespace Ludots.Physics.Broadphase.Strategies
{
    public sealed class BruteForceStrategy : ISpatialPartitionStrategy
    {
        private readonly List<RigidBodyDesc> _dynamicBodies = new();
        private readonly List<RigidBodyDesc> _staticBodies = new();

        public void Build(
            ReadOnlySpan<RigidBodyDesc> dynamicBodies,
            ReadOnlySpan<RigidBodyDesc> staticBodies,
            bool rebuildStatic)
        {
            _dynamicBodies.Clear();
            _dynamicBodies.EnsureCapacity(dynamicBodies.Length);
            for (int i = 0; i < dynamicBodies.Length; i++)
            {
                _dynamicBodies.Add(dynamicBodies[i]);
            }

            if (!rebuildStatic)
            {
                return;
            }

            _staticBodies.Clear();
            _staticBodies.EnsureCapacity(staticBodies.Length);
            for (int i = 0; i < staticBodies.Length; i++)
            {
                _staticBodies.Add(staticBodies[i]);
            }
        }

        public void QueryPotentialCollisions(List<(int, int)> bodyPairs)
        {
            bodyPairs.Clear();

            for (int i = 0; i < _dynamicBodies.Count; i++)
            {
                for (int j = i + 1; j < _dynamicBodies.Count; j++)
                {
                    var aabbA = _dynamicBodies[i].BoundingBox;
                    var aabbB = _dynamicBodies[j].BoundingBox;
                    if (aabbA.Overlaps(in aabbB))
                    {
                        bodyPairs.Add((_dynamicBodies[i].Index, _dynamicBodies[j].Index));
                    }
                }
            }

            for (int i = 0; i < _dynamicBodies.Count; i++)
            {
                for (int j = 0; j < _staticBodies.Count; j++)
                {
                    var aabbA = _dynamicBodies[i].BoundingBox;
                    var aabbB = _staticBodies[j].BoundingBox;
                    if (aabbA.Overlaps(in aabbB))
                    {
                        bodyPairs.Add((_dynamicBodies[i].Index, _staticBodies[j].Index));
                    }
                }
            }
        }

        public void QueryAABB(in Aabb queryArea, List<int> results)
        {
            results.Clear();
            for (int i = 0; i < _dynamicBodies.Count; i++)
            {
                var body = _dynamicBodies[i];
                if (body.BoundingBox.Overlaps(in queryArea))
                {
                    results.Add(body.Index);
                }
            }

            for (int i = 0; i < _staticBodies.Count; i++)
            {
                var body = _staticBodies[i];
                if (body.BoundingBox.Overlaps(in queryArea))
                {
                    results.Add(body.Index);
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
                TotalDynamicEntities = _dynamicBodies.Count
            };
        }

        public void Clear()
        {
            _dynamicBodies.Clear();
            _staticBodies.Clear();
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
