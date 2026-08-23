using Ludots.Adapter.Raylib;

var baseDir = AppDomain.CurrentDomain.BaseDirectory;
string? diagnosticPath = Environment.GetEnvironmentVariable("LUDOTS_RAYLIB_DIAGNOSTIC_PATH");
try
{
    var configFile = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0] : "launcher.runtime.json";
    AppendStartupDiagnostic(diagnosticPath, $"program-start baseDir={baseDir} config={configFile}");
    using var host = new RaylibGameHost(baseDir, configFile);
    AppendStartupDiagnostic(diagnosticPath, "host-created");
    host.Run();
    AppendStartupDiagnostic(diagnosticPath, "host-run-returned");
}
catch (Exception ex)
{
    AppendStartupDiagnostic(diagnosticPath, $"program-exception {ex}");
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

static void AppendStartupDiagnostic(string? diagnosticPath, string message)
{
    if (string.IsNullOrWhiteSpace(diagnosticPath))
    {
        return;
    }

    string fullPath = Path.GetFullPath(diagnosticPath);
    string? directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.AppendAllText(fullPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
}
