using System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using EastAsiaNavMeshDebugMod.Input;
using EastAsiaNavMeshDebugMod.Systems;

namespace EastAsiaNavMeshDebugMod
{
    public sealed class EastAsiaNavMeshDebugModEntry : IMod
    {
        private const string ToggleSystemName = "EastAsiaNavWalkabilityTextureToggle";

        public void OnLoad(IModContext context)
        {
            context.Log("[EastAsiaNavMeshDebugMod] Loaded - T: toggle nav walkability texture");

            context.SystemFactoryRegistry.RegisterPresentation(ToggleSystemName, scriptContext =>
            {
                if (!scriptContext.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    throw new InvalidOperationException(
                        $"{ToggleSystemName} requires {CoreServiceKeys.Engine.Name}.");
                }

                return new EastAsiaNavWalkabilityTextureToggleSystem(engine, ResolveInput(engine));
            });

            context.OnEvent(GameEvents.MapLoaded, scriptContext =>
            {
                if (!scriptContext.TryGet(CoreServiceKeys.Engine, out GameEngine? engine) || engine == null)
                {
                    throw new InvalidOperationException(
                        $"East Asia nav debug map-load hook requires {CoreServiceKeys.Engine.Name}.");
                }

                PlayerInputHandler input = ResolveInput(engine);
                EnsureInputSchema(input);
                input.PushContext(EastAsiaNavMeshDebugInputContexts.Debug);
                engine.ModLoader.SystemFactoryRegistry.TryActivate(ToggleSystemName, scriptContext, engine);
                return System.Threading.Tasks.Task.CompletedTask;
            });
        }

        public void OnUnload() { }

        private static PlayerInputHandler ResolveInput(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out object? inputObject) &&
                inputObject is PlayerInputHandler input)
            {
                return input;
            }

            throw new InvalidOperationException(
                $"East Asia nav debug requires {CoreServiceKeys.InputHandler.Name}.");
        }

        private static void EnsureInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(EastAsiaNavMeshDebugInputContexts.Debug))
            {
                throw new InvalidOperationException(
                    $"Missing input context: {EastAsiaNavMeshDebugInputContexts.Debug}");
            }

            if (!input.HasAction(EastAsiaNavMeshDebugInputActions.ToggleNavWalkabilityTexture))
            {
                throw new InvalidOperationException(
                    $"Missing input action: {EastAsiaNavMeshDebugInputActions.ToggleNavWalkabilityTexture}");
            }
        }
    }
}
