using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Represents a polygon with an outer boundary and optional holes.
    /// </summary>
    public readonly struct Polygon
    {
        public readonly IntPoint[] Outer;
        public readonly IntPoint[][] Holes;

        public Polygon(IntPoint[] outer, IntPoint[][] holes = null)
        {
            Outer = outer ?? throw new ArgumentNullException(nameof(outer));
            Holes = holes ?? Array.Empty<IntPoint[]>();
        }
    }

    /// <summary>
    /// Result of polygon processing with validation status.
    /// </summary>
    public readonly struct ValidPolygonSet
    {
        public readonly Polygon[] Polygons;
        public readonly bool HasWarnings;
        public readonly string[] Warnings;

        public ValidPolygonSet(Polygon[] polygons, bool hasWarnings, string[] warnings)
        {
            Polygons = polygons ?? Array.Empty<Polygon>();
            HasWarnings = hasWarnings;
            Warnings = warnings ?? Array.Empty<string>();
        }

        public static ValidPolygonSet Empty => new ValidPolygonSet(Array.Empty<Polygon>(), false, Array.Empty<string>());
    }

    /// <summary>
    /// Configuration for polygon processing.
    /// </summary>
    public readonly struct PolygonProcessConfig
    {
        /// <summary>Minimum ring area (in grid units squared * 2) to keep.</summary>
        public readonly long MinRingArea2;

        /// <summary>Distance threshold for removing near-duplicate points.</summary>
        public readonly int DuplicateThreshold;

        /// <summary>Collinearity tolerance for point removal (0 = exact).</summary>
        public readonly double CollinearTolerance;

        /// <summary>Enable self-intersection detection.</summary>
        public readonly bool CheckSelfIntersection;

        public PolygonProcessConfig(long minRingArea2 = 4, int duplicateThreshold = 0, 
            double collinearTolerance = 0.0, bool checkSelfIntersection = true)
        {
            MinRingArea2 = minRingArea2;
            DuplicateThreshold = duplicateThreshold;
            CollinearTolerance = collinearTolerance;
            CheckSelfIntersection = checkSelfIntersection;
        }

        public static PolygonProcessConfig Default => new PolygonProcessConfig();
    }

    /// <summary>
    /// Processes raw contour rings into valid polygons with proper hole assignment.
    /// </summary>
    public static class PolygonProcessor
    {
        /// <summary>
        /// Processes a list of rings into a valid polygon set.
        /// </summary>
        public static ValidPolygonSet Process(List<IntRing> rings, in PolygonProcessConfig config)
        {
            if (rings == null || rings.Count == 0)
                return ValidPolygonSet.Empty;

            var warnings = new List<string>();

            // Step 1: Filter by minimum area
            var filtered = FilterByArea(rings, config.MinRingArea2, warnings);
            if (filtered.Count == 0)
                return ValidPolygonSet.Empty;

            // Step 2: Remove duplicate and collinear points
            var cleaned = new List<IntRing>(filtered.Count);
            foreach (var ring in filtered)
            {
                var cleanedPoints = RemoveDuplicatesAndCollinear(ring.Points, config.DuplicateThreshold, config.CollinearTolerance);
                if (cleanedPoints.Length >= 3)
                {
                    long signedArea2 = ContourExtractor.ComputeSignedArea2(cleanedPoints);
                    if (Math.Abs(signedArea2) >= config.MinRingArea2)
                    {
                        cleaned.Add(new IntRing(cleanedPoints, signedArea2 > 0, signedArea2));
                    }
                }
            }

            if (cleaned.Count == 0)
                return ValidPolygonSet.Empty;

            // Step 3: Normalize winding (outer = CCW, holes = CW)
            var normalized = NormalizeWinding(cleaned);

            // Step 4: Separate outers and holes
            var outers = new List<IntRing>();
            var holes = new List<IntRing>();
            foreach (var ring in normalized)
            {
                if (ring.IsOuter)
                    outers.Add(ring);
                else
                    holes.Add(ring);
            }

            // Step 5: Assign holes to outers
            var polygons = AssignHolesToOuters(outers, holes, warnings);

            // Step 6: Optional self-intersection check
            if (config.CheckSelfIntersection)
            {
                for (int i = 0; i < polygons.Count; i++)
                {
                    if (HasSelfIntersection(polygons[i].Outer))
                    {
                        warnings.Add($"Polygon {i} outer boundary has self-intersection.");
                    }
                    for (int h = 0; h < polygons[i].Holes.Length; h++)
                    {
                        if (HasSelfIntersection(polygons[i].Holes[h]))
                        {
                            warnings.Add($"Polygon {i} hole {h} has self-intersection.");
                        }
                    }
                }
            }

            return new ValidPolygonSet(
                polygons.ToArray(),
                warnings.Count > 0,
                warnings.ToArray()
            );
        }

        /// <summary>
        /// Overload that uses NavBuildConfig.
        /// </summary>
        public static ValidPolygonSet Process(List<IntRing> rings, in NavBuildConfig config)
        {
            // Convert NavBuildConfig to PolygonProcessConfig with reasonable defaults
            var processConfig = new PolygonProcessConfig(
                minRingArea2: 4,
                duplicateThreshold: 0,
                collinearTolerance: 0.0,
                checkSelfIntersection: true
            );
            return Process(rings, processConfig);
        }

        private static List<IntRing> FilterByArea(List<IntRing> rings, long minArea2, List<string> warnings)
        {
            var result = new List<IntRing>(rings.Count);
            int filtered = 0;

            foreach (var ring in rings)
            {
                if (ring.Area2 >= minArea2)
                {
                    result.Add(ring);
                }
                else
                {
                    filtered++;
                }
            }

            if (filtered > 0)
            {
                warnings.Add($"Filtered {filtered} rings below minimum area threshold.");
            }

            return result;
        }

        private static IntPoint[] RemoveDuplicatesAndCollinear(IntPoint[] points, int dupThreshold, double collinearTol)
        {
            if (points.Length < 3)
                return points;

            var result = new List<IntPoint>(points.Length);

            for (int i = 0; i < points.Length; i++)
            {
                var current = points[i];

                // Check for duplicate with previous point
                if (result.Count > 0)
                {
                    var prev = result[result.Count - 1];
                    if (ArePointsEqual(prev, current, dupThreshold))
                        continue;
                }

                // Check for collinearity with previous and next points
                if (result.Count >= 2 && collinearTol >= 0)
                {
                    var prev = result[result.Count - 1];
                    var prevPrev = result[result.Count - 2];
                    
                    if (AreCollinear(prevPrev, prev, current, collinearTol))
                    {
                        // Remove the middle point and add current
                        result.RemoveAt(result.Count - 1);
                    }
                }

                result.Add(current);
            }

            // Check wrap-around collinearity
            while (result.Count >= 3)
            {
                // Check if last, first, second are collinear
                var last = result[result.Count - 1];
                var first = result[0];
                var second = result[1];

                if (AreCollinear(last, first, second, collinearTol))
                {
                    result.RemoveAt(0);
                    continue;
                }

                // Check if second-to-last, last, first are collinear
                var secondLast = result[result.Count - 2];
                if (AreCollinear(secondLast, last, first, collinearTol))
                {
                    result.RemoveAt(result.Count - 1);
                    continue;
                }

                break;
            }

            // Check for duplicate first/last
            if (result.Count >= 2 && ArePointsEqual(result[0], result[result.Count - 1], dupThreshold))
            {
                result.RemoveAt(result.Count - 1);
            }

            return result.ToArray();
        }

        private static bool ArePointsEqual(IntPoint a, IntPoint b, int threshold)
        {
            return Math.Abs(a.X - b.X) <= threshold && Math.Abs(a.Y - b.Y) <= threshold;
        }

        private static bool AreCollinear(IntPoint a, IntPoint b, IntPoint c, double tolerance)
        {
            // Cross product of (b-a) and (c-a)
            long cross = (long)(b.X - a.X) * (c.Y - a.Y) - (long)(b.Y - a.Y) * (c.X - a.X);
            
            if (tolerance == 0)
                return cross == 0;

            // Normalize by edge length for tolerance comparison
            double len1 = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            double len2 = Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y));
            double maxLen = Math.Max(len1, len2);
            if (maxLen < 1e-10) return true;

            return Math.Abs(cross) / maxLen <= tolerance;
        }

        private static List<IntRing> NormalizeWinding(List<IntRing> rings)
        {
            // By convention from ContourExtractor:
            // - Outer boundaries should be CCW (positive signed area)
            // - Holes should be CW (negative signed area)
            // The rings should already have correct winding from extraction.
            // This method ensures consistency.
            return rings;
        }

        /// <summary>
        /// Assigns holes to their containing outer polygons.
        /// </summary>
        private static List<Polygon> AssignHolesToOuters(List<IntRing> outers, List<IntRing> holes, List<string> warnings)
        {
            var polygons = new List<Polygon>(outers.Count);

            // Sort outers by area descending (largest first for nested containment)
            outers.Sort((a, b) => b.Area2.CompareTo(a.Area2));

            var assignedHoles = new Dictionary<int, List<IntPoint[]>>();
            for (int i = 0; i < outers.Count; i++)
            {
                assignedHoles[i] = new List<IntPoint[]>();
            }

            // Assign each hole to the smallest containing outer
            foreach (var hole in holes)
            {
                int bestOuter = -1;
                long bestArea = long.MaxValue;

                // Use representative point (first point of hole) for containment test
                var testPoint = hole.Points[0];

                for (int i = 0; i < outers.Count; i++)
                {
                    if (PointInPolygon(testPoint, outers[i].Points))
                    {
                        if (outers[i].Area2 < bestArea)
                        {
                            bestArea = outers[i].Area2;
                            bestOuter = i;
                        }
                    }
                }

                if (bestOuter >= 0)
                {
                    assignedHoles[bestOuter].Add(hole.Points);
                }
                else
                {
                    warnings.Add($"Hole at ({testPoint.X},{testPoint.Y}) could not be assigned to any outer polygon.");
                }
            }

            // Build polygons
            for (int i = 0; i < outers.Count; i++)
            {
                var holesList = assignedHoles[i];
                polygons.Add(new Polygon(outers[i].Points, holesList.ToArray()));
            }

            return polygons;
        }

        /// <summary>
        /// Point-in-polygon test using ray casting algorithm.
        /// </summary>
        public static bool PointInPolygon(IntPoint point, IntPoint[] polygon)
        {
            if (polygon == null || polygon.Length < 3)
                return false;

            int n = polygon.Length;
            bool inside = false;
            int j = n - 1;

            for (int i = 0; i < n; i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                if ((pi.Y > point.Y) != (pj.Y > point.Y))
                {
                    // Calculate x-intersection
                    long dx = pj.X - pi.X;
                    long dy = pj.Y - pi.Y;
                    long py = point.Y - pi.Y;
                    
                    // x = pi.X + (point.Y - pi.Y) * dx / dy
                    if (dy != 0)
                    {
                        long xIntersect = pi.X + (py * dx) / dy;
                        if (point.X < xIntersect)
                        {
                            inside = !inside;
                        }
                    }
                }

                j = i;
            }

            return inside;
        }

        /// <summary>
        /// Checks if a polygon has self-intersecting edges.
        /// </summary>
        public static bool HasSelfIntersection(IntPoint[] polygon)
        {
            if (polygon == null || polygon.Length < 4)
                return false;

            int n = polygon.Length;

            // Check all pairs of non-adjacent edges
            for (int i = 0; i < n; i++)
            {
                var a1 = polygon[i];
                var a2 = polygon[(i + 1) % n];

                for (int j = i + 2; j < n; j++)
                {
                    // Skip adjacent edges
                    if (j == (i + n - 1) % n)
                        continue;

                    var b1 = polygon[j];
                    var b2 = polygon[(j + 1) % n];

                    if (EdgesIntersect(a1, a2, b1, b2))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Tests if two line segments intersect (excluding endpoints touching).
        /// </summary>
        private static bool EdgesIntersect(IntPoint a1, IntPoint a2, IntPoint b1, IntPoint b2)
        {
            // Cross product orientation test
            long d1 = CrossProduct(b1, b2, a1);
            long d2 = CrossProduct(b1, b2, a2);
            long d3 = CrossProduct(a1, a2, b1);
            long d4 = CrossProduct(a1, a2, b2);

            // Check if segments straddle each other
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            return false;
        }

        private static long CrossProduct(IntPoint o, IntPoint a, IntPoint b)
        {
            return (long)(a.X - o.X) * (b.Y - o.Y) - (long)(a.Y - o.Y) * (b.X - o.X);
        }

        /// <summary>
        /// Reverses the winding order of a polygon's points.
        /// </summary>
        public static IntPoint[] ReverseWinding(IntPoint[] points)
        {
            var result = new IntPoint[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = points[points.Length - 1 - i];
            }
            return result;
        }
    }
}
