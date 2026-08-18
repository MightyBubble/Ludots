using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Scripting;
using NavMeshDebugLaunchMod.Input;

namespace NavMeshDebugLaunchMod.Systems
{
    public sealed class NavMeshDebugOverlaySystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private PlayerInputHandler? _input;

        public NavMeshDebugOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            ResolveInput();
            if (_input == null) return;
            if (!_input.PressedThisFrame(NavMeshDebugInputActions.ToggleOverlay)) return;

            if (!_engine.TryGetService(CoreServiceKeys.NavMeshPresentationState, out NavMeshPresentationState? state) || state == null)
            {
                throw new InvalidOperationException(
                    "NavMeshDebug overlay toggle requires NavMeshPresentationState; load a map with Feature.NavMesh:On.");
            }

            bool enable = !state.Enabled;
            if (enable)
            {
                WarmResidentTiles(state.Layer, state.Profile);
            }

            state.SetEnabled(enable);
            Console.WriteLine($"[NavMeshDebugOverlay] {(enable ? "enabled" : "disabled")} layer={state.Layer} profile={state.Profile}");

            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out var debugObj) &&
                debugObj is RenderDebugState renderDebugState)
            {
                renderDebugState.DrawNavMesh = enable;
            }
        }

        private void WarmResidentTiles(int layer, int profile)
        {
            if (!_engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry? registry) || registry == null)
            {
                throw new InvalidOperationException("NavMeshDebug overlay warm-up requires NavQueryServices.");
            }

            if (!_engine.TryGetService(CoreServiceKeys.LogicTerrain, out LogicTerrainField? terrain) || terrain == null)
            {
                throw new InvalidOperationException("NavMeshDebug overlay warm-up requires a loaded LogicTerrain.");
            }

            if (!registry.TryGetStore(layer, profile, out NavTileStore? store) || store == null)
            {
                throw new InvalidOperationException(
                    $"NavMeshDebug overlay warm-up found no NavTileStore for layer={layer}, profile={profile}.");
            }

            int loaded = 0;
            for (int cy = 0; cy < terrain.HeightChunks; cy++)
            {
                for (int cx = 0; cx < terrain.WidthChunks; cx++)
                {
                    store.GetOrLoad(new NavTileId(cx, cy, layer));
                    loaded++;
                }
            }

            Console.WriteLine($"[NavMeshDebugOverlay] warm-up touched {loaded} tiles; resident={store.SnapshotLoadedTiles().Length}");
        }

        private void ResolveInput()
        {
            if (_input != null) return;
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                inputObj is PlayerInputHandler input)
            {
                _input = input;
            }
        }
    }
}
