using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.WebGpu
{
    public sealed class WebGpuGameHost : IGameHost
    {
        private readonly string _baseDir;
        private readonly string? _gameConfigFile;

        public WebGpuGameHost(string baseDir, string? gameConfigFile = null)
        {
            _baseDir = baseDir;
            _gameConfigFile = gameConfigFile;
        }

        public void Run()
        {
            WebGpuHostSetup setup = WebGpuHostComposer.Compose(_baseDir, _gameConfigFile);
            WebGpuHostLoop.Run(setup);
        }

        public void Dispose()
        {
        }
    }
}
