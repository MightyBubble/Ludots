using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MinimapControlMod.Runtime;
using MinimapControlMod.Systems;

namespace MinimapControlMod.Triggers;

internal sealed class InstallMinimapControlOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "MinimapControlMod.Installed";
    private readonly IModContext _context;

    public InstallMinimapControlOnGameStartTrigger(IModContext context)
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

        if (engine.GlobalContext.TryGetValue(InstalledKey, out object? installedObj) &&
            installedObj is bool installed &&
            installed)
        {
            return Task.CompletedTask;
        }

        engine.GlobalContext[InstalledKey] = true;

        var runtime = new MinimapControlRuntime();
        engine.SetService(MinimapControlServiceKeys.Runtime, runtime);
        engine.RegisterPresentationSystem(new MinimapControlPresentationSystem(engine, runtime));

        _context.Log("[MinimapControlMod] Runtime and presentation overlay installed.");
        return Task.CompletedTask;
    }
}
