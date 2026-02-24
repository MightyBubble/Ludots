using System;
using System.Collections.Generic;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Physics.Broadphase
{
    public interface ISpatialPartitionStrategy : IDisposable
    {
        void Build(ReadOnlySpan<RigidBodyDesc> bodies);
        void QueryPotentialCollisions(List<(int, int)> bodyPairs);
        void QueryAABB(in Aabb queryArea, List<int> results);
        void Update(int bodyIndex, in Aabb newAabb);
        void Remove(int bodyIndex);
        SpatialMetrics GetMetrics();
        void Clear();
    }

    public struct RigidBodyDesc
    {
        public int Index;
        public int EntityIndex;
        public Aabb BoundingBox;
        public bool IsStatic;
    }

    /// <summary>
    /// 2D 轴对齐包围盒（定点数厘米）。
    /// </summary>
    public struct Aabb
    {
        public Fix64Vec2 Min;
        public Fix64Vec2 Max;

        public readonly Fix64Vec2 Center => (Min + Max) * Fix64.HalfValue;
        public readonly Fix64Vec2 Size => Max - Min;

        public readonly bool Overlaps(in Aabb other)
        {
            return Min.X <= other.Max.X && Max.X >= other.Min.X &&
                   Min.Y <= other.Max.Y && Max.Y >= other.Min.Y;
        }
    }

    public struct SpatialMetrics
    {
        public int TotalDynamicEntities;
        public float SceneDensity;
        public float UpdateTimeMs;
        public float QueryTimeMs;
        public float DistributionVariance;
        public float EmptyCellRatio;
        public int TreeDepth;
        public int PotentialPairCount;
    }
}
