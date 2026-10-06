using System.Threading.Tasks;
using Arch.System;
using GamepadShowcaseMod.Input;
using GamepadShowcaseMod.Systems;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GamepadShowcaseMod
{
    public sealed class GamepadShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[GamepadShowcaseMod] Loaded — Start/F6 toggles the gamepad HUD, A pulses, B cycles color");

            context.SystemFactoryRegistry.RegisterPresentation("GamepadShowcase", scriptCtx =>
            {
                var engine = scriptCtx.GetEngine();
                if (engine == null) return new NoopSystem();
                return new GamepadShowcaseSystem(engine);
            });

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    if (engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                        inputObj is PlayerInputHandler input)
                    {
                        EnsureShowcaseInputSchema(input);
                        input.PushContext(GamepadShowcaseInputContexts.Showcase);
                    }

                    engine.ModLoader.SystemFactoryRegistry.TryActivate("GamepadShowcase", ctx, engine);
                }
                return Task.CompletedTask;
            });
        }

        public void OnUnload() { }

        private static void EnsureShowcaseInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(GamepadShowcaseInputContexts.Showcase))
            {
                throw new System.InvalidOperationException(
                    $"Missing input context: {GamepadShowcaseInputContexts.Showcase}");
            }

            if (!input.HasAction(GamepadShowcaseInputActions.ToggleHud)) throw Missing(GamepadShowcaseInputActions.ToggleHud);
            if (!input.HasAction(GamepadShowcaseInputActions.Pulse)) throw Missing(GamepadShowcaseInputActions.Pulse);
            if (!input.HasAction(GamepadShowcaseInputActions.CycleColor)) throw Missing(GamepadShowcaseInputActions.CycleColor);
            if (!input.HasAction(GamepadShowcaseInputActions.LeftTrigger)) throw Missing(GamepadShowcaseInputActions.LeftTrigger);
            if (!input.HasAction(GamepadShowcaseInputActions.RightTrigger)) throw Missing(GamepadShowcaseInputActions.RightTrigger);
        }

        private static System.InvalidOperationException Missing(string actionId) =>
            new($"Missing input action: {actionId}");

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
