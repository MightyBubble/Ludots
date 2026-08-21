using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RngShowcaseMod.Runtime;

namespace RngShowcaseMod.Triggers;

internal sealed class InstallRngShowcaseOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "RngShowcaseMod.Installed";
    private readonly IModContext _context;

    public InstallRngShowcaseOnGameStartTrigger(IModContext context)
    {
        _context = context;
        EventKey = GameEvents.GameStart;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (engine.GlobalContext.TryGetValue(InstalledKey, out object? installedObj) && installedObj is bool installed && installed)
        {
            return Task.CompletedTask;
        }

        engine.GlobalContext[InstalledKey] = true;

        var picks = engine.GetService(CoreServiceKeys.RngPickService)
            ?? throw new InvalidOperationException(
                "Core rng pick service is missing; the engine must register it during core system init.");
        var runtime = new RngShowcaseRuntime(picks);
        engine.SetService(RngShowcaseServiceKeys.Runtime, runtime);
        engine.RegisterSystem(new RngShowcaseSystem(engine, runtime, _context.Log), SystemGroup.Cleanup);
        _context.Log($"[RngShowcaseMod] Auto-pick loop running on distribution '{runtime.BuildState()["distribution"]}'.");
        return Task.CompletedTask;
    }
}
