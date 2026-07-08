using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Map.Board;
using Ludots.Core.Navigation.GraphWorld;
using Ludots.Core.Presentation.Surfaces;
using Ludots.Core.Scripting;
using Ludots.Core.TransportNetwork;

namespace CapabilityStandardTransportNetworkMod.Runtime;

internal sealed class CapabilityStandardTransportNetworkRuntime : IDisposable
{
    private const string ShowcaseMapId = "capability_standard_transport_network";
    private TransportNetworkChunkGraphSource? _graphSource;
    private TransportNetworkRibbonSource? _ribbonSource;
    private SurfaceSourcePayloadRegistry? _payloads;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (!string.Equals(context.GetMapSession().MapId.Value, ShowcaseMapId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        Dispose();

        GameEngine engine = context.GetEngine();
        INodeGraphBoard graphBoard = BoardResolution.RequireSingleNodeGraphBoard(
            context.GetMapSession(),
            "CapabilityStandardTransportNetworkMod");

        if (graphBoard.LoadedChunks is not WorldGridLoadedChunks loadedChunks)
        {
            throw new InvalidOperationException("CapabilityStandardTransportNetworkMod requires WorldGridLoadedChunks.");
        }

        TransportNetworkAsset asset = new TransportNetworkAssetLoader(engine.ConfigPipeline)
            .Load(engine.ConfigCatalog, engine.ConfigConflictReport);
        TransportNetworkBakedAsset baked = new TransportNetworkBaker().Bake(asset, loadedChunks.ChunkSizeCm);

        _graphSource = new TransportNetworkChunkGraphSource(graphBoard.GraphStore, loadedChunks, baked);
        foreach (long chunkKey in baked.GraphChunks.Keys)
        {
            loadedChunks.SetLoaded(chunkKey, loaded: true);
        }

        _graphSource.LoadActiveChunks();

        _payloads = engine.GetService(CoreServiceKeys.SurfaceSourcePayloadRegistry)
            ?? throw new InvalidOperationException("CapabilityStandardTransportNetworkMod requires SurfaceSourcePayloadRegistry.");
        _ribbonSource = new TransportNetworkRibbonSource(baked);
        _ribbonSource.SyncPayloads(
            loadedChunks.ActiveChunkKeys,
            _payloads,
            TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId);

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_ribbonSource != null && _payloads != null)
        {
            _ribbonSource.SyncPayloads(
                Array.Empty<long>(),
                _payloads,
                TransportNetworkRibbonSource.ComposeDefaultSurfaceScopeId);
        }

        _graphSource?.Dispose();
        _graphSource = null;
        _ribbonSource = null;
        _payloads = null;
    }

}
