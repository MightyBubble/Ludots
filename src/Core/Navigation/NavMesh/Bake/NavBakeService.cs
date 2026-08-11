using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public interface INavBakeAlgorithm
    {
        NavBakeAlgorithmKind Kind { get; }

        /// <summary>
        /// Runtime authority for mode/input gating. Phase 0 boolean members must stay derived-consistent:
        /// <see cref="SupportsMode"/> equals whether any input flag of that mode is declared.
        /// </summary>
        NavBakeAdapterCapabilities Capabilities { get; }

        bool SupportsMode(NavBakeMode mode);

        bool GuaranteesBitwiseDeterminism { get; }

        bool Supports3DMultiLayer { get; }

        bool IsZeroAllocationHotPath { get; }

        bool TryBake(
            NavBakeContext context,
            NavBakeTileCoord target,
            NavLayerConfig layer,
            NavMeshAgentProfileConfig navProfile,
            AgentProfileConfig agentProfile,
            out NavTile tile,
            out byte[] detourTileBytes,
            out NavBakeArtifact artifact);

        /// <summary>
        /// Runtime bake into a caller-owned banked <see cref="NavTile"/>.
        /// Default bridge may allocate (Recast/CDT honesty); LayeredSpan overrides for 0GC.
        /// </summary>
        bool TryBakeInto(
            NavBakeContext context,
            NavBakeTileCoord target,
            NavLayerConfig layer,
            NavMeshAgentProfileConfig navProfile,
            AgentProfileConfig agentProfile,
            NavTile destination,
            Span<byte> checksumScratch,
            out NavBakeArtifact artifact)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            bool success = TryBake(
                context,
                target,
                layer,
                navProfile,
                agentProfile,
                out NavTile tile,
                out _,
                out artifact);
            if (!success)
            {
                destination.ClearTopology();
                return false;
            }

            destination.CopyGeometryFrom(tile);
            return true;
        }
    }

    /// <summary>
    /// Exact constrained-triangulation adapter over triangle-surface input.
    /// </summary>
    public sealed class ExactCdtNavBakeAlgorithm : INavBakeAlgorithm
    {
        private const int DefaultMaxLawsonFlipCount = 100_000;

        public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.ExactCdt;

        public NavBakeAdapterCapabilities Capabilities =>
            NavBakeAdapterCapabilities.OfflineTriangleSurface |
            NavBakeAdapterCapabilities.RuntimeIncrementalTriangleSurface;

        public bool SupportsMode(NavBakeMode mode)
        {
            return mode switch
            {
                NavBakeMode.Offline => true,
                NavBakeMode.RuntimeIncremental => true,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"Unknown nav bake mode '{mode}'.")
            };
        }

        public bool GuaranteesBitwiseDeterminism => true;

        public bool Supports3DMultiLayer => true;

        public bool IsZeroAllocationHotPath => false;

        public bool TryBake(
            NavBakeContext context,
            NavBakeTileCoord target,
            NavLayerConfig layer,
            NavMeshAgentProfileConfig navProfile,
            AgentProfileConfig agentProfile,
            out NavTile tile,
            out byte[] detourTileBytes,
            out NavBakeArtifact artifact)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (navProfile == null) throw new ArgumentNullException(nameof(navProfile));
            if (agentProfile == null) throw new ArgumentNullException(nameof(agentProfile));

            if (context.InputKind != NavBakeInputKind.TriangleSurface)
            {
                throw new NavBakeUnsupportedInputException(
                    NavBakeAlgorithmKind.ExactCdt,
                    NavBakeAdapterCapability.FormatInputKind(context.InputKind),
                    "ExactCdtNavBakeAlgorithm declares triangle-surface capabilities only.");
            }

            NavTriangleSurfaceTileIndex surfaceIndex = context.RequireTriangleSurface();
            int agentHeightCm = RequireExactPositiveIntCm(agentProfile.HeightCm, $"AgentProfile '{agentProfile.Id}'.heightCm");
            int agentRadiusCm = RequireExactNonNegativeIntCm(agentProfile.RadiusCm, $"AgentProfile '{agentProfile.Id}'.radiusCm");
            int minWalkableUpDotQ1M = LayeredSpanSlopeQ1M.CompileMinWalkableUpDotQ1M(
                navProfile.MaxSlopeDeg,
                $"NavMeshBakeConfig.profiles['{navProfile.Id}'].maxSlopeDeg");

            if (navProfile.MaxClimbCm < 0)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.profiles['{navProfile.Id}'].maxClimbCm must be >= 0.");
            }

            ulong buildConfigHash = ComputeBuildConfigHash(
                context.BuildConfig,
                context.Config.TriangleSurface,
                navProfile.MaxClimbCm,
                minWalkableUpDotQ1M,
                agentHeightCm,
                agentRadiusCm,
                layer.Layer);

            var tileId = new NavTileId(target.ChunkX, target.ChunkY, layer.Layer);
            var request = new ExactCdtTriangleSurfaceBakeRequest(
                surfaceIndex,
                target,
                tileId,
                context.TileVersion,
                buildConfigHash,
                layer.Id,
                navProfile.MaxClimbCm,
                minWalkableUpDotQ1M,
                agentHeightCm,
                agentRadiusCm,
                context.Obstacles,
                DefaultMaxLawsonFlipCount);

            NavTile baked = ExactCdtTriangleSurfaceBaker.Bake(in request);
            tile = baked.TileId.Layer == layer.Layer
                ? baked
                : NavTileLayerRewriter.WithLayer(baked, layer.Layer);
            detourTileBytes = Array.Empty<byte>();
            artifact = new NavBakeArtifact(
                tile.TileId,
                tile.TileVersion,
                NavBakeStage.Serialize,
                NavBakeErrorCode.None,
                message: tile.TriangleCount == 0 ? NavValidEmptyTile.DefaultMessage : string.Empty,
                walkableTriangleCount: tile.TriangleCount,
                vertexCount: tile.VertexCount,
                triangleCount: tile.TriangleCount,
                portalCount: tile.PortalCount);
            return true;
        }

        private static ulong ComputeBuildConfigHash(
            NavBuildConfig buildConfig,
            NavTriangleSurfaceConfig triangleSurface,
            int maxClimbCm,
            int minWalkableUpDotQ1M,
            int agentHeightCm,
            int agentRadiusCm,
            int layer)
        {
            if (triangleSurface == null)
            {
                throw new InvalidOperationException("NavMeshBakeConfig.triangleSurface is required for ExactCdtNavBakeAlgorithm.");
            }

            ulong h = buildConfig.ComputeHash();
            h = Mix(h, triangleSurface.HaloPaddingCm);
            h = Mix(h, DefaultMaxLawsonFlipCount);
            h = Mix(h, maxClimbCm);
            h = Mix(h, minWalkableUpDotQ1M);
            h = Mix(h, agentHeightCm);
            h = Mix(h, agentRadiusCm);
            h = Mix(h, layer);
            return h;
        }

        private static ulong Mix(ulong hash, int value)
            => (hash ^ (ulong)(uint)value) * 1099511628211UL;

        private static int RequireExactPositiveIntCm(float value, string owner)
        {
            int cm = RequireExactIntCm(value, owner);
            if (cm <= 0)
            {
                throw new InvalidOperationException($"{owner} must be an exact positive integer centimeter value.");
            }

            return cm;
        }

        private static int RequireExactNonNegativeIntCm(float value, string owner)
        {
            int cm = RequireExactIntCm(value, owner);
            if (cm < 0)
            {
                throw new InvalidOperationException($"{owner} must be an exact nonnegative integer centimeter value.");
            }

            return cm;
        }

        private static int RequireExactIntCm(float value, string owner)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidOperationException($"{owner} must be a finite number.");
            }

            int cm = (int)value;
            if ((float)cm != value)
            {
                throw new InvalidOperationException(
                    $"{owner} must be an exact integer centimeter value for ExactCdt triangle-surface bake; got {value}.");
            }

            return cm;
        }
    }

    public sealed class NavBakeResultEntry
    {
        public NavBakeResultEntry(
            NavBakeTileCoord target,
            string profileId,
            int layer,
            bool success,
            NavTile tile,
            byte[] detourTileBytes,
            NavBakeArtifact artifact)
        {
            Target = target;
            ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
            Layer = layer;
            Success = success;
            Tile = tile;
            DetourTileBytes = detourTileBytes ?? Array.Empty<byte>();
            Artifact = artifact;
        }

        public NavBakeTileCoord Target { get; }

        public string ProfileId { get; }

        public int Layer { get; }

        public bool Success { get; }

        public NavTile Tile { get; }

        public byte[] DetourTileBytes { get; }

        public NavBakeArtifact Artifact { get; }

        public byte[] ToTileBytes()
        {
            if (!Success || Tile == null)
            {
                return Array.Empty<byte>();
            }

            using var stream = new MemoryStream();
            NavTileBinary.Write(stream, Tile);
            return stream.ToArray();
        }
    }

    public sealed class NavBakeResult
    {
        public NavBakeResult(IReadOnlyList<NavBakeResultEntry> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            int ok = 0;
            int fail = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Success) ok++;
                else fail++;
            }

            SuccessCount = ok;
            FailureCount = fail;
        }

        public IReadOnlyList<NavBakeResultEntry> Entries { get; }

        public int SuccessCount { get; }

        public int FailureCount { get; }
    }

    public sealed class NavBakeService
    {
        private readonly Dictionary<NavBakeAlgorithmKind, INavBakeAlgorithm> _algorithms;

        public NavBakeService(params INavBakeAlgorithm[] algorithms)
            : this((IReadOnlyList<INavBakeAlgorithm>)algorithms)
        {
        }

        public NavBakeService(IReadOnlyList<INavBakeAlgorithm> algorithms)
        {
            if (algorithms == null || algorithms.Count == 0)
            {
                throw new InvalidOperationException("NavBakeService requires at least one bake algorithm adapter.");
            }

            _algorithms = new Dictionary<NavBakeAlgorithmKind, INavBakeAlgorithm>(algorithms.Count);
            for (int i = 0; i < algorithms.Count; i++)
            {
                INavBakeAlgorithm algorithm = algorithms[i]
                    ?? throw new InvalidOperationException($"NavBakeService algorithm[{i}] is null.");
                NavBakeAdapterCapability.ValidateConsistency(algorithm.Kind, algorithm.Capabilities, algorithm.SupportsMode);
                if (!_algorithms.TryAdd(algorithm.Kind, algorithm))
                {
                    throw new InvalidOperationException($"NavBakeService duplicate algorithm adapter: {algorithm.Kind}.");
                }
            }
        }

        /// <summary>
        /// Input-specific support gate: missing adapter and unsupported mode/input each fail with a distinct
        /// diagnostic; both resolve through the single adapter resolver.
        /// </summary>
        public bool HasAdapter(NavBakeAlgorithmKind kind) => _algorithms.ContainsKey(kind);

        public bool TryGetAlgorithm(NavBakeAlgorithmKind kind, out INavBakeAlgorithm algorithm)
            => _algorithms.TryGetValue(kind, out algorithm!);

        public NavBakeAlgorithmKind[] RegisteredKinds
        {
            get
            {
                var kinds = new NavBakeAlgorithmKind[_algorithms.Count];
                int i = 0;
                foreach (NavBakeAlgorithmKind kind in _algorithms.Keys)
                {
                    kinds[i++] = kind;
                }

                Array.Sort(kinds, static (a, b) => ((byte)a).CompareTo((byte)b));
                return kinds;
            }
        }

        public void EnsureSupports(NavBakeContext context)
        {
            _ = ResolveSupportedAdapter(context);
        }

        /// <summary>
        /// Mode-only support gate for callers that do not have a complete bake context.
        /// Resolves through the same adapter registry as <see cref="EnsureSupports"/>.
        /// </summary>
        public void EnsureSupportsMode(NavBakeAlgorithmKind algorithm, NavBakeMode mode)
        {
            if (!TryResolveAdapter(algorithm, out INavBakeAlgorithm adapter))
            {
                throw new InvalidOperationException(
                    $"NavBakeService has no adapter for algorithm '{NavBakeNames.FormatAlgorithm(algorithm)}' in bake mode '{NavBakeNames.FormatMode(mode)}'.");
            }

            if (!adapter.SupportsMode(mode))
            {
                throw new InvalidOperationException(
                    $"NavBakeService algorithm '{NavBakeNames.FormatAlgorithm(adapter.Kind)}' does not support bake mode '{NavBakeNames.FormatMode(mode)}'.");
            }
        }

        public NavBakeResult Bake(NavBakeContext context)
        {
            context.Validate();
            INavBakeAlgorithm algorithm = ResolveSupportedAdapter(context);

            int total = checked(context.Targets.Count * context.Config.Layers.Count * context.Config.Profiles.Count);
            var entries = new NavBakeResultEntry[total];
            int cursor = -1;

            void BakeTarget(NavBakeTileCoord target)
            {
                for (int li = 0; li < context.Config.Layers.Count; li++)
                {
                    NavLayerConfig layer = context.Config.Layers[li];
                    for (int pi = 0; pi < context.Config.Profiles.Count; pi++)
                    {
                        NavMeshAgentProfileConfig navProfile = context.Config.Profiles[pi];
                        AgentProfileConfig agentProfile = context.AgentProfiles.Require(navProfile.Id, $"{NavMeshConfigPaths.BakeConfigPath}.profiles[{pi}]");
                        bool success = algorithm.TryBake(context, target, layer, navProfile, agentProfile, out NavTile tile, out byte[] detourTileBytes, out NavBakeArtifact artifact);
                        int index = Interlocked.Increment(ref cursor);
                        entries[index] = new NavBakeResultEntry(target, navProfile.Id, layer.Layer, success, tile, detourTileBytes, artifact);
                    }
                }
            }

            if (context.Execution.Parallel)
            {
                Parallel.ForEach(
                    context.Targets,
                    new ParallelOptions { MaxDegreeOfParallelism = context.Execution.MaxDegreeOfParallelism },
                    BakeTarget);
            }
            else
            {
                for (int i = 0; i < context.Targets.Count; i++)
                {
                    BakeTarget(context.Targets[i]);
                }
            }

            return new NavBakeResult(entries);
        }

        /// <summary>
        /// Runtime bake into a single preallocated destination. Skips allocating result arrays and
        /// does not re-run HashSet layer validation (caller must validate once at queue construction).
        /// </summary>
        public bool BakeInto(
            NavBakeContext context,
            NavBakeTileCoord target,
            NavLayerConfig layer,
            NavMeshAgentProfileConfig navProfile,
            AgentProfileConfig agentProfile,
            NavTile destination,
            Span<byte> checksumScratch,
            out NavBakeArtifact artifact)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (navProfile == null) throw new ArgumentNullException(nameof(navProfile));
            if (agentProfile == null) throw new ArgumentNullException(nameof(agentProfile));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            INavBakeAlgorithm algorithm = ResolveSupportedAdapter(context);
            return algorithm.TryBakeInto(
                context,
                target,
                layer,
                navProfile,
                agentProfile,
                destination,
                checksumScratch,
                out artifact);
        }

        private INavBakeAlgorithm ResolveSupportedAdapter(NavBakeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (!TryResolveAdapter(context.Algorithm, out INavBakeAlgorithm adapter))
            {
                throw new InvalidOperationException(
                    $"NavBakeService has no adapter for algorithm '{NavBakeNames.FormatAlgorithm(context.Algorithm)}' " +
                    $"in bake mode '{NavBakeNames.FormatMode(context.Mode)}' with input '{NavBakeAdapterCapability.FormatInputKind(context.InputKind)}'.");
            }

            NavBakeAdapterCapabilities required = NavBakeAdapterCapability.Require(context.Mode, context.InputKind);
            if ((adapter.Capabilities & required) != required)
            {
                throw new InvalidOperationException(
                    $"NavBakeService algorithm '{NavBakeNames.FormatAlgorithm(adapter.Kind)}' does not support " +
                    $"{NavBakeNames.FormatMode(context.Mode)}/{NavBakeAdapterCapability.FormatInputKind(context.InputKind)} " +
                    $"(required {required}, declared {adapter.Capabilities}).");
            }

            return adapter;
        }

        private bool TryResolveAdapter(NavBakeAlgorithmKind algorithm, out INavBakeAlgorithm adapter)
            => _algorithms.TryGetValue(algorithm, out adapter);
    }

    internal static class NavTileLayerRewriter
    {
        public static NavTile WithLayer(NavTile tile, int layer)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            return new NavTile(
                new NavTileId(tile.TileId.ChunkX, tile.TileId.ChunkY, layer),
                tile.TileVersion,
                tile.BuildConfigHash,
                tile.Checksum,
                tile.OriginXcm,
                tile.OriginZcm,
                tile.VertexXcm,
                tile.VertexYcm,
                tile.VertexZcm,
                tile.TriA,
                tile.TriB,
                tile.TriC,
                tile.N0,
                tile.N1,
                tile.N2,
                tile.TriAreaIds,
                tile.Portals);
        }

        public static NavBakeArtifact WithLayer(NavBakeArtifact artifact, int layer)
        {
            return new NavBakeArtifact(
                new NavTileId(artifact.TileId.ChunkX, artifact.TileId.ChunkY, layer),
                artifact.TileVersion,
                artifact.Stage,
                artifact.ErrorCode,
                artifact.Message,
                artifact.WalkableTriangleCount,
                artifact.VertexCount,
                artifact.TriangleCount,
                artifact.PortalCount,
                artifact.DebugLog);
        }
    }
}
