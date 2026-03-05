using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Arch.System;
using GmConsoleMod.Input;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace GmConsoleMod.Systems
{
    public sealed class GmConsoleSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private PlayerInputHandler _input;
        private IInputBackend _backend;

        private bool _visible;
        private string _inputBuffer = string.Empty;
        private readonly List<string> _logLines = new List<string>(64);
        private const int MaxLogLines = 24;
        private const int PanelWidth = 620;
        private const int PanelX = 20;
        private const int PanelY = 200;

        public GmConsoleSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            ResolveServices();
            if (_input == null) return;

            if (_input.PressedThisFrame(GmConsoleInputActions.ToggleConsole))
            {
                _visible = !_visible;
                _inputBuffer = string.Empty;
            }

            if (!_visible) return;

            ProcessTextInput();
            Render();
        }

        private void ResolveServices()
        {
            if (_input != null) return;

            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var ih) && ih is PlayerInputHandler handler)
                _input = handler;

            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InputBackend.Name, out var ib) && ib is IInputBackend backend)
                _backend = backend;
        }

        private void ProcessTextInput()
        {
            if (_backend != null)
            {
                string chars = _backend.GetCharBuffer();
                if (!string.IsNullOrEmpty(chars))
                {
                    foreach (char c in chars)
                    {
                        if (c == '`' || c == '~') continue;
                        _inputBuffer += c;
                    }
                }
            }

            if (_input.PressedThisFrame(GmConsoleInputActions.Backspace))
            {
                if (_inputBuffer.Length > 0)
                    _inputBuffer = _inputBuffer.Substring(0, _inputBuffer.Length - 1);
            }

            if (_input.PressedThisFrame(GmConsoleInputActions.Submit))
            {
                if (!string.IsNullOrWhiteSpace(_inputBuffer))
                {
                    var cmd = _inputBuffer.Trim();
                    _logLines.Add($"> {cmd}");
                    ExecuteCommand(cmd);
                    _inputBuffer = string.Empty;

                    while (_logLines.Count > MaxLogLines)
                        _logLines.RemoveAt(0);
                }
            }
        }

        private void ExecuteCommand(string raw)
        {
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string cmd = parts[0].ToLowerInvariant();

            var camDebug = ResolveCameraDebug();
            var cullDebug = ResolveCullingDebug();
            var renderDebug = ResolveRenderDebugState();

            switch (cmd)
            {
                case "help":
                    Log("Commands: help, cam.detach, cam.pull <m>, cam.offset <x> <y> <z>,");
                    Log("  cam.reset, cull.debug, cull.state, accept.scale <f>, accept.probe");
                    break;

                case "cam.detach":
                    if (camDebug != null)
                    {
                        camDebug.Enabled = !camDebug.Enabled;
                        Log($"Camera debug: {(camDebug.Enabled ? "DETACHED" : "ATTACHED")}");
                    }
                    else Log("RenderCameraDebugState not found");
                    break;

                case "cam.pull":
                    if (camDebug != null && parts.Length >= 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pull))
                    {
                        camDebug.PullBackMeters = pull;
                        Log($"PullBack = {pull:F1}m");
                    }
                    else Log("Usage: cam.pull <meters>");
                    break;

                case "cam.offset":
                    if (camDebug != null && parts.Length >= 4 &&
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ox) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float oy) &&
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float oz))
                    {
                        camDebug.PositionOffsetMeters = new Vector3(ox, oy, oz);
                        Log($"Offset = ({ox:F1}, {oy:F1}, {oz:F1})");
                    }
                    else Log("Usage: cam.offset <x> <y> <z>");
                    break;

                case "cam.reset":
                    if (camDebug != null)
                    {
                        camDebug.Enabled = false;
                        camDebug.PullBackMeters = 0;
                        camDebug.PositionOffsetMeters = Vector3.Zero;
                        Log("Camera debug reset");
                    }
                    break;

                case "cull.debug":
                    if (camDebug != null)
                    {
                        camDebug.DrawLogicalCullingDebug = !camDebug.DrawLogicalCullingDebug;
                        Log($"Culling debug draw: {(camDebug.DrawLogicalCullingDebug ? "ON" : "OFF")}");
                    }
                    break;

                case "cull.state":
                    if (cullDebug != null)
                    {
                        Log($"AABB: ({cullDebug.MinX:F0},{cullDebug.MinY:F0})-({cullDebug.MaxX:F0},{cullDebug.MaxY:F0})");
                        Log($"LOD: High<{cullDebug.HighLodDist:F0} Med<{cullDebug.MediumLodDist:F0} Low<{cullDebug.LowLodDist:F0}");
                        Log($"Visible: {cullDebug.VisibleEntityCount}");
                    }
                    else Log("CameraCullingDebugState not found");
                    break;

                case "accept.scale":
                    if (renderDebug != null && parts.Length >= 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sc))
                    {
                        renderDebug.AcceptanceScaleMultiplier = sc;
                        Log($"AcceptanceScaleMultiplier = {sc:F2}");
                    }
                    else Log("Usage: accept.scale <multiplier>");
                    break;

                case "accept.probe":
                    if (camDebug != null)
                    {
                        camDebug.DrawAcceptanceProbes = !camDebug.DrawAcceptanceProbes;
                        Log($"Acceptance probes: {(camDebug.DrawAcceptanceProbes ? "ON" : "OFF")}");
                    }
                    break;

                default:
                    Log($"Unknown command: {cmd}");
                    break;
            }
        }

        private void Log(string message)
        {
            _logLines.Add(message);
        }

        private void Render()
        {
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var obj) ||
                obj is not ScreenOverlayBuffer overlay) return;

            int lineHeight = 16;
            int padding = 10;
            int visibleLines = Math.Min(_logLines.Count, MaxLogLines);
            int panelHeight = (visibleLines + 3) * lineHeight + padding * 2;

            var bg = new Vector4(0.05f, 0.05f, 0.12f, 0.85f);
            var border = new Vector4(0.4f, 1f, 0.4f, 0.7f);
            var titleColor = new Vector4(0.4f, 1f, 0.4f, 1f);
            var textColor = new Vector4(0.85f, 0.9f, 0.85f, 1f);
            var inputColor = new Vector4(1f, 1f, 0.6f, 1f);

            overlay.AddRect(PanelX, PanelY, PanelWidth, panelHeight, bg, border);
            overlay.AddText(PanelX + padding, PanelY + padding, "[GM CONSOLE] type 'help' for commands", 14, titleColor);

            int y = PanelY + padding + lineHeight + 4;
            int startIdx = _logLines.Count > MaxLogLines ? _logLines.Count - MaxLogLines : 0;
            for (int i = startIdx; i < _logLines.Count; i++)
            {
                overlay.AddText(PanelX + padding, y, _logLines[i], 13, textColor);
                y += lineHeight;
            }

            y += 4;
            overlay.AddText(PanelX + padding, y, $"> {_inputBuffer}_", 14, inputColor);
        }

        private RenderCameraDebugState ResolveCameraDebug()
        {
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.RenderCameraDebugState.Name, out var obj) &&
                obj is RenderCameraDebugState state)
                return state;
            return null;
        }

        private CameraCullingDebugState ResolveCullingDebug()
        {
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.CameraCullingDebugState.Name, out var obj) &&
                obj is CameraCullingDebugState state)
                return state;
            return null;
        }

        private RenderDebugState ResolveRenderDebugState()
        {
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out var obj) &&
                obj is RenderDebugState state)
                return state;
            return null;
        }
    }
}
