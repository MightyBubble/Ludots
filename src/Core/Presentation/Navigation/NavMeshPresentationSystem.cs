using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Navigation
{
    /// <summary>
    /// Publishes the selected resident NavMesh store and its tile lifecycle into Core presentation.
    /// It never loads tiles and performs no managed allocation after construction.
    /// </summary>
    public sealed class NavMeshPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NavMeshPresentationState _state;
        private readonly NavMeshPresentationBuffer _buffer;
        private readonly NavTile[] _tileScratch;
        private readonly NavBakeTileCoord[] _pendingScratch;
        private readonly NavBakeTileCoord[] _rebuildingScratch;
        private readonly NavBakeTileCoord[] _committedScratch;

        public NavMeshPresentationSystem(
            GameEngine engine,
            NavMeshPresentationState state,
            NavMeshPresentationBuffer buffer)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _tileScratch = new NavTile[buffer.TileCapacity];
            _pendingScratch = new NavBakeTileCoord[buffer.TileStateCapacity];
            _rebuildingScratch = new NavBakeTileCoord[buffer.TileStateCapacity];
            _committedScratch = new NavBakeTileCoord[buffer.TileStateCapacity];
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float t)
        {
            NavMeshPresentationStyle style = _state.Style;
            int layer = _state.Layer;
            int profile = _state.Profile;
            NavQueryTileSpace tileSpace = default;
            if (!_state.Enabled ||
                !_engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry registry))
            {
                _buffer.BeginFrame(
                    layer,
                    profile,
                    in tileSpace,
                    storeRevision: 0u,
                    storeGeneration: 0UL,
                    _state.Revision,
                    in style);
                return;
            }

            tileSpace = registry.TileSpace;
            if (!registry.TryGetStore(layer, profile, out NavTileStore store))
            {
                throw new InvalidOperationException(
                    $"NavMesh presentation selected layer={layer}, profile={profile}, but no matching NavTileStore is registered.");
            }

            int tileCount = store.CopyResidentTiles(
                _tileScratch,
                out uint storeRevision,
                out ulong storeGeneration);
            _buffer.BeginFrame(
                layer,
                profile,
                in tileSpace,
                storeRevision,
                storeGeneration,
                _state.Revision,
                in style);
            for (int i = 0; i < tileCount; i++)
            {
                _buffer.AddTile(_tileScratch[i]);
            }

            if (!_engine.TryGetService(
                    CoreServiceKeys.RuntimeNavMeshRebuildQueue,
                    out RuntimeIncrementalNavMeshRebuildQueue queue))
            {
                return;
            }

            int committedCount = queue.CopyLastCommittedTiles(
                layer,
                profile,
                _committedScratch,
                out ulong committedGeneration);
            if (committedCount > 0 && committedGeneration != storeGeneration)
            {
                throw new InvalidOperationException(
                    $"NavMesh presentation committed generation {committedGeneration} does not match selected store generation {storeGeneration} " +
                    $"for layer={layer}, profile={profile}.");
            }

            for (int i = 0; i < committedCount; i++)
            {
                _buffer.SetTileState(in _committedScratch[i], NavMeshPresentationTileState.Committed);
            }

            int pendingCount = queue.CopyPendingTiles(_pendingScratch);
            for (int i = 0; i < pendingCount; i++)
            {
                _buffer.SetTileState(in _pendingScratch[i], NavMeshPresentationTileState.Pending);
            }

            int rebuildingCount = queue.CopyRebuildingTiles(layer, profile, _rebuildingScratch);
            for (int i = 0; i < rebuildingCount; i++)
            {
                _buffer.SetTileState(in _rebuildingScratch[i], NavMeshPresentationTileState.Rebuilding);
            }

            _buffer.SortTileStates();
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }
    }
}
