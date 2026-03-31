using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using TimeflowShowcaseMod.Runtime;
using TimeflowShowcaseMod.Systems;

namespace TimeflowShowcaseMod.Triggers;

internal sealed class InstallTimeflowShowcaseOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "TimeflowShowcaseMod.Installed";

    private readonly IModContext _context;
    private readonly TimeflowShowcaseRuntime _runtime;

    public InstallTimeflowShowcaseOnGameStartTrigger(IModContext context, TimeflowShowcaseRuntime runtime)
    {
        _context = context;
        _runtime = runtime;
        EventKey = GameEvents.GameStart;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
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
        engine.SetService(TimeflowShowcaseServiceKeys.Runtime, _runtime);
        engine.RegisterSystem(new TimeflowShowcaseSimulationSystem(engine, _runtime), SystemGroup.PostMovement);
        engine.RegisterPresentationSystem(new TimeflowShowcaseOverlaySystem(engine, _runtime));
        _context.Log("[TimeflowShowcaseMod] Registered simulation and overlay systems.");
        return Task.CompletedTask;
    }
}
