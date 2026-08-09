using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    /// <summary>
    /// Data-driven layered-span bake capacities and contour/triangulation controls.
    /// Every operational value and scratch capacity is explicit; no defaults or fallback.
    /// </summary>
    public sealed class NavLayeredSpanConfig
    {
        public const string HeightRoundingFloorTowardNegativeInfinity = "floorTowardNegativeInfinity";
        public const string HeightRoundingRoundHalfAwayFromZero = "roundHalfAwayFromZero";

        public int ScratchSlotCount { get; set; }
        public int RasterCellSizeCm { get; set; }
        public int RasterHaloCells { get; set; }
        public int SameSurfaceToleranceCm { get; set; }
        public int MaxSimplificationErrorCm { get; set; }
        public string HeightRounding { get; set; } = string.Empty;
        public int MaxLawsonFlipCount { get; set; }

        public int ColumnCapacity { get; set; }
        public int SpanCapacity { get; set; }
        public int ClassifiedSpanCapacity { get; set; }
        public int WalkableSpanCapacity { get; set; }
        public int LinkCapacity { get; set; }
        public int SheetCapacity { get; set; }
        public int PortalIntervalCapacity { get; set; }
        public int RegionCapacity { get; set; }
        public int ChartCapacity { get; set; }
        public int RingCapacity { get; set; }
        public int ContourVertexCapacity { get; set; }
        public int ContourEdgeCapacity { get; set; }
        public int SeamCapacity { get; set; }
        public int CanonicalLinkCapacity { get; set; }
        public int SplitPointCapacity { get; set; }
        public int TriangulationVertexCapacity { get; set; }
        public int TriangulationTriangleCapacity { get; set; }
        public int ConstrainedEdgeCapacity { get; set; }
        public int BorderPortalCapacity { get; set; }
        public int PolygonVertexCapacity { get; set; }
        public int AdjacencyEdgeCapacity { get; set; }
        public int BridgeCandidateCapacity { get; set; }
        public int RingWorkCapacity { get; set; }
        public int TemporaryConstraintFlagCapacity { get; set; }

        public LayeredSpanHeightRounding ParsedHeightRounding => ParseHeightRounding(HeightRounding, "NavMeshBakeConfig.layeredSpan.heightRounding");

        public static LayeredSpanHeightRounding ParseHeightRounding(string text, string path)
        {
            if (string.Equals(text, HeightRoundingFloorTowardNegativeInfinity, StringComparison.Ordinal))
            {
                return LayeredSpanHeightRounding.FloorTowardNegativeInfinity;
            }

            if (string.Equals(text, HeightRoundingRoundHalfAwayFromZero, StringComparison.Ordinal))
            {
                return LayeredSpanHeightRounding.RoundHalfAwayFromZero;
            }

            throw new InvalidOperationException(
                $"{path} must be '{HeightRoundingFloorTowardNegativeInfinity}' or '{HeightRoundingRoundHalfAwayFromZero}'.");
        }

        public void Validate(string path = "NavMeshBakeConfig.layeredSpan")
        {
            RequirePositive(ScratchSlotCount, nameof(ScratchSlotCount), path);
            RequirePositive(RasterCellSizeCm, nameof(RasterCellSizeCm), path);
            RequireNonNegative(RasterHaloCells, nameof(RasterHaloCells), path);
            RequireNonNegative(SameSurfaceToleranceCm, nameof(SameSurfaceToleranceCm), path);
            RequireNonNegative(MaxSimplificationErrorCm, nameof(MaxSimplificationErrorCm), path);
            _ = ParsedHeightRounding;
            RequireNonNegative(MaxLawsonFlipCount, nameof(MaxLawsonFlipCount), path);

            RequirePositive(ColumnCapacity, nameof(ColumnCapacity), path);
            RequirePositive(SpanCapacity, nameof(SpanCapacity), path);
            RequirePositive(ClassifiedSpanCapacity, nameof(ClassifiedSpanCapacity), path);
            RequirePositive(WalkableSpanCapacity, nameof(WalkableSpanCapacity), path);
            RequirePositive(LinkCapacity, nameof(LinkCapacity), path);
            RequirePositive(SheetCapacity, nameof(SheetCapacity), path);
            RequirePositive(PortalIntervalCapacity, nameof(PortalIntervalCapacity), path);
            RequirePositive(RegionCapacity, nameof(RegionCapacity), path);
            RequirePositive(ChartCapacity, nameof(ChartCapacity), path);
            RequirePositive(RingCapacity, nameof(RingCapacity), path);
            RequirePositive(ContourVertexCapacity, nameof(ContourVertexCapacity), path);
            RequirePositive(ContourEdgeCapacity, nameof(ContourEdgeCapacity), path);
            RequirePositive(SeamCapacity, nameof(SeamCapacity), path);
            RequirePositive(CanonicalLinkCapacity, nameof(CanonicalLinkCapacity), path);
            RequirePositive(SplitPointCapacity, nameof(SplitPointCapacity), path);
            RequirePositive(TriangulationVertexCapacity, nameof(TriangulationVertexCapacity), path);
            RequirePositive(TriangulationTriangleCapacity, nameof(TriangulationTriangleCapacity), path);
            RequirePositive(ConstrainedEdgeCapacity, nameof(ConstrainedEdgeCapacity), path);
            RequirePositive(BorderPortalCapacity, nameof(BorderPortalCapacity), path);
            RequirePositive(PolygonVertexCapacity, nameof(PolygonVertexCapacity), path);
            RequirePositive(AdjacencyEdgeCapacity, nameof(AdjacencyEdgeCapacity), path);
            RequirePositive(BridgeCandidateCapacity, nameof(BridgeCandidateCapacity), path);
            RequirePositive(RingWorkCapacity, nameof(RingWorkCapacity), path);
            RequirePositive(TemporaryConstraintFlagCapacity, nameof(TemporaryConstraintFlagCapacity), path);
        }

        private static void RequirePositive(int value, string field, string path)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException($"{path}.{field} must be > 0.");
            }
        }

        private static void RequireNonNegative(int value, string field, string path)
        {
            if (value < 0)
            {
                throw new InvalidOperationException($"{path}.{field} must be >= 0.");
            }
        }
    }
}
