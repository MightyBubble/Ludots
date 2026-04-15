using System.Threading.Tasks;
using EntityInfoPanelsMod.Insight;
using EntityInfoPanelsMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
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

        PresentationTextCatalog presentationTextCatalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.PresentationTextCatalog to be registered.");
        PresentationTextLocaleSelection presentationLocaleSelection = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.PresentationTextLocaleSelection to be registered.");
        PresentationSemanticCatalog presentationSemanticCatalog = engine.GetService(CoreServiceKeys.PresentationSemanticCatalog)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.PresentationSemanticCatalog to be registered.");
        PresentationImageRegistry imageRegistry = engine.GetService(CoreServiceKeys.PresentationImageRegistry)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.PresentationImageRegistry to be registered.");
        string backendId = engine.GetService(CoreServiceKeys.PresentationBackendId)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.PresentationBackendId to be registered.");
        var imageSourceResolver = new PresentationImageSourceResolver(imageRegistry, engine.VFS, backendId);
        var insightCatalog = new EntityInsightProfileLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport,
            engine.MapLoader.EntityTemplateKeys,
            presentationTextCatalog,
            imageRegistry);

        var service = new EntityInfoPanelService(
            insightCatalog,
            presentationTextCatalog,
            presentationLocaleSelection,
            presentationSemanticCatalog,
            imageSourceResolver,
            engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry),
            engine.GetService(CoreServiceKeys.TagOps));
        var handles = new EntityInfoPanelHandleStore();
        engine.SetService(EntityInfoPanelServiceKeys.Service, service);
        engine.SetService(EntityInfoPanelServiceKeys.HandleStore, handles);
        engine.RegisterPresentationSystem(new EntityInfoPanelPresentationSystem(engine, service));

        _context.Log("[EntityInfoPanelsMod] Service, handle store, and presentation system registered.");
        return Task.CompletedTask;
    }
}
