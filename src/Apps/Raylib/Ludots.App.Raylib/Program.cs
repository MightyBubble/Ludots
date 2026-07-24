using Ludots.Adapter.LiteNetLib;
using Ludots.Adapter.Raylib;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
try
{
    var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "launcher.runtime.json";
    using var host = new RaylibGameHost(
        baseDir,
        configFile,
        result =>
        {
            if (result.NetworkHost != null)
            {
                LiteNetLibNetworkRuntimeInstaller.Install(in result, baseDir);
            }
        });
    host.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
