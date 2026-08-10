using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using StrategicDomainMod.Providers;
using StrategicDomainMod.Runtime;

namespace StrategicDomainMod.Triggers
{
    public sealed class InstallStrategicDomainOnGameStartTrigger
    {
        private readonly IModContext _context;

        public InstallStrategicDomainOnGameStartTrigger(IModContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public Task ExecuteAsync(ScriptContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            GameEngine engine = context.Get(CoreServiceKeys.Engine)
                ?? throw new InvalidOperationException("GameEngine missing.");
            ProviderServices providers = context.Get(CoreServiceKeys.ProviderServices)
                ?? throw new InvalidOperationException("ProviderServices missing.");

            var runtime = new StrategicDomainRuntime(engine.World);
            StrategicDomainProviderInstaller.Install(providers, runtime);
            engine.SetService(StrategicDomainServiceKeys.Runtime, runtime);
            context.Set(StrategicDomainServiceKeys.Runtime, runtime);
            _context.Log("[StrategicDomainMod] Domain providers installed.");
            return Task.CompletedTask;
        }
    }
}
