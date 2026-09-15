using System;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.Raylib
{
    /// <summary>
    /// LUDOTS_HUD_TRACE=1 时的残留取证探针：稳态打印 worldHud/screenHud 计数与前几条文本锚点，
    /// 用于把"镜头离开后 HUD 文本残留"定位到 worldHud 保留（发射侧）或 screenHud 投影（消费侧）。
    /// </summary>
    internal static class OverlayTraceProbe
    {
        private static readonly bool Enabled =
            Environment.GetEnvironmentVariable("LUDOTS_HUD_TRACE") is "1" or "true" or "yes" or "on";

        private static int _frame;

        internal static void TraceHudAnchorState(GameEngine engine)
        {
            if (!Enabled || (_frame++ % 30) != 0)
            {
                return;
            }

            var worldHud = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            if (worldHud == null || screenHud == null)
            {
                return;
            }

            var anchorText = new System.Text.StringBuilder();
            int printed = 0;
            foreach (ref readonly WorldHudItem item in worldHud.GetSpan())
            {
                if (item.Kind != WorldHudItemKind.Text)
                {
                    continue;
                }

                anchorText.Append($" [id={item.StableId} owner={item.Owner.Id} w=({item.WorldPosition.X:F1},{item.WorldPosition.Y:F1},{item.WorldPosition.Z:F1})]");
                if (++printed >= 5)
                {
                    break;
                }
            }

            Log.Info(
                in LogChannels.Presentation,
                $"[hud-trace] f={_frame} worldHud={worldHud.Count} screenBar={screenHud.BarCount} screenText={screenHud.TextCount}{anchorText}");
        }
    }
}
