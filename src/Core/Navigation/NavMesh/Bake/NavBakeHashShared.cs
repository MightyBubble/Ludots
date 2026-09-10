using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.Terrain;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Single shared implementation of the low-level bake hashing primitives. The estimator and
    /// the artifact source fingerprint both consume these helpers so the cell-content encoding,
    /// integer encoding and config/profile/obstacle canonical text cannot diverge between
    /// estimate gating and manifest provenance.
    /// </summary>
    internal static class NavBakeHashShared
    {
        public static void AppendInt32(IncrementalHash hash, Span<byte> buffer, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.Slice(0, 4), value);
            hash.AppendData(buffer.Slice(0, 4));
        }

        public static void AppendInt64(IncrementalHash hash, Span<byte> buffer, long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(0, 8), value);
            hash.AppendData(buffer.Slice(0, 8));
        }

        public static void AppendText(IncrementalHash hash, Span<byte> buffer, string? value)
        {
            // Proper UTF-8 with an explicit length prefix. No per-char byte allocation and no
            // UTF-16 low-byte truncation, so two identifiers that share a low byte still hash
            // differently (e.g. layer ids in different scripts).
            string text = value ?? string.Empty;
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            AppendInt32(hash, buffer, utf8.Length);
            hash.AppendData(utf8);
        }

        /// <summary>
        /// One logic-terrain cell's bake-relevant content. Used verbatim by the estimator's
        /// terrain-content hash and by the artifact source fingerprint over the whole field.
        /// </summary>
        public static void AppendCellContent(
            IncrementalHash hash,
            Span<byte> buffer,
            LogicTerrainField terrain,
            int col,
            int row)
        {
            LogicTerrainCell cell = terrain.GetCell(col, row);
            buffer[0] = cell.HeightLevel;
            buffer[1] = cell.WaterHeightLevel;
            buffer[2] = (byte)cell.SurfaceFlags;
            buffer[3] = cell.AreaId;
            // Note: cell.Cost is legacy residue and deliberately NOT hashed. Per the area/cost
            // contract (#372) terrain cells only carry areaId/tags; traversal cost lives in
            // PathingConfig.agentTypes[].navMesh.areaCosts and must never re-bake geometry. No
            // producer in the repo writes a non-default cell cost and no bake consumer reads it.
            // cell.AreaId IS baked (authored area ids flow into heightfield spans and TriAreaIds),
            // so it stays part of the geometry content.
            for (int edge = 0; edge < 3; edge++)
            {
                buffer[4 + edge] = terrain.TryGetCliffStraightenEdge(col, row, edge, out bool value) && value
                    ? (byte)1
                    : (byte)0;
            }

            hash.AppendData(buffer.Slice(0, 7));
        }

        public static void AppendLayersText(StringBuilder sb, IReadOnlyList<NavLayerConfig>? layers)
        {
            if (layers == null) return;
            sb.Append("layers=");
            for (int i = 0; i < layers.Count; i++)
            {
                NavLayerConfig layer = layers[i];
                sb.Append(layer.Id).Append(':').Append(layer.Layer).Append(';');
            }

            sb.Append('|');
        }

        public static void AppendProfilesText(
            StringBuilder sb,
            IReadOnlyList<NavMeshAgentProfileConfig>? profiles,
            AgentProfileRegistry? agentProfiles)
        {
            if (profiles == null) return;
            if (agentProfiles == null)
            {
                throw new InvalidOperationException(
                    "Profile geometry cannot be fingerprinted without an AgentProfileRegistry; refusing to emit a degraded hash.");
            }

            sb.Append("profiles=");
            for (int i = 0; i < profiles.Count; i++)
            {
                NavMeshAgentProfileConfig navProfile = profiles[i];
                AgentProfileConfig agent = agentProfiles.Require(navProfile.Id, $"{NavMeshConfigPaths.BakeConfigPath}.profiles[{i}]");
                sb.Append(navProfile.Id).Append(':')
                    .Append(navProfile.MaxClimbCm).Append(':')
                    .Append(navProfile.MaxSlopeDeg.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(agent.RadiusCm.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(agent.HeightCm.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                    .Append(agent.ClearanceCm.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }

            sb.Append('|');
        }

        public static void AppendObstaclesText(StringBuilder sb, IReadOnlyList<NavObstacle>? obstacles)
        {
            if (obstacles == null) return;
            sb.Append("obstacles=");
            for (int i = 0; i < obstacles.Count; i++)
            {
                NavObstacle obstacle = obstacles[i];
                sb.Append(obstacle.Id).Append(':')
                    .Append(obstacle.Enabled).Append(':')
                    .Append(obstacle.Kind).Append(':')
                    .Append(obstacle.LayerId).Append(':')
                    .Append(obstacle.AreaId?.ToString(CultureInfo.InvariantCulture) ?? "").Append(':')
                    .Append(obstacle.Center.Xcm).Append(',').Append(obstacle.Center.Zcm).Append(':')
                    .Append(obstacle.RadiusCm).Append(':')
                    .Append(obstacle.A.Xcm).Append(',').Append(obstacle.A.Zcm).Append(':')
                    .Append(obstacle.B.Xcm).Append(',').Append(obstacle.B.Zcm).Append(':');
                if (obstacle.Points != null)
                {
                    for (int p = 0; p < obstacle.Points.Count; p++)
                    {
                        NavPointCm point = obstacle.Points[p];
                        sb.Append(point.Xcm).Append(',').Append(point.Zcm).Append(',');
                    }
                }

                sb.Append(';');
            }

            sb.Append('|');
        }
    }
}
