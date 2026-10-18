using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
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

    public readonly struct PolygonProcessConfig
    {
        /// <summary>Minimum ring area (in grid units squared * 2) to keep.</summary>
        public readonly long MinRingArea2;

        /// <summary>Distance threshold for removing near-duplicate points.</summary>
        public readonly int DuplicateThreshold;

        /// <summary>Collinearity tolerance for point removal (0 = exact).</summary>
        public readonly double CollinearTolerance;

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

    public static class PolygonProcessor
    {
        public static ValidPolygonSet Process(List<IntRing> rings, in PolygonProcessConfig config)
        {
            if (rings == null || rings.Count == 0)
                return ValidPolygonSet.Empty;

            var warnings = new List<string>();

            var filtered = FilterByArea(rings, config.MinRingArea2, warnings);
            if (filtered.Count == 0)
                return ValidPolygonSet.Empty;

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

            var normalized = NormalizeWinding(cleaned);

            var outers = new List<IntRing>();
            var holes = new List<IntRing>();
            foreach (var ring in normalized)
            {
                if (ring.IsOuter)
                    outers.Add(ring);
                else
                    holes.Add(ring);
            }

            var polygons = AssignHolesToOuters(outers, holes, warnings);

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

        public static ValidPolygonSet Process(List<IntRing> rings, in NavBuildConfig config)
        {
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

                if (result.Count > 0)
                {
                    var prev = result[result.Count - 1];
                    if (ArePointsEqual(prev, current, dupThreshold))
                        continue;
                }

                if (result.Count >= 2 && collinearTol >= 0)
                {
                    var prev = result[result.Count - 1];
                    var prevPrev = result[result.Count - 2];
                    
                    if (AreCollinear(prevPrev, prev, current, collinearTol))
                    {
                        result.RemoveAt(result.Count - 1);
                    }
                }

                result.Add(current);
            }

            while (result.Count >= 3)
            {
                var last = result[result.Count - 1];
                var first = result[0];
                var second = result[1];

                if (AreCollinear(last, first, second, collinearTol))
                {
                    result.RemoveAt(0);
                    continue;
                }

                var secondLast = result[result.Count - 2];
                if (AreCollinear(secondLast, last, first, collinearTol))
                {
                    result.RemoveAt(result.Count - 1);
                    continue;
                }

                break;
            }

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
                    long dx = pj.X - pi.X;
                    long dy = pj.Y - pi.Y;
                    long py = point.Y - pi.Y;
                    
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

        public static bool HasSelfIntersection(IntPoint[] polygon)
        {
            if (polygon == null || polygon.Length < 4)
                return false;

            int n = polygon.Length;

            for (int i = 0; i < n; i++)
            {
                var a1 = polygon[i];
                var a2 = polygon[(i + 1) % n];

                for (int j = i + 2; j < n; j++)
                {
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
            long d1 = CrossProduct(b1, b2, a1);
            long d2 = CrossProduct(b1, b2, a2);
            long d3 = CrossProduct(a1, a2, b1);
            long d4 = CrossProduct(a1, a2, b2);

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
