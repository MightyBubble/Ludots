using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using TimeFlowShowcaseMod.Systems;

namespace TimeFlowShowcaseMod.Triggers;

internal sealed class InstallTimeFlowShowcaseOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "TimeFlowShowcaseMod.Installed";
    private readonly TimeFlowShowcaseRuntime _runtime;

    public InstallTimeFlowShowcaseOnGameStartTrigger(TimeFlowShowcaseRuntime runtime)
    {
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

        if (!TimeFlowProfileBridge.TryCreate(engine, out TimeFlowProfileBridge? timeFlow) || timeFlow == null)
        {
            throw new InvalidOperationException("TimeFlowShowcaseMod requires TimeFlowMod.");
        }

        _runtime.Attach(engine, timeFlow);
        engine.GlobalContext[InstalledKey] = true;
        engine.SetService(TimeFlowShowcaseServiceKeys.Service, _runtime);
        engine.RegisterSystem(new TimeFlowShowcaseSimulationSystem(engine, _runtime), SystemGroup.InputCollection);
        engine.RegisterPresentationSystem(new TimeFlowShowcasePresentationSystem(engine, _runtime));
        return Task.CompletedTask;
    }
}
