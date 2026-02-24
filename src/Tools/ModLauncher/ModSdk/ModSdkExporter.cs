using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Ludots.ModLauncher.ModSdk
{
    public static class ModSdkExporter
    {
        public static void Export(string rootDir, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(rootDir)) throw new ArgumentException("rootDir is required.", nameof(rootDir));
            rootDir = Path.GetFullPath(rootDir);

            var sdkDir = Path.Combine(rootDir, "assets", "ModSdk");
            var refDir = Path.Combine(sdkDir, "ref");
            Directory.CreateDirectory(refDir);

            var projects = new[]
            {
                ProjectSpec("src/Core/Ludots.Core.csproj", "Ludots.Core.dll"),
                ProjectSpec("src/Platform/Ludots.Platform.Abstractions/Ludots.Platform.Abstractions.csproj", "Ludots.Platform.Abstractions.dll"),
                ProjectSpec("src/Libraries/Ludots.UI/Ludots.UI.csproj", "Ludots.UI.dll"),
                ProjectSpec("src/Libraries/Ludots.UI.HtmlEngine/Ludots.UI.HtmlEngine.csproj", "Ludots.UI.HtmlEngine.dll"),
                ProjectSpec("src/Libraries/Arch/src/Arch/Arch.csproj", "Arch.dll"),
                ProjectSpec("src/Libraries/Arch.Extended/Arch.System/Arch.System.csproj", "Arch.System.dll")
            };

            for (int i = 0; i < projects.Length; i++)
            {
                var p = projects[i];
                string csproj = Path.GetFullPath(Path.Combine(rootDir, p.RelativeCsprojPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(csproj))
                {
                    throw new FileNotFoundException($"SDK project not found: {csproj}");
                }

                log($"Building SDK project (Release): {csproj}");
                int exitCode = RunDotnetBuild(csproj, rootDir, "/p:ProduceReferenceAssembly=true -c Release");
                if (exitCode != 0) throw new InvalidOperationException($"dotnet build failed for {csproj} (exit={exitCode}).");

                string projectDir = Path.GetDirectoryName(csproj) ?? rootDir;
                string referenceAssembly = FindReferenceAssemblyStrict(projectDir, Path.GetFileNameWithoutExtension(p.OutputDllName));
                var target = Path.Combine(refDir, p.OutputDllName);
                File.Copy(referenceAssembly, target, overwrite: true);
                log($"Exported SDK ref: {target}");
            }
        }

        private static (string RelativeCsprojPath, string OutputDllName) ProjectSpec(string relativeCsprojPath, string outputDllName)
        {
            return (relativeCsprojPath, outputDllName);
        }

        private static int RunDotnetBuild(string csprojPath, string workingDirectory, string additionalArgs)
        {
            var startInfo = new ProcessStartInfo("dotnet", $"build \"{csprojPath}\" {additionalArgs}".Trim())
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet process.");
            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }

        private static string FindReferenceAssemblyStrict(string projectDir, string assemblyName)
        {
            var objDir = Path.Combine(projectDir, "obj");
            if (!Directory.Exists(objDir))
            {
                throw new DirectoryNotFoundException($"obj directory not found: {objDir}");
            }

            var candidates = Directory.EnumerateFiles(objDir, $"{assemblyName}.dll", SearchOption.AllDirectories)
                .Where(p =>
                {
                    var normalized = p.Replace('\\', '/');
                    if (!normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase)) return false;
                    if (normalized.Contains("/refint/", StringComparison.OrdinalIgnoreCase)) return false;
                    if (!normalized.Contains("/release/", StringComparison.OrdinalIgnoreCase)) return false;
                    return true;
                })
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();

            if (candidates.Count == 0)
            {
                throw new FileNotFoundException($"Reference assembly not found for {assemblyName} under {objDir}.");
            }

            return candidates[0];
        }
    }
}
