using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.NavMesh.Geometry;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Deterministic integer ear clipping + Lawson flips for coplanar XZ polygons with explicit constraints.
    /// Shared by CDT / Recast concave obstacle convex decomposition; fail-fast, no silent fallback.
    /// </summary>
    public static class ExactConstrainedTriangulation
    {
        public static void TriangulatePolygon(
            ReadOnlySpan<int> polyX,
            ReadOnlySpan<int> polyZ,
            ReadOnlySpan<int> constrainedA,
            ReadOnlySpan<int> constrainedB,
            int maxLawsonFlipCount,
            List<int> triA,
            List<int> triB,
            List<int> triC)
        {
            int polyCount = polyX.Length;
            if (polyCount < 3)
            {
                throw new InvalidOperationException("ExactConstrainedTriangulation requires at least 3 polygon vertices.");
            }

            var next = new int[polyCount];
            var prev = new int[polyCount];
            var active = new byte[polyCount];
            for (int i = 0; i < polyCount; i++)
            {
                active[i] = 1;
                next[i] = i + 1 == polyCount ? 0 : i + 1;
                prev[i] = i == 0 ? polyCount - 1 : i - 1;
            }

            int localTriCount = 0;
            var localA = new List<int>(polyCount);
            var localB = new List<int>(polyCount);
            var localC = new List<int>(polyCount);
            EarClip(polyCount, polyX, polyZ, next, prev, active, localA, localB, localC, ref localTriCount);
            LawsonFlip(
                polyCount,
                polyX,
                polyZ,
                constrainedA,
                constrainedB,
                maxLawsonFlipCount,
                localA,
                localB,
                localC);

            triA.AddRange(localA);
            triB.AddRange(localB);
            triC.AddRange(localC);
        }

        private static void EarClip(
            int polyCount,
            ReadOnlySpan<int> polyX,
            ReadOnlySpan<int> polyZ,
            int[] next,
            int[] prev,
            byte[] active,
            List<int> triA,
            List<int> triB,
            List<int> triC,
            ref int triCount)
        {
            int activeCount = polyCount;
            while (activeCount > 3)
            {
                // Strip exact-collinear active vertices without emitting triangles so lowest-index
                // ear preference cannot collapse a rectangle with mandatory edge samples into a
                // zero-area chain (no valid convex ear). Constraint endpoints stay published via marks.
                // Keep zero-length / bridge-duplicate tips (shared XZ with a neighbor).
                int flatTip = -1;
                for (int tip = 0; tip < polyCount; tip++)
                {
                    if (active[tip] == 0)
                    {
                        continue;
                    }

                    int pFlat = prev[tip];
                    int nFlat = next[tip];
                    if ((polyX[tip] == polyX[pFlat] && polyZ[tip] == polyZ[pFlat]) ||
                        (polyX[tip] == polyX[nFlat] && polyZ[tip] == polyZ[nFlat]) ||
                        (polyX[pFlat] == polyX[nFlat] && polyZ[pFlat] == polyZ[nFlat]))
                    {
                        continue;
                    }

                    if (ExactPredicates2D.Orient2Sign(
                            polyX[pFlat], polyZ[pFlat],
                            polyX[tip], polyZ[tip],
                            polyX[nFlat], polyZ[nFlat]) != 0)
                    {
                        continue;
                    }

                    if (!ExactPredicates2D.PointOnSegmentInclusive(
                            polyX[pFlat], polyZ[pFlat],
                            polyX[nFlat], polyZ[nFlat],
                            polyX[tip], polyZ[tip]))
                    {
                        continue;
                    }

                    if (flatTip < 0 || tip < flatTip)
                    {
                        flatTip = tip;
                    }
                }

                if (flatTip >= 0)
                {
                    int flatPrev = prev[flatTip];
                    int flatNext = next[flatTip];
                    next[flatPrev] = flatNext;
                    prev[flatNext] = flatPrev;
                    active[flatTip] = 0;
                    activeCount--;
                    continue;
                }

                int bestEar = -1;
                for (int tip = 0; tip < polyCount; tip++)
                {
                    if (active[tip] == 0)
                    {
                        continue;
                    }

                    int p = prev[tip];
                    int n = next[tip];
                    if (!IsEar(tip, p, n, polyCount, polyX, polyZ, next, prev, active))
                    {
                        continue;
                    }

                    if (bestEar < 0 || tip < bestEar)
                    {
                        bestEar = tip;
                    }
                }

                if (bestEar < 0)
                {
                    throw new InvalidOperationException("ExactConstrainedTriangulation ear clipping found no valid ear.");
                }

                int prevV = prev[bestEar];
                int nextV = next[bestEar];
                triA.Add(prevV);
                triB.Add(bestEar);
                triC.Add(nextV);
                triCount++;

                next[prevV] = nextV;
                prev[nextV] = prevV;
                active[bestEar] = 0;
                activeCount--;
            }

            if (activeCount != 3)
            {
                throw new InvalidOperationException("ExactConstrainedTriangulation ear clipping did not reduce to a triangle.");
            }

            int a = -1;
            for (int i = 0; i < polyCount; i++)
            {
                if (active[i] != 0)
                {
                    a = i;
                    break;
                }
            }

            if (a < 0)
            {
                throw new InvalidOperationException("ExactConstrainedTriangulation ear clipping lost active vertices.");
            }

            int b = next[a];
            int c = next[b];
            if (ExactPredicates2D.Orient2Sign(
                    polyX[a], polyZ[a],
                    polyX[b], polyZ[b],
                    polyX[c], polyZ[c]) == 0)
            {
                throw new InvalidOperationException(
                    "ExactConstrainedTriangulation degenerate input produced a zero-area final triangle.");
            }

            triA.Add(a);
            triB.Add(b);
            triC.Add(c);
            triCount++;
        }

        private static bool IsEar(
            int tip,
            int prev,
            int next,
            int polyCount,
            ReadOnlySpan<int> polyX,
            ReadOnlySpan<int> polyZ,
            int[] polyNext,
            int[] polyPrev,
            byte[] polyActive)
        {
            if (ExactPredicates2D.Orient2Sign(
                    polyX[prev], polyZ[prev],
                    polyX[tip], polyZ[tip],
                    polyX[next], polyZ[next]) <= 0)
            {
                return false;
            }

            for (int v = 0; v < polyCount; v++)
            {
                if (polyActive[v] == 0 || v == tip || v == prev || v == next)
                {
                    continue;
                }

                if ((polyX[v] == polyX[prev] && polyZ[v] == polyZ[prev]) ||
                    (polyX[v] == polyX[tip] && polyZ[v] == polyZ[tip]) ||
                    (polyX[v] == polyX[next] && polyZ[v] == polyZ[next]))
                {
                    continue;
                }

                if (ExactPredicates2D.PointInTriangleStrict(
                        polyX[v], polyZ[v],
                        polyX[prev], polyZ[prev],
                        polyX[tip], polyZ[tip],
                        polyX[next], polyZ[next]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void LawsonFlip(
            int polyCount,
            ReadOnlySpan<int> polyX,
            ReadOnlySpan<int> polyZ,
            ReadOnlySpan<int> constrainedA,
            ReadOnlySpan<int> constrainedB,
            int maxLawsonFlipCount,
            List<int> triA,
            List<int> triB,
            List<int> triC)
        {
            int flipCount = 0;
            bool flipped = true;
            while (flipped)
            {
                flipped = false;
                int triCount = triA.Count;
                for (int t0 = 0; t0 < triCount; t0++)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int v0 = EdgeVertex(t0, e, triA, triB, triC);
                        int v1 = EdgeVertex(t0, (e + 1) % 3, triA, triB, triC);
                        if (IsConstrained(v0, v1, constrainedA, constrainedB))
                        {
                            continue;
                        }

                        int opp0 = OppositeVertex(t0, v0, v1, triA, triB, triC);
                        int t1 = FindMateTriangle(triCount, t0, v0, v1, triA, triB, triC);
                        if (t1 < 0)
                        {
                            continue;
                        }

                        int opp1 = OppositeVertex(t1, v0, v1, triA, triB, triC);
                        if (IsConstrained(opp0, opp1, constrainedA, constrainedB))
                        {
                            continue;
                        }

                        if (ExactPredicates2D.InCircleSign(
                                polyX[v0], polyZ[v0],
                                polyX[v1], polyZ[v1],
                                polyX[opp0], polyZ[opp0],
                                polyX[opp1], polyZ[opp1]) <= 0)
                        {
                            continue;
                        }

                        FlipEdge(t0, t1, v0, v1, opp0, opp1, triA, triB, triC);
                        flipCount++;
                        if (flipCount > maxLawsonFlipCount)
                        {
                            throw new InvalidOperationException(
                                $"ExactConstrainedTriangulation exceeded maxLawsonFlipCount ({maxLawsonFlipCount}).");
                        }

                        flipped = true;
                    }
                }
            }
        }

        private static bool IsConstrained(int a, int b, ReadOnlySpan<int> markA, ReadOnlySpan<int> markB)
        {
            int lo = a < b ? a : b;
            int hi = a < b ? b : a;
            for (int i = 0; i < markA.Length; i++)
            {
                int ma = markA[i];
                int mb = markB[i];
                int mlo = ma < mb ? ma : mb;
                int mhi = ma < mb ? mb : ma;
                if (mlo == lo && mhi == hi)
                {
                    return true;
                }
            }

            return false;
        }

        private static int EdgeVertex(int tri, int edge, List<int> triA, List<int> triB, List<int> triC)
            => edge switch
            {
                0 => triA[tri],
                1 => triB[tri],
                _ => triC[tri]
            };

        private static int OppositeVertex(int tri, int v0, int v1, List<int> triA, List<int> triB, List<int> triC)
        {
            int a = triA[tri];
            int b = triB[tri];
            int c = triC[tri];
            if (a != v0 && a != v1) return a;
            if (b != v0 && b != v1) return b;
            return c;
        }

        private static int FindMateTriangle(
            int triCount,
            int self,
            int v0,
            int v1,
            List<int> triA,
            List<int> triB,
            List<int> triC)
        {
            for (int t = 0; t < triCount; t++)
            {
                if (t == self)
                {
                    continue;
                }

                if (SharesEdge(t, v0, v1, triA, triB, triC))
                {
                    return t;
                }
            }

            return -1;
        }

        private static bool SharesEdge(int tri, int v0, int v1, List<int> triA, List<int> triB, List<int> triC)
        {
            int a = triA[tri];
            int b = triB[tri];
            int c = triC[tri];
            return (Contains(a, b, c, v0) && Contains(a, b, c, v1));
        }

        private static bool Contains(int a, int b, int c, int v)
            => v == a || v == b || v == c;

        private static void FlipEdge(
            int t0,
            int t1,
            int v0,
            int v1,
            int opp0,
            int opp1,
            List<int> triA,
            List<int> triB,
            List<int> triC)
        {
            triA[t0] = opp0;
            triB[t0] = v0;
            triC[t0] = opp1;
            triA[t1] = opp0;
            triB[t1] = opp1;
            triC[t1] = v1;
        }
    }
}
