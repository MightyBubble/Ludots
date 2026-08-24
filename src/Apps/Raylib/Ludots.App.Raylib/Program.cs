using System;
using Ludots.Adapter.Raylib;
using Ludots.Platform.Abstractions;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
try
{
    var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "launcher.runtime.json";
    var appHost = new RaylibAppHost(configFile);
    appHost.Initialize(new AppInitContext(baseDir, Array.Empty<string>(), AssetsRoot: null));
    appHost.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
