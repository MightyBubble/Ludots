using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Spatial;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Content fingerprint of the actual bake input snapshot. Single authority for a board
    /// manifest's sourceRevision: hashes what the bake actually consumed — full logic terrain
    /// content, terrain geometry identity (topology / extents / chunking / cell spacing / origin),
    /// layers, per-profile agent geometry (radius/height/clearance/climb/slope) and the
    /// obstacle set plus geometry-affecting config — from the in-memory snapshot the bake used,
    /// never by re-reading a file at publish time.
    ///
    /// Per-agent <c>areaCosts</c> are deliberately orthogonal: they belong to PathingConfig and
    /// never re-bake geometry, so they are not part of this fingerprint.
    /// </summary>
    public static class NavBakeSnapshotFingerprint
    {
        public static string Compute(NavBakeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.Terrain == null) throw new InvalidOperationException("NavBakeContext.terrain is required to fingerprint the bake snapshot.");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> buffer = stackalloc byte[16];

            NavBakeHashShared.AppendText(hash, buffer, "nav-bake-snapshot-v1");

            // Terrain geometry identity (topology / extents / chunking / spacing / origin) —
            // not just the cell contents, so two fields with identical cells but different
            // addressing or topology still differ.
            NavBakeHashShared.AppendText(hash, buffer, context.Terrain.Topology.ToString());
            NavBakeHashShared.AppendInt32(hash, buffer, context.Terrain.WidthCells);
            NavBakeHashShared.AppendInt32(hash, buffer, context.Terrain.HeightCells);
            NavBakeHashShared.AppendInt32(hash, buffer, context.Terrain.ChunkSizeCells);
            NavBakeHashShared.AppendInt32(hash, buffer, context.Terrain.ChunkWidthCm);
            NavBakeHashShared.AppendInt32(hash, buffer, context.Terrain.ChunkHeightCm);
            NavBakeHashShared.AppendInt32(hash, buffer, context.Terrain.HorizontalStepCm);
            NavBakeHashShared.AppendInt32(hash, buffer, context.Terrain.VerticalStepCm);
            context.Terrain.GetWorldPositionMeters(0, 0, out float originXm, out float originZm);
            NavBakeHashShared.AppendInt32(hash, buffer, checked((int)Math.Round(SpatialScaleDefaults.MetersToCentimeters(originXm))));
            NavBakeHashShared.AppendInt32(hash, buffer, checked((int)Math.Round(SpatialScaleDefaults.MetersToCentimeters(originZm))));

            // Geometry-affecting bake configuration and capability.
            NavBakeHashShared.AppendText(hash, buffer, context.Mode.ToString());
            NavBakeHashShared.AppendText(hash, buffer, context.Algorithm.ToString());
            NavBakeHashShared.AppendText(hash, buffer, context.Config?.TerrainFeed ?? NavBakeNames.TerrainFeedTriangles);
            NavBakeHashShared.AppendText(hash, buffer, context.BuildConfig.HeightScaleMeters.ToString("R", CultureInfo.InvariantCulture));
            NavBakeHashShared.AppendText(hash, buffer, context.BuildConfig.MinWalkableUpDot.ToString("R", CultureInfo.InvariantCulture));
            NavBakeHashShared.AppendInt32(hash, buffer, context.BuildConfig.CliffHeightThreshold);

            // Full terrain content (independent of the tile subset this particular bake touched).
            for (int row = 0; row < context.Terrain.HeightCells; row++)
            {
                for (int col = 0; col < context.Terrain.WidthCells; col++)
                {
                    NavBakeHashShared.AppendCellContent(hash, buffer, context.Terrain, col, row);
                }
            }

            // Layers and profile geometry (agent radius/height/clearance, climb/slope) come from
            // the same canonical text the estimator uses for its estimate hash.
            var sb = new StringBuilder(1024);
            NavBakeHashShared.AppendLayersText(sb, context.Config?.Layers);
            NavBakeHashShared.AppendProfilesText(sb, context.Config?.Profiles, context.AgentProfiles);
            NavBakeHashShared.AppendObstaclesText(sb, context.Obstacles?.Obstacles);
            NavBakeHashShared.AppendText(hash, buffer, sb.ToString());

            return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
    }
}
