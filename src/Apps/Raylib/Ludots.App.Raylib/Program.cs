using System;
using Ludots.Adapter.LiteNetLib;
using Ludots.Adapter.Raylib;
using Ludots.Core.Hosting;
using Ludots.Platform.Abstractions;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
try
{
    var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "launcher.runtime.json";
    string resolvedBootstrapPath = GameBootstrapper.ResolveBootstrapPath(baseDir, configFile);
    string hostArtifactBaseDirectory = Path.GetDirectoryName(resolvedBootstrapPath)
        ?? throw new InvalidOperationException(
            $"Resolved launcher bootstrap has no parent directory: {resolvedBootstrapPath}");
    var appHost = new RaylibAppHost(
        configFile,
        configure: result =>
        {
            if (result.NetworkHost != null)
            {
                LiteNetLibNetworkRuntimeInstaller.Install(in result, hostArtifactBaseDirectory);
            }
        });
    appHost.Initialize(new AppInitContext(baseDir, Array.Empty<string>(), AssetsRoot: null));
    appHost.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
