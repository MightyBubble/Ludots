using System;

namespace Ludots.Platform.Abstractions
{
    [Flags]
    public enum VisualRuntimeFlags : ushort
    {
        None = 0,
        Visible = 1 << 0,
        HasAnimator = 1 << 1,
    }
}
