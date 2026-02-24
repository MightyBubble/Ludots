using Ludots.Adapter.Raylib;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
try
{
    var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "game.json";
    using var host = new RaylibGameHost(baseDir, configFile);
    host.Run();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
