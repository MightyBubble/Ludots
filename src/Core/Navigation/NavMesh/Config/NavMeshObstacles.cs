using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public enum NavObstacleKind : byte
    {
        Polygon = 0,
        Circle = 1,
        Segment = 2
    }

    public sealed class NavObstacleSet
    {
        public int Version { get; set; } = 1;
        public List<NavObstacle> Obstacles { get; set; } = new List<NavObstacle>();
    }

    public sealed class NavObstacle
    {
        public string Id { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public NavObstacleKind Kind { get; set; } = NavObstacleKind.Polygon;
        public string LayerId { get; set; } = "Ground";
        public int? AreaId { get; set; }

        public List<NavPointCm> Points { get; set; } = new List<NavPointCm>();
        public NavPointCm Center { get; set; }
        public int RadiusCm { get; set; }
        public NavPointCm A { get; set; }
        public NavPointCm B { get; set; }
    }

    public readonly struct NavPointCm
    {
        public int Xcm { get; init; }
        public int Zcm { get; init; }

        public NavPointCm(int xcm, int zcm)
        {
            Xcm = xcm;
            Zcm = zcm;
        }
    }
}
