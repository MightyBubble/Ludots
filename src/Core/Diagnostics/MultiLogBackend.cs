using System;

namespace Ludots.Core.Diagnostics
{
    public sealed class MultiLogBackend : ILogBackend
    {
        private readonly ILogBackend[] _backends;

        public MultiLogBackend(params ILogBackend[] backends)
        {
            _backends = backends ?? Array.Empty<ILogBackend>();
        }

        public void Write(LogLevel level, in LogChannel channel, string message)
        {
            for (int i = 0; i < _backends.Length; i++)
                _backends[i].Write(level, in channel, message);
        }

        public void Flush()
        {
            for (int i = 0; i < _backends.Length; i++)
                _backends[i].Flush();
        }

        public void Dispose()
        {
            for (int i = 0; i < _backends.Length; i++)
                _backends[i].Dispose();
        }
    }
}
