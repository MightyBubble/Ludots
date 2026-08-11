using System;
using System.Collections.Generic;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Navigation.Terrain;

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
        ExactCdt = 1,
        LayeredSpan = 2
    }

    public enum NavBakeInputKind : byte
    {
        LogicTerrain = 0,
        TriangleSurface = 1
    }

    [Flags]
    public enum NavBakeAdapterCapabilities : byte
    {
        None = 0,
        OfflineLogicTerrain = 1 << 0,
        OfflineTriangleSurface = 1 << 1,
        RuntimeIncrementalLogicTerrain = 1 << 2,
        RuntimeIncrementalTriangleSurface = 1 << 3
    }

    public static class NavBakeAdapterCapability
    {
        public static NavBakeAdapterCapabilities Require(NavBakeMode mode, NavBakeInputKind inputKind)
        {
            return (mode, inputKind) switch
            {
                (NavBakeMode.Offline, NavBakeInputKind.LogicTerrain)
                    => NavBakeAdapterCapabilities.OfflineLogicTerrain,
                (NavBakeMode.Offline, NavBakeInputKind.TriangleSurface)
                    => NavBakeAdapterCapabilities.OfflineTriangleSurface,
                (NavBakeMode.RuntimeIncremental, NavBakeInputKind.LogicTerrain)
                    => NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain,
                (NavBakeMode.RuntimeIncremental, NavBakeInputKind.TriangleSurface)
                    => NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface,
                _ => throw new InvalidOperationException(
                    $"Unsupported bake mode/input combination: {NavBakeNames.FormatMode(mode)}/{FormatInputKind(inputKind)}.")
            };
        }

        public static string FormatInputKind(NavBakeInputKind inputKind)
            => inputKind switch
            {
                NavBakeInputKind.LogicTerrain => "logic-terrain",
                NavBakeInputKind.TriangleSurface => "triangle-surface",
                _ => throw new InvalidOperationException($"Unknown NavBakeInputKind '{inputKind}'.")
            };

        /// <summary>
        /// Boolean mode support derived from the capability flags: a mode is supported when at least one
        /// input flag of that mode is declared. Adapters must keep <see cref="INavBakeAlgorithm.SupportsMode"/>
        /// consistent with this derivation.
        /// </summary>
        public static bool SupportsMode(NavBakeAdapterCapabilities capabilities, NavBakeMode mode)
        {
            return (capabilities & ModeMask(mode)) != 0;
        }

        public static void ValidateConsistency(
            NavBakeAlgorithmKind kind,
            NavBakeAdapterCapabilities capabilities,
            Func<NavBakeMode, bool> supportsMode)
        {
            for (NavBakeMode mode = NavBakeMode.Offline; mode <= NavBakeMode.RuntimeIncremental; mode++)
            {
                bool declared = supportsMode(mode);
                bool derived = SupportsMode(capabilities, mode);
                if (declared != derived)
                {
                    throw new InvalidOperationException(
                        $"NavBakeService adapter '{NavBakeNames.FormatAlgorithm(kind)}' capability matrix is inconsistent: " +
                        $"SupportsMode({NavBakeNames.FormatMode(mode)})={declared} but Capabilities={capabilities}.");
                }
            }
        }

        private static NavBakeAdapterCapabilities ModeMask(NavBakeMode mode)
        {
            return mode switch
            {
                NavBakeMode.Offline => NavBakeAdapterCapabilities.OfflineLogicTerrain | NavBakeAdapterCapabilities.OfflineTriangleSurface,
                NavBakeMode.RuntimeIncremental => NavBakeAdapterCapabilities.RuntimeIncrementalLogicTerrain | NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"Unknown nav bake mode '{mode}'.")
            };
        }
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

        public LogicTerrainField? Terrain { get; init; }

        /// <summary>
        /// Mutable: the runtime rebuild queue owns in-place replacement of the active surface
        /// (<see cref="RuntimeIncrementalNavMeshRebuildQueue.ReplaceTriangleSurface"/>) so sealed
        /// generations never observe a mixed surface. Cold construction remains init-only elsewhere.
        /// </summary>
        public NavTriangleSurfaceTileIndex? TriangleSurface { get; set; }

        public INavObstacleSource Obstacles { get; init; } = new NavObstacleSet();

        public NavMeshBakeConfig Config { get; init; } = null!;

        public AgentProfileRegistry AgentProfiles { get; init; } = null!;

        public IReadOnlyList<NavBakeTileCoord> Targets { get; init; } = Array.Empty<NavBakeTileCoord>();

        public NavBuildConfig BuildConfig { get; init; }

        public uint TileVersion { get; set; } = 1;

        public NavBakeMode Mode { get; init; } = NavBakeMode.Offline;

        /// <summary>
        /// Mutable: the runtime rebuild queue switches the active bake adapter on its frame context
        /// during an algorithm-switch generation (<see cref="RuntimeIncrementalNavMeshRebuildQueue.SwitchAlgorithm"/>)
        /// while the committed/visible algorithm stays unchanged until atomic commit.
        /// </summary>
        public NavBakeAlgorithmKind Algorithm { get; set; } = NavBakeAlgorithmKind.Recast;

        public NavBakeExecutionOptions Execution { get; init; } = new();

        public NavBakeInputKind InputKind
        {
            get
            {
                bool hasTerrain = Terrain != null;
                bool hasTriangleSurface = TriangleSurface != null;
                if (hasTerrain == hasTriangleSurface)
                {
                    throw new InvalidOperationException(
                        hasTerrain
                            ? "NavBakeContext requires exactly one of terrain or triangleSurface; both were provided."
                            : "NavBakeContext requires exactly one of terrain or triangleSurface; neither was provided.");
                }

                return hasTerrain ? NavBakeInputKind.LogicTerrain : NavBakeInputKind.TriangleSurface;
            }
        }

        public LogicTerrainField RequireTerrain()
        {
            if (InputKind != NavBakeInputKind.LogicTerrain || Terrain == null)
            {
                throw new InvalidOperationException(
                    "NavBakeContext.RequireTerrain failed: active input is not LogicTerrain.");
            }

            return Terrain;
        }

        public NavTriangleSurfaceTileIndex RequireTriangleSurface()
        {
            if (InputKind != NavBakeInputKind.TriangleSurface || TriangleSurface == null)
            {
                throw new InvalidOperationException(
                    "NavBakeContext.RequireTriangleSurface failed: active input is not TriangleSurface.");
            }

            return TriangleSurface;
        }

        public void Validate()
        {
            ValidateSourceUri(SourceUri);

            // Resolve InputKind first so both/neither fail before other fields are checked.
            NavBakeInputKind inputKind = InputKind;

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

            ValidateLayerIds();
            Obstacles.ValidateForBake(Config.Layers, "NavBakeContext.obstacles");
            ValidateTargets(inputKind);
        }

        private void ValidateLayerIds()
        {
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
        }

        private void ValidateTargets(NavBakeInputKind inputKind)
        {
            int width;
            int height;
            string rangeOwner;
            switch (inputKind)
            {
                case NavBakeInputKind.LogicTerrain:
                    LogicTerrainField terrain = Terrain!;
                    width = terrain.WidthChunks;
                    height = terrain.HeightChunks;
                    rangeOwner = "terrain";
                    break;
                case NavBakeInputKind.TriangleSurface:
                    NavTriangleSurfaceTileGrid grid = TriangleSurface!.Grid;
                    width = grid.TileCountX;
                    height = grid.TileCountZ;
                    rangeOwner = "triangleSurface.grid";
                    break;
                default:
                    throw new InvalidOperationException($"Unknown NavBakeInputKind '{inputKind}'.");
            }

            for (int i = 0; i < Targets.Count; i++)
            {
                NavBakeTileCoord target = Targets[i];
                if (target.ChunkX < 0 || target.ChunkY < 0 ||
                    target.ChunkX >= width ||
                    target.ChunkY >= height)
                {
                    throw new InvalidOperationException(
                        $"NavBakeContext.targets[{i}] is out of {rangeOwner} range: {target}.");
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
        public const string AlgorithmExactCdt = "exact-cdt";
        public const string AlgorithmLayeredSpan = "layered-span";

        public static NavBakeMode ParseMode(string text, string path)
        {
            if (string.Equals(text, ModeOffline, StringComparison.Ordinal)) return NavBakeMode.Offline;
            if (string.Equals(text, ModeRuntimeIncremental, StringComparison.Ordinal)) return NavBakeMode.RuntimeIncremental;
            throw new InvalidOperationException($"{path} must be '{ModeOffline}' or '{ModeRuntimeIncremental}'.");
        }

        public static NavBakeAlgorithmKind ParseAlgorithm(string text, string path)
        {
            if (string.Equals(text, AlgorithmRecast, StringComparison.Ordinal)) return NavBakeAlgorithmKind.Recast;
            if (string.Equals(text, AlgorithmExactCdt, StringComparison.Ordinal)) return NavBakeAlgorithmKind.ExactCdt;
            if (string.Equals(text, AlgorithmLayeredSpan, StringComparison.Ordinal)) return NavBakeAlgorithmKind.LayeredSpan;
            throw new InvalidOperationException(
                $"{path} must be '{AlgorithmRecast}', '{AlgorithmExactCdt}', or '{AlgorithmLayeredSpan}'.");
        }

        public static string FormatAlgorithm(NavBakeAlgorithmKind algorithm)
        {
            return algorithm switch
            {
                NavBakeAlgorithmKind.Recast => AlgorithmRecast,
                NavBakeAlgorithmKind.ExactCdt => AlgorithmExactCdt,
                NavBakeAlgorithmKind.LayeredSpan => AlgorithmLayeredSpan,
                _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, $"Unknown nav bake algorithm kind '{algorithm}'.")
            };
        }

        public static string FormatMode(NavBakeMode mode)
        {
            return mode switch
            {
                NavBakeMode.Offline => ModeOffline,
                NavBakeMode.RuntimeIncremental => ModeRuntimeIncremental,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"Unknown nav bake mode '{mode}'.")
            };
        }
    }
}
