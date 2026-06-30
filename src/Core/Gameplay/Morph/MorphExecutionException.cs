using System;

namespace Ludots.Core.Gameplay.Morph
{
    public sealed class MorphExecutionException : InvalidOperationException
    {
        public MorphExecutionException(string message)
            : base(message)
        {
        }
    }
}
