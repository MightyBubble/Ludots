using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Canonical evidence hashes for triangle-surface input and the complete effective bake config.
    /// Missing measurements must not be encoded as zero — callers must fail when required inputs are absent.
    /// </summary>
    public static class NavBakeCanonicalHash
    {
        private const ulong FnvOffset = 1469598103934665603UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>
        /// Full input hash over every triangle channel, stable id, and owning tile index in stable order.
        /// </summary>
        public static string ComputeTriangleSurfaceInputHash(NavTriangleSurfaceTileIndex triangleSurface)
        {
            if (triangleSurface == null)
            {
                throw new ArgumentNullException(nameof(triangleSurface));
            }

            NavTriangleSurfaceSnapshot surface = triangleSurface.Surface;
            NavTriangleSurfaceTileGrid grid = triangleSurface.Grid;
            ulong hash = FnvOffset;
            hash = Mix(hash, grid.OriginXcm);
            hash = Mix(hash, grid.OriginZcm);
            hash = Mix(hash, grid.TileWidthCm);
            hash = Mix(hash, grid.TileHeightCm);
            hash = Mix(hash, grid.TileCountX);
            hash = Mix(hash, grid.TileCountZ);
            hash = Mix(hash, grid.HaloPaddingCm);
            hash = Mix(hash, surface.VertexCount);
            hash = Mix(hash, surface.TriangleCount);

            ReadOnlySpan<int> vx = surface.VertexXcm;
            ReadOnlySpan<int> vy = surface.VertexYcm;
            ReadOnlySpan<int> vz = surface.VertexZcm;
            for (int i = 0; i < surface.VertexCount; i++)
            {
                hash = Mix(hash, vx[i]);
                hash = Mix(hash, vy[i]);
                hash = Mix(hash, vz[i]);
            }

            ReadOnlySpan<int> ta = surface.TriA;
            ReadOnlySpan<int> tb = surface.TriB;
            ReadOnlySpan<int> tc = surface.TriC;
            ReadOnlySpan<byte> areas = surface.TriAreaIds;
            ReadOnlySpan<int> stables = surface.TriStableIds;
            ReadOnlySpan<NavTriangleSurfaceFlags> flags = surface.TriFlags;

            // Stable tile order (Z then X); each CSR membership contributes tile index + triangle channels.
            for (int tz = 0; tz < grid.TileCountZ; tz++)
            {
                for (int tx = 0; tx < grid.TileCountX; tx++)
                {
                    int tileIndex = checked(tz * grid.TileCountX + tx);
                    ReadOnlySpan<int> membership = triangleSurface.GetTriangleIndices(tx, tz);
                    hash = Mix(hash, tileIndex);
                    hash = Mix(hash, membership.Length);
                    for (int m = 0; m < membership.Length; m++)
                    {
                        int tri = membership[m];
                        hash = Mix(hash, tri);
                        hash = Mix(hash, ta[tri]);
                        hash = Mix(hash, tb[tri]);
                        hash = Mix(hash, tc[tri]);
                        hash = Mix(hash, areas[tri]);
                        hash = Mix(hash, stables[tri]);
                        hash = Mix(hash, (int)flags[tri]);
                    }
                }
            }

            return ToHex(hash);
        }

        /// <summary>
        /// Complete effective <see cref="NavMeshBakeConfig"/> hash including slope Q1M and algorithm settings.
        /// </summary>
        public static string ComputeEffectiveBakeConfigHash(NavMeshBakeConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (config.RuntimeIncremental == null)
            {
                throw new InvalidOperationException(
                    "NavBakeCanonicalHash requires NavMeshBakeConfig.runtimeIncremental.");
            }

            if (config.LayeredSpan == null)
            {
                throw new InvalidOperationException(
                    "NavBakeCanonicalHash requires NavMeshBakeConfig.layeredSpan.");
            }

            if (config.TriangleSurface == null)
            {
                throw new InvalidOperationException(
                    "NavBakeCanonicalHash requires NavMeshBakeConfig.triangleSurface.");
            }

            if (config.Recast == null)
            {
                throw new InvalidOperationException(
                    "NavBakeCanonicalHash requires NavMeshBakeConfig.recast.");
            }

            var sb = new StringBuilder(2048);
            sb.Append(config.Mode).Append('|')
                .Append(config.Algorithm).Append('|');

            AppendProfiles(sb, config);
            AppendLayers(sb, config);
            AppendAreas(sb, config);
            AppendRuntime(sb, config.RuntimeIncremental);
            AppendLayered(sb, config.LayeredSpan);
            sb.Append("triHalo=").Append(config.TriangleSurface.HaloPaddingCm).Append('|')
                .Append("recastCell=").Append(config.Recast.RasterCellSizeCm).Append('x')
                .Append(config.Recast.RasterCellHeightCm);

            byte[] utf8 = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] digest = SHA256.HashData(utf8);
            var hex = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }

        /// <summary>
        /// Canonical offline result order: ChunkY, ChunkX, Layer, ProfileId.
        /// Shared by checksum generation and ordered evidence assertions.
        /// </summary>
        public static int CompareOfflineResultEntries(NavBakeResultEntry a, NavBakeResultEntry b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            int y = a.Target.ChunkY.CompareTo(b.Target.ChunkY);
            if (y != 0) return y;
            int x = a.Target.ChunkX.CompareTo(b.Target.ChunkX);
            if (x != 0) return x;
            int layer = a.Layer.CompareTo(b.Layer);
            if (layer != 0) return layer;
            return string.CompareOrdinal(a.ProfileId, b.ProfileId);
        }

        /// <summary>
        /// FNV-1a mix of every tile checksum from a formal offline <see cref="NavBakeResult"/> in stable order,
        /// plus a generation checksum over the same ordered set.
        /// </summary>
        public static void ComputeOfflineResultChecksums(
            NavBakeResult result,
            out ulong[] orderedTileChecksums,
            out ulong generationChecksum)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (result.FailureCount != 0)
            {
                throw new InvalidOperationException(
                    "NavBakeCanonicalHash cannot checksum a failed offline bake result.");
            }

            var ordered = new NavBakeResultEntry[result.Entries.Count];
            for (int i = 0; i < result.Entries.Count; i++)
            {
                ordered[i] = result.Entries[i];
            }

            Array.Sort(ordered, CompareOfflineResultEntries);

            orderedTileChecksums = new ulong[ordered.Length];
            ulong mix = FnvOffset;
            for (int i = 0; i < ordered.Length; i++)
            {
                ulong tileChecksum = ordered[i].Tile.Checksum;
                orderedTileChecksums[i] = tileChecksum;
                mix = Mix(mix, ordered[i].Target.ChunkY);
                mix = Mix(mix, ordered[i].Target.ChunkX);
                mix = Mix(mix, ordered[i].Layer);
                mix = MixString(mix, ordered[i].ProfileId);
                mix = Mix(mix, tileChecksum);
            }

            generationChecksum = mix;
        }

        private static void AppendProfiles(StringBuilder sb, NavMeshBakeConfig config)
        {
            sb.Append("profiles=");
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                NavMeshAgentProfileConfig profile = config.Profiles[i];
                int slopeQ1M = LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(
                    profile.MaxSlopeDeg,
                    $"{NavMeshConfigPaths.BakeConfigPath}.profiles[{i}].maxSlopeDeg");
                if (i > 0) sb.Append(',');
                sb.Append(profile.Id).Append(':')
                    .Append(profile.MaxClimbCm).Append(':')
                    .Append(profile.MaxSlopeDeg.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(slopeQ1M);
            }

            sb.Append('|');
        }

        private static void AppendLayers(StringBuilder sb, NavMeshBakeConfig config)
        {
            sb.Append("layers=");
            for (int i = 0; i < config.Layers.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(config.Layers[i].Id).Append(':').Append(config.Layers[i].Layer);
            }

            sb.Append('|');
        }

        private static void AppendAreas(StringBuilder sb, NavMeshBakeConfig config)
        {
            sb.Append("areas=");
            for (int i = 0; i < config.Areas.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NavAreaCostConfig area = config.Areas[i];
                sb.Append(area.Id).Append(':')
                    .Append(area.AreaId).Append(':')
                    .Append(area.Cost.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append('|');
        }

        private static void AppendRuntime(StringBuilder sb, NavRuntimeIncrementalConfig runtime)
        {
            sb.Append("runtime=")
                .Append(runtime.TileBudgetPerFixedTick).Append(',')
                .Append(runtime.IncludeNeighborTiles).Append(',')
                .Append(runtime.HeightScaleMeters.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(runtime.MinWalkableUpDot.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(runtime.CliffHeightThreshold).Append(',')
                .Append(runtime.TrackedStructuralEntityCapacity).Append(',')
                .Append(runtime.ObstaclePrimitiveCapacity).Append(',')
                .Append(runtime.PolygonVertexCapacity).Append(',')
                .Append(runtime.DirtyTileCapacity).Append(',')
                .Append(runtime.StagedEntryCapacity).Append(',')
                .Append(runtime.PublishedTileCapacity).Append(',')
                .Append(runtime.StoreGroupCapacity).Append(',')
                .Append(runtime.ResidentTileCapacity).Append(',')
                .Append(runtime.OutputVertexCapacity).Append(',')
                .Append(runtime.OutputTriangleCapacity).Append(',')
                .Append(runtime.OutputPortalCapacity).Append(',')
                .Append(runtime.InitialResidentChunkX).Append(',')
                .Append(runtime.InitialResidentChunkZ).Append(',')
                .Append(runtime.InitialResidentWidthChunks).Append(',')
                .Append(runtime.InitialResidentHeightChunks)
                .Append('|');
        }

        private static void AppendLayered(StringBuilder sb, NavLayeredSpanConfig layered)
        {
            sb.Append("layered=")
                .Append(layered.ScratchSlotCount).Append(',')
                .Append(layered.RasterCellSizeCm).Append(',')
                .Append(layered.RasterHaloCells).Append(',')
                .Append(layered.SameSurfaceToleranceCm).Append(',')
                .Append(layered.MaxSimplificationErrorCm).Append(',')
                .Append(layered.HeightRounding).Append(',')
                .Append(layered.MaxLawsonFlipCount).Append(',')
                .Append(layered.ColumnCapacity).Append(',')
                .Append(layered.SpanCapacity).Append(',')
                .Append(layered.ClassifiedSpanCapacity).Append(',')
                .Append(layered.WalkableSpanCapacity).Append(',')
                .Append(layered.LinkCapacity).Append(',')
                .Append(layered.SheetCapacity).Append(',')
                .Append(layered.PortalIntervalCapacity).Append(',')
                .Append(layered.RegionCapacity).Append(',')
                .Append(layered.ChartCapacity).Append(',')
                .Append(layered.RingCapacity).Append(',')
                .Append(layered.ContourVertexCapacity).Append(',')
                .Append(layered.ContourEdgeCapacity).Append(',')
                .Append(layered.SeamCapacity).Append(',')
                .Append(layered.CanonicalLinkCapacity).Append(',')
                .Append(layered.SplitPointCapacity).Append(',')
                .Append(layered.TriangulationVertexCapacity).Append(',')
                .Append(layered.TriangulationTriangleCapacity).Append(',')
                .Append(layered.ConstrainedEdgeCapacity).Append(',')
                .Append(layered.BorderPortalCapacity).Append(',')
                .Append(layered.PolygonVertexCapacity).Append(',')
                .Append(layered.AdjacencyEdgeCapacity).Append(',')
                .Append(layered.BridgeCandidateCapacity).Append(',')
                .Append(layered.RingWorkCapacity).Append(',')
                .Append(layered.TemporaryConstraintFlagCapacity)
                .Append('|');
        }

        private static ulong Mix(ulong hash, int value)
        {
            hash ^= unchecked((ulong)(uint)value);
            hash *= FnvPrime;
            return hash;
        }

        private static ulong Mix(ulong hash, byte value)
        {
            hash ^= value;
            hash *= FnvPrime;
            return hash;
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            hash ^= value;
            hash *= FnvPrime;
            return hash;
        }

        private static ulong MixString(ulong hash, string value)
        {
            if (value == null)
            {
                hash ^= 0xFFFFFFFFFFFFFFFFUL;
                hash *= FnvPrime;
                return hash;
            }

            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= FnvPrime;
            }

            hash ^= 0xFFUL;
            hash *= FnvPrime;
            return hash;
        }

        private static string ToHex(ulong value)
            => value.ToString("x16", CultureInfo.InvariantCulture);
    }
}
