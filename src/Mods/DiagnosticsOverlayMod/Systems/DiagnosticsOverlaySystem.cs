using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace DiagnosticsOverlayMod.Systems
{
    public sealed class DiagnosticsOverlaySystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private IInputBackend? _input;

        private enum Panel { None, Config, Mods, Attributes }
        private Panel _activePanel = Panel.None;
        private bool _prevF5, _prevF6, _prevF7, _prevF8, _prevSpace;
        private bool _turnBasedMode;
        private TurnBasedPacemaker? _turnPacemaker;
        private IPacemaker? _savedRealtime;
        private int _turnCount;

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

            HandleToggle("<Keyboard>/f5", ref _prevF5, Panel.Config);
            HandleToggle("<Keyboard>/f6", ref _prevF6, Panel.Mods);
            HandleToggle("<Keyboard>/f7", ref _prevF7, Panel.Attributes);
            HandleTurnBasedToggle();
            HandleTurnStep();

            if (_activePanel == Panel.None && !_turnBasedMode) return;

            if (!_engine.GlobalContext.TryGetValue(ContextKeys.ScreenOverlayBuffer, out var obj) ||
                obj is not ScreenOverlayBuffer overlay) return;

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
            if (_engine.GlobalContext.TryGetValue(ContextKeys.InputBackend, out var inputObj) &&
                inputObj is IInputBackend backend)
                _input = backend;
        }

        private void HandleToggle(string path, ref bool prev, Panel panel)
        {
            bool down = _input!.GetButton(path);
            if (down && !prev)
                _activePanel = _activePanel == panel ? Panel.None : panel;
            prev = down;
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
            bool down = _input!.GetButton("<Keyboard>/f8");
            if (down && !_prevF8)
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
            _prevF8 = down;
        }

        private void HandleTurnStep()
        {
            if (!_turnBasedMode || _turnPacemaker == null) return;
            bool down = _input!.GetButton("<Keyboard>/space");
            if (down && !_prevSpace)
            {
                _turnPacemaker.Step();
                _turnCount++;
            }
            _prevSpace = down;
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
    }
}
