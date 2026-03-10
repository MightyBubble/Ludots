using System;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Core.Gameplay.Camera
{
    internal sealed class CameraBehaviorContext
    {
        public IInputActionReader Input { get; }
        public IViewController Viewport { get; }

        public CameraBehaviorContext(IInputActionReader input, IViewController viewport)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        }
    }
}
