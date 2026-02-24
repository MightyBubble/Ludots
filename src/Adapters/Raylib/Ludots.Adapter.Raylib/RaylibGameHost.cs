using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Raylib
{
    public sealed class RaylibGameHost : IGameHost
    {
        private readonly string _baseDir;
        private readonly string? _gameConfigFile;

        public RaylibGameHost(string baseDir, string? gameConfigFile = null)
        {
            _baseDir = baseDir;
            _gameConfigFile = gameConfigFile;
        }

        public void Run()
        {
            var setup = RaylibHostComposer.Compose(_baseDir, _gameConfigFile);
            RaylibHostLoop.Run(setup);
        }

        public void Dispose()
        {
        }
    }
}
