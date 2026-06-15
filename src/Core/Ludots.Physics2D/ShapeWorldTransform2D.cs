using System.Runtime.CompilerServices;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D
{
    internal static class ShapeWorldTransform2D
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 RotateLocal(in Fix64Vec2 local, Fix64 rotation)
        {
            if (rotation == Fix64.Zero)
            {
                return local;
            }

            Fix64 sin = Fix64Math.Sin(rotation);
            Fix64 cos = Fix64Math.Cos(rotation);
            return RotateLocal(local, sin, cos);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 RotateLocal(in Fix64Vec2 local, Fix64 sin, Fix64 cos)
        {
            return new Fix64Vec2(
                (cos * local.X) - (sin * local.Y),
                (sin * local.X) + (cos * local.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 ToWorld(in Fix64Vec2 worldPosition, in Fix64Vec2 local, Fix64 rotation)
        {
            return worldPosition + RotateLocal(local, rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 ToWorld(in Fix64Vec2 worldPosition, in Fix64Vec2 local, Fix64 sin, Fix64 cos)
        {
            return worldPosition + RotateLocal(local, sin, cos);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 GetCircleCenter(in Fix64Vec2 worldPosition, Fix64 rotation, in CircleShapeData circle)
        {
            return ToWorld(worldPosition, circle.LocalCenter, rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 GetBoxCenter(in Fix64Vec2 worldPosition, Fix64 rotation, in BoxShapeData box)
        {
            return ToWorld(worldPosition, box.LocalCenter, rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 GetPolygonCenter(in Fix64Vec2 worldPosition, Fix64 rotation, in PolygonShapeData polygon)
        {
            return ToWorld(worldPosition, polygon.LocalOffset, rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 GetPolygonLocalVertex(in PolygonShapeData polygon, int vertexIndex)
        {
            return polygon.LocalOffset + polygon.Vertices[vertexIndex] - polygon.LocalCenter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 GetPolygonWorldVertex(
            in Fix64Vec2 worldPosition,
            Fix64 rotation,
            in PolygonShapeData polygon,
            int vertexIndex)
        {
            Fix64Vec2 local = GetPolygonLocalVertex(polygon, vertexIndex);
            return ToWorld(worldPosition, local, rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Fix64Vec2 GetPolygonWorldVertex(
            in Fix64Vec2 worldPosition,
            in Fix64Vec2 localVertex,
            Fix64 sin,
            Fix64 cos)
        {
            return ToWorld(worldPosition, localVertex, sin, cos);
        }
    }
}
