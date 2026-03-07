using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using System.Numerics;
 
namespace Ludots.Core.Presentation.Systems
{
    public sealed class ResponseChainUiSyncSystem : ISystem<float>
    {
        private readonly Dictionary<string, object> _globals;
        private readonly ResponseChainUiState _ui;
        private readonly OrderTypeRegistry _orderTypeRegistry;
 
        public ResponseChainUiSyncSystem(Dictionary<string, object> globals, ResponseChainUiState ui, OrderTypeRegistry orderTypeRegistry)
        {
            _globals = globals;
            _ui = ui;
            _orderTypeRegistry = orderTypeRegistry;
        }
 
        public void Initialize() { }
 
        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            if (!_ui.Visible)
            {
                if (_ui.Dirty)
                {
                    _ui.MarkClean();
                }
                return;
            }

            Vector4 bg = new(0f, 0f, 0f, 0.78f);
            Vector4 border = new(0.35f, 0.55f, 1f, 0.85f);
            Vector4 title = new(1f, 0.92f, 0.35f, 1f);
            Vector4 text = new(1f, 1f, 1f, 0.95f);
            Vector4 hint = new(0.72f, 0.82f, 0.95f, 0.9f);

            int x = 12;
            int y = 12;
            int width = 340;
            int lineHeight = 18;
            int padding = 10;
            int panelHeight = 126 + (_ui.AllowedCount * lineHeight);

            overlay.AddRect(x, y, width, panelHeight, bg, border);
            overlay.AddText(x + padding, y + padding, "Response Chain", 16, title);
            overlay.AddText(x + padding, y + 32, $"RootId: {_ui.RootId}  Player: {_ui.PlayerId}", 14, text);
            overlay.AddText(x + padding, y + 50, $"PromptTagId: {_ui.PromptTagId}", 14, text);
            overlay.AddText(x + padding, y + 70, "Allowed Orders:", 14, text);

            int lineY = y + 88;
            for (int i = 0; i < _ui.AllowedCount; i++)
            {
                int orderTypeId = _ui.AllowedOrderTypeIds[i];
                string label = orderTypeId.ToString();
                if (_orderTypeRegistry.TryGet(orderTypeId, out var config) && !string.IsNullOrEmpty(config.Label))
                {
                    label = config.Label;
                }

                overlay.AddText(x + padding + 6, lineY, $"- {label} ({orderTypeId})", 13, text);
                lineY += lineHeight;
            }

            overlay.AddText(x + padding, lineY + 4, "Space=Pass  N=Negate  1=Chain", 13, hint);

            if (_ui.Dirty)
            {
                _ui.MarkClean();
            }
        }
 
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
