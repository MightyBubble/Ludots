using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Projects WorldHudBatchBuffer to screen space and culls off-screen items.
    /// Outputs to ScreenHudBatchBuffer. Adapter draws without projection or culling.
    /// </summary>
    public sealed class WorldHudToScreenSystem : BaseSystem<World, float>
    {
        private readonly WorldHudBatchBuffer _worldHud;
        private readonly WorldHudStringTable? _strings;
        private readonly IScreenProjector _projector;
        private readonly IViewController _view;
        private readonly ScreenHudBatchBuffer _screenHud;
        private readonly PresentationTimingDiagnostics? _timingDiagnostics;

        private const int MaxBarDim = 512;
        private const int Margin = 200;

        public WorldHudToScreenSystem(
            World world,
            WorldHudBatchBuffer worldHud,
            WorldHudStringTable? strings,
            IScreenProjector projector,
            IViewController view,
            ScreenHudBatchBuffer screenHud,
            PresentationTimingDiagnostics? timingDiagnostics = null)
            : base(world)
        {
            _worldHud = worldHud ?? throw new System.ArgumentNullException(nameof(worldHud));
            _strings = strings;
            _projector = projector ?? throw new System.ArgumentNullException(nameof(projector));
            _view = view ?? throw new System.ArgumentNullException(nameof(view));
            _screenHud = screenHud ?? throw new System.ArgumentNullException(nameof(screenHud));
            _timingDiagnostics = timingDiagnostics;
        }

        public override void Update(in float dt)
        {
            long start = Stopwatch.GetTimestamp();
            _screenHud.Clear();

            var res = _view.Resolution;
            float screenWidth = res.X;
            float screenHeight = res.Y;

            var span = _worldHud.GetSpan();
            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                var screen = _projector.WorldToScreen(item.WorldPosition);
                if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) ||
                    float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                    continue;

                float x = MathF.Round(screen.X - item.Width * 0.5f);
                float y = MathF.Round(screen.Y);

                int ix = (int)x;
                int iy = (int)y;
                int iw = (int)item.Width;
                int ih = (int)item.Height;

                if (item.Kind == WorldHudItemKind.Bar)
                {
                    if (iw <= 0 || ih <= 0 || iw > MaxBarDim || ih > MaxBarDim) continue;
                    if (ix + iw < -Margin || iy + ih < -Margin ||
                        ix > screenWidth + Margin || iy > screenHeight + Margin)
                        continue;

                    _screenHud.TryAddBar(new ScreenHudBarItem
                    {
                        StableId = item.StableId,
                        DirtySerial = item.DirtySerial,
                        ScreenX = x,
                        ScreenY = y,
                        Color0 = item.Color0,
                        Color1 = item.Color1,
                        Width = item.Width,
                        Height = item.Height,
                        Value0 = item.Value0,
                    });
                    continue;
                }

                if (item.Kind == WorldHudItemKind.Text)
                {
                    int fontSize = item.FontSize <= 0 ? 16 : item.FontSize;
                    if (ix + fontSize < -Margin || iy + fontSize < -Margin ||
                        ix > screenWidth + Margin || iy > screenHeight + Margin)
                    {
                        continue;
                    }

                    _screenHud.TryAddText(new ScreenHudTextItem
                    {
                        StableId = item.StableId,
                        DirtySerial = item.DirtySerial,
                        ScreenX = x,
                        ScreenY = y,
                        Color0 = item.Color0,
                        Value0 = item.Value0,
                        Value1 = item.Value1,
                        Id0 = item.Id0,
                        Id1 = item.Id1,
                        FontSize = item.FontSize,
                        Text = item.Text,
                    });
                }
            }

            _timingDiagnostics?.ObserveWorldHudProjection((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }
    }
}
