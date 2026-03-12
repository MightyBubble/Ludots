using System;
using System.Numerics;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceProjectionBoundsOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 BoundsFillColor = new(0.16f, 0.78f, 0.96f, 0.28f);
        private static readonly Vector4 BoundsBorderColor = new(0.16f, 0.88f, 1f, 0.92f);
        private static readonly Vector4 PanelFillColor = new(0.03f, 0.07f, 0.12f, 0.78f);
        private static readonly Vector4 PanelBorderColor = new(0.16f, 0.88f, 1f, 0.48f);
        private static readonly Vector4 TextColor = new(0.92f, 0.96f, 1f, 1f);

        private readonly GameEngine _engine;

        public CameraAcceptanceProjectionBoundsOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, CameraAcceptanceIds.ProjectionMapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            WorldAabbCm bounds = _engine.CurrentMapSession?.PrimaryBoard?.WorldSize.Bounds ?? _engine.WorldSizeSpec.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is GroundOverlayBuffer ground)
            {
                DrawBounds(ground, bounds);
            }

            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is ScreenOverlayBuffer overlay)
            {
                DrawOverlay(overlay, bounds);
            }
        }

        private static void DrawBounds(GroundOverlayBuffer overlay, in WorldAabbCm bounds)
        {
            const float lineY = 0.04f;
            const float lineWidth = 0.18f;

            float left = WorldUnits.CmToM(bounds.Left);
            float right = WorldUnits.CmToM(bounds.Right);
            float top = WorldUnits.CmToM(bounds.Top);
            float bottom = WorldUnits.CmToM(bounds.Bottom);
            float width = WorldUnits.CmToM(bounds.Width);
            float height = WorldUnits.CmToM(bounds.Height);

            overlay.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Line,
                Center = new Vector3(left, lineY, top),
                Length = width,
                Width = lineWidth,
                Rotation = 0f,
                FillColor = BoundsFillColor,
                BorderColor = BoundsBorderColor,
                BorderWidth = 0.08f
            });

            overlay.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Line,
                Center = new Vector3(left, lineY, bottom),
                Length = width,
                Width = lineWidth,
                Rotation = 0f,
                FillColor = BoundsFillColor,
                BorderColor = BoundsBorderColor,
                BorderWidth = 0.08f
            });

            overlay.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Line,
                Center = new Vector3(left, lineY, top),
                Length = height,
                Width = lineWidth,
                Rotation = MathF.PI / 2f,
                FillColor = BoundsFillColor,
                BorderColor = BoundsBorderColor,
                BorderWidth = 0.08f
            });

            overlay.TryAdd(new GroundOverlayItem
            {
                Shape = GroundOverlayShape.Line,
                Center = new Vector3(right, lineY, top),
                Length = height,
                Width = lineWidth,
                Rotation = MathF.PI / 2f,
                FillColor = BoundsFillColor,
                BorderColor = BoundsBorderColor,
                BorderWidth = 0.08f
            });
        }

        private void DrawOverlay(ScreenOverlayBuffer overlay, in WorldAabbCm bounds)
        {
            Vector2 resolution = _engine.GetService(CoreServiceKeys.ViewController) is IViewController view
                ? view.Resolution
                : new Vector2(1920f, 1080f);

            int width = 760;
            int x = Math.Max(16, (int)resolution.X - width - 16);
            int y = 16;
            int height = 78;

            string boundsLine = $"Projection bounds X[{bounds.Left},{bounds.Right}] Y[{bounds.Top},{bounds.Bottom}]";
            const string statusLine = "Blue frame is the active board boundary for the current map.";
            const string guidanceLine = "Ground clicks outside the frame are clamped by Core to the nearest board boundary.";

            overlay.AddRect(x, y, width, height, PanelFillColor, PanelBorderColor);
            overlay.AddText(x + 12, y + 10, boundsLine, 16, TextColor);
            overlay.AddText(x + 12, y + 32, statusLine, 14, TextColor);
            overlay.AddText(x + 12, y + 52, guidanceLine, 12, TextColor);
        }
    }
}
