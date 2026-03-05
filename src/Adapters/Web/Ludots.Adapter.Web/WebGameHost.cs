using System.Threading;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Web
{
    public sealed class WebGameHost : IGameHost
    {
        private readonly string _baseDir;
        private readonly string? _gameConfigFile;
        private WebHostSetup? _setup;

        public WebGameHost(string baseDir, string? gameConfigFile = null)
        {
            _baseDir = baseDir;
            _gameConfigFile = gameConfigFile;
        }

        public WebHostSetup Setup => _setup ??= WebHostComposer.Compose(_baseDir, _gameConfigFile);

        public void Run()
        {
            WebHostLoop.Run(Setup, CancellationToken.None);
        }

        public void Run(CancellationToken ct)
        {
            WebHostLoop.Run(Setup, ct);
        }

        public void Dispose()
        {
        }
    }
}
