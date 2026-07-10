using System;

namespace Ludots.Core.Persistence
{
    public sealed class SaveContextException : InvalidOperationException
    {
        public SaveContextException(string message)
            : base(message)
        {
        }

        public SaveContextException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
