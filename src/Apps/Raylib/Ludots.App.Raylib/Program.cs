using Ludots.Adapter.Raylib;
using Ludots.Adapter.LiteNetLib;
using Ludots.Core.Hosting;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
try
{
    var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "launcher.runtime.json";
    string resolvedBootstrapPath = GameBootstrapper.ResolveBootstrapPath(baseDir, configFile);
    string hostArtifactBaseDirectory = Path.GetDirectoryName(resolvedBootstrapPath)
        ?? throw new InvalidOperationException(
            $"Resolved launcher bootstrap has no parent directory: {resolvedBootstrapPath}");
    using var host = new RaylibGameHost(
        baseDir,
        resolvedBootstrapPath,
        result =>
        {
            if (result.NetworkHost != null)
            {
                LiteNetLibNetworkRuntimeInstaller.Install(in result, hostArtifactBaseDirectory);
            }
        });
    host.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
