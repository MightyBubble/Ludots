using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Physics2D.Components
{
    /// <summary>
    /// 圆形碰撞体数据（定点数厘米）。
    /// </summary>
    public struct CircleShapeData
    {
        public Fix64 Radius;
        public Fix64Vec2 LocalCenter;
    }

    /// <summary>
    /// 矩形碰撞体数据（定点数厘米）。
    /// </summary>
    public struct BoxShapeData
    {
        public Fix64 HalfWidth;
        public Fix64 HalfHeight;
        public Fix64Vec2 LocalCenter;
    }

    /// <summary>
    /// 多边形碰撞体数据（定点数厘米）。
    /// 注意：Vertices 是引用类型，注册后不应修改。
    /// </summary>
    public struct PolygonShapeData
    {
        public Fix64Vec2[] Vertices;
        public int VertexCount;
        public Fix64Vec2 LocalCenter;
    }
}
