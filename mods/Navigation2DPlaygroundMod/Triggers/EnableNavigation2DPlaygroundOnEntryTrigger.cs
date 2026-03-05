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
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;
using Navigation2DPlaygroundMod.Systems;

namespace Navigation2DPlaygroundMod.Triggers
{
    public sealed class EnableNavigation2DPlaygroundOnEntryTrigger : Trigger
    {
        private readonly IModContext _ctx;
        private bool _installed;

        public EnableNavigation2DPlaygroundOnEntryTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.MapLoaded;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var mapId = context.Get<MapId>(ContextKeys.MapId);
            bool isEntry = mapId.Value == engine.MergedConfig.StartupMapId;

            if (isEntry)
            {
                if (!_installed)
                {
                    if (!engine.GlobalContext.TryGetValue(ContextKeys.Navigation2DRuntime, out var navObj) || navObj is not Navigation2DRuntime navRuntime)
                    {
                        throw new InvalidOperationException("Navigation2DPlaygroundMod requires Navigation2D.Enabled=true so Navigation2DRuntime exists in GlobalContext.");
                    }

                    // Fix1: Playground场景自动开启FlowField导航
                    navRuntime.FlowEnabled = true;

                    var debugDrawBuffer = new DebugDrawCommandBuffer();
                    engine.GlobalContext[ContextKeys.DebugDrawCommandBuffer] = debugDrawBuffer;

                    engine.RegisterSystem(new Physics2DToWorldPositionSyncSystem(engine.World), SystemGroup.PostMovement);
                    engine.RegisterSystem(new IntegrationSystem2D(engine.World), SystemGroup.InputCollection);

                    engine.RegisterPresentationSystem(new Navigation2DPlaygroundControlSystem(engine));
                    engine.RegisterPresentationSystem(new Navigation2DPlaygroundPresentationSystem(engine, debugDrawBuffer));

                    Navigation2DPlaygroundControlSystem.SpawnScenario(engine.World, Navigation2DPlaygroundState.AgentsPerTeam);
                    engine.GlobalContext[ContextKeys.Navigation2DPlayground_AgentsPerTeam] = Navigation2DPlaygroundState.AgentsPerTeam;
                    engine.GlobalContext[ContextKeys.Navigation2DPlayground_LiveAgentsTotal] = Navigation2DPlaygroundState.AgentsPerTeam * 2;

                    _installed = true;
                    _ctx.Log("[Navigation2DPlaygroundMod] Installed systems and spawned two-team pass-through scenario.");
                }

                Navigation2DPlaygroundState.Enabled = true;

                var session = context.Get<GameSession>(ContextKeys.GameSession);
                var input = context.Get<PlayerInputHandler>(ContextKeys.InputHandler);
                if (session != null && input != null)
                {
                    session.Camera.State.TargetCm = System.Numerics.Vector2.Zero;
                        session.Camera.State.Pitch = 65f;
                        session.Camera.State.DistanceCm = 18000f;

                    if (session.Camera.Controller == null)
                    {
                        engine.GlobalContext[ContextKeys.CameraControllerRequest] = new CameraControllerRequest
                        {
                            Id = CameraControllerIds.Orbit3C,
                            Config = new Orbit3CCameraConfig
                            {
                                EnablePan = true,
                                PanCmPerSecond = 12000f,
                                ZoomCmPerWheel = 10000f,
                                RotateDegPerSecond = 90f
                            }
                        };
                    }
                }
            }
            else
            {
                Navigation2DPlaygroundState.Enabled = false;
            }

            return Task.CompletedTask;
        }
    }
}
