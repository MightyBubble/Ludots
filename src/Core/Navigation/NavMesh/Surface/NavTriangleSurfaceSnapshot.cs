using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.Surface
{
    /// <summary>
    /// Immutable Core-owned arbitrary-3D triangle surface snapshot (SoA, centimeter integers).
    /// Layered geometry is preserved: overlapping XZ footprints at different Y remain distinct triangles.
    /// Caller-provided channel arrays are validated then defensively copied; later mutation of inputs cannot change the snapshot.
    /// Triangle area ids match NavTile storage as bytes. Stable triangle ids are nonnegative deterministic identities (zero is valid).
    /// <see cref="TriFlags"/> is mandatory: each triangle must be exactly <see cref="NavTriangleSurfaceFlags.Solid"/>
    /// or <see cref="NavTriangleSurfaceFlags.Solid"/>|<see cref="NavTriangleSurfaceFlags.WalkCandidate"/>.
    /// </summary>
    public sealed class NavTriangleSurfaceSnapshot
    {
        private const NavTriangleSurfaceFlags ValidFlagsMask =
            NavTriangleSurfaceFlags.Solid | NavTriangleSurfaceFlags.WalkCandidate;

        private readonly int[] _vertexXcm;
        private readonly int[] _vertexYcm;
        private readonly int[] _vertexZcm;
        private readonly int[] _triA;
        private readonly int[] _triB;
        private readonly int[] _triC;
        private readonly byte[] _triAreaIds;
        private readonly int[] _triStableIds;
        private readonly NavTriangleSurfaceFlags[] _triFlags;

        public NavTriangleSurfaceSnapshot(
            int[] vertexXcm,
            int[] vertexYcm,
            int[] vertexZcm,
            int[] triA,
            int[] triB,
            int[] triC,
            byte[] triAreaIds,
            int[] triStableIds,
            NavTriangleSurfaceFlags[] triFlags)
        {
            if (vertexXcm == null) throw new ArgumentNullException(nameof(vertexXcm));
            if (vertexYcm == null) throw new ArgumentNullException(nameof(vertexYcm));
            if (vertexZcm == null) throw new ArgumentNullException(nameof(vertexZcm));
            if (triA == null) throw new ArgumentNullException(nameof(triA));
            if (triB == null) throw new ArgumentNullException(nameof(triB));
            if (triC == null) throw new ArgumentNullException(nameof(triC));
            if (triAreaIds == null) throw new ArgumentNullException(nameof(triAreaIds));
            if (triStableIds == null) throw new ArgumentNullException(nameof(triStableIds));
            if (triFlags == null) throw new ArgumentNullException(nameof(triFlags));

            if (vertexYcm.Length != vertexXcm.Length)
            {
                throw new ArgumentException(
                    $"Vertex Y length {vertexYcm.Length} must match vertex X length {vertexXcm.Length}.",
                    nameof(vertexYcm));
            }

            if (vertexZcm.Length != vertexXcm.Length)
            {
                throw new ArgumentException(
                    $"Vertex Z length {vertexZcm.Length} must match vertex X length {vertexXcm.Length}.",
                    nameof(vertexZcm));
            }

            if (triB.Length != triA.Length)
            {
                throw new ArgumentException(
                    $"Triangle B length {triB.Length} must match triangle A length {triA.Length}.",
                    nameof(triB));
            }

            if (triC.Length != triA.Length)
            {
                throw new ArgumentException(
                    $"Triangle C length {triC.Length} must match triangle A length {triA.Length}.",
                    nameof(triC));
            }

            if (triAreaIds.Length != triA.Length)
            {
                throw new ArgumentException(
                    $"Triangle area id length {triAreaIds.Length} must match triangle A length {triA.Length}.",
                    nameof(triAreaIds));
            }

            if (triStableIds.Length != triA.Length)
            {
                throw new ArgumentException(
                    $"Triangle stable id length {triStableIds.Length} must match triangle A length {triA.Length}.",
                    nameof(triStableIds));
            }

            if (triFlags.Length != triA.Length)
            {
                throw new ArgumentException(
                    $"Triangle flags length {triFlags.Length} must match triangle A length {triA.Length}.",
                    nameof(triFlags));
            }

            int vertexCount = vertexXcm.Length;
            var seenStableIds = new HashSet<int>(triStableIds.Length);
            for (int i = 0; i < triA.Length; i++)
            {
                ValidateVertexIndex(triA[i], vertexCount, nameof(triA), i);
                ValidateVertexIndex(triB[i], vertexCount, nameof(triB), i);
                ValidateVertexIndex(triC[i], vertexCount, nameof(triC), i);

                int stableId = triStableIds[i];
                if (stableId < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(triStableIds),
                        stableId,
                        $"Triangle {i} stable id {stableId} must be nonnegative.");
                }

                if (!seenStableIds.Add(stableId))
                {
                    throw new ArgumentException(
                        $"Triangle stable id {stableId} is duplicated at triangle index {i}.",
                        nameof(triStableIds));
                }

                ValidateTriFlags(triFlags[i], i, stableId);
            }

            _vertexXcm = (int[])vertexXcm.Clone();
            _vertexYcm = (int[])vertexYcm.Clone();
            _vertexZcm = (int[])vertexZcm.Clone();
            _triA = (int[])triA.Clone();
            _triB = (int[])triB.Clone();
            _triC = (int[])triC.Clone();
            _triAreaIds = (byte[])triAreaIds.Clone();
            _triStableIds = (int[])triStableIds.Clone();
            _triFlags = (NavTriangleSurfaceFlags[])triFlags.Clone();

            VertexCount = vertexCount;
            TriangleCount = _triA.Length;
        }

        public int VertexCount { get; }

        public int TriangleCount { get; }

        public ReadOnlySpan<int> VertexXcm => _vertexXcm;

        public ReadOnlySpan<int> VertexYcm => _vertexYcm;

        public ReadOnlySpan<int> VertexZcm => _vertexZcm;

        public ReadOnlySpan<int> TriA => _triA;

        public ReadOnlySpan<int> TriB => _triB;

        public ReadOnlySpan<int> TriC => _triC;

        public ReadOnlySpan<byte> TriAreaIds => _triAreaIds;

        public ReadOnlySpan<int> TriStableIds => _triStableIds;

        /// <summary>
        /// Typed per-triangle flags (byte-backed enum storage; no cast allocation).
        /// </summary>
        public ReadOnlySpan<NavTriangleSurfaceFlags> TriFlags => _triFlags;

        private static void ValidateTriFlags(NavTriangleSurfaceFlags flags, int triangleIndex, int stableId)
        {
            // Hot validation: explicit bit checks only — no Enum.IsDefined / boxing.
            const string triFlagsOwner = "triFlags";
            NavTriangleSurfaceFlags unknown = flags & ~ValidFlagsMask;
            if (unknown != 0)
            {
                throw new ArgumentException(
                    $"Triangle {triangleIndex} (stable id {stableId}) has unknown {triFlagsOwner} bits {(byte)unknown}; " +
                    $"owner {triFlagsOwner}. Valid values are {nameof(NavTriangleSurfaceFlags.Solid)} and " +
                    $"{nameof(NavTriangleSurfaceFlags.Solid)}|{nameof(NavTriangleSurfaceFlags.WalkCandidate)}.",
                    triFlagsOwner);
            }

            if (flags == 0)
            {
                throw new ArgumentException(
                    $"Triangle {triangleIndex} (stable id {stableId}) has zero {triFlagsOwner}; " +
                    $"owner {triFlagsOwner}. Valid values are {nameof(NavTriangleSurfaceFlags.Solid)} and " +
                    $"{nameof(NavTriangleSurfaceFlags.Solid)}|{nameof(NavTriangleSurfaceFlags.WalkCandidate)}.",
                    triFlagsOwner);
            }

            if ((flags & NavTriangleSurfaceFlags.Solid) == 0)
            {
                throw new ArgumentException(
                    $"Triangle {triangleIndex} (stable id {stableId}) has {nameof(NavTriangleSurfaceFlags.WalkCandidate)} without " +
                    $"{nameof(NavTriangleSurfaceFlags.Solid)}; owner {triFlagsOwner}.",
                    triFlagsOwner);
            }
        }

        private static void ValidateVertexIndex(int index, int vertexCount, string paramName, int triangleIndex)
        {
            if ((uint)index >= (uint)vertexCount)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    index,
                    $"Triangle {triangleIndex} references vertex index {index}, but vertex count is {vertexCount}.");
            }
        }
    }
}
