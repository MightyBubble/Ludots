using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Physics2D.Ticking;
using Physics2DPlaygroundMod.Input;
using Physics2DPlaygroundMod.Systems;

namespace Physics2DPlaygroundMod.Triggers
{
    public sealed class EnablePhysics2DPlaygroundOnEntryTrigger : Trigger
    {
        private readonly IModContext _ctx;
        private bool _installed;
        private Physics2DSimulationSystem? _sim;
        private bool _inputContextActive;
        private const string InputContextId = Physics2DPlaygroundInputContexts.Playground;

        public EnablePhysics2DPlaygroundOnEntryTrigger(IModContext ctx)
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
                    var debugDrawBuffer = new DebugDrawCommandBuffer();
                    engine.GlobalContext[ContextKeys.DebugDrawCommandBuffer] = debugDrawBuffer;

                    var clock = context.Get<IClock>(ContextKeys.Clock);
                    var tickPolicy = context.Get<Physics2DTickPolicy>(ContextKeys.Physics2DTickPolicy);
                    _sim = new Physics2DSimulationSystem(engine.World, clock, tickPolicy);
                    engine.RegisterSystem(_sim, Ludots.Core.Engine.SystemGroup.InputCollection);
                    
                    // 统一架构：
                    // SavePreviousWorldPositionSystem 已在 GameEngine.InitializeCoreSystems 中注册到 SchemaUpdate 阶段。
                    // 不要在此重复注册到 InputCollection，否则会在 Physics2D 更新后覆盖 Previous 值，破坏插值。
                    // Physics2DToWorldPositionSyncSystem: 物理结果同步到逻辑层 SSOT
                    // WorldToVisualSyncSystem: 统一的渲染帧插值
                    engine.RegisterSystem(new Physics2DToWorldPositionSyncSystem(engine.World), Ludots.Core.Engine.SystemGroup.PostMovement);
                    
                    engine.RegisterPresentationSystem(new WorldToVisualSyncSystem(engine.World));
                    engine.RegisterPresentationSystem(new Physics2DPlaygroundPresentationSystem(engine, _sim, debugDrawBuffer));

                    _installed = true;
                    _ctx.Log("[Physics2DPlaygroundMod] Installed simulation + presentation systems.");
                }

                Physics2DPlaygroundState.Enabled = true;
                if (_sim != null) _sim.Enabled = true;

                var session = context.Get<GameSession>(ContextKeys.GameSession);
                var input = context.Get<PlayerInputHandler>(ContextKeys.InputHandler);
                if (session != null && input != null)
                {
                    if (!_inputContextActive)
                    {
                        EnsurePlaygroundInputSchema(input);
                        input.PushContext(InputContextId);
                        _inputContextActive = true;
                    }

                    session.Camera.State.TargetCm = System.Numerics.Vector2.Zero;
                    session.Camera.State.Pitch = 60f;
                    session.Camera.State.DistanceCm = 12000f;

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
                Physics2DPlaygroundState.Enabled = false;
                if (_sim != null) _sim.Enabled = false;
                if (engine.GlobalContext.TryGetValue(ContextKeys.Physics2DController, out var physicsCtlObj) && physicsCtlObj is Ludots.Core.Engine.Physics2D.Physics2DController physicsCtl)
                {
                    physicsCtl.Disable();
                }
                var session = context.Get<GameSession>(ContextKeys.GameSession);
                var input = context.Get<PlayerInputHandler>(ContextKeys.InputHandler);
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
            if (!input.HasContext(Physics2DPlaygroundInputContexts.Playground))
            {
                throw new System.InvalidOperationException($"Missing input context: {Physics2DPlaygroundInputContexts.Playground}");
            }

            if (!input.HasAction(Physics2DPlaygroundInputActions.PointerPos)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.PointerPos}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.PrimaryClick)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.PrimaryClick}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.SecondaryClick)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.SecondaryClick}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey1)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey1}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey2)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey2}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey3)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey3}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey4)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey4}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey5)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey5}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey6)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey6}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey7)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey7}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey8)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey8}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.Hotkey9)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.Hotkey9}");
            if (!input.HasAction(Physics2DPlaygroundInputActions.ChainDemo)) throw new System.InvalidOperationException($"Missing input action: {Physics2DPlaygroundInputActions.ChainDemo}");
        }
    }
}
