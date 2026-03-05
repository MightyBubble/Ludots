using Arch.System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

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
        private IInputBackend? _input;
        private bool _prevPush;
        private bool _prevPop;
        private bool _prevReload;

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
                _engine.GlobalContext.TryGetValue(ContextKeys.InputBackend, out var inputObj) &&
                inputObj is IInputBackend backend)
            {
                _input = backend;
            }

            if (_input == null) return;

            HandlePressed("<Keyboard>/i", ref _prevPush, () =>
            {
                _engine.PushMap("audit_inner");
            });

            HandlePressed("<Keyboard>/o", ref _prevPop, () =>
            {
                if (_engine.MapSessions == null || _engine.MapSessions.All.Count <= 1)
                {
                    Log.Warn(in LogChannels.Engine, "[AuditPlaygroundMod] Pop skipped (no inner map).");
                    return;
                }
                _engine.PopMap();
            });

            HandlePressed("<Keyboard>/p", ref _prevReload, () =>
            {
                _engine.LoadMap("audit_outer");
            });
        }

        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        private void HandlePressed(string path, ref bool previous, System.Action onPressed)
        {
            bool down = _input!.GetButton(path);
            if (down && !previous)
            {
                onPressed();
            }

            previous = down;
        }
    }
}
