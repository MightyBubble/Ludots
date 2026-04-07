using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using NUnit.Framework;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace GasTests
{
    [TestFixture]
    public class ModLoaderLifecycleTests
    {
        [Test]
        public void LoadMods_WhenCalledAgain_ReplacesLoadedModIdsAndUnmountsStale()
        {
            var tempRoot = CreateTempDir();
            try
            {
                var modA = CreateModDir(tempRoot, "ModA");
                var modB = CreateModDir(tempRoot, "ModB");

                var vfs = new VirtualFileSystem();
                var loader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());

                loader.LoadMods(new[] { modA, modB });
                Assert.That(loader.LoadedModIds.Count, Is.EqualTo(2));
                Assert.That(loader.LoadedModIds, Does.Contain("ModA"));
                Assert.That(loader.LoadedModIds, Does.Contain("ModB"));
                Assert.That(vfs.TryResolveFullPath("ModB:mod.json", out _), Is.True);

                loader.LoadMods(new[] { modA });

                Assert.That(loader.LoadedModIds.Count, Is.EqualTo(1));
                Assert.That(loader.LoadedModIds.Single(), Is.EqualTo("ModA"));
                Assert.That(vfs.TryResolveFullPath("ModB:mod.json", out _), Is.False);
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        [Test]
        public void LoadMods_CodeModsUseCollectibleStreamLoad_AndDoNotLockMainDllOutputs()
        {
            var tempRoot = CreateTempDir();
            try
            {
                LoadAndUnloadCodeModSet(tempRoot);
            }
            finally
            {
                TryDelete(tempRoot);
            }
        }

        private static string CreateModDir(string root, string modName)
        {
            var modDir = Path.Combine(root, modName);
            Directory.CreateDirectory(modDir);
            var json = $$"""
            {
              "name": "{{modName}}",
              "version": "1.0.0",
              "description": "test",
              "main": "bin/Release/net8.0/{{modName}}.dll",
              "priority": 0,
              "dependencies": {}
            }
            """;
            File.WriteAllText(Path.Combine(modDir, "mod.json"), json);
            return modDir;
        }

        private static string CreateTempDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "ludots_modloader_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void LoadAndUnloadCodeModSet(string tempRoot)
        {
            var modSet = CreateCodeModSet(tempRoot);
            var vfs = new VirtualFileSystem();
            var loader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());

            loader.LoadMods(new[] { modSet.ConsumerDirectory, modSet.ProviderDirectory });

            Assert.That(loader.LoadedModIds, Is.EqualTo(new[] { modSet.ProviderName, modSet.ConsumerName }));

            var providerAssembly = FindLoadedAssembly(modSet.ProviderName);
            var consumerAssembly = FindLoadedAssembly(modSet.ConsumerName);
            var providerLoadContext = AssemblyLoadContext.GetLoadContext(providerAssembly);
            var consumerLoadContext = AssemblyLoadContext.GetLoadContext(consumerAssembly);

            Assert.That(providerLoadContext, Is.Not.Null);
            Assert.That(consumerLoadContext, Is.SameAs(providerLoadContext));
            Assert.That(providerLoadContext!.IsCollectible, Is.True);
            Assert.That(providerAssembly.Location, Is.Empty);
            Assert.That(consumerAssembly.Location, Is.Empty);
            Assert.That(CanOpenForExclusiveWrite(modSet.ProviderDllPath), Is.True);
            Assert.That(CanOpenForExclusiveWrite(modSet.ConsumerDllPath), Is.True);

            loader.UnloadAll();
            Assert.That(loader.LoadedModIds, Is.Empty);
        }

        private static CodeModSet CreateCodeModSet(string tempRoot)
        {
            var repoRoot = FindRepoRoot();
            var providerName = "StreamLoadProvider" + Guid.NewGuid().ToString("N");
            var consumerName = "StreamLoadConsumer" + Guid.NewGuid().ToString("N");
            var coreProjectPath = Path.Combine(repoRoot, "src", "Core", "Ludots.Core.csproj");

            var providerDirectory = CreateCodeModProject(
                tempRoot,
                providerName,
                coreProjectPath,
                null,
                $$"""
                using Ludots.Core.Modding;

                namespace {{providerName}};

                public interface IProviderMarker
                {
                    string Ping();
                }

                public sealed class ProviderMarker : IProviderMarker
                {
                    public string Ping() => "pong";
                }

                public sealed class {{providerName}}Entry : IMod
                {
                    public void OnLoad(IModContext context)
                    {
                        context.Log("provider loaded");
                    }

                    public void OnUnload()
                    {
                    }
                }
                """,
                null);

            var providerProjectPath = Path.Combine(providerDirectory, providerName + ".csproj");
            var consumerDirectory = CreateCodeModProject(
                tempRoot,
                consumerName,
                coreProjectPath,
                providerProjectPath,
                $$"""
                using Ludots.Core.Modding;
                using {{providerName}};

                namespace {{consumerName}};

                public sealed class {{consumerName}}Entry : IMod
                {
                    public void OnLoad(IModContext context)
                    {
                        var marker = new ProviderMarker();
                        context.Log("consumer loaded " + marker.Ping());
                    }

                    public void OnUnload()
                    {
                    }
                }
                """,
                providerName);

            BuildProject(Path.Combine(consumerDirectory, consumerName + ".csproj"));

            return new CodeModSet(
                providerName,
                consumerName,
                providerDirectory,
                consumerDirectory,
                Path.Combine(providerDirectory, "bin", "net8.0", providerName + ".dll"),
                Path.Combine(consumerDirectory, "bin", "net8.0", consumerName + ".dll"));
        }

        private static string CreateCodeModProject(
            string tempRoot,
            string modName,
            string coreProjectPath,
            string? providerProjectPath,
            string source,
            string? dependencyModName)
        {
            var modDir = Path.Combine(tempRoot, modName);
            Directory.CreateDirectory(modDir);

            var projectXml = $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <BaseOutputPath>bin\</BaseOutputPath>
                <OutputPath>bin\</OutputPath>
                <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="{{coreProjectPath}}">
                  <Private>false</Private>
                </ProjectReference>
            """;

            if (!string.IsNullOrWhiteSpace(providerProjectPath))
            {
                projectXml += $$"""
            
                <ProjectReference Include="{{providerProjectPath}}">
                  <Private>false</Private>
                </ProjectReference>
                """;
            }

            projectXml += """
              </ItemGroup>
            </Project>
            """;

            var manifestJson = $$"""
            {
              "name": "{{modName}}",
              "version": "1.0.0",
              "description": "temp code mod",
              "main": "bin/net8.0/{{modName}}.dll",
              "priority": 0,
              "dependencies": {
            """;

            if (!string.IsNullOrWhiteSpace(dependencyModName))
            {
                manifestJson += $$"""
                "{{dependencyModName}}": ">=1.0.0 <2.0.0"
                """;
            }

            manifestJson += """
              }
            }
            """;

            File.WriteAllText(Path.Combine(modDir, modName + ".csproj"), projectXml);
            File.WriteAllText(Path.Combine(modDir, "ModEntry.cs"), source);
            File.WriteAllText(Path.Combine(modDir, "mod.json"), manifestJson);
            return modDir;
        }

        private static void BuildProject(string projectPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" -c Debug --nologo",
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

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

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

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            var matches = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal))
                .ToArray();

            Assert.That(matches, Has.Length.EqualTo(1), $"Expected a single loaded assembly for '{assemblyName}'.");
            return matches[0];
        }

        private static bool CanOpenForExclusiveWrite(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }

        private sealed record CodeModSet(
            string ProviderName,
            string ConsumerName,
            string ProviderDirectory,
            string ConsumerDirectory,
            string ProviderDllPath,
            string ConsumerDllPath);
    }
}
