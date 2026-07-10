using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace CoreInputMod.Systems
{
    /// <summary>
    /// Shared presentation for screen-space command-source acquisition drag rectangles.
    /// </summary>
    public sealed class CommandSourceDragOverlaySystem : ISystem<float>
    {
        private static readonly Vector4 FillColor = new(0.18f, 0.55f, 0.95f, 0.12f);
        private static readonly Vector4 BorderColor = new(0.38f, 0.78f, 1f, 0.92f);

        private readonly World _world;
        private readonly Dictionary<string, object> _globals;
        private readonly CommandSourceAcquisitionSystem.CommandSourceOwnerProvider _commandSourceOwnerProvider;
        private readonly CommandSourceAcquisitionConfig _config;

        public CommandSourceDragOverlaySystem(
            World world,
            Dictionary<string, object> globals,
            CommandSourceAcquisitionSystem.CommandSourceOwnerProvider commandSourceOwnerProvider,
            CommandSourceAcquisitionConfig config)
        {
            _world = world;
            _globals = globals;
            _commandSourceOwnerProvider = commandSourceOwnerProvider;
            _config = config;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.ScreenOverlayBuffer.Name, out var overlayObj) || overlayObj is not ScreenOverlayBuffer overlay)
            {
                return;
            }

            if (!_commandSourceOwnerProvider(out Entity owner) || owner == Entity.Null || !_world.IsAlive(owner))
            {
                return;
            }

            if (!_world.Has<CommandSourceDragState>(owner))
            {
                return;
            }

            ref var drag = ref _world.Get<CommandSourceDragState>(owner);
            if (!drag.Active || !drag.ExceedsThreshold(_config.DragThresholdPixels))
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
