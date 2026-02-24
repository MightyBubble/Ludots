using System;

namespace Ludots.Platform.Abstractions
{
    public interface IGameHost : IDisposable
    {
        void Run();
    }
}
