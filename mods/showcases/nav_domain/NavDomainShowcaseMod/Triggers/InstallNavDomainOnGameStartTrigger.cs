using System.Threading.Tasks;
using Ludots.Core.Scripting;
using NavDomainShowcaseMod.Runtime;
using NavDomainShowcaseMod.Systems;

namespace NavDomainShowcaseMod.Triggers;

internal sealed class InstallNavDomainOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "NavDomainShowcaseMod.Installed";
    private readonly NavDomainAuthoringRuntime _runtime;

    public InstallNavDomainOnGameStartTrigger(NavDomainAuthoringRuntime runtime)
    {
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
        engine.RegisterPresentationSystem(new NavDomainPresentationSystem(engine, _runtime));
        return Task.CompletedTask;
    }
}
