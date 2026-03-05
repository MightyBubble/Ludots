using Arch.System;
using GmConsoleMod.Input;
using GmConsoleMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GmConsoleMod
{
    public sealed class GmConsoleModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[GmConsoleMod] Loaded — Backquote (`) to toggle GM console");

            context.SystemFactoryRegistry.RegisterPresentation("GmConsole", scriptCtx =>
            {
                var engine = scriptCtx.GetEngine();
                if (engine == null) return new NoopSystem();
                return new GmConsoleSystem(engine);
            });

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    if (engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                        inputObj is PlayerInputHandler input)
                    {
                        if (input.HasContext(GmConsoleInputContexts.Console))
                            input.PushContext(GmConsoleInputContexts.Console);
                    }

                    var sfr = engine.ModLoader.SystemFactoryRegistry;
                    sfr.TryActivate("GmConsole", ctx, engine);
                }
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
    }
}
