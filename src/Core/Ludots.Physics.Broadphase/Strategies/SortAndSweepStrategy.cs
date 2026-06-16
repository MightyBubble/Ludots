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
            public int BodyListIndex;
            public bool IsMin;

            public EndpointMarker(Fix64 value, int bodyListIndex, bool isMin)
            {
                Value = value;
                BodyListIndex = bodyListIndex;
                IsMin = isMin;
            }
        }

        private readonly List<RigidBodyDesc> _dynamicBodies = new();
        private readonly List<RigidBodyDesc> _staticBodies = new();
        private readonly List<EndpointMarker> _dynamicEndpoints = new();
        private readonly List<EndpointMarker> _staticEndpoints = new();
        private readonly List<int> _activeDynamicList = new();

        public void Build(
            ReadOnlySpan<RigidBodyDesc> dynamicBodies,
            ReadOnlySpan<RigidBodyDesc> staticBodies,
            bool rebuildStatic)
        {
            _dynamicBodies.Clear();
            _dynamicEndpoints.Clear();

            _dynamicBodies.EnsureCapacity(dynamicBodies.Length);
            _dynamicEndpoints.EnsureCapacity(dynamicBodies.Length * 2);
            for (int i = 0; i < dynamicBodies.Length; i++)
            {
                var body = dynamicBodies[i];
                _dynamicBodies.Add(body);
                _dynamicEndpoints.Add(new EndpointMarker(body.BoundingBox.Min.X, i, isMin: true));
                _dynamicEndpoints.Add(new EndpointMarker(body.BoundingBox.Max.X, i, isMin: false));
            }

            if (!rebuildStatic)
            {
                return;
            }

            _staticBodies.Clear();
            _staticEndpoints.Clear();
            _staticBodies.EnsureCapacity(staticBodies.Length);
            _staticEndpoints.EnsureCapacity(staticBodies.Length * 2);
            for (int i = 0; i < staticBodies.Length; i++)
            {
                var body = staticBodies[i];
                _staticBodies.Add(body);
                _staticEndpoints.Add(new EndpointMarker(body.BoundingBox.Min.X, i, isMin: true));
                _staticEndpoints.Add(new EndpointMarker(body.BoundingBox.Max.X, i, isMin: false));
            }

            _staticEndpoints.Sort(static (a, b) => a.Value.CompareTo(b.Value));
        }

        public void QueryPotentialCollisions(List<(int, int)> bodyPairs)
        {
            bodyPairs.Clear();
            if (_dynamicEndpoints.Count == 0)
            {
                return;
            }

            QueryDynamicDynamic(bodyPairs);
            QueryDynamicStatic(bodyPairs);
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
            _dynamicEndpoints.Clear();
            _staticEndpoints.Clear();
            _activeDynamicList.Clear();
        }

        public void Dispose()
        {
            Clear();
        }

        private void QueryDynamicDynamic(List<(int, int)> bodyPairs)
        {
            _dynamicEndpoints.Sort(static (a, b) => a.Value.CompareTo(b.Value));
            _activeDynamicList.Clear();

            for (int i = 0; i < _dynamicEndpoints.Count; i++)
            {
                var endpoint = _dynamicEndpoints[i];
                if (endpoint.IsMin)
                {
                    for (int j = 0; j < _activeDynamicList.Count; j++)
                    {
                        int activeBodyIndex = _activeDynamicList[j];
                        var bodyA = _dynamicBodies[activeBodyIndex];
                        var bodyB = _dynamicBodies[endpoint.BodyListIndex];

                        if (AabbOverlapsY(in bodyA.BoundingBox, in bodyB.BoundingBox))
                        {
                            bodyPairs.Add((bodyA.Index, bodyB.Index));
                        }
                    }

                    _activeDynamicList.Add(endpoint.BodyListIndex);
                }
                else
                {
                    _activeDynamicList.Remove(endpoint.BodyListIndex);
                }
            }
        }

        private void QueryDynamicStatic(List<(int, int)> bodyPairs)
        {
            if (_staticEndpoints.Count == 0)
            {
                return;
            }

            for (int dynamicIndex = 0; dynamicIndex < _dynamicBodies.Count; dynamicIndex++)
            {
                var dynamicBody = _dynamicBodies[dynamicIndex];
                for (int endpointIndex = 0; endpointIndex < _staticEndpoints.Count; endpointIndex++)
                {
                    EndpointMarker endpoint = _staticEndpoints[endpointIndex];
                    if (endpoint.Value > dynamicBody.BoundingBox.Max.X)
                    {
                        break;
                    }

                    if (!endpoint.IsMin)
                    {
                        continue;
                    }

                    var staticBody = _staticBodies[endpoint.BodyListIndex];
                    if (staticBody.BoundingBox.Max.X < dynamicBody.BoundingBox.Min.X)
                    {
                        continue;
                    }

                    if (dynamicBody.BoundingBox.Overlaps(in staticBody.BoundingBox))
                    {
                        bodyPairs.Add((dynamicBody.Index, staticBody.Index));
                    }
                }
            }
        }

        private static bool AabbOverlapsY(in Aabb a, in Aabb b) => a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
    }
}
