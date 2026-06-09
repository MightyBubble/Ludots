using System;
using System.Collections.Generic;

namespace Ludots.Core.Navigation.NavMesh.Diagnostics
{
    public static class NavBakeDiagnosticsContract
    {
        public const string SchemaVersion = "ludots.nav-bake-diagnostics.v1";
        public const int MaxFailureSamples = 128;
    }

    public sealed class NavBakeDiagnosticsDocument
    {
        public string SchemaVersion { get; set; } = NavBakeDiagnosticsContract.SchemaVersion;
        public string MapId { get; set; } = string.Empty;
        public string Tool { get; set; } = string.Empty;
        public string SourceMapPath { get; set; } = string.Empty;
        public string GeneratedAtUtc { get; set; } = string.Empty;
        public int TargetChunkCount { get; set; }
        public int WorldChunkCount { get; set; }
        public int ActiveWindowMinChunkX { get; set; } = -1;
        public int ActiveWindowMinChunkY { get; set; } = -1;
        public int ActiveWindowMaxChunkX { get; set; } = -1;
        public int ActiveWindowMaxChunkY { get; set; } = -1;
        public int ActiveWindowChunkCount { get; set; }
        public bool IsPartialCoverage { get; set; }
        public int LayerCount { get; set; }
        public int ProfileCount { get; set; }
        public int TotalExpectedTileBakes { get; set; }
        public int TotalBakedTiles { get; set; }
        public int TotalFailedTiles { get; set; }
        public List<NavBakeLayerProfileSummary> LayerProfiles { get; set; } = new();
        public List<NavBakeFailureSample> FailureSamples { get; set; } = new();
    }

    public sealed class NavBakeLayerProfileSummary
    {
        public int Layer { get; set; }
        public string LayerId { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public int TargetChunks { get; set; }
        public int BakedTiles { get; set; }
        public int FailedTiles { get; set; }
        public int MissingTiles { get; set; }
        public int DirtyTiles { get; set; }
        public int NotLoadedTiles { get; set; }
        public int CoveragePercent { get; set; }
        public bool IsComplete { get; set; }

        public static NavBakeLayerProfileSummary Create(
            int layer,
            string layerId,
            string profileId,
            int targetChunks,
            int bakedTiles,
            int failedTiles,
            int missingTiles,
            int dirtyTiles,
            int notLoadedTiles)
        {
            if (targetChunks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetChunks));
            }

            if (bakedTiles < 0 || failedTiles < 0 || missingTiles < 0 || dirtyTiles < 0 || notLoadedTiles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bakedTiles), "Nav bake tile counts must be >= 0.");
            }

            int coverage = targetChunks > 0
                ? (int)MathF.Round(bakedTiles * 100f / targetChunks)
                : 0;
            return new NavBakeLayerProfileSummary
            {
                Layer = layer,
                LayerId = layerId ?? string.Empty,
                ProfileId = profileId ?? string.Empty,
                TargetChunks = targetChunks,
                BakedTiles = bakedTiles,
                FailedTiles = failedTiles,
                MissingTiles = missingTiles,
                DirtyTiles = dirtyTiles,
                NotLoadedTiles = notLoadedTiles,
                CoveragePercent = coverage,
                IsComplete = targetChunks > 0 &&
                    bakedTiles == targetChunks &&
                    failedTiles == 0 &&
                    missingTiles == 0 &&
                    dirtyTiles == 0 &&
                    notLoadedTiles == 0
            };
        }
    }

    public sealed class NavBakeFailureSample
    {
        public int ChunkX { get; set; }
        public int ChunkY { get; set; }
        public int Layer { get; set; }
        public string LayerId { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
