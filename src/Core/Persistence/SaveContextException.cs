using System;

namespace Ludots.Core.Persistence
{
    public sealed class SaveContextException : InvalidOperationException
    {
        public SaveContextException(string message)
            : base(message)
        {
        }
    }
}
