using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassNavigationMod.Systems;

namespace MassNavigationMod.Triggers;

public sealed class InstallMassNavigationInputOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "MassNavigationMod.LocalInputInstalled";
    private readonly IModContext _context;

    public InstallMassNavigationInputOnGameStartTrigger(IModContext context)
    {
        _context = context;
        EventKey = GameEvents.GameStart;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (engine.GlobalContext.TryGetValue(InstalledKey, out object? installedObj) &&
            installedObj is bool installed &&
            installed)
        {
            return Task.CompletedTask;
        }

        engine.GlobalContext[InstalledKey] = true;
        if (engine.GetService(CoreServiceKeys.OrderQueue) is not OrderQueue orders)
        {
            throw new System.InvalidOperationException("MassNavigationMod requires OrderQueue before installing local order source.");
        }

        engine.RegisterSystem(new MassNavigationLocalOrderSourceSystem(engine, orders, _context), SystemGroup.InputCollection);
        _context.Log("[MassNavigationMod] Local order source registered.");
        return Task.CompletedTask;
    }
}
