using Arch.System;
using AuditPlaygroundMod.Input;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using System.Numerics;

namespace AuditPlaygroundMod.Systems
{
    /// <summary>
    /// Interactive controls for demo:
    /// - I: Push inner map
    /// - O: Pop map
    /// - P: Reload outer map
    /// </summary>
    public sealed class AuditMapControlPresentationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private PlayerInputHandler? _input;
        private bool _inputContextPushed;

        public AuditMapControlPresentationSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize()
        {
            Log.Info(in LogChannels.Engine, "[AuditPlaygroundMod] Interactive controls active: I=Push, O=Pop, P=ReloadOuter");
        }

        public void BeforeUpdate(in float t) { }

        public void Update(in float t)
        {
            if (_input == null &&
                _engine.GlobalContext.TryGetValue(ContextKeys.InputHandler, out var inputObj) &&
                inputObj is PlayerInputHandler input)
            {
                EnsureInputSchema(input);
                _input = input;
                _input.PushContext(AuditPlaygroundInputContexts.Playground);
                _inputContextPushed = true;
            }

            if (_input == null) return;

            HandlePressed(AuditPlaygroundInputActions.PushInner, () =>
            {
                _engine.PushMap("audit_inner");
            });

            HandlePressed(AuditPlaygroundInputActions.PopInner, () =>
            {
                if (_engine.MapSessions == null || _engine.MapSessions.All.Count <= 1)
                {
                    Log.Warn(in LogChannels.Engine, "[AuditPlaygroundMod] Pop skipped (no inner map).");
                    return;
                }
                _engine.PopMap();
            });

            HandlePressed(AuditPlaygroundInputActions.ReloadOuter, () =>
            {
                _engine.LoadMap("audit_outer");
            });

            RenderAuditOverlay();
        }

        public void AfterUpdate(in float t) { }
        public void Dispose()
        {
            if (_input != null && _inputContextPushed)
            {
                _input.PopContext(AuditPlaygroundInputContexts.Playground);
                _inputContextPushed = false;
            }
        }

        private void HandlePressed(string actionId, System.Action onPressed)
        {
            if (_input!.PressedThisFrame(actionId))
            {
                onPressed();
            }
        }

        private static void EnsureInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(AuditPlaygroundInputContexts.Playground))
            {
                throw new System.InvalidOperationException($"Missing input context: {AuditPlaygroundInputContexts.Playground}");
            }
            if (!input.HasAction(AuditPlaygroundInputActions.PushInner)) throw new System.InvalidOperationException($"Missing input action: {AuditPlaygroundInputActions.PushInner}");
            if (!input.HasAction(AuditPlaygroundInputActions.PopInner)) throw new System.InvalidOperationException($"Missing input action: {AuditPlaygroundInputActions.PopInner}");
            if (!input.HasAction(AuditPlaygroundInputActions.ReloadOuter)) throw new System.InvalidOperationException($"Missing input action: {AuditPlaygroundInputActions.ReloadOuter}");
        }

        private void RenderAuditOverlay()
        {
            if (!_engine.GlobalContext.TryGetValue(ContextKeys.ScreenOverlayBuffer, out var overlayObj) ||
                overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            int global = _engine.GlobalContext.TryGetValue("Audit.GlobalMapLoadedCount", out var agObj) && agObj is int ag ? ag : 0;
            int scoped = _engine.GlobalContext.TryGetValue("Audit.ScopedMapLoadedCount", out var asObj) && asObj is int asc ? asc : 0;
            int named = _engine.GlobalContext.TryGetValue("Audit.NamedDecoratorCount", out var anObj) && anObj is int an ? an : 0;
            int anchor = _engine.GlobalContext.TryGetValue("Audit.AnchorDecoratorCount", out var aaObj) && aaObj is int aa ? aa : 0;
            int factory = _engine.GlobalContext.TryGetValue("Audit.FactoryActivationCount", out var afObj) && afObj is int af ? af : 0;

            int x = 16;
            int y = 340;
            int w = 420;
            int h = 138;
            var bg = new Vector4(0.02f, 0.05f, 0.02f, 0.7f);
            var border = new Vector4(0.5f, 0.9f, 0.5f, 0.5f);
            var title = new Vector4(0.85f, 1f, 0.85f, 1f);
            var text = new Vector4(0.8f, 0.95f, 0.8f, 1f);
            var hint = new Vector4(0.72f, 0.88f, 0.72f, 0.95f);

            overlay.AddRect(x, y, w, h, bg, border);
            overlay.AddText(x + 10, y + 8, "Audit Playground", 16, title);
            overlay.AddText(x + 10, y + 30, $"Global={global}  Scoped={scoped}  Named={named}", 14, text);
            overlay.AddText(x + 10, y + 50, $"Anchor={anchor}  FactoryActivation={factory}", 14, text);
            overlay.AddText(x + 10, y + 76, "I PushInner  |  O PopInner  |  P ReloadOuter", 13, hint);
            overlay.AddText(x + 10, y + 96, "Counters come from trigger/decorator pipeline.", 13, hint);
        }
    }
}
