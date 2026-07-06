using System;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public sealed class LifecycleExecutionException : Exception
    {
        public LifecycleExecutionException(string message)
            : base(message)
        {
        }
    }
}
