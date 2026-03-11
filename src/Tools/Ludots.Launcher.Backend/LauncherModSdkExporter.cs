using System.Diagnostics;

namespace Ludots.Launcher.Backend;

internal static class LauncherModSdkExporter
{
    private static readonly (string RelativeProjectPath, string OutputDllName)[] ProjectSpecs =
    {
        ("src/Core/Ludots.Core.csproj", "Ludots.Core.dll"),
        ("src/Platform/Ludots.Platform.Abstractions/Ludots.Platform.Abstractions.csproj", "Ludots.Platform.Abstractions.dll"),
        ("src/Libraries/Ludots.UI/Ludots.UI.csproj", "Ludots.UI.dll"),
        ("src/Libraries/Ludots.UI.HtmlEngine/Ludots.UI.HtmlEngine.csproj", "Ludots.UI.HtmlEngine.dll"),
        ("src/Libraries/Arch/src/Arch/Arch.csproj", "Arch.dll"),
        ("src/Libraries/Arch.Extended/Arch.System/Arch.System.csproj", "Arch.System.dll")
    };

    public static async Task<string> ExportAsync(string repoRoot, CancellationToken ct)
    {
        var sdkRoot = Path.Combine(repoRoot, "assets", "ModSdk");
        var refDir = Path.Combine(sdkRoot, "ref");
        Directory.CreateDirectory(refDir);

        foreach (var (relativeProjectPath, outputDllName) in ProjectSpecs)
        {
            var projectPath = Path.GetFullPath(Path.Combine(repoRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"SDK project not found: {projectPath}");
            }

            var build = await RunProcessAsync(
                "dotnet",
                $"build \"{projectPath}\" /p:ProduceReferenceAssembly=true -c Release",
                repoRoot,
                ct,
                timeoutMs: 300_000);

            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException(build.Output);
            }

            var projectDir = Path.GetDirectoryName(projectPath) ?? repoRoot;
            var referencePath = FindReferenceAssembly(projectDir, Path.GetFileNameWithoutExtension(outputDllName));
            var targetPath = Path.Combine(refDir, outputDllName);
            File.Copy(referencePath, targetPath, overwrite: true);
        }

        return refDir;
    }

    private static string FindReferenceAssembly(string projectDirectory, string assemblyName)
    {
        var objectDirectory = Path.Combine(projectDirectory, "obj");
        if (!Directory.Exists(objectDirectory))
        {
            throw new DirectoryNotFoundException($"obj directory not found: {objectDirectory}");
        }

        var candidates = Directory.EnumerateFiles(objectDirectory, $"{assemblyName}.dll", SearchOption.AllDirectories)
            .Where(candidate =>
            {
                var normalized = candidate.Replace('\\', '/');
                return normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase)
                    && !normalized.Contains("/refint/", StringComparison.OrdinalIgnoreCase)
                    && normalized.Contains("/release/", StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new FileNotFoundException($"Reference assembly not found for {assemblyName} under {objectDirectory}.");
        }

        return candidates[0];
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        CancellationToken ct,
        int timeoutMs)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var output = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text)));
        return (process.ExitCode, output);
    }
}
