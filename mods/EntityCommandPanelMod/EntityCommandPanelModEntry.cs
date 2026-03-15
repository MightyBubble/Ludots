using System.Threading.Tasks;
using EntityCommandPanelMod.Runtime;
using EntityCommandPanelMod.Systems;
using EntityCommandPanelMod.UI;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace EntityCommandPanelMod
{
    public sealed class EntityCommandPanelModEntry : IMod
    {
        private const string InstalledKey = "EntityCommandPanelMod.Installed";

        public void OnLoad(IModContext context)
        {
            context.Log("[EntityCommandPanelMod] Loaded");
            context.OnEvent(GameEvents.GameStart, ctx => InstallAsync(context, ctx));
        }

        public void OnUnload()
        {
        }

        private static Task InstallAsync(IModContext modContext, ScriptContext context)
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

            var sources = new EntityCommandPanelSourceRegistry();
            var handles = new EntityCommandPanelAliasStore();
            sources.Register(GasEntityCommandPanelSource.SourceId, new GasEntityCommandPanelSource(engine));

            var runtime = new EntityCommandPanelRuntime(engine, sources, handles);
            var controller = new EntityCommandPanelController(engine, runtime);

            engine.SetService(CoreServiceKeys.EntityCommandPanelSourceRegistry, sources);
            engine.SetService(CoreServiceKeys.EntityCommandPanelHandleStore, handles);
            engine.SetService(CoreServiceKeys.EntityCommandPanelService, runtime);
            engine.RegisterPresentationSystem(new EntityCommandPanelPresentationSystem(engine, runtime, controller));

            modContext.Log("[EntityCommandPanelMod] Installed GAS entity command panel runtime.");
            return Task.CompletedTask;
        }
    }
}
