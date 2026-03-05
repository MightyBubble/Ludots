using Arch.System;
using DiagnosticsOverlayMod.Input;
using DiagnosticsOverlayMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace DiagnosticsOverlayMod
{
    public sealed class DiagnosticsOverlayModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[DiagnosticsOverlayMod] Loaded — F5: Config | F6: Mods | F7: Attributes");

            context.SystemFactoryRegistry.RegisterPresentation("DiagnosticsOverlay", scriptCtx =>
            {
                var engine = scriptCtx.GetEngine();
                if (engine == null) return new NoopSystem();
                return new DiagnosticsOverlaySystem(engine);
            });

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    if (engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                        inputObj is PlayerInputHandler input)
                    {
                        EnsureOverlayInputSchema(input);
                        input.PushContext(DiagnosticsOverlayInputContexts.Overlay);
                    }

                    var sfr = engine.ModLoader.SystemFactoryRegistry;
                    sfr.TryActivate("DiagnosticsOverlay", ctx, engine);
                }
                return System.Threading.Tasks.Task.CompletedTask;
            });
        }

        public void OnUnload() { }

        private static void EnsureOverlayInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(DiagnosticsOverlayInputContexts.Overlay))
            {
                throw new System.InvalidOperationException($"Missing input context: {DiagnosticsOverlayInputContexts.Overlay}");
            }

            if (!input.HasAction(DiagnosticsOverlayInputActions.ToggleConfigPanel)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.ToggleConfigPanel}");
            if (!input.HasAction(DiagnosticsOverlayInputActions.ToggleModsPanel)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.ToggleModsPanel}");
            if (!input.HasAction(DiagnosticsOverlayInputActions.ToggleAttributesPanel)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.ToggleAttributesPanel}");
            if (!input.HasAction(DiagnosticsOverlayInputActions.ToggleTurnBased)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.ToggleTurnBased}");
            if (!input.HasAction(DiagnosticsOverlayInputActions.StepTurn)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.StepTurn}");
            if (!input.HasAction(DiagnosticsOverlayInputActions.ToggleTerrain)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.ToggleTerrain}");
            if (!input.HasAction(DiagnosticsOverlayInputActions.TogglePrimitives)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.TogglePrimitives}");
            if (!input.HasAction(DiagnosticsOverlayInputActions.ToggleDebugDraw)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.ToggleDebugDraw}");
            if (!input.HasAction(DiagnosticsOverlayInputActions.ToggleSkiaUi)) throw new System.InvalidOperationException($"Missing input action: {DiagnosticsOverlayInputActions.ToggleSkiaUi}");
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
