using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FourXDemoMod.Triggers
{
    public sealed class InstallFourXDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "FourXDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallFourXDemoOnGameStartTrigger(IModContext ctx)
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
            _ctx.Log("[FourXDemoMod] Ability definitions loaded via GAS/abilities.json");

            return Task.CompletedTask;
        }
    }
}

