using System.Numerics;

namespace Ludots.Core.Presentation.Requests
{
    public struct RoadSplineRequest
    {
        public int StableId;
        public Vector3 P0;
        public Vector3 P1;
        public Vector3 P2;
        public Vector3 P3;
        public float Width;
        public Vector4 FillColor;
        public Vector4 BorderColor;
        public float BorderWidth;
        public byte Style;
    }
}
