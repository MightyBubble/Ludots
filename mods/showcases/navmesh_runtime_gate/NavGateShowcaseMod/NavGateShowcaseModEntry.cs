using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.Terrain;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Scripting;
using NavGateShowcaseMod.Input;
using NavGateShowcaseMod.Runtime;
using NavGateShowcaseMod.Systems;

namespace NavGateShowcaseMod
{
    public sealed class NavGateShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[NavGateShowcaseMod] Loaded - G drop/lift gate | F freeze rebake (ablation) | N navmesh view | P/O manual obstacle | R radius | T pace");

            var shared = new NavGateState();

            context.SystemFactoryRegistry.RegisterPresentation("NavGateTimeline", scriptCtx =>
            {
                if (!scriptCtx.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    return new NoopSystem();
                }

                return new NavGateTimelineSystem(engine, shared);
            });

            context.SystemFactoryRegistry.RegisterPresentation("NavGateMarch", scriptCtx =>
            {
                if (!scriptCtx.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    return new NoopSystem();
                }

                return new NavGateMarchSystem(engine, shared);
            });

            context.SystemFactoryRegistry.RegisterPresentation("NavGateRender", scriptCtx =>
            {
                if (!scriptCtx.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    return new NoopSystem();
                }

                return new NavGateRenderSystem(engine, shared);
            });

            context.OnEvent(GameEvents.MapLoaded, ctx =>
            {
                if (!ctx.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                if (engine.CurrentMapSession?.MapId.Value != NavGateIds.MapId)
                {
                    return System.Threading.Tasks.Task.CompletedTask;
                }

                if (engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                    inputObj is PlayerInputHandler input)
                {
                    if (!input.HasAction(NavGateInputActions.ToggleGate))
                    {
                        throw new InvalidOperationException(
                            $"Missing NavGate input actions; check assets/Input/default_input.json for '{NavGateInputActions.ToggleGate}'.");
                    }

                    input.PushContext(NavGateInputContexts.Overlay);
                }

                WarmUpNavMeshOverlay(engine);

                var sfr = engine.ModLoader.SystemFactoryRegistry;
                sfr.TryActivate("NavGateTimeline", ctx, engine);
                sfr.TryActivate("NavGateMarch", ctx, engine);
                sfr.TryActivate("NavGateRender", ctx, engine);
                return System.Threading.Tasks.Task.CompletedTask;
            });
        }

        public void OnUnload() { }

        private sealed class NoopSystem : ISystem<float>
        {
            public void Initialize() { }
            public void BeforeUpdate(in float t) { }
            public void Update(in float t) { }
            public void AfterUpdate(in float t) { }
            public void Dispose() { }
        }

        private static void WarmUpNavMeshOverlay(GameEngine engine)
        {
            if (!engine.TryGetService(CoreServiceKeys.NavQueryServices, out NavQueryServiceRegistry? registry) || registry == null)
            {
                throw new InvalidOperationException(
                    $"NavGate showcase requires NavQueryServices; load map '{NavGateIds.MapId}' with Feature.NavMesh:On.");
            }

            if (!engine.TryGetService(CoreServiceKeys.LogicTerrain, out LogicTerrainField? terrain) || terrain == null)
            {
                throw new InvalidOperationException("NavGate showcase requires LogicTerrain.");
            }

            if (!registry.TryGetStore(0, 0, out NavTileStore? store) || store == null)
            {
                throw new InvalidOperationException("NavGate showcase requires NavTileStore layer0/profile0.");
            }

            int loaded = 0;
            for (int cy = 0; cy < terrain.HeightChunks; cy++)
            {
                for (int cx = 0; cx < terrain.WidthChunks; cx++)
                {
                    store.GetOrLoad(new NavTileId(cx, cy, 0));
                    loaded++;
                }
            }

            // navmesh 面向画面层的启用只在有呈现宿主时进行；无头环境（合同测试/CI）跳过
            // 画面层，行军与重烤链路照常运转。
            bool hasPresentationHost =
                engine.TryGetService(CoreServiceKeys.PresentationAdapterCapabilities, out Ludots.Core.Presentation.Rendering.PresentationAdapterCapabilities? caps) &&
                caps != null &&
                caps.Visuals.HasFlag(Ludots.Core.Presentation.Rendering.PresentationVisualCapabilities.NavMeshTileGeometry);
            if (hasPresentationHost)
            {
                if (engine.TryGetService(CoreServiceKeys.NavMeshPresentationState, out NavMeshPresentationState? state) && state != null)
                {
                    state.SetEnabled(true);
                }

                if (engine.GlobalContext.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out var debugObj) &&
                    debugObj is RenderDebugState renderDebugState)
                {
                    renderDebugState.DrawNavMesh = true;
                }
            }

            Console.WriteLine($"[NavGate] navmesh overlay warmed {loaded} tiles; presentation host={(hasPresentationHost ? "on" : "headless")}");
        }
    }
}
