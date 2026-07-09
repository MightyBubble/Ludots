using System;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Core.Gameplay.Camera
{
    internal sealed class CameraBehaviorContext
    {
        public CameraBehaviorInputState BehaviorInput { get; }
        public IViewController Viewport { get; }

        public CameraBehaviorContext(CameraBehaviorInputState behaviorInput, IViewController viewport)
        {
            BehaviorInput = behaviorInput ?? throw new ArgumentNullException(nameof(behaviorInput));
            Viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        }
    }
}
