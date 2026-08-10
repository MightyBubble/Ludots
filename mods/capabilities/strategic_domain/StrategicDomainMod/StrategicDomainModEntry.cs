using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using StrategicDomainMod.Providers;
using StrategicDomainMod.Runtime;

namespace StrategicDomainMod
{
    public sealed class StrategicDomainModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Log("[StrategicDomainMod] Loaded.");
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                GameEngine engine = ctx.Get(CoreServiceKeys.Engine)
                    ?? throw new InvalidOperationException("GameEngine missing.");
                ProviderServices providers = ctx.Get(CoreServiceKeys.ProviderServices)
                    ?? throw new InvalidOperationException("ProviderServices missing.");
                var runtime = new StrategicDomainRuntime(engine.World);
                StrategicDomainProviderInstaller.Install(providers, runtime);
                engine.SetService(StrategicDomainServiceKeys.Runtime, runtime);
                ctx.Set(StrategicDomainServiceKeys.Runtime, runtime);
                context.Log("[StrategicDomainMod] Domain providers installed.");
                return Task.CompletedTask;
            });
        }

        public void OnUnload()
        {
        }
    }
}
