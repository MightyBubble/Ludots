using System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Collision
{
    public static class CollisionAlgorithms2D
    {
        public static bool Detect(
            ShapeDataStorage2D shapeStorage,
            in Fix64Vec2 posA,
            in Rotation2D rotA,
            in Collider2D colliderA,
            in Fix64Vec2 posB,
            in Rotation2D rotB,
            in Collider2D colliderB,
            out Fix64Vec2 normal,
            out Fix64 penetration)
        {
            ArgumentNullException.ThrowIfNull(shapeStorage);
            normal = Fix64Vec2.Zero;
            penetration = Fix64.Zero;

            if (colliderA.Type == ColliderType2D.Circle && colliderB.Type == ColliderType2D.Circle)
            {
                return CircleCircle(shapeStorage, posA, rotA.Value, colliderA.ShapeDataIndex, posB, rotB.Value, colliderB.ShapeDataIndex, out normal, out penetration);
            }

            if (colliderA.Type == ColliderType2D.Circle && colliderB.Type == ColliderType2D.Box)
            {
                return CircleBox(shapeStorage, posA, rotA.Value, colliderA.ShapeDataIndex, posB, rotB.Value, colliderB.ShapeDataIndex, out normal, out penetration);
            }

            if (colliderA.Type == ColliderType2D.Box && colliderB.Type == ColliderType2D.Circle)
            {
                bool hit = CircleBox(shapeStorage, posB, rotB.Value, colliderB.ShapeDataIndex, posA, rotA.Value, colliderA.ShapeDataIndex, out normal, out penetration);
                normal = -normal;
                return hit;
            }

            if (colliderA.Type == ColliderType2D.Box && colliderB.Type == ColliderType2D.Box)
            {
                return BoxBox(shapeStorage, posA, rotA.Value, colliderA.ShapeDataIndex, posB, rotB.Value, colliderB.ShapeDataIndex, out normal, out penetration);
            }

            if (colliderA.Type == ColliderType2D.Polygon && colliderB.Type == ColliderType2D.Polygon)
            {
                return PolygonPolygon(shapeStorage, posA, rotA.Value, colliderA.ShapeDataIndex, posB, rotB.Value, colliderB.ShapeDataIndex, out normal, out penetration);
            }

            if (colliderA.Type == ColliderType2D.Polygon && colliderB.Type == ColliderType2D.Circle)
            {
                return PolygonCircle(shapeStorage, posA, rotA.Value, colliderA.ShapeDataIndex, posB, rotB.Value, colliderB.ShapeDataIndex, out normal, out penetration);
            }

            if (colliderA.Type == ColliderType2D.Circle && colliderB.Type == ColliderType2D.Polygon)
            {
                bool hit = PolygonCircle(shapeStorage, posB, rotB.Value, colliderB.ShapeDataIndex, posA, rotA.Value, colliderA.ShapeDataIndex, out normal, out penetration);
                normal = -normal;
                return hit;
            }

            if (colliderA.Type == ColliderType2D.Box && colliderB.Type == ColliderType2D.Polygon)
            {
                return BoxPolygon(shapeStorage, posA, rotA.Value, colliderA.ShapeDataIndex, posB, rotB.Value, colliderB.ShapeDataIndex, out normal, out penetration);
            }

            if (colliderA.Type == ColliderType2D.Polygon && colliderB.Type == ColliderType2D.Box)
            {
                bool hit = BoxPolygon(shapeStorage, posB, rotB.Value, colliderB.ShapeDataIndex, posA, rotA.Value, colliderA.ShapeDataIndex, out normal, out penetration);
                normal = -normal;
                return hit;
            }

            return false;
        }

        private static bool CircleCircle(
            ShapeDataStorage2D shapeStorage,
            Fix64Vec2 posA, Fix64 rotA, int shapeIndexA,
            Fix64Vec2 posB, Fix64 rotB, int shapeIndexB,
            out Fix64Vec2 normal,
            out Fix64 penetration)
        {
            normal = Fix64Vec2.Zero;
            penetration = Fix64.Zero;

            if (!shapeStorage.TryGetCircle(shapeIndexA, out var a) ||
                !shapeStorage.TryGetCircle(shapeIndexB, out var b))
            {
                return false;
            }

            Fix64Vec2 centerA = ShapeWorldTransform2D.GetCircleCenter(posA, rotA, a);
            Fix64Vec2 centerB = ShapeWorldTransform2D.GetCircleCenter(posB, rotB, b);
            Fix64Vec2 delta = centerB - centerA;
            Fix64 distSq = delta.LengthSquared();
            Fix64 radiusSum = a.Radius + b.Radius;
            Fix64 radiusSumSq = radiusSum * radiusSum;
            if (distSq >= radiusSumSq)
            {
                return false;
            }

            Fix64 dist = Fix64Math.Sqrt(distSq);
            if (dist > Fix64.Zero)
            {
                normal = delta / dist;
            }
            else
            {
                normal = Fix64Vec2.UnitX;
            }

            penetration = radiusSum - dist;
            return true;
        }

        private static bool CircleBox(
            ShapeDataStorage2D shapeStorage,
            Fix64Vec2 circlePos, Fix64 circleRotation, int circleShapeIndex,
            Fix64Vec2 boxPos, Fix64 boxRotation, int boxShapeIndex,
            out Fix64Vec2 normal,
            out Fix64 penetration)
        {
            normal = Fix64Vec2.Zero;
            penetration = Fix64.Zero;

            if (!shapeStorage.TryGetCircle(circleShapeIndex, out var circle) ||
                !shapeStorage.TryGetBox(boxShapeIndex, out var box))
            {
                return false;
            }

            Fix64Vec2 circleCenter = ShapeWorldTransform2D.GetCircleCenter(circlePos, circleRotation, circle);
            Fix64Vec2 boxCenter = ShapeWorldTransform2D.GetBoxCenter(boxPos, boxRotation, box);

            Fix64Vec2 rel = circleCenter - boxCenter;
            Fix64Vec2 relLocal = rel;
            Fix64 sin = Fix64.Zero;
            Fix64 cos = Fix64.OneValue;
            if (boxRotation != Fix64.Zero)
            {
                sin = Fix64Math.Sin(boxRotation);
                cos = Fix64Math.Cos(boxRotation);
                relLocal = RotateInv(rel, sin, cos);
            }

            Fix64 clampedX = Fix64.Clamp(relLocal.X, -box.HalfWidth, box.HalfWidth);
            Fix64 clampedY = Fix64.Clamp(relLocal.Y, -box.HalfHeight, box.HalfHeight);
            Fix64Vec2 closestLocal = new Fix64Vec2(clampedX, clampedY);
            Fix64Vec2 closestWorld = boxCenter;
            if (boxRotation != Fix64.Zero)
            {
                closestWorld = boxCenter + Rotate(closestLocal, sin, cos);
            }
            else
            {
                closestWorld = boxCenter + closestLocal;
            }

            Fix64Vec2 diff = closestWorld - circleCenter;
            Fix64 distSq = diff.LengthSquared();
            Fix64 rSq = circle.Radius * circle.Radius;
            if (distSq > rSq)
            {
                return false;
            }

            Fix64 dist = Fix64Math.Sqrt(distSq);
            if (dist > Fix64.Zero)
            {
                normal = diff / dist;
                penetration = circle.Radius - dist;
                return true;
            }

            Fix64 dx = box.HalfWidth - Fix64.Abs(relLocal.X);
            Fix64 dy = box.HalfHeight - Fix64.Abs(relLocal.Y);
            if (dx < dy)
            {
                normal = new Fix64Vec2(relLocal.X >= Fix64.Zero ? -Fix64.OneValue : Fix64.OneValue, Fix64.Zero);
                penetration = circle.Radius + dx;
            }
            else
            {
                normal = new Fix64Vec2(Fix64.Zero, relLocal.Y >= Fix64.Zero ? -Fix64.OneValue : Fix64.OneValue);
                penetration = circle.Radius + dy;
            }

            if (boxRotation != Fix64.Zero)
            {
                normal = Rotate(normal, sin, cos);
            }

            return true;
        }

        private static bool BoxBox(
            ShapeDataStorage2D shapeStorage,
            Fix64Vec2 posA, Fix64 rotA, int shapeIndexA,
            Fix64Vec2 posB, Fix64 rotB, int shapeIndexB,
            out Fix64Vec2 normal,
            out Fix64 penetration)
        {
            normal = Fix64Vec2.Zero;
            penetration = Fix64.Zero;

            if (!shapeStorage.TryGetBox(shapeIndexA, out var a) ||
                !shapeStorage.TryGetBox(shapeIndexB, out var b))
            {
                return false;
            }

            Fix64Vec2 centerA = ShapeWorldTransform2D.GetBoxCenter(posA, rotA, a);
            Fix64Vec2 centerB = ShapeWorldTransform2D.GetBoxCenter(posB, rotB, b);
            Fix64Vec2 d = centerB - centerA;

            Fix64 sinA = Fix64.Zero;
            Fix64 cosA = Fix64.OneValue;
            if (rotA != Fix64.Zero)
            {
                sinA = Fix64Math.Sin(rotA);
                cosA = Fix64Math.Cos(rotA);
            }

            Fix64 sinB = Fix64.Zero;
            Fix64 cosB = Fix64.OneValue;
            if (rotB != Fix64.Zero)
            {
                sinB = Fix64Math.Sin(rotB);
                cosB = Fix64Math.Cos(rotB);
            }

            Fix64Vec2 uA = new Fix64Vec2(cosA, sinA);
            Fix64Vec2 vA = new Fix64Vec2(-sinA, cosA);
            Fix64Vec2 uB = new Fix64Vec2(cosB, sinB);
            Fix64Vec2 vB = new Fix64Vec2(-sinB, cosB);

            Fix64 minOverlap = Fix64.MaxValue;
            Fix64Vec2 bestAxis = Fix64Vec2.Zero;

            if (!TestAxis(uA, d, a, uA, vA, b, uB, vB, ref minOverlap, ref bestAxis)) return false;
            if (!TestAxis(vA, d, a, uA, vA, b, uB, vB, ref minOverlap, ref bestAxis)) return false;
            if (!TestAxis(uB, d, a, uA, vA, b, uB, vB, ref minOverlap, ref bestAxis)) return false;
            if (!TestAxis(vB, d, a, uA, vA, b, uB, vB, ref minOverlap, ref bestAxis)) return false;

            Fix64 sign = Fix64Vec2.Dot(d, bestAxis) >= Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue;
            normal = bestAxis * sign;
            penetration = minOverlap;
            return true;
        }

        private static bool PolygonPolygon(
            ShapeDataStorage2D shapeStorage,
            Fix64Vec2 posA, Fix64 rotA, int shapeIndexA,
            Fix64Vec2 posB, Fix64 rotB, int shapeIndexB,
            out Fix64Vec2 normal,
            out Fix64 penetration)
        {
            normal = Fix64Vec2.Zero;
            penetration = Fix64.Zero;

            if (!shapeStorage.TryGetPolygon(shapeIndexA, out var a) ||
                !shapeStorage.TryGetPolygon(shapeIndexB, out var b))
            {
                return false;
            }

            Fix64 minOverlap = Fix64.MaxValue;
            Fix64Vec2 bestAxis = Fix64Vec2.Zero;

            if (!TestPolygonAxes(posA, rotA, a, posB, rotB, b, ref minOverlap, ref bestAxis)) return false;
            if (!TestPolygonAxes(posB, rotB, b, posA, rotA, a, ref minOverlap, ref bestAxis)) return false;

            Fix64Vec2 centerA = ShapeWorldTransform2D.GetPolygonCenter(posA, rotA, a);
            Fix64Vec2 centerB = ShapeWorldTransform2D.GetPolygonCenter(posB, rotB, b);
            Fix64Vec2 d = centerB - centerA;

            Fix64 sign = Fix64Vec2.Dot(d, bestAxis) >= Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue;
            normal = bestAxis * sign;
            penetration = minOverlap;
            return true;
        }

        private static bool BoxPolygon(
            ShapeDataStorage2D shapeStorage,
            Fix64Vec2 boxPos, Fix64 boxRot, int boxShapeIndex,
            Fix64Vec2 polyPos, Fix64 polyRot, int polyShapeIndex,
            out Fix64Vec2 normal,
            out Fix64 penetration)
        {
            normal = Fix64Vec2.Zero;
            penetration = Fix64.Zero;

            if (!shapeStorage.TryGetBox(boxShapeIndex, out var box) ||
                !shapeStorage.TryGetPolygon(polyShapeIndex, out var poly))
            {
                return false;
            }

            Span<Fix64Vec2> boxVerts = stackalloc Fix64Vec2[4];
            Span<Fix64Vec2> polyVerts = stackalloc Fix64Vec2[8];

            int boxCount = FillBoxWorldVertices(boxPos, boxRot, box, boxVerts);
            int polyCount = FillPolygonWorldVertices(polyPos, polyRot, poly, polyVerts);

            if (!SatConvexConvex(
                boxVerts.Slice(0, boxCount),
                polyVerts.Slice(0, polyCount),
                out normal,
                out penetration))
            {
                return false;
            }

            Fix64Vec2 boxCenter = ShapeWorldTransform2D.GetBoxCenter(boxPos, boxRot, box);
            Fix64Vec2 polyCenter = ShapeWorldTransform2D.GetPolygonCenter(polyPos, polyRot, poly);
            Fix64Vec2 d = polyCenter - boxCenter;
            if (Fix64Vec2.Dot(d, normal) < Fix64.Zero)
            {
                normal = -normal;
            }

            return true;
        }

        private static bool PolygonCircle(
            ShapeDataStorage2D shapeStorage,
            Fix64Vec2 polyPos, Fix64 polyRot, int polyShapeIndex,
            Fix64Vec2 circlePos, Fix64 circleRot, int circleShapeIndex,
            out Fix64Vec2 normal,
            out Fix64 penetration)
        {
            normal = Fix64Vec2.Zero;
            penetration = Fix64.Zero;

            if (!shapeStorage.TryGetPolygon(polyShapeIndex, out var poly) ||
                !shapeStorage.TryGetCircle(circleShapeIndex, out var circle))
            {
                return false;
            }

            Fix64Vec2 circleCenter = ShapeWorldTransform2D.GetCircleCenter(circlePos, circleRot, circle);

            Fix64 sin = Fix64.Zero;
            Fix64 cos = Fix64.OneValue;
            if (polyRot != Fix64.Zero)
            {
                sin = Fix64Math.Sin(polyRot);
                cos = Fix64Math.Cos(polyRot);
            }

            Fix64 minOverlap = Fix64.MaxValue;
            Fix64Vec2 bestAxis = Fix64Vec2.Zero;

            for (int i = 0; i < poly.VertexCount; i++)
            {
                Fix64Vec2 a = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, i);
                Fix64Vec2 b = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, (i + 1) % poly.VertexCount);
                if (polyRot != Fix64.Zero)
                {
                    a = Rotate(a, sin, cos);
                    b = Rotate(b, sin, cos);
                }

                Fix64Vec2 edge = b - a;
                Fix64Vec2 axis = new Fix64Vec2(-edge.Y, edge.X).Normalized();
                if (axis == Fix64Vec2.Zero) continue;

                ProjectPolygon(polyPos, poly, polyRot, axis, out Fix64 minP, out Fix64 maxP);
                Fix64 centerProj = Fix64Vec2.Dot(circleCenter, axis);
                Fix64 minC = centerProj - circle.Radius;
                Fix64 maxC = centerProj + circle.Radius;

                Fix64 overlap = Fix64.Min(maxP, maxC) - Fix64.Max(minP, minC);
                if (overlap <= Fix64.Zero) return false;
                if (overlap < minOverlap)
                {
                    minOverlap = overlap;
                    bestAxis = axis;
                }
            }

            Fix64Vec2 closest = ClosestPointOnPolygon(polyPos, polyRot, poly, circleCenter);
            Fix64Vec2 axisToCircle = (circleCenter - closest);
            if (axisToCircle != Fix64Vec2.Zero)
            {
                Fix64Vec2 axis = axisToCircle.Normalized();
                ProjectPolygon(polyPos, poly, polyRot, axis, out Fix64 minP, out Fix64 maxP);
                Fix64 centerProj = Fix64Vec2.Dot(circleCenter, axis);
                Fix64 minC = centerProj - circle.Radius;
                Fix64 maxC = centerProj + circle.Radius;

                Fix64 overlap = Fix64.Min(maxP, maxC) - Fix64.Max(minP, minC);
                if (overlap <= Fix64.Zero) return false;
                if (overlap < minOverlap)
                {
                    minOverlap = overlap;
                    bestAxis = axis;
                }
            }

            Fix64Vec2 polyCenter = ShapeWorldTransform2D.GetPolygonCenter(polyPos, polyRot, poly);
            Fix64Vec2 d = circleCenter - polyCenter;
            Fix64 sign = Fix64Vec2.Dot(d, bestAxis) >= Fix64.Zero ? Fix64.OneValue : -Fix64.OneValue;
            normal = bestAxis * sign;
            penetration = minOverlap;
            return true;
        }

        private static bool TestPolygonAxes(
            Fix64Vec2 posA, Fix64 rotA, in PolygonShapeData a,
            Fix64Vec2 posB, Fix64 rotB, in PolygonShapeData b,
            ref Fix64 minOverlap,
            ref Fix64Vec2 bestAxis)
        {
            Fix64 sinA = Fix64.Zero;
            Fix64 cosA = Fix64.OneValue;
            if (rotA != Fix64.Zero)
            {
                sinA = Fix64Math.Sin(rotA);
                cosA = Fix64Math.Cos(rotA);
            }

            for (int i = 0; i < a.VertexCount; i++)
            {
                Fix64Vec2 va = ShapeWorldTransform2D.GetPolygonLocalVertex(a, i);
                Fix64Vec2 vb = ShapeWorldTransform2D.GetPolygonLocalVertex(a, (i + 1) % a.VertexCount);
                if (rotA != Fix64.Zero)
                {
                    va = Rotate(va, sinA, cosA);
                    vb = Rotate(vb, sinA, cosA);
                }

                Fix64Vec2 edge = vb - va;
                Fix64Vec2 axis = new Fix64Vec2(-edge.Y, edge.X).Normalized();
                if (axis == Fix64Vec2.Zero) continue;

                ProjectPolygon(posA, a, rotA, axis, out Fix64 minPA, out Fix64 maxPA);
                ProjectPolygon(posB, b, rotB, axis, out Fix64 minPB, out Fix64 maxPB);

                Fix64 overlap = Fix64.Min(maxPA, maxPB) - Fix64.Max(minPA, minPB);
                if (overlap <= Fix64.Zero) return false;
                if (overlap < minOverlap)
                {
                    minOverlap = overlap;
                    bestAxis = axis;
                }
            }

            return true;
        }

        private static void ProjectPolygon(
            Fix64Vec2 polyPos,
            in PolygonShapeData poly,
            Fix64 rot,
            Fix64Vec2 axis,
            out Fix64 min,
            out Fix64 max)
        {
            Fix64 sin = Fix64.Zero;
            Fix64 cos = Fix64.OneValue;
            if (rot != Fix64.Zero)
            {
                sin = Fix64Math.Sin(rot);
                cos = Fix64Math.Cos(rot);
            }

            Fix64Vec2 v0 = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, 0);
            if (rot != Fix64.Zero)
            {
                v0 = Rotate(v0, sin, cos);
            }

            Fix64Vec2 w0 = polyPos + v0;
            Fix64 p0 = Fix64Vec2.Dot(w0, axis);
            min = p0;
            max = p0;

            for (int i = 1; i < poly.VertexCount; i++)
            {
                Fix64Vec2 v = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, i);
                if (rot != Fix64.Zero)
                {
                    v = Rotate(v, sin, cos);
                }

                Fix64Vec2 w = polyPos + v;
                Fix64 p = Fix64Vec2.Dot(w, axis);
                min = Fix64.Min(min, p);
                max = Fix64.Max(max, p);
            }
        }

        private static Fix64Vec2 ClosestPointOnPolygon(Fix64Vec2 polyPos, Fix64 rot, in PolygonShapeData poly, Fix64Vec2 point)
        {
            Fix64 sin = Fix64.Zero;
            Fix64 cos = Fix64.OneValue;
            if (rot != Fix64.Zero)
            {
                sin = Fix64Math.Sin(rot);
                cos = Fix64Math.Cos(rot);
            }

            Fix64 bestDistSq = Fix64.MaxValue;
            Fix64Vec2 best = Fix64Vec2.Zero;

            Fix64Vec2 prev = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, poly.VertexCount - 1);
            if (rot != Fix64.Zero)
            {
                prev = Rotate(prev, sin, cos);
            }
            prev = polyPos + prev;

            for (int i = 0; i < poly.VertexCount; i++)
            {
                Fix64Vec2 curr = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, i);
                if (rot != Fix64.Zero)
                {
                    curr = Rotate(curr, sin, cos);
                }
                curr = polyPos + curr;

                Fix64Vec2 closest = ClosestPointOnSegment(prev, curr, point);
                Fix64 distSq = (point - closest).LengthSquared();
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = closest;
                }

                prev = curr;
            }

            return best;
        }

        private static Fix64Vec2 ClosestPointOnSegment(Fix64Vec2 a, Fix64Vec2 b, Fix64Vec2 p)
        {
            Fix64Vec2 ab = b - a;
            Fix64 abLenSq = ab.LengthSquared();
            if (abLenSq == Fix64.Zero) return a;
            Fix64 t = Fix64Vec2.Dot(p - a, ab) / abLenSq;
            t = Fix64.Clamp(t, Fix64.Zero, Fix64.OneValue);
            return a + ab * t;
        }

        private static bool TestAxis(
            Fix64Vec2 axis,
            Fix64Vec2 d,
            in BoxShapeData boxA,
            Fix64Vec2 uA,
            Fix64Vec2 vA,
            in BoxShapeData boxB,
            Fix64Vec2 uB,
            Fix64Vec2 vB,
            ref Fix64 minOverlap,
            ref Fix64Vec2 bestAxis)
        {
            Fix64 ra = Fix64.Abs(Fix64Vec2.Dot(axis, uA)) * boxA.HalfWidth + Fix64.Abs(Fix64Vec2.Dot(axis, vA)) * boxA.HalfHeight;
            Fix64 rb = Fix64.Abs(Fix64Vec2.Dot(axis, uB)) * boxB.HalfWidth + Fix64.Abs(Fix64Vec2.Dot(axis, vB)) * boxB.HalfHeight;
            Fix64 dist = Fix64.Abs(Fix64Vec2.Dot(d, axis));
            Fix64 overlap = ra + rb - dist;
            if (overlap <= Fix64.Zero)
            {
                return false;
            }

            if (overlap < minOverlap)
            {
                minOverlap = overlap;
                bestAxis = axis;
            }

            return true;
        }

        private static Fix64Vec2 Rotate(Fix64Vec2 v, Fix64 sin, Fix64 cos)
        {
            return new Fix64Vec2(cos * v.X - sin * v.Y, sin * v.X + cos * v.Y);
        }

        private static Fix64Vec2 RotateInv(Fix64Vec2 v, Fix64 sin, Fix64 cos)
        {
            return new Fix64Vec2(cos * v.X + sin * v.Y, -sin * v.X + cos * v.Y);
        }

        private static int FillBoxWorldVertices(Fix64Vec2 boxPos, Fix64 boxRot, in BoxShapeData box, Span<Fix64Vec2> dst)
        {
            Fix64 sin = Fix64.Zero;
            Fix64 cos = Fix64.OneValue;
            if (boxRot != Fix64.Zero)
            {
                sin = Fix64Math.Sin(boxRot);
                cos = Fix64Math.Cos(boxRot);
            }

            Fix64Vec2 c = ShapeWorldTransform2D.GetBoxCenter(boxPos, boxRot, box);
            Fix64Vec2 v0 = new Fix64Vec2(-box.HalfWidth, -box.HalfHeight);
            Fix64Vec2 v1 = new Fix64Vec2(box.HalfWidth, -box.HalfHeight);
            Fix64Vec2 v2 = new Fix64Vec2(box.HalfWidth, box.HalfHeight);
            Fix64Vec2 v3 = new Fix64Vec2(-box.HalfWidth, box.HalfHeight);

            if (boxRot != Fix64.Zero)
            {
                v0 = Rotate(v0, sin, cos);
                v1 = Rotate(v1, sin, cos);
                v2 = Rotate(v2, sin, cos);
                v3 = Rotate(v3, sin, cos);
            }

            dst[0] = c + v0;
            dst[1] = c + v1;
            dst[2] = c + v2;
            dst[3] = c + v3;
            return 4;
        }

        private static int FillPolygonWorldVertices(Fix64Vec2 polyPos, Fix64 polyRot, in PolygonShapeData poly, Span<Fix64Vec2> dst)
        {
            Fix64 sin = Fix64.Zero;
            Fix64 cos = Fix64.OneValue;
            if (polyRot != Fix64.Zero)
            {
                sin = Fix64Math.Sin(polyRot);
                cos = Fix64Math.Cos(polyRot);
            }

            for (int i = 0; i < poly.VertexCount; i++)
            {
                Fix64Vec2 v = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, i);
                if (polyRot != Fix64.Zero)
                {
                    v = Rotate(v, sin, cos);
                }
                dst[i] = polyPos + v;
            }

            return poly.VertexCount;
        }

        private static bool SatConvexConvex(
            ReadOnlySpan<Fix64Vec2> a,
            ReadOnlySpan<Fix64Vec2> b,
            out Fix64Vec2 normal,
            out Fix64 penetration)
        {
            normal = Fix64Vec2.Zero;
            penetration = Fix64.Zero;

            Fix64 minOverlap = Fix64.MaxValue;
            Fix64Vec2 bestAxis = Fix64Vec2.Zero;

            if (!SatEdges(a, b, ref minOverlap, ref bestAxis)) return false;
            if (!SatEdges(b, a, ref minOverlap, ref bestAxis)) return false;

            normal = bestAxis;
            penetration = minOverlap;
            return true;
        }

        private static bool SatEdges(
            ReadOnlySpan<Fix64Vec2> a,
            ReadOnlySpan<Fix64Vec2> b,
            ref Fix64 minOverlap,
            ref Fix64Vec2 bestAxis)
        {
            Fix64Vec2 prev = a[a.Length - 1];
            for (int i = 0; i < a.Length; i++)
            {
                Fix64Vec2 curr = a[i];
                Fix64Vec2 edge = curr - prev;
                Fix64Vec2 axis = new Fix64Vec2(-edge.Y, edge.X).Normalized();
                if (axis == Fix64Vec2.Zero)
                {
                    prev = curr;
                    continue;
                }

                ProjectVertices(a, axis, out Fix64 minA, out Fix64 maxA);
                ProjectVertices(b, axis, out Fix64 minB, out Fix64 maxB);

                Fix64 overlap = Fix64.Min(maxA, maxB) - Fix64.Max(minA, minB);
                if (overlap <= Fix64.Zero) return false;
                if (overlap < minOverlap)
                {
                    minOverlap = overlap;
                    bestAxis = axis;
                }

                prev = curr;
            }

            return true;
        }

        private static void ProjectVertices(ReadOnlySpan<Fix64Vec2> verts, Fix64Vec2 axis, out Fix64 min, out Fix64 max)
        {
            Fix64 p0 = Fix64Vec2.Dot(verts[0], axis);
            min = p0;
            max = p0;
            for (int i = 1; i < verts.Length; i++)
            {
                Fix64 p = Fix64Vec2.Dot(verts[i], axis);
                min = Fix64.Min(min, p);
                max = Fix64.Max(max, p);
            }
        }

    }
}
