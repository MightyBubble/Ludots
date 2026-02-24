using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Physics.Broadphase.Strategies
{
    public sealed class SortAndSweepStrategy : ISpatialPartitionStrategy
    {
        private struct EndpointMarker
        {
            public Fix64 Value;
            public int BodyIndex;
            public bool IsMin;

            public EndpointMarker(Fix64 value, int bodyIndex, bool isMin)
            {
                Value = value;
                BodyIndex = bodyIndex;
                IsMin = isMin;
            }
        }

        private readonly List<RigidBodyDesc> _bodies = new();
        private readonly List<EndpointMarker> _endpoints = new();
        private readonly List<int> _activeList = new();

        public void Build(ReadOnlySpan<RigidBodyDesc> bodies)
        {
            _bodies.Clear();
            _endpoints.Clear();

            _bodies.EnsureCapacity(bodies.Length);
            _endpoints.EnsureCapacity(bodies.Length * 2);

            for (int i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                _bodies.Add(body);
                _endpoints.Add(new EndpointMarker(body.BoundingBox.Min.X, i, isMin: true));
                _endpoints.Add(new EndpointMarker(body.BoundingBox.Max.X, i, isMin: false));
            }
        }

        public void QueryPotentialCollisions(List<(int, int)> bodyPairs)
        {
            bodyPairs.Clear();
            if (_endpoints.Count == 0) return;

            _endpoints.Sort(static (a, b) => a.Value.CompareTo(b.Value));
            _activeList.Clear();

            for (int i = 0; i < _endpoints.Count; i++)
            {
                var endpoint = _endpoints[i];
                if (endpoint.IsMin)
                {
                    for (int j = 0; j < _activeList.Count; j++)
                    {
                        int activeBodyIndex = _activeList[j];
                        var bodyA = _bodies[activeBodyIndex];
                        var bodyB = _bodies[endpoint.BodyIndex];

                        if (AabbOverlapsY(in bodyA.BoundingBox, in bodyB.BoundingBox))
                        {
                            bodyPairs.Add((activeBodyIndex, endpoint.BodyIndex));
                        }
                    }

                    _activeList.Add(endpoint.BodyIndex);
                }
                else
                {
                    _activeList.Remove(endpoint.BodyIndex);
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
            _endpoints.Clear();
            _activeList.Clear();
        }

        public void Dispose()
        {
            Clear();
        }

        private static bool AabbOverlapsY(in Aabb a, in Aabb b) => a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
    }
}
