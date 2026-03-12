using System;

namespace Ludots.Core.Presentation.Components
{
    [Flags]
    public enum AnimatorPackedStateFlags : ushort
    {
        None = 0,
        Active = 1 << 0,
        Looping = 1 << 1,
        InTransition = 1 << 2,
        PendingTrigger = 1 << 3,
    }
}
