using System;
using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Map;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Surfaces;

namespace Ludots.Core.TransportNetwork
{
    /// <summary>
    /// Installs each board-declared transport asset into that board's chunk graph and the ribbon registry.
    /// A failure rolls back boards already installed in this call.
    /// </summary>
    public sealed class TransportNetworkMapRuntime : IDisposable
    {
        private readonly SurfaceSourcePayloadRegistry _payloads;
        private readonly List<InstalledBoardNetwork> _installed = new();

        public TransportNetworkMapRuntime(SurfaceSourcePayloadRegistry payloads)
        {
            _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
        }

        public static bool AnyBoardDeclares(MapConfig mapConfig)
        {
            if (mapConfig?.Boards is not { Count: > 0 })
            {
                return false;
            }

            foreach (BoardConfig board in mapConfig.Boards)
            {
                if (board?.TransportNetwork != null)
                {
                    return true;
                }
            }

            return false;
        }

        public void Install(
            MapSession session,
            MapConfig mapConfig,
            ConfigPipeline pipeline,
            ConfigCatalog catalog,
            ConfigConflictReport conflictReport)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (mapConfig?.Boards is not { Count: > 0 })
            {
                throw new InvalidOperationException(
                    $"Map '{session.MapId.Value}' cannot install transport networks: the map declares no boards.");
            }

            try
            {
                foreach (BoardConfig boardConfig in mapConfig.Boards)
                {
                    if (boardConfig?.TransportNetwork is not { } declaration)
                    {
                        continue;
                    }

                    declaration.Validate(session.MapId.Value, boardConfig.Name);
                    if (session.GetBoard(boardConfig.Name) is not INodeGraphBoard graphBoard)
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' board '{boardConfig.Name}' declares TransportNetwork but the session board is not a NodeGraph board.");
                    }

                    if (graphBoard.LoadedChunks is not WorldGridLoadedChunks loadedChunks)
                    {
                        throw new InvalidOperationException(
                            $"Map '{session.MapId.Value}' board '{boardConfig.Name}' declares TransportNetwork but its loaded-chunks source is not WorldGridLoadedChunks.");
                    }

                    TransportNetworkAsset asset = new TransportNetworkAssetLoader(pipeline)
                        .Load(catalog, conflictReport, declaration.AssetPath);
                    TransportNetworkBakedAsset baked = new TransportNetworkBaker().Bake(asset, loadedChunks.ChunkSizeCm);

                    var graphSource = new TransportNetworkChunkGraphSource(graphBoard.GraphStore, loadedChunks, baked);
                    foreach (long chunkKey in baked.GraphChunks.Keys)
                    {
                        loadedChunks.SetLoaded(chunkKey, loaded: true);
                    }

                    graphSource.LoadActiveChunks();

                    var ribbonSource = new TransportNetworkRibbonSource(baked);
                    ribbonSource.SyncPayloads(loadedChunks.ActiveChunkKeys, _payloads, ComposeSurfaceScopeId);

                    _installed.Add(new InstalledBoardNetwork(graphSource, ribbonSource));
                    Log.Info(
                        in LogChannels.Map,
                        $"Installed transport network '{asset.Id}' on board '{boardConfig.Name}' (chunks: {baked.GraphChunks.Count}).");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            for (int i = _installed.Count - 1; i >= 0; i--)
            {
                _installed[i].Dispose(_payloads);
            }

            _installed.Clear();
        }

        // Presenter scope tags for these ribbons live in the 700000000 band.
        private static int ComposeSurfaceScopeId(long chunkKey)
        {
            unchecked
            {
                int mixed = (int)(chunkKey ^ (chunkKey >> 32));
                return 700000000 + Math.Abs(mixed % 100000000);
            }
        }

        private sealed record InstalledBoardNetwork(
            TransportNetworkChunkGraphSource GraphSource,
            TransportNetworkRibbonSource RibbonSource)
        {
            public void Dispose(SurfaceSourcePayloadRegistry payloads)
            {
                RibbonSource.SyncPayloads(Array.Empty<long>(), payloads, ComposeSurfaceScopeId);
                GraphSource.Dispose();
            }
        }
    }
}
