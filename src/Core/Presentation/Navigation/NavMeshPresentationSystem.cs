using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.Navigation
{
    /// <summary>
    /// Publishes the selected resident NavMesh store and its source metadata into Core presentation.
    /// It never loads tiles and performs no managed allocation after construction.
    /// </summary>
    public sealed class NavMeshPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NavMeshPresentationState _state;
        private readonly NavMeshPresentationBuffer _buffer;
        private readonly NavTile[] _tileScratch;

        public NavMeshPresentationSystem(
            GameEngine engine,
            NavMeshPresentationState state,
            NavMeshPresentationBuffer buffer)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _tileScratch = new NavTile[buffer.TileCapacity];
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
            if (!_state.Enabled)
            {
                PublishNotReady(layer, profile, in style);
                return;
            }

            if (!_engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry registry) ||
                !_engine.TryGetService(CoreServiceKeys.NavMeshBakeConfig, out NavMeshBakeConfig bakeConfig) ||
                !_engine.TryGetService(CoreServiceKeys.NavMeshProfiles, out NavMeshProfileRegistry profileRegistry))
            {
                PublishNotReady(layer, profile, in style);
                return;
            }

            NavMeshPresentationCapabilityValidator.Require(
                _engine.TryGetService(CoreServiceKeys.PresentationAdapterCapabilities, out PresentationAdapterCapabilities? capabilities)
                    ? capabilities
                    : null);

            if (!registry.TryGetStore(layer, profile, out NavTileStore store))
            {
                throw new InvalidOperationException(
                    $"NavMesh presentation enabled with layer={layer}, profile={profile}, but no matching NavTileStore is registered.");
            }

            int tileCount = store.CopyResidentTiles(_tileScratch, out uint storeRevision);
            _buffer.BeginFrame(
                layer,
                profile,
                profileRegistry.GetId(profile),
                bakeConfig.ParsedMode,
                bakeConfig.ParsedAlgorithm,
                storeRevision,
                _state.Revision,
                ResolveBuildConfig(bakeConfig),
                in style);
            for (int i = 0; i < tileCount; i++)
            {
                _buffer.AddTile(_tileScratch[i]);
            }
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }

        private void PublishNotReady(int layer, int profile, in NavMeshPresentationStyle style)
        {
            _buffer.BeginFrame(
                layer,
                profile,
                string.Empty,
                default,
                default,
                storeRevision: 0u,
                _state.Revision,
                default,
                in style);
        }

        private static NavBuildConfig ResolveBuildConfig(NavMeshBakeConfig config)
        {
            NavRuntimeIncrementalConfig runtime = config.RuntimeIncremental
                ?? throw new InvalidOperationException("NavMeshBakeConfig.runtimeIncremental must be configured for NavMesh presentation.");
            return new NavBuildConfig(
                runtime.HeightScaleMeters,
                runtime.MinWalkableUpDot,
                runtime.CliffHeightThreshold);
        }
    }
}
