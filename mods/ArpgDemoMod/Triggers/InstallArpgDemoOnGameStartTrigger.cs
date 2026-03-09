using System.Threading.Tasks;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using ArpgDemoMod.Systems;

namespace ArpgDemoMod.Triggers
{
    /// <summary>
    /// Registers ARPG order source on game start.
    /// Camera is driven by map DefaultCamera PresetId "TPS" (AlwaysFollow).
    /// Follow target is wired in <see cref="ArpgSetupOnMapLoadedTrigger"/>.
    /// </summary>
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
                installedObj is bool installed && installed)
            {
                return Task.CompletedTask;
            }
            engine.GlobalContext[InstalledKey] = true;
            _ctx.Log("[ArpgDemoMod] Ability definitions loaded via GAS/abilities.json");

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out var oq) && oq is OrderQueue orders)
            {
                engine.RegisterSystem(new ArpgLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx), SystemGroup.InputCollection);
                _ctx.Log("[ArpgDemoMod] ArpgLocalOrderSourceSystem registered");
            }

            ViewModeRegistrar.RegisterFromVfs(_ctx, engine.GlobalContext, "TPS");

            return Task.CompletedTask;
        }
    }
}

