using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace TcgDemoMod.Triggers
{
    public sealed class InstallTcgDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "TcgDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallTcgDemoOnGameStartTrigger(IModContext ctx)
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
            _ctx.Log("[TcgDemoMod] Ability definitions loaded via GAS/abilities.json");

            return Task.CompletedTask;
        }
    }
}

