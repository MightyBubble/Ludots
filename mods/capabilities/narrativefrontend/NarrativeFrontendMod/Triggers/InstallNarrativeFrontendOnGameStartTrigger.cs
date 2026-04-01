using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NarrativeFrontendMod.Runtime;
using NarrativeFrontendMod.Systems;

namespace NarrativeFrontendMod.Triggers;

internal sealed class InstallNarrativeFrontendOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "NarrativeFrontendMod.Installed";
    private readonly IModContext _context;

    public InstallNarrativeFrontendOnGameStartTrigger(IModContext context)
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

        var service = new NarrativeFrontendService();
        engine.SetService(NarrativeFrontendServiceKeys.Service, service);
        engine.RegisterPresentationSystem(new NarrativeFrontendPresentationSystem(engine, service));

        _context.Log("[NarrativeFrontendMod] Service and presentation system registered.");
        return Task.CompletedTask;
    }
}
