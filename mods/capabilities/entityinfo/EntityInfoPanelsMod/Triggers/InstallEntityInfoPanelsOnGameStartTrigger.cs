using System.Threading.Tasks;
using EntityInfoPanelsMod.Insight;
using EntityInfoPanelsMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.GAS;
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
        RelationshipRuntime relationshipRuntime = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.RelationshipRuntime to be registered.");
        RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.RelationshipTypeRegistry to be registered.");
        RelationshipMetricRegistry relationshipMetrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.RelationshipMetricRegistry to be registered.");
        RelationshipFlagRegistry relationshipFlags = engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
            ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.RelationshipFlagRegistry to be registered.");
        var imageSourceResolver = new PresentationImageSourceResolver(imageRegistry, engine.VFS, backendId);
        var imageBindingResolver = new PresentationImageBindingResolver(imageSourceResolver);
        var runtimeAdapter = new EntityInsightRuntimeAdapter(
            imageSourceResolver,
            imageBindingResolver,
            relationshipRuntime,
            relationshipTypes,
            relationshipMetrics,
            relationshipFlags);
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
            runtimeAdapter,
            engine.GetService(CoreServiceKeys.TagOps)
                ?? throw new InvalidOperationException("EntityInfoPanelsMod requires CoreServiceKeys.TagOps to be registered."),
            engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry));
        var handles = new EntityInfoPanelHandleStore();
        engine.SetService(EntityInfoPanelServiceKeys.Service, service);
        engine.SetService(EntityInfoPanelServiceKeys.HandleStore, handles);
        engine.RegisterPresentationSystem(new EntityInfoPanelPresentationSystem(engine, service));
        engine.GlobalContext[InstalledKey] = true;

        _context.Log("[EntityInfoPanelsMod] Service, handle store, and presentation system registered.");
        return Task.CompletedTask;
    }
}
