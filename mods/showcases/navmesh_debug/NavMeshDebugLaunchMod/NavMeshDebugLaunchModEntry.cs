using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NavMeshDebugLaunchMod.Input;
using NavMeshDebugLaunchMod.Systems;

namespace NavMeshDebugLaunchMod
{
    public sealed class NavMeshDebugLaunchModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[NavMeshDebugLaunchMod] Loaded - N: toggle navmesh overlay, T: toggle nav walkability texture, P: spawn nav obstacle, O: clear nav obstacles");

            context.SystemFactoryRegistry.RegisterPresentation("NavMeshDebugOverlay", scriptCtx =>
            {
                if (!scriptCtx.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    return new NoopSystem();
                }

                return new NavMeshDebugOverlaySystem(engine);
            });

            context.SystemFactoryRegistry.RegisterPresentation("NavMeshDebugObstacle", scriptCtx =>
            {
                if (!scriptCtx.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    return new NoopSystem();
                }

                return new NavMeshDebugObstacleSystem(engine);
            });

            context.OnEvent(GameEvents.MapLoaded, ctx =>
            {
                if (ctx.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) && engine != null)
                {
                    if (engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                        inputObj is PlayerInputHandler input)
                    {
                        EnsureOverlayInputSchema(input);
                        input.PushContext(NavMeshDebugInputContexts.Overlay);
                    }

                    var sfr = engine.ModLoader.SystemFactoryRegistry;
                    sfr.TryActivate("NavMeshDebugOverlay", ctx, engine);
                    sfr.TryActivate("NavMeshDebugObstacle", ctx, engine);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            });
        }

        public void OnUnload() { }

        private static void EnsureOverlayInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(NavMeshDebugInputContexts.Overlay))
            {
                throw new System.InvalidOperationException($"Missing input context: {NavMeshDebugInputContexts.Overlay}");
            }

            if (!input.HasAction(NavMeshDebugInputActions.ToggleOverlay))
            {
                throw new System.InvalidOperationException($"Missing input action: {NavMeshDebugInputActions.ToggleOverlay}");
            }

            if (!input.HasAction(NavMeshDebugInputActions.ToggleNavWalkabilityTexture))
            {
                throw new System.InvalidOperationException($"Missing input action: {NavMeshDebugInputActions.ToggleNavWalkabilityTexture}");
            }

            if (!input.HasAction(NavMeshDebugInputActions.SpawnObstacle))
            {
                throw new System.InvalidOperationException($"Missing input action: {NavMeshDebugInputActions.SpawnObstacle}");
            }

            if (!input.HasAction(NavMeshDebugInputActions.ClearObstacles))
            {
                throw new System.InvalidOperationException($"Missing input action: {NavMeshDebugInputActions.ClearObstacles}");
            }
        }

        private sealed class NoopSystem : ISystem<float>
        {
            public void Initialize() { }
            public void BeforeUpdate(in float t) { }
            public void Update(in float t) { }
            public void AfterUpdate(in float t) { }
            public void Dispose() { }
        }
    }
}
