using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using UxPrototypeMod.Runtime;
using UxPrototypeMod.Systems;

namespace UxPrototypeMod.Triggers;

internal sealed class InstallUxPrototypeOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "UxPrototypeMod.Installed";
    private readonly IModContext _context;
    private readonly UxPrototypeRuntime _runtime;

    public InstallUxPrototypeOnGameStartTrigger(IModContext context, UxPrototypeRuntime runtime)
    {
        _context = context;
        _runtime = runtime;
        EventKey = GameEvents.GameStart;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
            installedObj is bool installed &&
            installed)
        {
            return Task.CompletedTask;
        }

        engine.GlobalContext[InstalledKey] = true;
        engine.GlobalContext["UxPrototypeMod.Runtime"] = _runtime;
        engine.GlobalContext["UxPrototypeMod.State"] = _runtime.State;

        if (engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry) is not IEntityCommandPanelSourceRegistry panelSources)
        {
            throw new InvalidOperationException("UxPrototypeMod requires EntityCommandPanelSourceRegistry from EntityCommandPanelMod.");
        }

        panelSources.Register(UxPrototypeEntityCommandPanelSource.SourceId, new UxPrototypeEntityCommandPanelSource(engine, _runtime.State));

        if (engine.GetService(CoreServiceKeys.OrderQueue) is OrderQueue orders)
        {
            engine.RegisterSystem(
                new UxPrototypeLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _context),
                SystemGroup.InputCollection);
        }

        engine.RegisterSystem(new UxPrototypeSimulationSystem(engine, _runtime), SystemGroup.InputCollection);
        engine.RegisterPresentationSystem(new UxPrototypePanelPresentationSystem(engine, _runtime));
        _context.Log("[UxPrototypeMod] Prototype order source, simulation state, and HUD presentation registered.");
        return Task.CompletedTask;
    }
}
