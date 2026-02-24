using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    /// <summary>
    /// Extended bake artifact with full replay data for debugging.
    /// </summary>
    public sealed class ExtendedBakeArtifact
    {
        public NavTileId TileId { get; }
        public uint TileVersion { get; }
        public NavBakeStage FailedStage { get; }
        public NavBakeErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public DateTime Timestamp { get; }

        // Pipeline statistics
        public int InputWalkableTriangles { get; set; }
        public int ExtractedRingCount { get; set; }
        public int ProcessedPolygonCount { get; set; }
        public int OutputTriangleCount { get; set; }
        public int OutputVertexCount { get; set; }
        public int OutputPortalCount { get; set; }

        // Detailed data for replay/debugging
        public TriWalkMask? WalkMask { get; set; }
        public List<IntRing> ContourRings { get; set; }
        public ValidPolygonSet? PolygonSet { get; set; }
        public TriMesh? TriMesh { get; set; }
        public List<string> Logs { get; }

        public ExtendedBakeArtifact(NavTileId tileId, uint tileVersion, NavBakeStage failedStage, NavBakeErrorCode errorCode, string errorMessage)
        {
            TileId = tileId;
            TileVersion = tileVersion;
            FailedStage = failedStage;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? "";
            Timestamp = DateTime.UtcNow;
            Logs = new List<string>();
        }

        /// <summary>
        /// Converts to basic NavBakeArtifact for standard API compatibility.
        /// </summary>
        public NavBakeArtifact ToBasicArtifact()
        {
            return new NavBakeArtifact(
                TileId,
                TileVersion,
                FailedStage,
                ErrorCode,
                ErrorMessage,
                InputWalkableTriangles,
                OutputVertexCount,
                OutputTriangleCount,
                OutputPortalCount,
                Logs.ToArray());
        }

        /// <summary>
        /// Generates a human-readable report of the bake artifact.
        /// </summary>
        public string GenerateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== NavMesh Bake Artifact Report ===");
            sb.AppendLine($"Tile: ({TileId.ChunkX}, {TileId.ChunkY}, Layer={TileId.Layer})");
            sb.AppendLine($"Version: {TileVersion}");
            sb.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            if (ErrorCode != NavBakeErrorCode.None)
            {
                sb.AppendLine($"[FAILED] Stage: {FailedStage}");
                sb.AppendLine($"Error Code: {ErrorCode}");
                sb.AppendLine($"Message: {ErrorMessage}");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("[SUCCESS]");
                sb.AppendLine();
            }

            sb.AppendLine("Pipeline Statistics:");
            sb.AppendLine($"  - Input Walkable Triangles: {InputWalkableTriangles}");
            sb.AppendLine($"  - Extracted Rings: {ExtractedRingCount}");
            sb.AppendLine($"  - Processed Polygons: {ProcessedPolygonCount}");
            sb.AppendLine($"  - Output Triangles: {OutputTriangleCount}");
            sb.AppendLine($"  - Output Vertices: {OutputVertexCount}");
            sb.AppendLine($"  - Output Portals: {OutputPortalCount}");
            sb.AppendLine();

            if (Logs.Count > 0)
            {
                sb.AppendLine("Pipeline Logs:");
                foreach (var log in Logs)
                {
                    sb.AppendLine($"  {log}");
                }
                sb.AppendLine();
            }

            if (WalkMask.HasValue)
            {
                sb.AppendLine("WalkMask Details:");
                sb.AppendLine($"  - Tile Size: {WalkMask.Value.TileWidth}x{WalkMask.Value.TileHeight}");
                sb.AppendLine($"  - Walkable Count: {WalkMask.Value.WalkableTriangleCount}");
            }

            if (ContourRings != null)
            {
                sb.AppendLine("Contour Rings:");
                int outerCount = 0, holeCount = 0;
                foreach (var ring in ContourRings)
                {
                    if (ring.IsOuter) outerCount++;
                    else holeCount++;
                }
                sb.AppendLine($"  - Outer Boundaries: {outerCount}");
                sb.AppendLine($"  - Holes: {holeCount}");
            }

            if (PolygonSet.HasValue)
            {
                sb.AppendLine("Polygon Set:");
                sb.AppendLine($"  - Polygon Count: {PolygonSet.Value.Polygons.Length}");
                sb.AppendLine($"  - Has Warnings: {PolygonSet.Value.HasWarnings}");
                if (PolygonSet.Value.HasWarnings)
                {
                    foreach (var warning in PolygonSet.Value.Warnings)
                    {
                        sb.AppendLine($"    ! {warning}");
                    }
                }
            }

            if (TriMesh.HasValue)
            {
                sb.AppendLine("TriMesh:");
                sb.AppendLine($"  - Vertices: {TriMesh.Value.VertexCount}");
                sb.AppendLine($"  - Triangles: {TriMesh.Value.TriangleCount}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Builds and manages extended bake artifacts.
    /// </summary>
    public static class BakeArtifactBuilder
    {
        /// <summary>
        /// Creates an extended artifact from pipeline context.
        /// </summary>
        public static ExtendedBakeArtifact CreateFromContext(
            BakePipelineContext context,
            NavTileId tileId,
            uint tileVersion,
            NavBakeStage failedStage,
            NavBakeErrorCode errorCode,
            string errorMessage,
            bool includeDetailedData = true)
        {
            var artifact = new ExtendedBakeArtifact(tileId, tileVersion, failedStage, errorCode, errorMessage);

            // Copy logs
            artifact.Logs.AddRange(context.Logs);

            // Statistics
            if (context.WalkMask.Walkable != null)
            {
                artifact.InputWalkableTriangles = context.WalkMask.WalkableTriangleCount;
            }

            if (context.ContourRings != null)
            {
                artifact.ExtractedRingCount = context.ContourRings.Count;
            }

            if (context.PolygonSet.Polygons != null)
            {
                artifact.ProcessedPolygonCount = context.PolygonSet.Polygons.Length;
            }

            if (context.TriMesh.Vertices != null)
            {
                artifact.OutputVertexCount = context.TriMesh.VertexCount;
                artifact.OutputTriangleCount = context.TriMesh.TriangleCount;
            }

            // Detailed data for debugging
            if (includeDetailedData)
            {
                if (context.WalkMask.Walkable != null)
                {
                    artifact.WalkMask = context.WalkMask;
                }

                if (context.ContourRings != null)
                {
                    artifact.ContourRings = new List<IntRing>(context.ContourRings);
                }

                if (context.PolygonSet.Polygons != null)
                {
                    artifact.PolygonSet = context.PolygonSet;
                }

                if (context.TriMesh.Vertices != null)
                {
                    artifact.TriMesh = context.TriMesh;
                }
            }

            return artifact;
        }

        /// <summary>
        /// Serializes contour rings to a simple text format for external analysis.
        /// </summary>
        public static void WriteContourRingsToFile(string path, List<IntRing> rings)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine($"# Contour Rings Export");
            writer.WriteLine($"# Ring Count: {rings.Count}");
            writer.WriteLine();

            for (int i = 0; i < rings.Count; i++)
            {
                var ring = rings[i];
                writer.WriteLine($"# Ring {i}: {(ring.IsOuter ? "OUTER" : "HOLE")}, Points: {ring.Points.Length}, Area2: {ring.SignedArea2}");
                writer.WriteLine($"RING {i}");
                foreach (var p in ring.Points)
                {
                    writer.WriteLine($"{p.X} {p.Y}");
                }
                writer.WriteLine("END");
                writer.WriteLine();
            }
        }

        /// <summary>
        /// Serializes polygon set to a simple text format for external analysis.
        /// </summary>
        public static void WritePolygonSetToFile(string path, ValidPolygonSet polygonSet)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine($"# Polygon Set Export");
            writer.WriteLine($"# Polygon Count: {polygonSet.Polygons.Length}");
            if (polygonSet.HasWarnings)
            {
                foreach (var warning in polygonSet.Warnings)
                {
                    writer.WriteLine($"# Warning: {warning}");
                }
            }
            writer.WriteLine();

            for (int i = 0; i < polygonSet.Polygons.Length; i++)
            {
                var polygon = polygonSet.Polygons[i];
                writer.WriteLine($"POLYGON {i}");
                writer.WriteLine("OUTER");
                foreach (var p in polygon.Outer)
                {
                    writer.WriteLine($"{p.X} {p.Y}");
                }
                writer.WriteLine("END_OUTER");

                for (int h = 0; h < polygon.Holes.Length; h++)
                {
                    writer.WriteLine($"HOLE {h}");
                    foreach (var p in polygon.Holes[h])
                    {
                        writer.WriteLine($"{p.X} {p.Y}");
                    }
                    writer.WriteLine("END_HOLE");
                }

                writer.WriteLine("END_POLYGON");
                writer.WriteLine();
            }
        }

        /// <summary>
        /// Serializes the triangulated mesh to OBJ format for external visualization.
        /// </summary>
        public static void WriteTriMeshToObjFile(string path, TriMesh mesh, float zScale = 1.0f)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine("# NavMesh TriMesh Export");
            writer.WriteLine($"# Vertices: {mesh.VertexCount}");
            writer.WriteLine($"# Triangles: {mesh.TriangleCount}");
            writer.WriteLine();

            // Write vertices (OBJ uses Y-up, so we use X, Z, Y from XZ plane)
            foreach (var v in mesh.Vertices)
            {
                writer.WriteLine($"v {v.X:F6} 0.0 {v.Y * zScale:F6}");
            }
            writer.WriteLine();

            // Write faces (OBJ indices are 1-based)
            for (int i = 0; i < mesh.TriangleCount; i++)
            {
                int a = mesh.Triangles[i * 3 + 0] + 1;
                int b = mesh.Triangles[i * 3 + 1] + 1;
                int c = mesh.Triangles[i * 3 + 2] + 1;
                writer.WriteLine($"f {a} {b} {c}");
            }
        }

        /// <summary>
        /// Writes a complete debug dump of the artifact to a directory.
        /// </summary>
        public static void WriteDiagnosticDump(string directory, ExtendedBakeArtifact artifact)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string baseName = $"tile_{artifact.TileId.ChunkX}_{artifact.TileId.ChunkY}_{artifact.TileVersion}";

            // Write report
            File.WriteAllText(Path.Combine(directory, $"{baseName}_report.txt"), artifact.GenerateReport());

            // Write contour rings if available
            if (artifact.ContourRings != null && artifact.ContourRings.Count > 0)
            {
                WriteContourRingsToFile(Path.Combine(directory, $"{baseName}_contours.txt"), artifact.ContourRings);
            }

            // Write polygon set if available
            if (artifact.PolygonSet.HasValue && artifact.PolygonSet.Value.Polygons.Length > 0)
            {
                WritePolygonSetToFile(Path.Combine(directory, $"{baseName}_polygons.txt"), artifact.PolygonSet.Value);
            }

            // Write tri mesh if available
            if (artifact.TriMesh.HasValue && artifact.TriMesh.Value.VertexCount > 0)
            {
                WriteTriMeshToObjFile(Path.Combine(directory, $"{baseName}_mesh.obj"), artifact.TriMesh.Value);
            }

            // Write walk mask visualization if available
            if (artifact.WalkMask.HasValue)
            {
                WriteWalkMaskVisualization(Path.Combine(directory, $"{baseName}_walkmask.txt"), artifact.WalkMask.Value);
            }
        }

        /// <summary>
        /// Writes a text visualization of the walk mask.
        /// </summary>
        private static void WriteWalkMaskVisualization(string path, TriWalkMask mask)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine($"# WalkMask Visualization");
            writer.WriteLine($"# Size: {mask.TileWidth}x{mask.TileHeight}");
            writer.WriteLine($"# Legend: . = no walkable, 1 = tri0 only, 2 = tri1 only, * = both walkable");
            writer.WriteLine();

            for (int r = 0; r < mask.TileHeight; r++)
            {
                var line = new StringBuilder();
                for (int c = 0; c < mask.TileWidth; c++)
                {
                    bool t0 = mask.IsWalkable(c, r, 0);
                    bool t1 = mask.IsWalkable(c, r, 1);

                    if (t0 && t1) line.Append('*');
                    else if (t0) line.Append('1');
                    else if (t1) line.Append('2');
                    else line.Append('.');
                }
                writer.WriteLine(line.ToString());
            }
        }
    }
}
