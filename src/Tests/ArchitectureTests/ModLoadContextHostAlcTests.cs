using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using NUnit.Framework;

namespace ArchitectureTests
{
    [TestFixture]
    public sealed class ModLoadContextHostAlcTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void LoadMods_WhenHostRunsInNonDefaultAlc_ResolvesHostSharedAssembliesFromHostAlc(
            bool declareCoreAsProcessShared)
        {
            string tempRoot = CreateTempDir();
            HostAssemblyLoadContext? hostContext = null;
            try
            {
                string repoRoot = FindRepoRoot();
                string coreProjectPath = Path.Combine(repoRoot, "src", "Core", "Ludots.Core.csproj");
                string markerPath = Path.Combine(tempRoot, "mod-onload.marker");
                string modName = "HostSharedConsumer" + Guid.NewGuid().ToString("N");
                string modDir = CreateHostSharedConsumerMod(
                    tempRoot,
                    modName,
                    coreProjectPath,
                    markerPath,
                    declareCoreAsProcessShared);
                string hostRunnerPath = CreateHostRunner(tempRoot, coreProjectPath);

                BuildProject(hostRunnerPath);
                BuildProject(Path.Combine(modDir, modName + ".csproj"));

                string hostRunnerDllPath = Path.Combine(
                    Path.GetDirectoryName(hostRunnerPath)!,
                    "bin",
                    "Debug",
                    "net8.0",
                    "HostAlcRunner.dll");

                hostContext = new HostAssemblyLoadContext(hostRunnerDllPath);
                Assembly hostRunnerAssembly = hostContext.LoadFromAssemblyPath(hostRunnerDllPath);
                Type entrypoint = hostRunnerAssembly.GetType("HostAlcRunner.HostEntrypoint", throwOnError: true)!;
                MethodInfo loadMod = entrypoint.GetMethod("LoadMod", BindingFlags.Public | BindingFlags.Static)!;

                object result = loadMod.Invoke(null, new object[] { modDir, modName, markerPath })!;

                Assert.That(GetResult<bool>(result, "HostCoreUsesDefaultContext"), Is.False);
                Assert.That(GetResult<bool>(result, "ModEntryAssignableToHostImod"), Is.True);
                Assert.That(GetResult<bool>(result, "MarkerWritten"), Is.True);
                Assert.That(File.Exists(markerPath), Is.True);
            }
            finally
            {
                hostContext?.Unload();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                TryDelete(tempRoot);
            }
        }

        private static string CreateHostSharedConsumerMod(
            string tempRoot,
            string modName,
            string coreProjectPath,
            string markerPath,
            bool declareCoreAsProcessShared)
        {
            string modDir = Path.Combine(tempRoot, modName);
            Directory.CreateDirectory(modDir);

            string projectXml = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <BaseOutputPath>bin\</BaseOutputPath>
                <OutputPath>bin\</OutputPath>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="{{coreProjectPath}}">
                  <Private>false</Private>
                </ProjectReference>
              </ItemGroup>
            </Project>
            """;

            string source = $$"""
            using Ludots.Core.Modding;

            namespace {{modName}};

            public sealed class {{modName}}Entry : IMod
            {
                public void OnLoad(IModContext context)
                {
                    File.WriteAllText({{CSharpStringLiteral(markerPath)}}, "loaded");
                }

                public void OnUnload()
                {
                }
            }
            """;

            string processSharedJson = declareCoreAsProcessShared
                ? """
              ,
              "processSharedAssemblies": [
                "Ludots.Core"
              ]
            """
                : string.Empty;

            string manifestJson = $$"""
            {
              "name": "{{modName}}",
              "version": "1.0.0",
              "description": "host ALC shared assembly regression mod",
              "main": "bin/net8.0/{{modName}}.dll",
              "priority": 0,
              "dependencies": {}{{processSharedJson}}
            }
            """;

            File.WriteAllText(Path.Combine(modDir, modName + ".csproj"), projectXml);
            File.WriteAllText(Path.Combine(modDir, "ModEntry.cs"), source);
            File.WriteAllText(Path.Combine(modDir, "mod.json"), manifestJson);
            return modDir;
        }

        private static string CreateHostRunner(string tempRoot, string coreProjectPath)
        {
            string hostDir = Path.Combine(tempRoot, "HostAlcRunner");
            Directory.CreateDirectory(hostDir);
            string projectPath = Path.Combine(hostDir, "HostAlcRunner.csproj");

            string projectXml = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="{{coreProjectPath}}" />
              </ItemGroup>
            </Project>
            """;

            string source = """
            using System;
            using System.IO;
            using System.Linq;
            using System.Runtime.Loader;
            using Ludots.Core.Modding;
            using Ludots.Core.Scripting;

            namespace HostAlcRunner;

            public sealed record HostLoadResult(
                bool MarkerWritten,
                bool ModEntryAssignableToHostImod,
                bool HostCoreUsesDefaultContext);

            public static class HostEntrypoint
            {
                public static HostLoadResult LoadMod(string modDirectory, string modName, string markerPath)
                {
                    var loader = new ModLoader(
                        new VirtualFileSystem(),
                        new FunctionRegistry(),
                        new TriggerManager());

                    try
                    {
                        loader.LoadMods(new[] { modDirectory });
                        var modAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, modName, StringComparison.Ordinal));
                        var modEntry = modAssembly?.GetTypes()
                            .FirstOrDefault(candidate => candidate.Name == modName + "Entry");

                        return new HostLoadResult(
                            File.Exists(markerPath),
                            modEntry != null && typeof(IMod).IsAssignableFrom(modEntry),
                            AssemblyLoadContext.GetLoadContext(typeof(IMod).Assembly) == AssemblyLoadContext.Default);
                    }
                    finally
                    {
                        loader.UnloadAll();
                    }
                }
            }
            """;

            File.WriteAllText(projectPath, projectXml);
            File.WriteAllText(Path.Combine(hostDir, "HostEntrypoint.cs"), source);
            return projectPath;
        }

        private static T GetResult<T>(object result, string propertyName)
        {
            PropertyInfo property = result.GetType().GetProperty(propertyName)
                ?? throw new InvalidOperationException($"Result property '{propertyName}' was not found.");
            return (T)property.GetValue(result)!;
        }

        private static void BuildProject(string projectPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" -c Debug --nologo -p:BuildInParallel=false -m:1 -nodeReuse:false",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start dotnet build process.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                throw new TimeoutException($"dotnet build timed out for '{projectPath}'.");
            }

            string output = outputTask.GetAwaiter().GetResult();
            string error = errorTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"dotnet build failed for '{projectPath}'.\nSTDOUT:\n{output}\nSTDERR:\n{error}");
            }
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")) &&
                    File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
        }

        private static string CreateTempDir()
        {
            string path = Path.Combine(Path.GetTempPath(), "ludots_hostalc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CSharpStringLiteral(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }
        }

        private sealed class HostAssemblyLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;

            public HostAssemblyLoadContext(string mainAssemblyPath)
                : base("LudotsHostAlcRegression", isCollectible: true)
            {
                _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
                return assemblyPath == null ? null : LoadFromAssemblyPath(assemblyPath);
            }
        }
    }
}
