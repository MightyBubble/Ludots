using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using DiagnosticsOverlayMod.Input;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map.Hex;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace DiagnosticsOverlayMod.Systems
{
    public sealed class DiagnosticsOverlaySystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private PlayerInputHandler? _input;

        private enum Panel { None, Config, Mods, Attributes }
        private Panel _activePanel = Panel.None;
        private bool _turnBasedMode;
        private TurnBasedPacemaker? _turnPacemaker;
        private IPacemaker? _savedRealtime;
        private int _turnCount;
        private static readonly QueryDescription WorldPositionQuery = new QueryDescription().WithAll<WorldPositionCm>();

        private string[]? _configLines;
        private string[]? _modLines;

        public DiagnosticsOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize()
        {
            BuildConfigInfo();
            BuildModInfo();
        }

        public void BeforeUpdate(in float t) { }

        public void Update(in float t)
        {
            ResolveInput();
            if (_input == null) return;
            var renderDebugState = ResolveRenderDebugState();

            HandleRenderDebugToggle(DiagnosticsOverlayInputActions.ToggleTerrain, () => renderDebugState.DrawTerrain = !renderDebugState.DrawTerrain);
            HandleRenderDebugToggle(DiagnosticsOverlayInputActions.TogglePrimitives, () => renderDebugState.DrawPrimitives = !renderDebugState.DrawPrimitives);
            HandleRenderDebugToggle(DiagnosticsOverlayInputActions.ToggleDebugDraw, () => renderDebugState.DrawDebugDraw = !renderDebugState.DrawDebugDraw);
            HandleRenderDebugToggle(DiagnosticsOverlayInputActions.ToggleSkiaUi, () => renderDebugState.DrawSkiaUi = !renderDebugState.DrawSkiaUi);

            HandleToggle(DiagnosticsOverlayInputActions.ToggleConfigPanel, Panel.Config);
            HandleToggle(DiagnosticsOverlayInputActions.ToggleModsPanel, Panel.Mods);
            HandleToggle(DiagnosticsOverlayInputActions.ToggleAttributesPanel, Panel.Attributes);
            HandleTurnBasedToggle();
            HandleTurnStep();

            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var obj) ||
                obj is not ScreenOverlayBuffer overlay) return;

            RenderRuntimeHud(overlay, renderDebugState, t);

            if (_activePanel == Panel.None && !_turnBasedMode) return;

            var bg = new Vector4(0f, 0f, 0f, 0.7f);
            var border = new Vector4(0.3f, 0.8f, 1f, 0.6f);
            var titleColor = new Vector4(0.3f, 0.9f, 1f, 1f);
            var textColor = new Vector4(0.9f, 0.95f, 1f, 1f);

            if (_turnBasedMode)
            {
                var tbColor = new Vector4(1f, 0.9f, 0.2f, 1f);
                overlay.AddText(20, 56, $"[TURN-BASED] Turn #{_turnCount}  |  Space=Step  F8=Switch to Realtime", 16, tbColor);
            }

            switch (_activePanel)
            {
                case Panel.Config:
                    RenderPanel(overlay, "CONFIG PIPELINE", _configLines ?? System.Array.Empty<string>(), bg, border, titleColor, textColor);
                    break;
                case Panel.Mods:
                    RenderPanel(overlay, "MODDING SYSTEM", _modLines ?? System.Array.Empty<string>(), bg, border, titleColor, textColor);
                    break;
                case Panel.Attributes:
                    RenderAttributePanel(overlay, bg, border, titleColor, textColor);
                    break;
            }
        }

        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        private void ResolveInput()
        {
            if (_input != null) return;
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InputHandler.Name, out var inputObj) &&
                inputObj is PlayerInputHandler input)
            {
                _input = input;
            }
        }

        private void HandleToggle(string actionId, Panel panel)
        {
            if (_input!.PressedThisFrame(actionId))
            {
                _activePanel = _activePanel == panel ? Panel.None : panel;
            }
        }

        private void HandleRenderDebugToggle(string actionId, Action toggle)
        {
            if (_input!.PressedThisFrame(actionId))
            {
                toggle();
            }
        }

        private RenderDebugState ResolveRenderDebugState()
        {
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.RenderDebugState.Name, out var debugObj) &&
                debugObj is RenderDebugState state)
            {
                return state;
            }

            throw new InvalidOperationException($"{CoreServiceKeys.RenderDebugState.Name} must be present in GlobalContext.");
        }

        private void BuildConfigInfo()
        {
            var lines = new List<string>();
            lines.Add("Config merge sources for game.json:");
            lines.Add("");

            try
            {
                var fragments = _engine.ConfigPipeline.CollectFragmentsWithSources("game.json");
                for (int i = 0; i < fragments.Count; i++)
                    lines.Add($"  {i + 1}. {fragments[i].SourceUri}");

                if (fragments.Count == 0)
                    lines.Add("  (no fragments found)");
            }
            catch
            {
                lines.Add("  (error reading fragments)");
            }

            lines.Add("");
            lines.Add($"StartupMapId: {_engine.MergedConfig?.StartupMapId ?? "(none)"}");
            lines.Add($"DefaultCoreMod: {_engine.MergedConfig?.DefaultCoreMod ?? "(none)"}");

            lines.Add("");
            lines.Add("Merge strategy: objects merge recursively, arrays/scalars override.");
            lines.Add("Catalog entries support: Replace, DeepObject, ArrayReplace, ArrayAppend, ArrayById.");

            _configLines = lines.ToArray();
        }

        private void BuildModInfo()
        {
            var lines = new List<string>();
            var ids = _engine.ModLoader.LoadedModIds;

            lines.Add($"Loaded Mods ({ids.Count}):");
            lines.Add("");
            for (int i = 0; i < ids.Count; i++)
                lines.Add($"  {i + 1}. {ids[i]}  [VFS: {ids[i]}:*]");

            lines.Add("");
            lines.Add("Dependency resolution: topological sort by mod.json dependencies.");
            lines.Add("Priority: lower number loads first (LudotsCoreMod = -1000).");
            lines.Add("");
            lines.Add("VFS path format: ModId:Path/To/Resource");
            lines.Add("  e.g. Core:Configs/game.json, MobaDemoMod:assets/GAS/effects.json");

            _modLines = lines.ToArray();
        }

        private void RenderAttributePanel(ScreenOverlayBuffer overlay, Vector4 bg, Vector4 border, Vector4 titleColor, Vector4 textColor)
        {
            var lines = new List<string>();
            lines.Add("Entity Attribute Inspector (first 8 entities with AttributeBuffer):");
            lines.Add("");

            int count = 0;
            var q = new QueryDescription().WithAll<AttributeBuffer>();
            _engine.World.Query(in q, (Entity e, ref AttributeBuffer buf) =>
            {
                if (count >= 8) return;

                string name = "???";
                if (_engine.World.Has<Name>(e))
                    name = _engine.World.Get<Name>(e).Value ?? "???";

                int healthId = AttributeRegistry.GetId("Health");
                float hp = buf.GetCurrent(healthId);
                float hpBase = buf.GetBase(healthId);

                lines.Add($"  [{e.Id}] {name}: HP={hp:F0}/{hpBase:F0}");

                int modCount = 0;
                for (int a = 0; a < AttributeBuffer.MAX_ATTRS; a++)
                {
                    float cur = buf.GetCurrent(a);
                    float bas = buf.GetBase(a);
                    if (a != healthId && (cur != 0f || bas != 0f))
                    {
                        string attrName = AttributeRegistry.GetName(a);
                        if (!string.IsNullOrEmpty(attrName))
                        {
                            lines.Add($"       {attrName}: {cur:F1} (base={bas:F1})");
                            modCount++;
                        }
                    }
                    if (modCount >= 4) break;
                }
                count++;
            });

            if (count == 0)
                lines.Add("  (no entities with AttributeBuffer found)");

            RenderPanel(overlay, "ATTRIBUTE INSPECTOR", lines.ToArray(), bg, border, titleColor, textColor);
        }

        private void HandleTurnBasedToggle()
        {
            if (_input!.PressedThisFrame(DiagnosticsOverlayInputActions.ToggleTurnBased))
            {
                _turnBasedMode = !_turnBasedMode;
                if (_turnBasedMode)
                {
                    _savedRealtime = _engine.Pacemaker;
                    _turnPacemaker = new TurnBasedPacemaker();
                    _engine.Pacemaker = _turnPacemaker;
                    _turnCount = 0;
                }
                else if (_savedRealtime != null)
                {
                    _engine.Pacemaker = _savedRealtime;
                    _turnPacemaker = null;
                }
            }
        }

        private void HandleTurnStep()
        {
            if (!_turnBasedMode || _turnPacemaker == null) return;
            if (_input!.PressedThisFrame(DiagnosticsOverlayInputActions.StepTurn))
            {
                _turnPacemaker.Step();
                _turnCount++;
            }
        }

        private static void RenderPanel(ScreenOverlayBuffer overlay, string title, string[] lines, Vector4 bg, Vector4 border, Vector4 titleColor, Vector4 textColor)
        {
            int lineHeight = 18;
            int padding = 12;
            int totalLines = lines.Length + 2;
            int panelHeight = totalLines * lineHeight + padding * 2;
            int panelWidth = 580;
            int startX = 20;
            int startY = 80;

            overlay.AddRect(startX, startY, panelWidth, panelHeight, bg, border);
            overlay.AddText(startX + padding, startY + padding, $"[{title}]", 16, titleColor);

            int y = startY + padding + lineHeight + 4;
            for (int i = 0; i < lines.Length; i++)
            {
                overlay.AddText(startX + padding, y, lines[i], 14, textColor);
                y += lineHeight;
            }
        }

        private void RenderRuntimeHud(ScreenOverlayBuffer overlay, RenderDebugState renderDebugState, float dt)
        {
            int primitives = 0;
            int primitivesDropped = 0;
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.PresentationPrimitiveDrawBuffer.Name, out var drawObj) &&
                drawObj is Ludots.Core.Presentation.Rendering.PrimitiveDrawBuffer draw)
            {
                primitives = draw.Count;
                primitivesDropped = draw.DroppedSinceClear;
            }

            int wpCount = 0;
            foreach (ref var chunk in _engine.World.Query(in WorldPositionQuery))
            {
                wpCount += chunk.Count;
            }

            var camera = _engine.GameSession.Camera.State;
            string vertexMapStatus = _engine.VertexMap == null
                ? "NULL"
                : $"{_engine.VertexMap.WidthInChunks}x{_engine.VertexMap.HeightInChunks}";

            int fps = dt > 0.0001f ? (int)MathF.Round(1f / dt) : 0;
            int x = 16;
            int y = 16;
            int w = 620;
            int h = 156;
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.ViewController.Name, out var viewObj) &&
                viewObj is IViewController view)
            {
                x = Math.Max(16, (int)view.Resolution.X - w - 16);
            }

            var bg = new Vector4(0f, 0f, 0f, 0.62f);
            var border = new Vector4(0.6f, 0.75f, 1f, 0.5f);
            var title = new Vector4(1f, 0.95f, 0.4f, 1f);
            var text = new Vector4(0.9f, 0.95f, 1f, 1f);

            overlay.AddRect(x, y, w, h, bg, border);
            overlay.AddText(x + 10, y + 8, $"Runtime HUD | FPS={fps}", 16, title);
            overlay.AddText(x + 10, y + 30, $"VertexMap: {vertexMapStatus} | Primitives: {primitives} (Dropped: {primitivesDropped}) | WorldPositionCm: {wpCount}", 14, text);
            overlay.AddText(x + 10, y + 50, $"Camera: Target=({camera.TargetCm.X:F0},{camera.TargetCm.Y:F0})cm Pitch={camera.Pitch:F1} Dist={camera.DistanceCm:F0}cm", 14, text);
            overlay.AddText(x + 10, y + 70, $"Scale: Grid=1.00m | HexWidth={HexCoordinates.HexWidth:F3}m | RowSpacing={HexCoordinates.RowSpacing:F3}m", 14, text);
            overlay.AddText(x + 10, y + 92, $"F9 Terrain[{OnOff(renderDebugState.DrawTerrain)}]  F10 Primitive[{OnOff(renderDebugState.DrawPrimitives)}]", 14, text);
            overlay.AddText(x + 10, y + 112, $"F11 DebugDraw[{OnOff(renderDebugState.DrawDebugDraw)}]  F12 SkiaUI[{OnOff(renderDebugState.DrawSkiaUi)}]", 14, text);
            overlay.AddText(x + 10, y + 132, "F5 Config | F6 Mods | F7 Attributes | F8 TurnBased", 13, new Vector4(0.7f, 0.8f, 0.9f, 0.95f));
        }

        private static string OnOff(bool enabled) => enabled ? "ON" : "OFF";
    }
}
