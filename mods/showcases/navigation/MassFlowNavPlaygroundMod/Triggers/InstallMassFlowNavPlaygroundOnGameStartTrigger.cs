using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using MassFlowNavPlaygroundMod.Runtime;
using MassFlowNavPlaygroundMod.Systems;

namespace MassFlowNavPlaygroundMod.Triggers
{
    internal sealed class InstallMassFlowNavPlaygroundOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;
        private readonly MassFlowNavPlaygroundRuntime _runtime;

        public InstallMassFlowNavPlaygroundOnGameStartTrigger(IModContext context, MassFlowNavPlaygroundRuntime runtime)
        {
            _context = context;
            _runtime = runtime;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            if (engine.TryGetService(MassFlowNavPlaygroundServiceKeys.Installed, out bool installed) && installed)
            {
                return Task.CompletedTask;
            }

            engine.SetService(MassFlowNavPlaygroundServiceKeys.Installed, true);
            engine.SetService(MassFlowNavPlaygroundServiceKeys.State, new MassFlowNavPlaygroundState());
            TeamManager.SetRelationshipSymmetric(
                MassFlowNavPlaygroundIds.FriendlyTeamId,
                MassFlowNavPlaygroundIds.EnemyTeamId,
                TeamRelationship.Hostile);

            engine.RegisterSystem(new MassFlowNavPlaygroundNavRuntimeSystem(engine), SystemGroup.InputCollection);
            engine.RegisterSystem(new MassFlowNavPlaygroundCommandSystem(engine), SystemGroup.InputCollection);
            engine.RegisterSystem(new MassFlowNavPlaygroundFormationRuntimeSystem(engine), SystemGroup.InputCollection);
            engine.RegisterSystem(new MassFlowNavPlaygroundMotionProbeSystem(engine), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new MassFlowNavPlaygroundPrimitiveRenderSystem(engine));
            engine.RegisterPresentationSystem(new MassFlowNavPlaygroundPanelPresentationSystem(engine, _runtime));
            engine.RegisterPresentationSystem(new MassFlowNavPlaygroundHudOverlaySystem(engine));
            engine.RegisterPresentationSystem(new MassFlowNavPlaygroundSelectionOverlaySystem(engine));
            _context.Log("[MassFlowNavPlaygroundMod] Installed mass-flow playground runtime and panel systems.");
            return Task.CompletedTask;
        }
    }
}
