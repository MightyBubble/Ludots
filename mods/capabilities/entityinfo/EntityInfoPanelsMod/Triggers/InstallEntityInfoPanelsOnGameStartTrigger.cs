using System.Threading.Tasks;
using EntityInfoPanelsMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace EntityInfoPanelsMod.Triggers;

internal sealed class InstallEntityInfoPanelsOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "EntityInfoPanelsMod.Installed";
    private readonly IModContext _context;

    public InstallEntityInfoPanelsOnGameStartTrigger(IModContext context)
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

        var service = new EntityInfoPanelService();
        var handles = new EntityInfoPanelHandleStore();
        engine.SetService(EntityInfoPanelServiceKeys.Service, service);
        engine.SetService(EntityInfoPanelServiceKeys.HandleStore, handles);
        engine.RegisterPresentationSystem(new EntityInfoPanelPresentationSystem(engine, service));

        _context.Log("[EntityInfoPanelsMod] Service, handle store, and presentation system registered.");
        return Task.CompletedTask;
    }
}
