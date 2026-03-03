using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ArpgDemoMod.Triggers
{
    public sealed class InstallArpgDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "ArpgDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallArpgDemoOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }
            engine.GlobalContext[InstalledKey] = true;
            _ctx.Log("[ArpgDemoMod] Ability definitions loaded via GAS/abilities.json");

            return Task.CompletedTask;
        }
    }
}

