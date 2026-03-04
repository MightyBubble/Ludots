using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Adapter.Raylib.Debug
{
    internal sealed class RaylibGmCommandConsole
    {
        private readonly GameEngine _engine;
        private readonly IDictionary<string, object> _globals;
        private readonly RenderCameraDebugState _cameraDebug;
        private readonly Dictionary<string, CommandDef> _commands =
            new Dictionary<string, CommandDef>(StringComparer.OrdinalIgnoreCase);

        private string _input = string.Empty;
        private string _lastMessage = "F8 打开 GM 控制台";
        private bool _prevF8;
        private bool _prevBackspace;
        private bool _prevEnter;
        private bool _prevEscape;

        private readonly struct CommandDef
        {
            public readonly string Help;
            public readonly Func<string[], string> Handler;

            public CommandDef(string help, Func<string[], string> handler)
            {
                Help = help;
                Handler = handler;
            }
        }

        public bool IsOpen { get; private set; }

        public RaylibGmCommandConsole(GameEngine engine, IDictionary<string, object> globals, RenderCameraDebugState cameraDebug)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
            _cameraDebug = cameraDebug ?? throw new ArgumentNullException(nameof(cameraDebug));
            RegisterBuiltInCommands();
        }

        public bool UpdateInput()
        {
            if (GetKeyPressed(KeyboardKey.KEY_F8, ref _prevF8))
            {
                IsOpen = !IsOpen;
                _lastMessage = IsOpen
                    ? "GM 已开启，输入 'gm help' 查看命令"
                    : "GM 已关闭";
            }

            if (!IsOpen) return false;

            int codepoint = Rl.GetCharPressed();
            while (codepoint > 0)
            {
                if (codepoint >= 32 && codepoint <= 126)
                {
                    if (_input.Length < 160)
                        _input += (char)codepoint;
                }

                codepoint = Rl.GetCharPressed();
            }

            if (GetKeyPressed(KeyboardKey.KEY_BACKSPACE, ref _prevBackspace) && _input.Length > 0)
            {
                _input = _input.Substring(0, _input.Length - 1);
            }

            if (GetKeyPressed(KeyboardKey.KEY_ENTER, ref _prevEnter))
            {
                Execute(_input);
                _input = string.Empty;
            }

            if (GetKeyPressed(KeyboardKey.KEY_ESCAPE, ref _prevEscape))
            {
                IsOpen = false;
                _lastMessage = "GM 已关闭";
            }

            return true;
        }

        public void DrawOverlay(int screenWidth, int screenHeight)
        {
            if (!IsOpen) return;

            int boxX = 12;
            int boxW = Math.Max(420, screenWidth - 24);
            int boxH = 78;
            int boxY = screenHeight - boxH - 12;

            Rl.DrawRectangle(boxX, boxY, boxW, boxH, new Color(0, 0, 0, 190));
            Rl.DrawRectangleLines(boxX, boxY, boxW, boxH, new Color(80, 190, 255, 220));

            Rl.DrawText("GM Console (F8 toggle)", boxX + 10, boxY + 8, 18, new Color(120, 220, 255, 255));
            Rl.DrawText("GM> " + _input, boxX + 10, boxY + 32, 20, Color.WHITE);
            Rl.DrawText(_lastMessage, boxX + 10, boxY + 56, 16, new Color(190, 220, 190, 255));
        }

        private void Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return;

            int start = 0;
            if (tokens[0].Equals("gm", StringComparison.OrdinalIgnoreCase))
            {
                start = 1;
            }

            if (start >= tokens.Length)
            {
                _lastMessage = "命令为空。示例: gm help";
                return;
            }

            string commandName = tokens[start];
            int argCount = tokens.Length - (start + 1);
            string[] args = new string[argCount];
            for (int i = 0; i < argCount; i++)
                args[i] = tokens[start + 1 + i];

            if (!_commands.TryGetValue(commandName, out var command))
            {
                _lastMessage = $"未知命令: {commandName}";
                return;
            }

            try
            {
                _lastMessage = command.Handler(args);
            }
            catch (Exception ex)
            {
                _lastMessage = $"执行失败: {ex.Message}";
            }
        }

        private void RegisterBuiltInCommands()
        {
            Register("help", "gm help", _ =>
                "可用: cam.detach cam.pull cam.offset cam.target_offset cam.reset cull.debug cull.state accept.case accept.focus accept.scale accept.probe accept.inspect");

            Register("cam.detach", "gm cam.detach on|off", args =>
            {
                if (args.Length != 1 || !TryParseBool(args[0], out bool enabled))
                    return "用法: gm cam.detach on|off";

                _cameraDebug.Enabled = enabled;
                return $"渲染相机调试: {(enabled ? "ON" : "OFF")}";
            });

            Register("cam.pull", "gm cam.pull <meters>", args =>
            {
                if (args.Length != 1 || !TryParseFloat(args[0], out float meters))
                    return "用法: gm cam.pull <meters>";

                _cameraDebug.Enabled = true;
                _cameraDebug.PullBackMeters = Math.Max(0f, meters);
                return $"渲染相机后拉: {_cameraDebug.PullBackMeters:F2}m";
            });

            Register("cam.offset", "gm cam.offset <x> <y> <z>", args =>
            {
                if (!TryParseVector3(args, out Vector3 vec))
                    return "用法: gm cam.offset <x> <y> <z>";

                _cameraDebug.Enabled = true;
                _cameraDebug.PositionOffsetMeters = vec;
                return $"渲染相机位置偏移: ({vec.X:F2}, {vec.Y:F2}, {vec.Z:F2})m";
            });

            Register("cam.target_offset", "gm cam.target_offset <x> <y> <z>", args =>
            {
                if (!TryParseVector3(args, out Vector3 vec))
                    return "用法: gm cam.target_offset <x> <y> <z>";

                _cameraDebug.Enabled = true;
                _cameraDebug.TargetOffsetMeters = vec;
                return $"渲染相机目标偏移: ({vec.X:F2}, {vec.Y:F2}, {vec.Z:F2})m";
            });

            Register("cam.reset", "gm cam.reset", _ =>
            {
                _cameraDebug.Reset();
                return "渲染相机调试参数已重置";
            });

            Register("cull.debug", "gm cull.debug on|off", args =>
            {
                if (args.Length != 1 || !TryParseBool(args[0], out bool enabled))
                    return "用法: gm cull.debug on|off";

                _cameraDebug.DrawLogicalCullingDebug = enabled;
                return $"逻辑裁剪可视化: {(enabled ? "ON" : "OFF")}";
            });

            Register("cull.state", "gm cull.state", _ =>
            {
                if (_globals.TryGetValue(ContextKeys.CameraCullingDebugState, out var obj) &&
                    obj is CameraCullingDebugState state)
                {
                    return $"Cull target=({state.LogicalTargetCm.X:F0},{state.LogicalTargetCm.Y:F0}) " +
                           $"vis(H/M/L)={state.VisibleHighCount}/{state.VisibleMediumCount}/{state.VisibleLowCount} " +
                           $"culled={state.CulledCount}";
                }

                return "Cull state 不可用";
            });

            Register("accept.case", "gm accept.case on|off", args =>
            {
                if (args.Length != 1 || !TryParseBool(args[0], out bool on))
                    return "用法: gm accept.case on|off";

                if (on)
                {
                    _cameraDebug.Enabled = true;
                    _cameraDebug.PullBackMeters = Math.Max(_cameraDebug.PullBackMeters, 20f);
                    _cameraDebug.DrawLogicalCullingDebug = true;
                    _cameraDebug.DrawAcceptanceProbes = true;
                    _cameraDebug.AcceptanceScaleMultiplier = Math.Max(_cameraDebug.AcceptanceScaleMultiplier, 2f);
                    return "验收用例模式 ON（逻辑裁剪+探针+放大比例）";
                }

                _cameraDebug.DrawAcceptanceProbes = false;
                _cameraDebug.DrawLogicalCullingDebug = false;
                _cameraDebug.AcceptanceScaleMultiplier = 1f;
                return "验收用例模式 OFF";
            });

            Register("accept.focus", "gm accept.focus <nameKeyword> [distanceCm]", args =>
            {
                if (args.Length < 1 || args.Length > 2)
                    return "用法: gm accept.focus <nameKeyword> [distanceCm]";

                float distanceCm = _engine.GameSession.Camera.State.DistanceCm;
                if (args.Length == 2)
                {
                    if (!TryParseFloat(args[1], out distanceCm))
                        return "distanceCm 需要是数字";
                }

                if (!TryFocusByName(args[0], distanceCm, out string result))
                    return result;

                return result;
            });

            Register("accept.scale", "gm accept.scale <factor>", args =>
            {
                if (args.Length != 1 || !TryParseFloat(args[0], out float factor))
                    return "用法: gm accept.scale <factor>";

                if (factor <= 0f) return "factor 必须 > 0";
                _cameraDebug.AcceptanceScaleMultiplier = factor;
                return $"验收比例缩放: x{factor:F2}";
            });

            Register("accept.probe", "gm accept.probe on|off", args =>
            {
                if (args.Length != 1 || !TryParseBool(args[0], out bool on))
                    return "用法: gm accept.probe on|off";

                _cameraDebug.DrawAcceptanceProbes = on;
                return $"验收探针: {(on ? "ON" : "OFF")}";
            });

            Register("accept.inspect", "gm accept.inspect", _ =>
            {
                int drawItems = TryGetInt(ContextKeys.PresentationPrimitiveDrawBuffer, b =>
                {
                    if (b is PrimitiveDrawBuffer pb) return pb.Count;
                    return -1;
                });
                int modelDraw = TryGetInt(ContextKeys.RenderModelDrawCalls);
                int modelCache = TryGetInt(ContextKeys.RenderModelCacheCount);
                int modelFail = TryGetInt(ContextKeys.RenderModelLoadFailures);
                int fallback = TryGetInt(ContextKeys.RenderModelFallbackDraws);
                int missingId = TryGetInt(ContextKeys.RenderMissingModelAssetId);
                return $"Inspect draw={drawItems} model(draw/cache/fail/fallback)={modelDraw}/{modelCache}/{modelFail}/{fallback} missingAsset={missingId}";
            });
        }

        private void Register(string name, string help, Func<string[], string> handler)
        {
            _commands[name] = new CommandDef(help, handler);
        }

        private static bool TryParseBool(string raw, out bool value)
        {
            if (raw.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("1", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (raw.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                raw.Equals("0", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryParseFloat(string raw, out float value)
        {
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseVector3(string[] args, out Vector3 vec)
        {
            if (args.Length != 3 ||
                !TryParseFloat(args[0], out float x) ||
                !TryParseFloat(args[1], out float y) ||
                !TryParseFloat(args[2], out float z))
            {
                vec = Vector3.Zero;
                return false;
            }

            vec = new Vector3(x, y, z);
            return true;
        }

        private static bool GetKeyPressed(KeyboardKey key, ref bool previous)
        {
            bool current = Rl.IsKeyDown(key);
            bool pressed = current && !previous;
            previous = current;
            return pressed;
        }

        private bool TryFocusByName(string keyword, float distanceCm, out string result)
        {
            var world = _engine.World;
            var queryDesc = new QueryDescription().WithAll<Name, WorldPositionCm>();
            var query = world.Query(in queryDesc);

            string key = keyword.Trim();
            foreach (var chunk in query)
            {
                var names = chunk.GetArray<Name>();
                var positions = chunk.GetArray<WorldPositionCm>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    string name = names[i].Value ?? string.Empty;
                    if (name.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var state = _engine.GameSession.Camera.State;
                    var p = positions[i].Value;
                    state.TargetCm = new Vector2(p.X.ToFloat(), p.Y.ToFloat());
                    state.DistanceCm = Math.Clamp(distanceCm, 2000f, 120000f);
                    state.Pitch = Math.Clamp(state.Pitch, 10f, 80f);
                    result = $"已聚焦 `{name}` (entity={chunk.Entity(i).Id}) 距离={state.DistanceCm:F0}cm";
                    return true;
                }
            }

            result = $"未找到名称包含 `{keyword}` 的实体";
            return false;
        }

        private int TryGetInt(string key, Func<object, int> convert = null)
        {
            if (!_globals.TryGetValue(key, out var obj) || obj == null) return -1;
            if (convert != null) return convert(obj);
            if (obj is int v) return v;
            return -1;
        }
    }
}
