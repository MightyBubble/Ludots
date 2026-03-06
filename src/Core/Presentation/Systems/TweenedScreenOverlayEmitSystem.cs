using Arch.Core;
using Arch.System;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.Presentation.Systems
{
    /// <summary>
    /// Emits persistent tween-driven overlay state into the per-frame ScreenOverlayBuffer.
    /// </summary>
    public sealed class TweenedScreenOverlayEmitSystem : BaseSystem<World, float>
    {
        private readonly TweenedScreenOverlayRegistry _items;
        private readonly ScreenOverlayBuffer _buffer;

        public TweenedScreenOverlayEmitSystem(World world, TweenedScreenOverlayRegistry items, ScreenOverlayBuffer buffer)
            : base(world)
        {
            _items = items;
            _buffer = buffer;
        }

        public override void Update(in float dt)
        {
            _buffer.Clear();
            _items.EmitTo(_buffer);
        }
    }
}
