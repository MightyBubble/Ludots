using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Physics2D;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
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
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            var mapId = context.Get(CoreServiceKeys.MapId);
            bool isEntry = mapId.Value == engine.MergedConfig.StartupMapId;

            if (isEntry)
            {
                if (!_installed)
                {
                    var debugDrawBuffer = new DebugDrawCommandBuffer();
                    engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDrawBuffer);

                    var solverConfig = context.Get(CoreServiceKeys.Physics2DSolverConfig)
                        ?? throw new System.InvalidOperationException("Physics2DPlaygroundMod requires Physics2DSolverConfig.");
                    var shapeStorage = context.Get(CoreServiceKeys.Physics2DShapeStorage) as ShapeDataStorage2D
                        ?? throw new System.InvalidOperationException("Physics2DPlaygroundMod requires Physics2D shape storage.");

                    _sim = engine.GlobalContext.TryGetValue(
                            "Ludots.Core.Physics2D.Ticking.Physics2DSimulationSystem",
                            out object? systemObj) &&
                        systemObj is Physics2DSimulationSystem coreSimulation
                            ? coreSimulation
                            : throw new System.InvalidOperationException(
                                "Physics2DPlaygroundMod requires the Core Physics2D simulation system registered from Physics2D/clock.json.");
                    engine.RegisterSystem(new Physics2DPlaygroundInteractionSystem(engine, _sim, shapeStorage, solverConfig), SystemGroup.InputCollection);
                    engine.RegisterPresentationSystem(new Physics2DDebugDrawSystem(engine.World, debugDrawBuffer, shapeStorage));

                    _installed = true;
                    _ctx.Log("[Physics2DPlaygroundMod] Attached to Core Physics2D simulation and installed interaction/debug presentation systems.");
                }

                Physics2DPlaygroundState.Enabled = true;
                if (_sim != null)
                {
                    _sim.Enabled = true;
                }

                var input = context.Get(CoreServiceKeys.InputHandler);
                if (input != null)
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
                        Pitch = 60f,
                        DistanceCm = 12000f,
                        FovYDeg = 60f
                    });
                }
            }
            else
            {
                Physics2DPlaygroundState.Enabled = false;
                if (_sim != null)
                {
                    _sim.Enabled = false;
                }

                if (engine.GlobalContext.TryGetValue(CoreServiceKeys.Physics2DController.Name, out var physicsCtlObj) &&
                    physicsCtlObj is Physics2DController physicsCtl)
                {
                    physicsCtl.Disable();
                }

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
