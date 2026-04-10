using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundSelectionOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 RingFill = new(0.96f, 0.76f, 0.24f, 0.14f);
        private static readonly Vector4 RingBorder = new(1f, 0.92f, 0.58f, 0.90f);
        private static readonly Vector4 LabelColor = new(1f, 0.96f, 0.78f, 1f);

        private readonly GameEngine _engine;
        private Entity[] _selectedScratch = Array.Empty<Entity>();

        public MassFlowNavPlaygroundSelectionOverlaySystem(GameEngine engine)
        {
            _engine = engine;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (_engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive ||
                !string.Equals(mapId, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase) ||
                _engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is not ScreenOverlayBuffer overlay ||
                _engine.GetService(CoreServiceKeys.GroundOverlayBuffer) is not GroundOverlayBuffer groundOverlay ||
                _engine.GetService(CoreServiceKeys.ScreenProjector) is not IScreenProjector projector)
            {
                return;
            }

            int selectedCount = SelectionContextRuntime.GetCurrentCount(_engine.World, _engine.GlobalContext);
            if (selectedCount <= 0)
            {
                return;
            }

            EnsureSelectedCapacity(selectedCount);
            int copied = SelectionContextRuntime.CopyCurrentSelection(_engine.World, _engine.GlobalContext, _selectedScratch);
            int maxRings = Math.Min(copied, 256);
            for (int i = 0; i < maxRings; i++)
            {
                Entity entity = _selectedScratch[i];
                if (!_engine.World.IsAlive(entity) || !_engine.World.TryGet(entity, out WorldPositionCm position))
                {
                    continue;
                }

                groundOverlay.TryAdd(new GroundOverlayItem
                {
                    Shape = GroundOverlayShape.Ring,
                    Center = WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0.04f),
                    Radius = 0.56f,
                    InnerRadius = 0.42f,
                    FillColor = RingFill,
                    BorderColor = RingBorder,
                    BorderWidth = 0.04f
                });
            }

            int maxLabels = Math.Min(copied, 20);
            for (int i = 0; i < maxLabels; i++)
            {
                Entity entity = _selectedScratch[i];
                if (!_engine.World.IsAlive(entity) || !_engine.World.TryGet(entity, out WorldPositionCm position))
                {
                    continue;
                }

                Vector2 screen = projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0.65f));
                if (float.IsNaN(screen.X) || float.IsNaN(screen.Y) || float.IsInfinity(screen.X) || float.IsInfinity(screen.Y))
                {
                    continue;
                }

                string label = $"#{entity.Id}";
                overlay.AddText((int)MathF.Round(screen.X) - label.Length * 4, (int)MathF.Round(screen.Y) - 12, label, 14, LabelColor);
            }
        }

        private void EnsureSelectedCapacity(int required)
        {
            if (required <= _selectedScratch.Length)
            {
                return;
            }

            int nextSize = _selectedScratch.Length == 0 ? 16 : _selectedScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _selectedScratch, nextSize);
        }
    }
}
