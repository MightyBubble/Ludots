using System.Threading.Tasks;
using EntityCommandPanelShowcaseMod.DataPlane;
using EntityCommandPanelShowcaseMod.Runtime;
using EntityCommandPanelShowcaseMod.Systems;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace EntityCommandPanelShowcaseMod
{
    public sealed class EntityCommandPanelShowcaseModEntry : IMod
    {
        private EntityCommandPanelShowcaseDataPlaneInstallation? _dataPlaneInstallation;

        public void OnLoad(IModContext context)
        {
            context.Log("[EntityCommandPanelShowcaseMod] Loaded");
            var runtime = new EntityCommandPanelShowcaseRuntime();

            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    engine.RegisterPresentationSystem(new EntityCommandPanelShowcasePresentationSystem(engine, runtime));
                }

                return Task.CompletedTask;
            });
            context.OnEvent(GameEvents.MapLoaded, async ctx =>
            {
                await runtime.HandleMapFocusedAsync(ctx).ConfigureAwait(false);
                await TryInstallDataPlaneForFocusedMapAsync(ctx, context).ConfigureAwait(false);
            });
            context.OnEvent(GameEvents.MapResumed, async ctx =>
            {
                await runtime.HandleMapFocusedAsync(ctx).ConfigureAwait(false);
                await TryInstallDataPlaneForFocusedMapAsync(ctx, context).ConfigureAwait(false);
            });
            context.OnEvent(GameEvents.MapUnloaded, async ctx =>
            {
                await runtime.HandleMapUnloadedAsync(ctx).ConfigureAwait(false);
                DisposeDataPlane();
            });
        }

        public void OnUnload()
        {
            DisposeDataPlane();
        }

        private async Task TryInstallDataPlaneForFocusedMapAsync(ScriptContext ctx, IModContext modContext)
        {
            var engine = ctx.GetEngine();
            if (engine == null ||
                !string.Equals(engine.CurrentMapSession?.MapId.Value, EntityCommandPanelShowcaseIds.MapId, System.StringComparison.Ordinal))
            {
                return;
            }

            _dataPlaneInstallation ??= await EntityCommandPanelShowcaseDataPlaneInstaller
                .TryInstallAsync(engine, modContext)
                .ConfigureAwait(false);
        }

        private void DisposeDataPlane()
        {
            _dataPlaneInstallation?.Dispose();
            _dataPlaneInstallation = null;
        }
    }
}
