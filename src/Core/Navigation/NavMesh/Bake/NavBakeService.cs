using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public interface INavBakeAlgorithm
    {
        NavBakeAlgorithmKind Kind { get; }

        bool TryBake(
            NavBakeContext context,
            NavBakeTileCoord target,
            NavLayerConfig layer,
            NavMeshAgentProfileConfig navProfile,
            AgentProfileConfig agentProfile,
            out NavTile tile,
            out byte[] detourTileBytes,
            out NavBakeArtifact artifact);
    }

    public sealed class CdtNavBakeAlgorithm : INavBakeAlgorithm
    {
        public NavBakeAlgorithmKind Kind => NavBakeAlgorithmKind.Cdt;

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
            BakePipelineResult result = BakePipeline.Execute(
                context.Terrain,
                target.ChunkX,
                target.ChunkY,
                context.TileVersion,
                context.BuildConfig,
                context.Obstacles,
                layer.Id);

            if (!result.Success || result.Tile == null)
            {
                tile = null!;
                detourTileBytes = Array.Empty<byte>();
                artifact = result.Artifact;
                return false;
            }

            tile = result.Tile.TileId.Layer == layer.Layer
                ? result.Tile
                : NavTileLayerRewriter.WithLayer(result.Tile, layer.Layer);
            detourTileBytes = Array.Empty<byte>();
            artifact = result.Artifact.TileId.Layer == layer.Layer
                ? result.Artifact
                : NavTileLayerRewriter.WithLayer(result.Artifact, layer.Layer);
            return true;
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
                if (!_algorithms.TryAdd(algorithm.Kind, algorithm))
                {
                    throw new InvalidOperationException($"NavBakeService duplicate algorithm adapter: {algorithm.Kind}.");
                }
            }
        }

        public NavBakeResult Bake(NavBakeContext context)
        {
            context.Validate();

            if (context.Mode == NavBakeMode.RuntimeIncremental &&
                context.Algorithm != NavBakeAlgorithmKind.Cdt)
            {
                throw new InvalidOperationException(
                    "NavBakeService runtime-incremental mode requires algorithm 'cdt'.");
            }

            if (!_algorithms.TryGetValue(context.Algorithm, out INavBakeAlgorithm algorithm))
            {
                throw new InvalidOperationException($"NavBakeService has no adapter for algorithm '{NavBakeNames.FormatAlgorithm(context.Algorithm)}'.");
            }

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
