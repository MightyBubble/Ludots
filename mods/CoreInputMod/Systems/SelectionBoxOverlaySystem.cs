using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Input.Selection;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// Shared presentation for screen-space selection drag rectangles.
    /// </summary>
    public sealed class SelectionBoxOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 FillColor = new(0.18f, 0.55f, 0.95f, 0.12f);
        private static readonly Vector4 BorderColor = new(0.38f, 0.78f, 1f, 0.92f);

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;

        public SelectionBoxOverlaySystem(World world, Dictionary<string, object> globals)
        {
            _world = world;
            _globals = globals;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) || localObj is not Entity local || !_world.IsAlive(local))
            {
                return;
            }

            if (!_world.Has<SelectionDragState>(local))
            {
                return;
            }

            ref var drag = ref _world.Get<SelectionDragState>(local);
            if (!drag.Active || !drag.ExceedsThreshold(EntityClickSelectSystem.DragThresholdPixels))
            {
                return;
            }

            var min = Vector2.Min(drag.StartScreen, drag.CurrentScreen);
            var max = Vector2.Max(drag.StartScreen, drag.CurrentScreen);

            int x = (int)min.X;
            int y = (int)min.Y;
            int width = (int)(max.X - min.X);
            int height = (int)(max.Y - min.Y);
            if (width <= 0 || height <= 0)
            {
                return;
            }

            overlay.AddRect(x, y, width, height, FillColor, BorderColor);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
