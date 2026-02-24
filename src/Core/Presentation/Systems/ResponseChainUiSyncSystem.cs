using System.Collections.Generic;
using System.Text;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Scripting;
using Ludots.Core.UI;
 
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
            if (!_ui.Dirty) return;
            if (!_globals.TryGetValue(ContextKeys.UISystem, out var uiObj) || uiObj is not IUiSystem uiSystem) return;
 
            if (!_ui.Visible)
            {
                uiSystem.SetHtml("", "");
                _ui.MarkClean();
                return;
            }
 
            var sb = new StringBuilder(512);
            sb.Append("<div style='position:absolute;left:12px;top:12px;background:rgba(0,0,0,0.75);color:#fff;padding:10px;border-radius:8px;font-family:monospace;font-size:14px;min-width:260px;'>");
            sb.Append("<div style='font-weight:bold;margin-bottom:6px;'>Chain</div>");
            sb.Append("<div>RootId: ").Append(_ui.RootId).Append("</div>");
            sb.Append("<div>PlayerId: ").Append(_ui.PlayerId).Append("</div>");
            sb.Append("<div>PromptTagId: ").Append(_ui.PromptTagId).Append("</div>");
            sb.Append("<div style='margin-top:8px;'>Allowed:</div><ul style='margin:6px 0 0 18px;padding:0;'>");
 
            for (int i = 0; i < _ui.AllowedCount; i++)
            {
                int tag = _ui.AllowedOrderTagIds[i];
                string label = tag.ToString();
                if (_orderTypeRegistry.TryGet(tag, out var config) && !string.IsNullOrEmpty(config.Label))
                {
                    label = config.Label;
                }
                sb.Append("<li>").Append(label).Append(" (").Append(tag).Append(")</li>");
            }
 
            sb.Append("</ul>");
            sb.Append("<div style='margin-top:8px;opacity:0.8;'>Space:Pass  N:Negate  1:Chain</div>");
            sb.Append("</div>");
 
            uiSystem.SetHtml(sb.ToString(), "");
            _ui.MarkClean();
        }
 
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
