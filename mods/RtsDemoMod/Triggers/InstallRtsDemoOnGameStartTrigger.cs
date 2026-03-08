using System.Threading.Tasks;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Navigation2D.Systems;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Scripting;
using RtsDemoMod.Systems;

namespace RtsDemoMod.Triggers
{
    public sealed class InstallRtsDemoOnGameStartTrigger : Trigger
    {
        private readonly IModContext _ctx;

        public InstallRtsDemoOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(RtsDemoKeys.Installed, out var obj) && obj is bool installed && installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[RtsDemoKeys.Installed] = true;
            engine.GlobalContext[RtsDemoKeys.SelectionState] = new RtsSelectionState();
            engine.GlobalContext[RtsDemoKeys.IsActiveMap] = false;

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.DebugDrawCommandBuffer.Name, out var debugObj) || debugObj is not DebugDrawCommandBuffer debugDraw)
            {
                debugDraw = new DebugDrawCommandBuffer();
                engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            }

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out var orderObj) && orderObj is OrderQueue orders)
            {

                engine.RegisterSystem(new RtsScenarioSystem(engine, orders), SystemGroup.InputCollection);
                engine.RegisterSystem(new RtsLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx), SystemGroup.InputCollection);
            }

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderTypeRegistry.Name, out var registryObj) && registryObj is OrderTypeRegistry orderTypeRegistry)
            {
                int stopOrderTagId = engine.GetService(CoreServiceKeys.GameConfig).Constants.OrderTags["stop"];
                engine.RegisterSystem(new RtsStopOrderNavMoveCleanupSystem(engine.World, stopOrderTagId), SystemGroup.AbilityActivation);
                engine.RegisterSystem(new Ludots.Core.Gameplay.GAS.Systems.StopOrderSystem(engine.World, orderTypeRegistry), SystemGroup.AbilityActivation);
                engine.RegisterSystem(new NavBlackboardSinkSystem(engine.World), SystemGroup.AbilityActivation);
                engine.RegisterSystem(new NavArrivalSystem(engine.World, engine.EventBus), SystemGroup.PostMovement);
            }

            engine.RegisterSystem(new IntegrationSystem2D(engine.World), SystemGroup.InputCollection);
            engine.RegisterSystem(new Physics2DToWorldPositionSyncSystem(engine.World), SystemGroup.PostMovement);

            var meshes = context.Get(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry ?? new MeshAssetRegistry();
            engine.RegisterPresentationSystem(new RtsPresentationSystem(engine, debugDraw, meshes));

            ViewModeRegistrar.RegisterFromVfs(_ctx, engine.GlobalContext, "Rts");
            _ctx.Log("[RtsDemoMod] Installed playable RTS input, scenario, navigation, and presentation systems.");
            return Task.CompletedTask;
        }
    }
}




