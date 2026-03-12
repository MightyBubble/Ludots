using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Navigation2DPlaygroundMod.Runtime;

namespace Navigation2DPlaygroundMod.Systems
{
    internal sealed class Navigation2DPlaygroundSelectionOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 LabelColor = new(0.99f, 0.97f, 0.75f, 1f);

        private readonly GameEngine _engine;

        public Navigation2DPlaygroundSelectionOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!Navigation2DPlaygroundState.Enabled || !Navigation2DPlaygroundIds.IsPlaygroundMap(mapId))
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay ||
                _engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
            {
                return;
            }

            Span<Entity> selected = stackalloc Entity[SelectionBuffer.CAPACITY];
            int count = Navigation2DPlaygroundSelectionView.CopySelectedEntities(_engine.World, _engine.GlobalContext, selected);
            int maxLabels = Math.Min(count, 16);
            for (int i = 0; i < maxLabels; i++)
            {
                Entity entity = selected[i];
                if (!_engine.World.TryGet(entity, out WorldPositionCm position))
                {
                    continue;
                }

                Vector2 screen = projector.WorldToScreen(Ludots.Core.Mathematics.WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 1.2f));
                if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) || float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                {
                    continue;
                }

                string label = Navigation2DPlaygroundSelectionView.FormatEntityId(entity);
                int x = (int)MathF.Round(screen.X) - label.Length * 4;
                int y = (int)MathF.Round(screen.Y) - 12;
                overlay.AddText(x, y, label, 14, LabelColor);
            }
        }
    }
}
