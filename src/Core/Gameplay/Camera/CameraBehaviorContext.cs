using System;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Core.Gameplay.Camera
{
    public sealed class CameraBehaviorContext
    {
        public PlayerInputHandler Input { get; }
        public IViewController Viewport { get; }

        public CameraBehaviorContext(PlayerInputHandler input, IViewController viewport)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        }
    }
}
