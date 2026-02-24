using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// A portal (gate) for funnel algorithm, representing left and right endpoints.
    /// </summary>
    public readonly struct FunnelPortal
    {
        public readonly Vector2 Left;
        public readonly Vector2 Right;

        public FunnelPortal(Vector2 left, Vector2 right)
        {
            Left = left;
            Right = right;
        }

        public FunnelPortal(float leftX, float leftZ, float rightX, float rightZ)
        {
            Left = new Vector2(leftX, leftZ);
            Right = new Vector2(rightX, rightZ);
        }

        public Vector2 Center => (Left + Right) * 0.5f;
    }

    /// <summary>
    /// Result of the funnel algorithm - a smoothed path.
    /// </summary>
    public readonly struct FunnelResult
    {
        public readonly Vector2[] Path;
        public readonly bool Success;

        public FunnelResult(Vector2[] path, bool success)
        {
            Path = path ?? Array.Empty<Vector2>();
            Success = success;
        }

        public static FunnelResult Failed => new FunnelResult(Array.Empty<Vector2>(), false);
    }

    /// <summary>
    /// Implementation of the Simple Stupid Funnel Algorithm for path smoothing.
    /// Reference: "Simple Stupid Funnel Algorithm" by Mikko Mononen
    /// http://digestingduck.blogspot.com/2010/03/simple-stupid-funnel-algorithm.html
    /// </summary>
    public static class FunnelAlgorithm
    {
        /// <summary>
        /// Smooths a path through a sequence of portals using the funnel algorithm.
        /// </summary>
        /// <param name="start">Start position.</param>
        /// <param name="goal">Goal position.</param>
        /// <param name="portals">Sequence of portals to traverse.</param>
        /// <returns>Smoothed path result.</returns>
        public static FunnelResult SmoothPath(Vector2 start, Vector2 goal, IReadOnlyList<FunnelPortal> portals)
        {
            if (portals == null || portals.Count == 0)
            {
                // Direct path from start to goal
                return new FunnelResult(new[] { start, goal }, true);
            }

            var path = new List<Vector2>(portals.Count + 2);
            path.Add(start);

            // Initialize funnel
            var apex = start;
            var portalLeft = start;
            var portalRight = start;
            int apexIndex = 0;
            int leftIndex = 0;
            int rightIndex = 0;

            for (int i = 0; i < portals.Count; i++)
            {
                var portal = portals[i];
                var left = portal.Left;
                var right = portal.Right;

                // Update right vertex
                if (TriangleArea2(apex, portalRight, right) <= 0f)
                {
                    if (ApproxEqual(apex, portalRight) || TriangleArea2(apex, portalLeft, right) > 0f)
                    {
                        // Tighten the funnel
                        portalRight = right;
                        rightIndex = i;
                    }
                    else
                    {
                        // Right over left, insert left to path and restart scan from portal left point
                        path.Add(portalLeft);
                        apex = portalLeft;
                        apexIndex = leftIndex;

                        // Reset portal
                        portalLeft = apex;
                        portalRight = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;

                        // Restart scan
                        i = apexIndex;
                        continue;
                    }
                }

                // Update left vertex
                if (TriangleArea2(apex, portalLeft, left) >= 0f)
                {
                    if (ApproxEqual(apex, portalLeft) || TriangleArea2(apex, portalRight, left) < 0f)
                    {
                        // Tighten the funnel
                        portalLeft = left;
                        leftIndex = i;
                    }
                    else
                    {
                        // Left over right, insert right to path and restart scan from portal right point
                        path.Add(portalRight);
                        apex = portalRight;
                        apexIndex = rightIndex;

                        // Reset portal
                        portalLeft = apex;
                        portalRight = apex;
                        leftIndex = apexIndex;
                        rightIndex = apexIndex;

                        // Restart scan
                        i = apexIndex;
                        continue;
                    }
                }
            }

            // Add goal
            path.Add(goal);

            return new FunnelResult(path.ToArray(), true);
        }

        /// <summary>
        /// Smooths a path given integer coordinates (centimeters).
        /// </summary>
        public static FunnelResult SmoothPathCm(
            int startXcm, int startZcm,
            int goalXcm, int goalZcm,
            IReadOnlyList<(int LeftXcm, int LeftZcm, int RightXcm, int RightZcm)> portalsCm)
        {
            var start = new Vector2(startXcm / 100f, startZcm / 100f);
            var goal = new Vector2(goalXcm / 100f, goalZcm / 100f);

            var portals = new List<FunnelPortal>(portalsCm.Count);
            foreach (var p in portalsCm)
            {
                portals.Add(new FunnelPortal(
                    p.LeftXcm / 100f, p.LeftZcm / 100f,
                    p.RightXcm / 100f, p.RightZcm / 100f));
            }

            return SmoothPath(start, goal, portals);
        }

        /// <summary>
        /// Converts funnel result back to centimeter coordinates.
        /// </summary>
        public static (int[] Xcm, int[] Zcm) ToIntPath(FunnelResult result)
        {
            var xcm = new int[result.Path.Length];
            var zcm = new int[result.Path.Length];

            for (int i = 0; i < result.Path.Length; i++)
            {
                xcm[i] = (int)MathF.Round(result.Path[i].X * 100f);
                zcm[i] = (int)MathF.Round(result.Path[i].Y * 100f);
            }

            return (xcm, zcm);
        }

        /// <summary>
        /// Calculates twice the signed area of triangle ABC.
        /// Positive = CCW, Negative = CW, Zero = collinear.
        /// </summary>
        private static float TriangleArea2(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        }

        /// <summary>
        /// Checks if two points are approximately equal.
        /// </summary>
        private static bool ApproxEqual(Vector2 a, Vector2 b, float epsilon = 1e-6f)
        {
            return MathF.Abs(a.X - b.X) < epsilon && MathF.Abs(a.Y - b.Y) < epsilon;
        }

        /// <summary>
        /// String-pulling algorithm variant for path smoothing.
        /// This is a simpler alternative to the funnel algorithm.
        /// </summary>
        public static FunnelResult StringPull(Vector2 start, Vector2 goal, IReadOnlyList<FunnelPortal> portals)
        {
            if (portals == null || portals.Count == 0)
            {
                return new FunnelResult(new[] { start, goal }, true);
            }

            var path = new List<Vector2>(portals.Count + 2);
            path.Add(start);

            var current = start;

            for (int i = 0; i < portals.Count; i++)
            {
                var portal = portals[i];

                // Find the point on the portal edge closest to a straight line from current to goal
                var portalPoint = ClosestPointOnLineSegment(portal.Left, portal.Right, 
                    ProjectPointOnLine(current, goal, portal.Left, portal.Right));

                // Check if we can see the goal directly through remaining portals
                bool canSeeGoal = true;
                for (int j = i; j < portals.Count && canSeeGoal; j++)
                {
                    var testPortal = portals[j];
                    if (!LineIntersectsSegment(current, goal, testPortal.Left, testPortal.Right))
                    {
                        canSeeGoal = false;
                    }
                }

                if (canSeeGoal)
                {
                    // We can see the goal, no need to add more waypoints
                    break;
                }

                // Add portal crossing point
                if (Vector2.DistanceSquared(current, portalPoint) > 0.0001f)
                {
                    path.Add(portalPoint);
                    current = portalPoint;
                }
            }

            path.Add(goal);

            return new FunnelResult(path.ToArray(), true);
        }

        /// <summary>
        /// Projects a point onto a line and returns the parametric position.
        /// </summary>
        private static Vector2 ProjectPointOnLine(Vector2 lineStart, Vector2 lineEnd, Vector2 portalLeft, Vector2 portalRight)
        {
            var lineDir = lineEnd - lineStart;
            var portalCenter = (portalLeft + portalRight) * 0.5f;
            return portalCenter;
        }

        /// <summary>
        /// Finds the closest point on a line segment to a given point.
        /// </summary>
        private static Vector2 ClosestPointOnLineSegment(Vector2 segStart, Vector2 segEnd, Vector2 point)
        {
            var seg = segEnd - segStart;
            float segLenSq = seg.LengthSquared();

            if (segLenSq < 1e-10f)
                return segStart;

            float t = Vector2.Dot(point - segStart, seg) / segLenSq;
            t = Math.Clamp(t, 0f, 1f);

            return segStart + seg * t;
        }

        /// <summary>
        /// Tests if a line segment intersects another line segment.
        /// </summary>
        private static bool LineIntersectsSegment(Vector2 lineStart, Vector2 lineEnd, Vector2 segStart, Vector2 segEnd)
        {
            float d1 = TriangleArea2(lineStart, lineEnd, segStart);
            float d2 = TriangleArea2(lineStart, lineEnd, segEnd);

            // Points on opposite sides of the line
            return (d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0) || d1 == 0 || d2 == 0;
        }
    }
}
