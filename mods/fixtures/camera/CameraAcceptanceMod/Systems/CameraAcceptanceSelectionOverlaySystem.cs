using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CameraAcceptanceMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CameraAcceptanceMod.Systems
{
    internal sealed class CameraAcceptanceSelectionOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 LabelColor = new(0.98f, 0.96f, 0.68f, 1f);

        private readonly GameEngine _engine;

        public CameraAcceptanceSelectionOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!string.Equals(mapId, CameraAcceptanceIds.ProjectionMapId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
            {
                return;
            }

            Span<Entity> selected = stackalloc Entity[SelectionBuffer.CAPACITY];
            int count = CameraAcceptanceSelectionView.CopySelectedEntities(_engine.World, _engine.GlobalContext, selected);
            for (int i = 0; i < count; i++)
            {
                Entity entity = selected[i];
                if (!_engine.World.TryGet(entity, out VisualTransform transform))
                {
                    continue;
                }

                Vector2 screen = projector.WorldToScreen(transform.Position + new Vector3(0f, 1.35f, 0f));
                if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) || float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                {
                    continue;
                }

                string label = CameraAcceptanceSelectionView.FormatEntityId(entity);
                int x = (int)MathF.Round(screen.X) - label.Length * 4;
                int y = (int)MathF.Round(screen.Y) - 10;
                overlay.AddText(x, y, label, 14, LabelColor);
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
