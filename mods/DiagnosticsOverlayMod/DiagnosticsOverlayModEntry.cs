using Arch.System;
using DiagnosticsOverlayMod.Systems;
using Ludots.Core.Engine;
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
                    var sfr = engine.ModLoader.SystemFactoryRegistry;
                    sfr.TryActivate("DiagnosticsOverlay", ctx, engine);
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
