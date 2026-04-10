using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundHudOverlaySystem : ISystem<float>
    {
        private static readonly QueryDescription PhysicsPerfQuery = new QueryDescription().WithAll<Physics2DPerfStats>();
        private static readonly Vector4 PanelFill = new(0.03f, 0.06f, 0.10f, 0.80f);
        private static readonly Vector4 PanelBorder = new(0.30f, 0.52f, 0.70f, 0.95f);
        private static readonly Vector4 TitleColor = new(0.96f, 0.97f, 0.99f, 1f);
        private static readonly Vector4 DetailColor = new(0.76f, 0.83f, 0.91f, 1f);
        private static readonly Vector4 ReadyColor = new(0.95f, 0.80f, 0.43f, 1f);

        private readonly GameEngine _engine;
        private float _smoothedFps;

        public MassFlowNavPlaygroundHudOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (_engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive ||
                !string.Equals(_engine.CurrentMapSession?.MapId.Value, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase) ||
                _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay ||
                _engine.GetService(CoreServiceKeys.ViewController) is not IViewController view)
            {
                return;
            }

            float instantFps = t > 0.00001f ? 1f / t : 0f;
            _smoothedFps = _smoothedFps <= 0.001f
                ? instantFps
                : _smoothedFps + ((instantFps - _smoothedFps) * 0.18f);

            int selectedCount = SelectionContextRuntime.GetCurrentCount(_engine.World, _engine.GlobalContext);
            bool uiCaptured = _engine.GetService(CoreServiceKeys.UiCaptured);
            bool hasGroundPointer = _engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader input &&
                                    AuthoritativeGroundPointerHelper.TryRead(input, out _);
            InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(_engine.GlobalContext, nameof(MassFlowNavPlaygroundHudOverlaySystem));
            bool liveCommandPressed = _engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader liveInput &&
                                      liveInput.PressedThisFrame(bindings.CommandActionId);
            bool liveCommandDown = _engine.GetService(CoreServiceKeys.AuthoritativeInput) is IInputActionReader heldInput &&
                                   heldInput.IsDown(bindings.CommandActionId);
            bool hasPointerSnapshot = PointerInteractionSnapshotReader.TryRead(_engine.GlobalContext, out PointerInteractionSnapshot pointer);
            bool snapshotCommandPressed = hasPointerSnapshot && pointer.Command.PressedThisFrame;
            bool snapshotCommandDown = hasPointerSnapshot && pointer.Command.IsDown;
            bool navRuntimeReady = _engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime;

            int width = 620;
            int height = 180;
            int x = Math.Max(12, (int)view.Resolution.X - width - 16);
            int y = 16;

            PresentationTimingDiagnostics? timing = _engine.GetService(CoreServiceKeys.PresentationTimingDiagnostics);
            Physics2DPerfStats physics = ReadPhysicsPerfStats();
            int navHz = _engine.GetService(CoreServiceKeys.Navigation2DTickPolicy) is Navigation2DTickPolicy tickPolicy ? tickPolicy.TargetHz : 0;
            string perfLine = navRuntimeReady && _engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime readyNav
                ? $"prim {timing?.PrimitiveRenderMs ?? 0f:0.0}ms inst {timing?.PrimitiveInstancesLastFrame ?? 0}/{timing?.PrimitiveBatchesLastFrame ?? 0} phys {physics.PhysicsUpdateMs:0.0}ms navHz {navHz} flow {readyNav.FlowIterationsPerTick} cache {readyNav.AgentSoA.SteeringCacheHitsFrame}/{readyNav.AgentSoA.SteeringCacheLookupsFrame}"
                : $"prim {timing?.PrimitiveRenderMs ?? 0f:0.0}ms inst 0/0 phys {physics.PhysicsUpdateMs:0.0}ms navHz {navHz}";

            overlay.AddRect(x, y, width, height, PanelFill, PanelBorder);
            overlay.AddText(x + 12, y + 10, $"FPS {MathF.Round(_smoothedFps):0}", 20, TitleColor);
            overlay.AddText(x + 12, y + 38, $"Selected {selectedCount} | Manual {state.ManualCount}", 14, DetailColor);
            overlay.AddText(
                x + 12,
                y + 58,
                $"RMB live d={Bool01(liveCommandDown)} p={Bool01(liveCommandPressed)} | snap d={Bool01(snapshotCommandDown)} p={Bool01(snapshotCommandPressed)}",
                12,
                ReadyColor);
            overlay.AddText(
                x + 12,
                y + 76,
                hasGroundPointer
                    ? $"Ground ready | ui={Bool01(uiCaptured)} | nav={Bool01(navRuntimeReady)}"
                    : $"Ground unresolved | ui={Bool01(uiCaptured)} | nav={Bool01(navRuntimeReady)}",
                12,
                ReadyColor);
            overlay.AddText(x + 12, y + 94, state.LastCommandDebug, 12, DetailColor);
            overlay.AddText(x + 12, y + 112, state.LastCommandInputDebug, 12, DetailColor);
            overlay.AddText(x + 12, y + 130, state.LastMotionProbeDebug, 12, DetailColor);
            overlay.AddText(x + 12, y + 148, perfLine, 12, DetailColor);
        }

        private static string Bool01(bool value)
        {
            return value ? "1" : "0";
        }

        private Physics2DPerfStats ReadPhysicsPerfStats()
        {
            Physics2DPerfStats stats = default;
            _engine.World.Query(in PhysicsPerfQuery, (Entity _, ref Physics2DPerfStats value) =>
            {
                stats = value;
            });
            return stats;
        }
    }
}
