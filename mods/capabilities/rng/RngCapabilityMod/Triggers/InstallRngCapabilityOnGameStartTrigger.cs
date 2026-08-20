using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using RngCapabilityMod.Rng;

namespace RngCapabilityMod.Triggers;

internal sealed class InstallRngCapabilityOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "RngCapabilityMod.Installed";
    private readonly IModContext _context;
    private readonly RngGraphOpContext _opContext;

    public InstallRngCapabilityOnGameStartTrigger(IModContext context, RngGraphOpContext opContext)
    {
        _context = context;
        _opContext = opContext;
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

        var streams = engine.GetService(CoreServiceKeys.RngStreamService)
            ?? throw new InvalidOperationException("Core rng stream service is missing; the engine must register it during core system init.");
        var tables = new DistributionConfigLoader(engine.ConfigPipeline).Load(engine.ConfigCatalog, engine.ConfigConflictReport, streams);
        var service = new RngPickService(streams, tables);

        _opContext.InstallService(service);
        engine.SetService(RngCapabilityServiceKeys.PickService, service);
        _context.Log($"[RngCapabilityMod] Installed {tables.Count} distribution(s): {string.Join(", ", service.DistributionIds)}");
        return Task.CompletedTask;
    }
}
