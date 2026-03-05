using Arch.System;
using FeatureHubMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FeatureHubMod
{
    public sealed class FeatureHubModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[FeatureHubMod] Loaded — Press 1-9 to navigate demos, 0 to return to hub.");

            context.SystemFactoryRegistry.RegisterPresentation("FeatureHubNavigation", scriptCtx =>
            {
                var engine = scriptCtx.GetEngine();
                if (engine == null) return new NoopSystem();
                return new FeatureHubNavigationSystem(engine);
            });

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    var sfr = engine.ModLoader.SystemFactoryRegistry;
                    sfr.TryActivate("FeatureHubNavigation", ctx, engine);
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
