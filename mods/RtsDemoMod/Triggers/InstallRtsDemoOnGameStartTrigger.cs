using System.Threading.Tasks;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using RtsDemoMod.Runtime;
using RtsDemoMod.Systems;

namespace RtsDemoMod.Triggers
{
    /// <summary>
    /// Registers RTS abilities, 3-faction teams, and SC2-style order source on game start.
    /// Camera is driven by map DefaultCamera.VirtualCameraId "Rts"; no manual setup is needed.
    /// </summary>
    public sealed class InstallRtsDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "RtsDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallRtsDemoOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var obj) && obj is bool b && b)
                return Task.CompletedTask;
            engine.GlobalContext[InstalledKey] = true;
            _ctx.Log("[RtsDemoMod] Ability definitions loaded via GAS/abilities.json");

            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(2, 3, TeamRelationship.Hostile);

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out var oq) && oq is OrderQueue orders)
            {
                engine.RegisterSystem(new RtsLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx), SystemGroup.InputCollection);
                _ctx.Log("[RtsDemoMod] RTS local order source system registered");
            }

            engine.RegisterSystem(new RtsPresentationBootstrapSystem(engine), SystemGroup.PostMovement);
            engine.SetService(CoreServiceKeys.EntityCommandPanelToolbarProvider, new RtsQuickSelectToolbarProvider(engine));
            // Run after effect/spawn processing so relation-driven garrison/build/morph state
            // becomes visible in the same simulation frame.
            engine.RegisterSystem(new RtsRelationRuntimeSystem(engine), SystemGroup.EffectProcessing);
            engine.RegisterPresentationSystem(new RtsCommandSourceCommandPanelSystem(engine));
            engine.InsertPresentationSystemBefore<PerformerRuleSystem>(new RtsCommandSourceFeedbackPresentationSystem(engine));
            _ctx.Log("[RtsDemoMod] RTS relation runtime and command-source panel systems registered");

            ViewModeRegistrar.RegisterFromVfs(_ctx, engine.GlobalContext, "Rts");

            return Task.CompletedTask;
        }
    }
}
