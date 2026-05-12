using System;

namespace SpatialBoundsShowcaseMod
{
    internal static class SpatialBoundsShowcaseIds
    {
        public const string ShowcaseMapId = "spatial_bounds_showcase";

        public static bool IsShowcaseMap(string? mapId)
        {
            return string.Equals(mapId, ShowcaseMapId, StringComparison.Ordinal);
        }

        public static readonly ShowcaseEntityDescriptor[] Descriptors =
        {
            new("PointPin", "Point", "Baseline point-only hit profile."),
            new("RectFootprint", "Footprint2D", "Single-polygon XZ footprint."),
            new("DiamondFootprint", "Footprint2D", "Rotated polygon footprint through VisualTransform."),
            new("TwinPads", "Footprint2D", "Two disjoint polygons in one reusable footprint."),
            new("BoxTower", "Box3D", "3D box with explicit height and local Y center."),
            new("OverlapPad", "Footprint2D", "Larger overlap target for tie-break inspection."),
            new("OverlapPoint", "Point", "Smaller overlap target that should win shared clicks."),
        };
    }

    internal readonly record struct ShowcaseEntityDescriptor(string Name, string Kind, string Hint);
}
