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

        private readonly List<RigidBodyDesc> _dynamicBodies = new();
        private readonly List<RigidBodyDesc> _staticBodies = new();
        private readonly List<EndpointMarker> _dynamicEndpoints = new();
        private readonly List<EndpointMarker> _staticEndpoints = new();
        private readonly List<int> _activeList = new();
        private bool _staticEndpointsSorted = true;

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
                RigidBodyDesc body = dynamicBodies[i];
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
            _staticEndpointsSorted = false;
            _staticBodies.EnsureCapacity(staticBodies.Length);
            _staticEndpoints.EnsureCapacity(staticBodies.Length * 2);

            for (int i = 0; i < staticBodies.Length; i++)
            {
                RigidBodyDesc body = staticBodies[i];
                _staticBodies.Add(body);
                _staticEndpoints.Add(new EndpointMarker(body.BoundingBox.Min.X, i, isMin: true));
                _staticEndpoints.Add(new EndpointMarker(body.BoundingBox.Max.X, i, isMin: false));
            }
        }

        public void QueryPotentialCollisions(List<(int, int)> bodyPairs)
        {
            bodyPairs.Clear();
            if (_dynamicEndpoints.Count == 0) return;

            _dynamicEndpoints.Sort(static (a, b) => a.Value.CompareTo(b.Value));
            if (!_staticEndpointsSorted)
            {
                _staticEndpoints.Sort(static (a, b) => a.Value.CompareTo(b.Value));
                _staticEndpointsSorted = true;
            }
            _activeList.Clear();

            int dynamicEndpointIndex = 0;
            int staticEndpointIndex = 0;
            while (dynamicEndpointIndex < _dynamicEndpoints.Count ||
                   staticEndpointIndex < _staticEndpoints.Count)
            {
                bool takeDynamic =
                    staticEndpointIndex >= _staticEndpoints.Count ||
                    (dynamicEndpointIndex < _dynamicEndpoints.Count &&
                     _dynamicEndpoints[dynamicEndpointIndex].Value <= _staticEndpoints[staticEndpointIndex].Value);

                EndpointMarker endpoint;
                int bodyIndex;
                if (takeDynamic)
                {
                    endpoint = _dynamicEndpoints[dynamicEndpointIndex++];
                    bodyIndex = endpoint.BodyIndex;
                }
                else
                {
                    endpoint = _staticEndpoints[staticEndpointIndex++];
                    bodyIndex = _dynamicBodies.Count + endpoint.BodyIndex;
                }

                if (endpoint.IsMin)
                {
                    for (int j = 0; j < _activeList.Count; j++)
                    {
                        int activeBodyIndex = _activeList[j];
                        if (IsStaticBodyIndex(activeBodyIndex) && IsStaticBodyIndex(bodyIndex))
                        {
                            continue;
                        }

                        RigidBodyDesc bodyA = ResolveBody(activeBodyIndex);
                        RigidBodyDesc bodyB = ResolveBody(bodyIndex);

                        if (AabbOverlapsY(in bodyA.BoundingBox, in bodyB.BoundingBox))
                        {
                            bodyPairs.Add((activeBodyIndex, bodyIndex));
                        }
                    }

                    _activeList.Add(bodyIndex);
                }
                else
                {
                    _activeList.Remove(bodyIndex);
                }
            }
        }

        public void QueryAABB(in Aabb queryArea, List<int> results)
        {
            results.Clear();
            for (int i = 0; i < _dynamicBodies.Count; i++)
            {
                var aabb = _dynamicBodies[i].BoundingBox;
                if (aabb.Overlaps(in queryArea))
                {
                    results.Add(i);
                }
            }

            int staticOffset = _dynamicBodies.Count;
            for (int i = 0; i < _staticBodies.Count; i++)
            {
                var aabb = _staticBodies[i].BoundingBox;
                if (aabb.Overlaps(in queryArea))
                {
                    results.Add(staticOffset + i);
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
            _activeList.Clear();
            _staticEndpointsSorted = true;
        }

        public void Dispose()
        {
            Clear();
        }

        private static bool AabbOverlapsY(in Aabb a, in Aabb b) => a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;

        private bool IsStaticBodyIndex(int bodyIndex)
        {
            return bodyIndex >= _dynamicBodies.Count;
        }

        private RigidBodyDesc ResolveBody(int bodyIndex)
        {
            return bodyIndex < _dynamicBodies.Count
                ? _dynamicBodies[bodyIndex]
                : _staticBodies[bodyIndex - _dynamicBodies.Count];
        }
    }
}
