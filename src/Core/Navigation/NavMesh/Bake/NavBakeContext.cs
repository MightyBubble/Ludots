using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Map.Fields;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public enum NavBakeMode : byte
    {
        Offline = 0,
        RuntimeIncremental = 1
    }

    public enum NavBakeAlgorithmKind : byte
    {
        Recast = 0,
        Cdt = 1
    }

    public readonly struct NavBakeTileCoord : IEquatable<NavBakeTileCoord>
    {
        public NavBakeTileCoord(int chunkX, int chunkY)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
        }

        public int ChunkX { get; }

        public int ChunkY { get; }

        public bool Equals(NavBakeTileCoord other) => ChunkX == other.ChunkX && ChunkY == other.ChunkY;

        public override bool Equals(object obj) => obj is NavBakeTileCoord other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ChunkX, ChunkY);

        public override string ToString() => $"{ChunkX},{ChunkY}";
    }

    public sealed class NavBakeExecutionOptions
    {
        public bool Parallel { get; set; } = true;

        public int MaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount);
    }

    public sealed class NavBakeContext
    {
        public string MapId { get; init; } = string.Empty;

        public string ModId { get; init; } = string.Empty;

        public string SourceUri { get; init; } = string.Empty;

        public LogicTerrainField Terrain { get; init; } = null!;

        public NavObstacleSet Obstacles { get; init; } = new();

        public NavMeshBakeConfig Config { get; init; } = null!;

        public AgentProfileRegistry AgentProfiles { get; init; } = null!;

        public IReadOnlyList<NavBakeTileCoord> Targets { get; init; } = Array.Empty<NavBakeTileCoord>();

        public NavBuildConfig BuildConfig { get; init; }

        public uint TileVersion { get; init; } = 1;

        public NavBakeMode Mode { get; init; } = NavBakeMode.Offline;

        public NavBakeAlgorithmKind Algorithm { get; init; } = NavBakeAlgorithmKind.Recast;

        public NavBakeExecutionOptions Execution { get; init; } = new();

        public void Validate()
        {
            ValidateSourceUri(SourceUri);

            if (Terrain == null)
            {
                throw new InvalidOperationException("NavBakeContext.terrain is required.");
            }

            if (Config == null)
            {
                throw new InvalidOperationException("NavBakeContext.config is required.");
            }

            if (AgentProfiles == null)
            {
                throw new InvalidOperationException("NavBakeContext.agentProfiles is required.");
            }

            if (Obstacles == null)
            {
                throw new InvalidOperationException("NavBakeContext.obstacles is required.");
            }

            if (Targets == null)
            {
                throw new InvalidOperationException("NavBakeContext.targets is required.");
            }

            if (Execution == null)
            {
                throw new InvalidOperationException("NavBakeContext.execution is required.");
            }

            if (Execution.MaxDegreeOfParallelism <= 0)
            {
                throw new InvalidOperationException("NavBakeContext.execution.maxDegreeOfParallelism must be > 0.");
            }

            if (Config.Profiles == null || Config.Profiles.Count == 0)
            {
                throw new InvalidOperationException("NavBakeContext.config.profiles is empty.");
            }

            if (Config.Layers == null || Config.Layers.Count == 0)
            {
                throw new InvalidOperationException("NavBakeContext.config.layers is empty.");
            }

            ValidateObstacles();

            for (int i = 0; i < Targets.Count; i++)
            {
                NavBakeTileCoord target = Targets[i];
                if (target.ChunkX < 0 || target.ChunkY < 0 ||
                    target.ChunkX >= Terrain.WidthChunks ||
                    target.ChunkY >= Terrain.HeightChunks)
                {
                    throw new InvalidOperationException($"NavBakeContext.targets[{i}] is out of terrain range: {target}.");
                }
            }
        }

        private void ValidateObstacles()
        {
            if (Obstacles.Obstacles == null)
            {
                throw new InvalidOperationException("NavBakeContext.obstacles.obstacles is required.");
            }

            var layerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Config.Layers.Count; i++)
            {
                NavLayerConfig layer = Config.Layers[i]
                    ?? throw new InvalidOperationException($"NavBakeContext.config.layers[{i}] is null.");
                if (string.IsNullOrWhiteSpace(layer.Id) ||
                    !string.Equals(layer.Id.Trim(), layer.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"NavBakeContext.config.layers[{i}].id must be a non-empty trimmed string.");
                }

                if (!layerIds.Add(layer.Id))
                {
                    throw new InvalidOperationException($"NavBakeContext.config.layers contains duplicate id '{layer.Id}'.");
                }
            }

            for (int i = 0; i < Obstacles.Obstacles.Count; i++)
            {
                NavObstacle obstacle = Obstacles.Obstacles[i]
                    ?? throw new InvalidOperationException($"NavBakeContext.obstacles.obstacles[{i}] is null.");
                string path = $"NavBakeContext.obstacles.obstacles[{i}]";
                if (string.IsNullOrWhiteSpace(obstacle.Id) ||
                    !string.Equals(obstacle.Id.Trim(), obstacle.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.id must be a non-empty trimmed string.");
                }

                if (string.IsNullOrWhiteSpace(obstacle.LayerId) ||
                    !string.Equals(obstacle.LayerId.Trim(), obstacle.LayerId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"{path}.layerId must be a non-empty trimmed string.");
                }

                if (!layerIds.Contains(obstacle.LayerId))
                {
                    throw new InvalidOperationException($"{path}.layerId references unknown nav layer '{obstacle.LayerId}'.");
                }

                if (obstacle.AreaId.HasValue && ((uint)obstacle.AreaId.Value > byte.MaxValue))
                {
                    throw new InvalidOperationException($"{path}.areaId must be between 0 and 255.");
                }

                switch (obstacle.Kind)
                {
                    case NavObstacleKind.Circle:
                        if (obstacle.RadiusCm <= 0)
                        {
                            throw new InvalidOperationException($"{path}.radiusCm must be > 0 for circle obstacles.");
                        }
                        break;
                    case NavObstacleKind.Polygon:
                        if (obstacle.Points == null || obstacle.Points.Count < 3)
                        {
                            throw new InvalidOperationException($"{path}.points must contain at least 3 points for polygon obstacles.");
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"{path}.kind '{obstacle.Kind}' is not supported by navmesh bake.");
                }
            }
        }

        private static void ValidateSourceUri(string sourceUri)
        {
            if (string.IsNullOrWhiteSpace(sourceUri))
            {
                throw new InvalidOperationException("NavBakeContext.sourceUri is required.");
            }

            string[] parts = sourceUri.Split(new[] { ':' }, 2);
            if (parts.Length != 2 ||
                string.IsNullOrWhiteSpace(parts[0]) ||
                string.IsNullOrWhiteSpace(parts[1]))
            {
                throw new InvalidOperationException("NavBakeContext.sourceUri must use VFS URI form 'ModId:relative/path'.");
            }

            string relativePath = parts[1];
            bool windowsRooted = relativePath.Length >= 3 &&
                char.IsLetter(relativePath[0]) &&
                relativePath[1] == ':' &&
                (relativePath[2] == '/' || relativePath[2] == '\\');
            if (relativePath[0] == '/' || relativePath[0] == '\\' || windowsRooted)
            {
                throw new InvalidOperationException("NavBakeContext.sourceUri must be a VFS-relative URI, not an absolute filesystem path.");
            }
        }
    }

    public static class NavBakeNames
    {
        public const string ModeOffline = "offline";
        public const string ModeRuntimeIncremental = "runtime-incremental";
        public const string AlgorithmRecast = "recast";
        public const string AlgorithmCdt = "cdt";

        public static NavBakeMode ParseMode(string text, string path)
        {
            if (string.Equals(text, ModeOffline, StringComparison.Ordinal)) return NavBakeMode.Offline;
            if (string.Equals(text, ModeRuntimeIncremental, StringComparison.Ordinal)) return NavBakeMode.RuntimeIncremental;
            throw new InvalidOperationException($"{path} must be '{ModeOffline}' or '{ModeRuntimeIncremental}'.");
        }

        public static NavBakeAlgorithmKind ParseAlgorithm(string text, string path)
        {
            if (string.Equals(text, AlgorithmRecast, StringComparison.Ordinal)) return NavBakeAlgorithmKind.Recast;
            if (string.Equals(text, AlgorithmCdt, StringComparison.Ordinal)) return NavBakeAlgorithmKind.Cdt;
            throw new InvalidOperationException($"{path} must be '{AlgorithmRecast}' or '{AlgorithmCdt}'.");
        }

        public static string FormatAlgorithm(NavBakeAlgorithmKind algorithm)
            => algorithm == NavBakeAlgorithmKind.Recast ? AlgorithmRecast : AlgorithmCdt;

        public static string FormatMode(NavBakeMode mode)
            => mode == NavBakeMode.Offline ? ModeOffline : ModeRuntimeIncremental;
    }
}
