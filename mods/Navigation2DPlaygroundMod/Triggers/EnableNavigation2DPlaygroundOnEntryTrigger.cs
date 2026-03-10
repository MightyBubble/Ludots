using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Modding;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Navigation2DPlaygroundMod.Input;
using Navigation2DPlaygroundMod.Systems;

namespace Navigation2DPlaygroundMod.Triggers
{
    public sealed class EnableNavigation2DPlaygroundOnEntryTrigger : Trigger
    {
        private readonly IModContext _ctx;
        private bool _installed;
        private bool _inputContextActive;
        private const string InputContextId = Navigation2DPlaygroundInputContexts.Playground;

        public EnableNavigation2DPlaygroundOnEntryTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapId = context.Get(CoreServiceKeys.MapId);
            bool isEntry = mapId.Value == engine.MergedConfig.StartupMapId;

            if (isEntry)
            {
                if (!_installed)
                {
                    if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.Navigation2DRuntime.Name, out var navObj) || navObj is not Navigation2DRuntime navRuntime)
                    {
                        throw new InvalidOperationException("Navigation2DPlaygroundMod requires Navigation2D.Enabled=true so Navigation2DRuntime exists in GlobalContext.");
                    }

                    // Fix1: Playground场景自动开启FlowField导航
                    navRuntime.FlowEnabled = true;

                    var debugDrawBuffer = new DebugDrawCommandBuffer();
                    engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDrawBuffer);

                    engine.RegisterSystem(new Physics2DToWorldPositionSyncSystem(engine.World), SystemGroup.PostMovement);
                    engine.RegisterSystem(new IntegrationSystem2D(engine.World), SystemGroup.InputCollection);

                    engine.RegisterPresentationSystem(new Navigation2DPlaygroundControlSystem(engine));
                    var meshRegistry = context.Get(CoreServiceKeys.PresentationMeshAssetRegistry) as MeshAssetRegistry;
                    engine.RegisterPresentationSystem(new Navigation2DPlaygroundPresentationSystem(engine, debugDrawBuffer, meshRegistry));

                    Navigation2DPlaygroundControlSystem.SpawnScenario(engine.World, Navigation2DPlaygroundState.AgentsPerTeam);
                    engine.SetService(Navigation2DPlaygroundKeys.AgentsPerTeam, Navigation2DPlaygroundState.AgentsPerTeam);
                    engine.SetService(Navigation2DPlaygroundKeys.LiveAgentsTotal, Navigation2DPlaygroundState.AgentsPerTeam * 2);

                    _installed = true;
                    _ctx.Log("[Navigation2DPlaygroundMod] Installed systems and spawned two-team pass-through scenario.");
                }

                Navigation2DPlaygroundState.Enabled = true;

                var session = context.Get(CoreServiceKeys.GameSession);
                var input = context.Get(CoreServiceKeys.InputHandler);
                if (session != null && input != null)
                {
                    if (!_inputContextActive)
                    {
                        EnsurePlaygroundInputSchema(input);
                        input.PushContext(InputContextId);
                        _inputContextActive = true;
                    }

                    engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
                    {
                        Id = "Default"
                    });
                    engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
                    {
                        VirtualCameraId = "Default",
                        TargetCm = System.Numerics.Vector2.Zero,
                        Pitch = 65f,
                        DistanceCm = 18000f
                    });
                }
            }
            else
            {
                Navigation2DPlaygroundState.Enabled = false;
                var input = context.Get(CoreServiceKeys.InputHandler);
                if (input != null && _inputContextActive)
                {
                    input.PopContext(InputContextId);
                    _inputContextActive = false;
                }
            }

            return Task.CompletedTask;
        }

        private static void EnsurePlaygroundInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(Navigation2DPlaygroundInputContexts.Playground))
            {
                throw new InvalidOperationException($"Missing input context: {Navigation2DPlaygroundInputContexts.Playground}");
            }

            if (!input.HasAction(Navigation2DPlaygroundInputActions.ToggleFlowEnabled)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ToggleFlowEnabled}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ToggleFlowDebug)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ToggleFlowDebug}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.CycleFlowDebugMode)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.CycleFlowDebugMode}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.IncreaseFlowIterations)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.IncreaseFlowIterations}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.DecreaseFlowIterations)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.DecreaseFlowIterations}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.IncreaseAgentsPerTeam)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.IncreaseAgentsPerTeam}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.DecreaseAgentsPerTeam)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.DecreaseAgentsPerTeam}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ResetScenario)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ResetScenario}");
        }
    }
}
