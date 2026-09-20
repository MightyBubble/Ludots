using System.Numerics;

namespace SplineSurfaceUatMod
{
    internal static class SplineSurfaceUatIds
    {
        public const string InstalledKey = "SplineSurfaceUatMod.Installed";
        public const string MapId = "spline_surface_uat";

        public const string RoadPresenterId = "uat_surface_road";
        public const string RiverPresenterId = "uat_surface_river";
        public const string LakePresenterId = "uat_surface_lake";
        public const string RawPresenterId = "uat_surface_raw";

        public const int RoadScopeId = 710001;
        public const int RiverScopeId = 710002;
        public const int LakeScopeId = 710003;
        public const int RawScopeId = 710004;

        public static readonly Vector3 RoadAnchorWorld = new(-14f, 0f, -6f);
        public static readonly Vector3 RiverAnchorWorld = new(-8f, 0f, 7f);
        public static readonly Vector3 LakeAnchorWorld = new(10f, 0f, -4f);
        public static readonly Vector3 RawAnchorWorld = new(12f, 0f, 9f);
        public static readonly Vector2 OverviewCameraTargetCm = new(0f, 1.5f);
    }
}
