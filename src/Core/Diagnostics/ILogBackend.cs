using System;

namespace Ludots.Core.Diagnostics
{
    public interface ILogBackend : IDisposable
    {
        void Write(LogLevel level, in LogChannel channel, string message);
        void Flush();
    }
}
