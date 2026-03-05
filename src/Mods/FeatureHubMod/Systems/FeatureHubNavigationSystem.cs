using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace FeatureHubMod.Systems
{
    public sealed class FeatureHubNavigationSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private IInputBackend? _input;
        private readonly bool[] _prevKeys = new bool[10];

        private static readonly (string key, string mapId, string label)[] Entries =
        {
            ("<Keyboard>/1", "entry",             "[1] MOBA Demo       — Skills, Selection, Movement, Camera"),
            ("<Keyboard>/2", "rts_entry",          "[2] RTS Demo        — Units, Buildings, Production, Aura"),
            ("<Keyboard>/3", "sc2_highlands",      "[3] RTS Showcase    — SC2/RA2/War3 Maps, HexGrid"),
            ("<Keyboard>/4", "arpg_entry",         "[4] ARPG Demo       — Projectile, Summon, DoT/HoT, Tags"),
            ("<Keyboard>/5", "tcg_modify",         "[5] TCG Demo        — Response Chain, Modify/Hook"),
            ("<Keyboard>/6", "fourx_entry",        "[6] 4X Demo         — Build, Colonize, Tag Gating"),
            ("<Keyboard>/7", "audit_outer",        "[7] Map Lifecycle   — Push/Pop/Reload (I/O/P keys)"),
            ("<Keyboard>/8", "nav2d_playground",   "[8] Navigation 2D   — FlowField, ORCA Avoidance"),
            ("<Keyboard>/9", "visual_benchmark",   "[9] Performance     — 100K Entities, Health Bars"),
            ("<Keyboard>/0", "feature_hub",        "[0] << Back to Hub"),
        };

        public FeatureHubNavigationSystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }

        public void Update(in float t)
        {
            ResolveInput();
            if (_input == null) return;

            if (!_engine.GlobalContext.TryGetValue(ContextKeys.ScreenOverlayBuffer, out var obj) ||
                obj is not ScreenOverlayBuffer overlay) return;

            RenderMenu(overlay);
            HandleNavigation();
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

        private void RenderMenu(ScreenOverlayBuffer overlay)
        {
            var bg = new Vector4(0f, 0f, 0.05f, 0.75f);
            var border = new Vector4(0.2f, 0.6f, 1f, 0.5f);
            var titleColor = new Vector4(1f, 0.85f, 0.2f, 1f);
            var itemColor = new Vector4(0.85f, 0.92f, 1f, 1f);
            var hintColor = new Vector4(0.6f, 0.7f, 0.8f, 0.8f);

            int lineHeight = 22;
            int padding = 14;
            int panelHeight = (Entries.Length + 5) * lineHeight + padding * 2;
            int panelWidth = 520;
            int startX = 16;
            int startY = 16;

            overlay.AddRect(startX, startY, panelWidth, panelHeight, bg, border);
            overlay.AddText(startX + padding, startY + padding, "LUDOTS FEATURE SHOWCASE", 18, titleColor);

            int y = startY + padding + lineHeight + 8;
            for (int i = 0; i < Entries.Length; i++)
            {
                overlay.AddText(startX + padding, y, Entries[i].label, 15, itemColor);
                y += lineHeight;
            }

            y += 8;
            overlay.AddText(startX + padding, y, "F5=Config  F6=Mods  F7=Attributes  F8=TurnBased", 13, hintColor);
            y += lineHeight;
            overlay.AddText(startX + padding, y, "Diagnostics overlays available in all scenes.", 13, hintColor);
        }

        private void HandleNavigation()
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                bool down = _input!.GetButton(Entries[i].key);
                if (down && !_prevKeys[i])
                {
                    try
                    {
                        _engine.LoadMap(Entries[i].mapId);
                    }
                    catch (Exception)
                    {
                        // Map may not be available if the corresponding mod isn't loaded
                    }
                }
                _prevKeys[i] = down;
            }
        }
    }
}
