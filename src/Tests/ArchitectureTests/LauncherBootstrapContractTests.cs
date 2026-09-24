using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ludots.Core.Hosting;
using Ludots.Launcher.Backend;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public class LauncherBootstrapContractTests
    {
        [Test]
        public void LauncherCli_ParsesExplicitWaitFlag()
        {
            CliCommand command = CliCommand.Parse(new[]
            {
                "launch",
                "example_binding",
                "--adapter",
                "raylib",
                "--build",
                "never",
                "--wait"
            });

            Assert.That(command.Primary, Is.EqualTo("launch"));
            Assert.That(command.Wait, Is.True);
            Assert.That(command.BuildMode, Is.EqualTo(LauncherBuildMode.Never));
            Assert.That(command.Operands, Is.EqualTo(new[] { "example_binding" }));
        }

        [Test]
        public void LauncherCli_PropagatesWaitedChildExitCode()
        {
            var result = new LauncherLaunchResult(
                false,
                "Platform process exited with code 7.",
                123,
                string.Empty,
                "launcher.runtime.json",
                null)
            {
                ExitCode = 7
            };

            Assert.That(LauncherCliExitCode.FromLaunchResult(result), Is.EqualTo(7));
        }

        [Test]
        public async Task WaitForExitAsync_WaitsForChildAndReturnsExactNonZeroExitCode()
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (OperatingSystem.IsWindows())
            {
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add("Start-Sleep -Milliseconds 150; exit 7");
            }
            else
            {
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("sleep 0.15; exit 7");
            }

            using var process = System.Diagnostics.Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start launcher wait test child process.");

            int? exitCode = await LauncherService.WaitForExitAsync(process, waitForExit: true);

            Assert.That(process.HasExited, Is.True);
            Assert.That(exitCode, Is.EqualTo(7));
        }

        [Test]
        public async Task PrepareAppForLaunchAsync_NeverValidatesExistingArtifactsWithoutWritingThem()
        {
            string repoRoot = FindRepoRoot();
            string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-never-app-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            string appAssemblyPath = Path.Combine(tempDirectory, "Test.App.dll");
            string depsPath = Path.ChangeExtension(appAssemblyPath, ".deps.json");
            string runtimeConfigPath = Path.ChangeExtension(appAssemblyPath, ".runtimeconfig.json");

            try
            {
                File.WriteAllText(appAssemblyPath, "assembly");
                File.WriteAllText(depsPath, "deps");
                File.WriteAllText(runtimeConfigPath, "runtimeconfig");
                DateTime assemblyWriteTime = File.GetLastWriteTimeUtc(appAssemblyPath);
                DateTime depsWriteTime = File.GetLastWriteTimeUtc(depsPath);
                DateTime runtimeConfigWriteTime = File.GetLastWriteTimeUtc(runtimeConfigPath);
                var launcher = new LauncherService(repoRoot);
                LauncherLaunchPlan plan = CreateAppArtifactTestPlan(tempDirectory, appAssemblyPath);

                LauncherBuildResult result = await launcher.PrepareAppForLaunchAsync(plan, LauncherBuildMode.Never);

                Assert.That(result.Ok, Is.True, result.Output);
                Assert.That(result.Output, Does.Contain("skipped by --build never"));
                Assert.That(File.GetLastWriteTimeUtc(appAssemblyPath), Is.EqualTo(assemblyWriteTime));
                Assert.That(File.GetLastWriteTimeUtc(depsPath), Is.EqualTo(depsWriteTime));
                Assert.That(File.GetLastWriteTimeUtc(runtimeConfigPath), Is.EqualTo(runtimeConfigWriteTime));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public async Task PrepareAppForLaunchAsync_NeverFailsWhenRuntimeArtifactIsMissing()
        {
            string repoRoot = FindRepoRoot();
            string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-never-missing-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            string appAssemblyPath = Path.Combine(tempDirectory, "Test.App.dll");
            File.WriteAllText(appAssemblyPath, "assembly");
            var launcher = new LauncherService(repoRoot);
            LauncherLaunchPlan plan = CreateAppArtifactTestPlan(tempDirectory, appAssemblyPath);

            try
            {
                LauncherBuildResult result = await launcher.PrepareAppForLaunchAsync(plan, LauncherBuildMode.Never);

                Assert.That(result.Ok, Is.False);
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Output, Does.Contain("required runtime artifact(s) are missing"));
                Assert.That(result.Output, Does.Contain(Path.ChangeExtension(appAssemblyPath, ".deps.json")));
                Assert.That(result.Output, Does.Contain(Path.ChangeExtension(appAssemblyPath, ".runtimeconfig.json")));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public async Task BuildAsync_NeverFailsForMissingModMainAssemblyWithoutBuilding()
        {
            string repoRoot = FindRepoRoot();
            string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-never-mod-{Guid.NewGuid():N}");
            string modRoot = Path.Combine(tempDirectory, "mods", "NeverBuildProbeMod");
            string buildMarkerPath = Path.Combine(modRoot, "build-invoked.marker");
            string mainAssemblyPath = Path.Combine(modRoot, "bin", "NeverBuildProbeMod.dll");

            try
            {
                Directory.CreateDirectory(modRoot);
                WriteBuildProbeProject(Path.Combine(modRoot, "NeverBuildProbeMod.csproj"));
                WriteTestModManifest(modRoot, "NeverBuildProbeMod", "bin/NeverBuildProbeMod.dll");
                LauncherService launcher = CreateIsolatedLauncher(tempDirectory);

                IReadOnlyList<LauncherBuildResult> results = await launcher.BuildAsync(
                    new[] { "mod:NeverBuildProbeMod" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never);

                LauncherBuildResult result = results.Single(item => item.Id == "NeverBuildProbeMod");
                Assert.That(result.Ok, Is.False);
                Assert.That(result.ExitCode, Is.Not.Zero);
                Assert.That(result.Output, Does.Contain("Mod build is disabled by --build never"));
                Assert.That(result.Output, Does.Contain(mainAssemblyPath));
                Assert.That(File.Exists(buildMarkerPath), Is.False, "--build never must not invoke the mod project.");
                Assert.That(Directory.Exists(Path.Combine(tempDirectory, "assets", "ModSdk")), Is.False,
                    "--build never must not export the Mod SDK before validating the existing main assembly.");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public async Task BuildAsync_NeverFailsForMissingBrowserRuntimePackageWithoutBuildingOrExportingSdk()
        {
            string repoRoot = FindRepoRoot();
            string tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-never-browser-{Guid.NewGuid():N}");
            string modRoot = Path.Combine(tempDirectory, "mods", "BrowserRuntimeProbeMod");
            string modAssemblyPath = Path.Combine(modRoot, "bin", "BrowserRuntimeProbeMod.dll");
            string providerRoot = Path.Combine(tempDirectory, "BrowserProvider");
            string providerBuildMarkerPath = Path.Combine(providerRoot, "build-invoked.marker");
            string providerAssemblyPath = Path.Combine(tempDirectory, "BrowserRuntime", "probe", "BrowserProvider.dll");

            try
            {
                Directory.CreateDirectory(modRoot);
                WriteBuildProbeProject(Path.Combine(modRoot, "BrowserRuntimeProbeMod.csproj"));
                WriteTestModManifest(modRoot, "BrowserRuntimeProbeMod", "bin/BrowserRuntimeProbeMod.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(modAssemblyPath)!);
                File.WriteAllText(modAssemblyPath, "prebuilt");

                Directory.CreateDirectory(providerRoot);
                WriteBuildProbeProject(Path.Combine(providerRoot, "BrowserProvider.csproj"));

                LauncherService launcher = CreateIsolatedLauncher(
                    tempDirectory,
                    browserRuntimeProvidersJson: """
                    [
                      {
                        "id": "probe",
                        "projectPath": "BrowserProvider/BrowserProvider.csproj",
                        "packageRootPath": "BrowserRuntime/probe",
                        "assemblyPath": "BrowserRuntime/probe/BrowserProvider.dll",
                        "hostTypeName": "BrowserProvider.Host"
                      }
                    ]
                    """,
                    presetsJson: """
                    {
                      "schemaVersion": 1,
                      "presets": [
                        {
                          "id": "browser-runtime-probe",
                          "name": "Browser runtime probe",
                          "selectors": [
                            "mod:BrowserRuntimeProbeMod"
                          ],
                          "adapterId": "raylib",
                          "buildMode": "never",
                          "browserRuntime": {
                            "enabled": true,
                            "required": true,
                            "provider": "probe"
                          }
                        }
                      ]
                    }
                    """);

                IReadOnlyList<LauncherBuildResult> results = await launcher.BuildAsync(
                    new[] { "preset:browser-runtime-probe" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never);

                Assert.That(results.Single(item => item.Id == "BrowserRuntimeProbeMod").Ok, Is.True);
                LauncherBuildResult browserResult = results.Single(item => item.Id == "browserRuntime:probe");
                Assert.That(browserResult.Ok, Is.False);
                Assert.That(browserResult.ExitCode, Is.Not.Zero);
                Assert.That(browserResult.Output, Does.Contain("Host browser runtime provider build is disabled by --build never"));
                Assert.That(browserResult.Output, Does.Contain($"Host browser runtime provider assembly is missing: {providerAssemblyPath}"));
                Assert.That(File.Exists(providerBuildMarkerPath), Is.False,
                    "--build never must not invoke the browser runtime provider project.");
                Assert.That(Directory.Exists(Path.Combine(tempDirectory, "assets", "ModSdk")), Is.False,
                    "--build never must not export the Mod SDK while validating a browser runtime package.");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public async Task RunProcessAsync_ReturnsWithoutHanging_WhenDescendantKeepsRedirectedOutputOpen()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("The redirected-handle regression uses Windows cmd/start process inheritance.");
            }

            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-output-drain-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            var scriptPath = Path.Combine(tempDirectory, "leave-output-open.cmd");

            try
            {
                File.WriteAllText(
                    scriptPath,
                    "@echo off\r\n" +
                    "start \"\" /b powershell.exe -NoProfile -NonInteractive -Command \"Start-Sleep -Milliseconds 1500\"\r\n" +
                    "echo parent-done\r\n" +
                    "exit /b 0\r\n");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var result = await LauncherService.RunProcessAsync(
                    "cmd.exe",
                    $"/c \"{scriptPath}\"",
                    tempDirectory,
                    timeoutMs: 5_000,
                    outputDrainTimeoutMs: 250);
                stopwatch.Stop();

                Assert.That(result.ExitCode, Is.Zero);
                Assert.That(result.Output, Does.Contain("parent-done"));
                Assert.That(result.Output, Does.Contain("Redirected output remained open"));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            }
            finally
            {
                await Task.Delay(1_600);
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        private static LauncherLaunchPlan CreateAppArtifactTestPlan(string outputDirectory, string appAssemblyPath)
        {
            return new LauncherLaunchPlan(
                LauncherPlatformIds.Raylib,
                "never",
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<LauncherPlannedMod>(),
                "generated",
                Path.Combine(outputDirectory, "launcher.runtime.json"),
                outputDirectory,
                appAssemblyPath,
                string.Empty,
                null,
                new LauncherPlanDiagnostics(Array.Empty<LauncherResolvedSetting>(), Array.Empty<string>()),
                new LauncherAdapterDescriptor(
                    LauncherPlatformIds.Raylib,
                    "Raylib",
                    "desktop",
                    "dotnet",
                    "launcher.runtime.v1",
                    "Test.App.csproj",
                    outputDirectory,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "launcher.runtime.json"),
                1,
                DateTime.UtcNow.ToString("O"),
                "test-plan",
                Path.Combine(outputDirectory, "raylib.launch.graph.json"));
        }

        private static LauncherService CreateIsolatedLauncher(
            string tempDirectory,
            string browserRuntimeProvidersJson = "[]",
            string presetsJson = """
            {
              "schemaVersion": 1,
              "presets": []
            }
            """)
        {
            Directory.CreateDirectory(tempDirectory);
            string configPath = Path.Combine(tempDirectory, "launcher.config.json");
            string presetsPath = Path.Combine(tempDirectory, "launcher.presets.json");
            string preferencesPath = Path.Combine(tempDirectory, "preferences.json");
            string userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
            File.WriteAllText(
                configPath,
                $$"""
                {
                  "schemaVersion": 1,
                  "scanRoots": [
                    {
                      "id": "repo_mods",
                      "path": "mods",
                      "scanMode": "recursive",
                      "enabled": true
                    }
                  ],
                  "adapters": {
                    "default": "raylib"
                  },
                  "browserRuntimeProviders": {{browserRuntimeProvidersJson}}
                }
                """);
            File.WriteAllText(presetsPath, presetsJson);
            File.WriteAllText(preferencesPath, "{}");
            File.WriteAllText(userConfigPath, "{}");
            return new LauncherService(tempDirectory, configPath, presetsPath, preferencesPath, userConfigPath);
        }

        private static void WriteBuildProbeProject(string projectPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <Target Name="RecordBuildInvocation" BeforeTargets="Build">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/build-invoked.marker" Lines="invoked" Overwrite="true" />
                  </Target>
                </Project>
                """);
        }

        private static void WriteTestModManifest(string modRoot, string modId, string mainAssemblyPath)
        {
            File.WriteAllText(
                Path.Combine(modRoot, "mod.json"),
                $$"""
                {
                  "name": "{{modId}}",
                  "version": "1.0.0",
                  "description": "launcher --build never contract probe",
                  "main": "{{mainAssemblyPath}}",
                  "priority": 0,
                  "dependencies": {}
                }
                """);
        }

        [Test]
        public void GameBootstrapper_PrefersLaunchGraphMetadata_WhenPresent()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "planFingerprint": "test-fingerprint",
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}"
                        }
                      ]
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "test-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var result = GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json");

                Assert.That(result.Engine, Is.Not.Null);
                Assert.That(result.Config, Is.Not.Null);
                Assert.That(result.AssetsRoot, Is.EqualTo(Path.Combine(repoRoot, "assets")));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_AcceptsFullLauncherGraphMetadata()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-full-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                      "planFingerprint": "full-graph-fingerprint",
                      "adapter": {
                        "id": "raylib",
                        "name": "Raylib",
                        "hostKind": "desktop",
                        "buildPipeline": "dotnet",
                        "runtimeBootstrapSchema": "launcher.runtime.v1",
                        "appProjectPath": "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj",
                        "outputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "clientProjectDirectory": "",
                        "clientDistributionDirectory": "",
                        "launchUrl": "",
                        "runtimeBootstrapFileName": "launcher.runtime.json"
                      },
                      "buildMode": "auto",
                      "selectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "rootModIds": [
                        "LudotsCoreMod"
                      ],
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}",
                          "projectPath": "{{Path.Combine(modRoot, "LudotsCoreMod.csproj").Replace("\\", "\\\\")}}",
                          "mainAssemblyPath": "{{Path.Combine(modRoot, "bin", "net8.0", "LudotsCoreMod.dll").Replace("\\", "\\\\")}}",
                          "kind": 2,
                          "buildState": 4,
                          "bindingNames": []
                        }
                      ],
                      "runtimeArtifacts": {
                        "bootstrapArtifactStrategy": "file",
                        "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                        "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                        "appOutputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "appAssemblyPath": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll",
                        "launchUrl": ""
                      },
                      "diagnostics": {
                        "settings": [],
                        "warnings": []
                      }
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanSelectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "PlanRootModIds": [
                        "LudotsCoreMod"
                      ],
                      "PlanOrderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "PlanFingerprint": "full-graph-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var result = GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json");

                Assert.That(result.Engine, Is.Not.Null);
                Assert.That(result.Config, Is.Not.Null);
                Assert.That(result.AssetsRoot, Is.EqualTo(Path.Combine(repoRoot, "assets")));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsOfficialGraph_WhenBootstrapOmitsFreshnessMetadata()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-missing-freshness-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                      "planFingerprint": "missing-freshness-fingerprint",
                      "adapter": {
                        "id": "raylib",
                        "name": "Raylib",
                        "hostKind": "desktop",
                        "buildPipeline": "dotnet",
                        "runtimeBootstrapSchema": "launcher.runtime.v1",
                        "appProjectPath": "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj",
                        "outputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "clientProjectDirectory": "",
                        "clientDistributionDirectory": "",
                        "launchUrl": "",
                        "runtimeBootstrapFileName": "launcher.runtime.json"
                      },
                      "buildMode": "auto",
                      "selectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "rootModIds": [
                        "LudotsCoreMod"
                      ],
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}",
                          "projectPath": "{{Path.Combine(modRoot, "LudotsCoreMod.csproj").Replace("\\", "\\\\")}}",
                          "mainAssemblyPath": "{{Path.Combine(modRoot, "bin", "net8.0", "LudotsCoreMod.dll").Replace("\\", "\\\\")}}",
                          "kind": 2,
                          "buildState": 4,
                          "bindingNames": []
                        }
                      ],
                      "runtimeArtifacts": {
                        "bootstrapArtifactStrategy": "file",
                        "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                        "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                        "appOutputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "appAssemblyPath": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll",
                        "launchUrl": ""
                      },
                      "diagnostics": {
                        "settings": [],
                        "warnings": []
                      }
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "missing-freshness-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("missing plan freshness metadata"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_ReadsGraphEmittedByOfficialLauncherBackend()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-official-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var graphPath = Path.Combine(repoRoot, "artifacts", "launcher", "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(
                repoRoot,
                "src",
                "Apps",
                "Raylib",
                "Ludots.App.Raylib",
                "bin",
                "Release",
                "net8.0",
                "launcher.runtime.json");
            var originalGraph = CaptureFile(graphPath);
            var originalBootstrap = CaptureFile(bootstrapPath);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var resolve = launcher.Resolve(new[] { "mod:LudotsCoreMod" }, LauncherPlatformIds.Raylib, LauncherBuildMode.Never);
                var writtenBootstrapPath = launcher.WriteBootstrap(resolve.Plan);

                Assert.That(resolve.Plan.GraphArtifactPath, Is.EqualTo(graphPath));
                Assert.That(writtenBootstrapPath, Is.EqualTo(bootstrapPath));
                Assert.That(File.Exists(graphPath), Is.True);
                Assert.That(File.Exists(bootstrapPath), Is.True);
                var bootstrapJson = File.ReadAllText(bootstrapPath);
                Assert.That(bootstrapJson, Does.Contain("PlanSelectors"));
                Assert.That(bootstrapJson, Does.Contain("PlanRootModIds"));
                Assert.That(bootstrapJson, Does.Contain("PlanOrderedModIds"));

                var result = GameBootstrapper.InitializeFromBaseDirectory(resolve.Plan.AppOutputDirectory, bootstrapPath);

                try
                {
                    Assert.That(result.Engine.ModLoader.LoadedModIds, Is.EqualTo(resolve.Plan.OrderedModIds));
                    Assert.That(result.Config, Is.Not.Null);
                    Assert.That(result.AssetsRoot, Is.EqualTo(Path.Combine(repoRoot, "assets")));
                }
                finally
                {
                    result.Engine.Dispose();
                }
            }
            finally
            {
                RestoreFile(graphPath, originalGraph);
                RestoreFile(bootstrapPath, originalBootstrap);
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsStaleGraph_WhenBootstrapPlanDiffers()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-stale-plan-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var coreInputRoot = Path.Combine(repoRoot, "mods", "CoreInputMod");
            var coreRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                      "planFingerprint": "stale-plan-fingerprint",
                      "adapter": {
                        "id": "raylib",
                        "name": "Raylib",
                        "hostKind": "desktop",
                        "buildPipeline": "dotnet",
                        "runtimeBootstrapSchema": "launcher.runtime.v1",
                        "appProjectPath": "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj",
                        "outputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "clientProjectDirectory": "",
                        "clientDistributionDirectory": "",
                        "launchUrl": "",
                        "runtimeBootstrapFileName": "launcher.runtime.json"
                      },
                      "buildMode": "never",
                      "selectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "rootModIds": [
                        "LudotsCoreMod"
                      ],
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{coreRoot.Replace("\\", "\\\\")}}",
                          "projectPath": "{{Path.Combine(coreRoot, "LudotsCoreMod.csproj").Replace("\\", "\\\\")}}",
                          "mainAssemblyPath": "{{Path.Combine(coreRoot, "bin", "net8.0", "LudotsCoreMod.dll").Replace("\\", "\\\\")}}",
                          "kind": 2,
                          "buildState": 4,
                          "bindingNames": []
                        }
                      ],
                      "runtimeArtifacts": {
                        "bootstrapArtifactStrategy": "file",
                        "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                        "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                        "appOutputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "appAssemblyPath": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll",
                        "launchUrl": ""
                      },
                      "diagnostics": {
                        "settings": [],
                        "warnings": []
                      }
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanSelectors": [
                        "mod:CoreInputMod"
                      ],
                      "PlanRootModIds": [
                        "CoreInputMod"
                      ],
                      "PlanOrderedModIds": [
                        "LudotsCoreMod",
                        "CoreInputMod"
                      ],
                      "PlanFingerprint": "stale-plan-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                Assert.That(coreInputRoot, Does.Exist);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("Stale launch graph rejected"));
                Assert.That(ex.Message, Does.Contain("selectors"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesCefBrowserRuntime_FromProviderPackageRoot()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-cef-runtime-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var graphPath = Path.Combine(repoRoot, "artifacts", "launcher", "raylib.launch.graph.json");
            var originalGraph = CaptureFile(graphPath);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var plan = launcher.Resolve(
                    new[] { "preset:browser_react_flow_cef_raylib" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;
                var runtime = plan.BrowserRuntime;
                string packageRootPath = Path.Combine(repoRoot, "BrowserRuntime", "cef");

                Assert.That(runtime, Is.Not.Null);
                Assert.That(runtime!.Provider, Is.EqualTo("cef"));
                Assert.That(runtime.ProviderAssemblyPath, Is.EqualTo(Path.Combine(packageRootPath, "Ludots.UI.Browser.Cef.dll")));
                Assert.That(runtime.RuntimeRootPath, Is.EqualTo(packageRootPath));
                Assert.That(runtime.ProviderProjectPath, Is.EqualTo(Path.Combine(
                    repoRoot,
                    "src",
                    "Libraries",
                    "Ludots.UI.Browser.Cef",
                    "Ludots.UI.Browser.Cef.csproj")));
            }
            finally
            {
                RestoreFile(graphPath, originalGraph);
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesCapabilityStandardShowcases_AsOnlyAcceptanceRoots()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-capability-standard-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$capability_standard_static_performer_30k" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    expectedRootModId: "CapabilityStandardStaticPerformer30kMod",
                    expectedStartupMapId: "capability_standard_static_performer_30k_showcase",
                    allowedModIds: new[] { "LudotsCoreMod", "CoreInputMod", "CapabilityStandardStaticPerformer30kMod" });

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$capability_standard_mass_navigation_large_world_10k" },
                        LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan,
                    expectedRootModId: "CapabilityStandardMassNavigationLargeWorld10kMod",
                    expectedStartupMapId: "mass_navigation",
                    allowedModIds: new[]
                    {
                        "LudotsCoreMod",
                        "CoreInputMod",
                        "MassNavigationMod",
                        "CapabilityStandardMassNavigationLargeWorld10kMod"
                    },
                    requiredModIds: new[] { "LudotsCoreMod", "CoreInputMod", "MassNavigationMod" });

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$formation_capability_showcase" },
                        LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan,
                    expectedRootModId: "FormationCapabilityShowcaseMod",
                    expectedStartupMapId: "formation_capability_showcase",
                    allowedModIds: new[]
                    {
                        "LudotsCoreMod",
                        "CoreInputMod",
                        "CameraProfilesMod",
                        "MassNavigationMod",
                        "FormationCapabilityShowcaseMod"
                    },
                    requiredModIds: new[] { "LudotsCoreMod", "CoreInputMod", "CameraProfilesMod", "MassNavigationMod" });

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$capability_standard_participant_views" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    expectedRootModId: "CapabilityStandardParticipantViewsMod",
                    expectedStartupMapId: "capability_standard_participant_views",
                    allowedModIds: new[]
                    {
                        "LudotsCoreMod",
                        "CoreInputMod",
                        "ParticipantViewCapabilityMod",
                        "CapabilityStandardParticipantViewsMod"
                    },
                    requiredModIds: new[] { "LudotsCoreMod", "CoreInputMod", "ParticipantViewCapabilityMod" });

                AssertCapabilityStandardPlan(
                    launcher.Resolve(
                        new[] { "$capability_standard_transport_network" },
                        LauncherPlatformIds.Raylib,
                        LauncherBuildMode.Never).Plan,
                    expectedRootModId: "CapabilityStandardTransportNetworkMod",
                    expectedStartupMapId: "capability_standard_transport_network",
                    allowedModIds: new[] { "LudotsCoreMod", "CoreInputMod", "CapabilityStandardTransportNetworkMod" });
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesProgressionScopeShowcase_AsSingleFeatureRoot()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-progression-scope-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var plan = launcher.Resolve(
                    new[] { "$progression_scope" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;

                Assert.That(plan.RootModIds, Is.EqualTo(new[] { "ProgressionScopeShowcaseMod" }));
                Assert.That(plan.OrderedModIds, Is.SubsetOf(new[]
                {
                    "LudotsCoreMod",
                    "CoreInputMod",
                    "EntityCommandPanelMod",
                    "ProgressionScopeShowcaseMod"
                }));
                Assert.That(plan.OrderedModIds, Does.Contain("ProgressionScopeShowcaseMod"));
                Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsDemoMod"));
                Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsWar3TrainingShowcaseMod"));
                Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsCncTrainingShowcaseMod"));
                Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsSc2TrainingShowcaseMod"));

                var startupMapSetting = plan.Diagnostics.Settings.First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
                Assert.That(startupMapSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo("progression_scope_showcase"));
                Assert.That(startupMapSetting.EffectiveSource, Does.Contain("ProgressionScopeShowcaseMod"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void Launcher_ResolvesAiShowcases_AsSingleFeatureRoots()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"launcher-ai-showcases-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var preferencesPath = Path.Combine(tempDirectory, "preferences.json");
                var userConfigPath = Path.Combine(tempDirectory, "config.overlay.json");
                File.WriteAllText(preferencesPath, "{}");
                File.WriteAllText(userConfigPath, "{}");

                var launcher = new LauncherService(
                    repoRoot,
                    Path.Combine(repoRoot, "launcher.config.json"),
                    Path.Combine(repoRoot, "launcher.presets.json"),
                    preferencesPath,
                    userConfigPath);

                var utilityPlan = launcher.Resolve(
                    new[] { "$utility_autocast_showcase" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;
                AssertAiShowcasePlan(
                    utilityPlan,
                    "UtilityAutocastShowcaseMod",
                    "utility_autocast_showcase",
                    new[]
                    {
                        "LudotsCoreMod",
                        "AIInspectorMod",
                        "UtilityAutocastShowcaseMod"
                    });

                var combatStancePlan = launcher.Resolve(
                    new[] { "$combat_stance_showcase" },
                    LauncherPlatformIds.Raylib,
                    LauncherBuildMode.Never).Plan;
                AssertAiShowcasePlan(
                    combatStancePlan,
                    "CombatStanceShowcaseMod",
                    "combat_stance_showcase",
                    new[]
                    {
                        "LudotsCoreMod",
                        "CombatStanceBehaviorMod",
                        "CombatStanceShowcaseMod"
                    });
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsUnknownLauncherMetadataFields()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-unknown-metadata-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var modRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                      "planFingerprint": "unknown-metadata-fingerprint",
                      "adapter": {
                        "id": "raylib",
                        "name": "Raylib",
                        "hostKind": "desktop",
                        "buildPipeline": "dotnet",
                        "runtimeBootstrapSchema": "launcher.runtime.v1",
                        "appProjectPath": "src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj",
                        "outputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "clientProjectDirectory": "",
                        "clientDistributionDirectory": "",
                        "launchUrl": "",
                        "runtimeBootstrapFileName": "launcher.runtime.json",
                        "unexpectedAdapterField": "must not be silently ignored"
                      },
                      "buildMode": "auto",
                      "selectors": [
                        "mod:LudotsCoreMod"
                      ],
                      "rootModIds": [
                        "LudotsCoreMod"
                      ],
                      "orderedModIds": [
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{modRoot.Replace("\\", "\\\\")}}",
                          "projectPath": "{{Path.Combine(modRoot, "LudotsCoreMod.csproj").Replace("\\", "\\\\")}}",
                          "mainAssemblyPath": "{{Path.Combine(modRoot, "bin", "net8.0", "LudotsCoreMod.dll").Replace("\\", "\\\\")}}",
                          "kind": 2,
                          "buildState": 4,
                          "bindingNames": []
                        }
                      ],
                      "runtimeArtifacts": {
                        "bootstrapArtifactStrategy": "file",
                        "bootstrapArtifactPath": "{{bootstrapPath.Replace("\\", "\\\\")}}",
                        "graphArtifactPath": "{{graphPath.Replace("\\", "\\\\")}}",
                        "appOutputDirectory": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0",
                        "appAssemblyPath": "src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/Ludots.App.Raylib.dll",
                        "launchUrl": ""
                      },
                      "diagnostics": {
                        "settings": [],
                        "warnings": []
                      }
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "unknown-metadata-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<Exception>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("unexpectedAdapterField"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsLaunchGraph_WhenDependencyOrderIsInvalid()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-graph-invalid-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            var coreInputRoot = Path.Combine(repoRoot, "mods", "CoreInputMod");
            var coreRoot = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
            var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
            var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

            try
            {
                File.WriteAllText(
                    graphPath,
                    $$"""
                    {
                      "schemaVersion": 1,
                      "planFingerprint": "invalid-order-fingerprint",
                      "orderedModIds": [
                        "CoreInputMod",
                        "LudotsCoreMod"
                      ],
                      "plannedMods": [
                        {
                          "id": "CoreInputMod",
                          "rootPath": "{{coreInputRoot.Replace("\\", "\\\\")}}"
                        },
                        {
                          "id": "LudotsCoreMod",
                          "rootPath": "{{coreRoot.Replace("\\", "\\\\")}}"
                        }
                      ]
                    }
                    """);

                File.WriteAllText(
                    bootstrapPath,
                    """
                    {
                      "LaunchGraphPath": "raylib.launch.graph.json",
                      "PlanFingerprint": "invalid-order-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("Launch plan order is invalid"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_UsesGraphPlanOrder_AsRuntimeLoadOrder()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var coreMod = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var lowPriorityMod = CreateTestMod(tempDirectory, "LowPriorityMod", priority: 0);
                var highPriorityMod = CreateTestMod(tempDirectory, "HighPriorityMod", priority: 100);
                var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
                var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

                WriteLaunchGraph(
                    graphPath,
                    planFingerprint: "graph-order-fingerprint",
                    orderedModIds: new[] { "LudotsCoreMod", "LowPriorityMod", "HighPriorityMod" },
                    plannedModsJson: $$"""
                    [
                      {
                        "id": "LudotsCoreMod",
                        "rootPath": "{{coreMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "LowPriorityMod",
                        "rootPath": "{{lowPriorityMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "HighPriorityMod",
                        "rootPath": "{{highPriorityMod.Replace("\\", "\\\\")}}"
                      }
                    ]
                    """);

                WriteBootstrap(bootstrapPath, "graph-order-fingerprint");

                var result = GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json");

                Assert.That(result.Engine.ModLoader.LoadedModIds, Is.EqualTo(new[] { "LudotsCoreMod", "LowPriorityMod", "HighPriorityMod" }),
                    "Graph-planned order should remain the runtime load order even when priority would have reordered an ad-hoc resolve path.");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsBootstrapWithoutLaunchGraphMetadata()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-missing-graph-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                File.WriteAllText(
                    Path.Combine(tempDirectory, "launcher.runtime.json"),
                    """
                    {
                      "PlanFingerprint": "graph-required-fingerprint",
                      "PlanSchemaVersion": 1
                    }
                    """);

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("missing launch graph metadata"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsGraphDependencyOrderViolations()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-invalid-order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var coreMod = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var baseMod = CreateTestMod(tempDirectory, "BaseMod", priority: 0);
                var featureMod = CreateTestMod(
                    tempDirectory,
                    "FeatureMod",
                    priority: 0,
                    dependenciesJson: """
                    {
                      "BaseMod": "^1.0.0"
                    }
                    """);
                var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
                var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

                WriteLaunchGraph(
                    graphPath,
                    planFingerprint: "graph-invalid-order-fingerprint",
                    orderedModIds: new[] { "LudotsCoreMod", "FeatureMod", "BaseMod" },
                    plannedModsJson: $$"""
                    [
                      {
                        "id": "LudotsCoreMod",
                        "rootPath": "{{coreMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "FeatureMod",
                        "rootPath": "{{featureMod.Replace("\\", "\\\\")}}"
                      },
                      {
                        "id": "BaseMod",
                        "rootPath": "{{baseMod.Replace("\\", "\\\\")}}"
                      }
                    ]
                    """);

                WriteBootstrap(bootstrapPath, "graph-invalid-order-fingerprint");

                var ex = Assert.Throws<InvalidOperationException>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("Launch plan order is invalid"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void GameBootstrapper_RejectsLaunchGraphModIdCaseAliases()
        {
            var repoRoot = FindRepoRoot();
            var tempDirectory = Path.Combine(repoRoot, "artifacts", "tests", $"bootstrap-case-alias-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var coreMod = Path.Combine(repoRoot, "mods", "LudotsCoreMod");
                var graphPath = Path.Combine(tempDirectory, "raylib.launch.graph.json");
                var bootstrapPath = Path.Combine(tempDirectory, "launcher.runtime.json");

                WriteLaunchGraph(
                    graphPath,
                    planFingerprint: "graph-case-alias-fingerprint",
                    orderedModIds: new[] { "ludotscoremod" },
                    plannedModsJson: $$"""
                    [
                      {
                        "id": "LudotsCoreMod",
                        "rootPath": "{{coreMod.Replace("\\", "\\\\")}}"
                      }
                    ]
                    """);

                WriteBootstrap(bootstrapPath, "graph-case-alias-fingerprint");

                var ex = Assert.Throws<Exception>(
                    () => GameBootstrapper.InitializeFromBaseDirectory(tempDirectory, "launcher.runtime.json"));

                Assert.That(ex!.Message, Does.Contain("does not match plannedMods"));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        private static string CreateTestMod(string root, string modName, int priority, string? dependenciesJson = null)
        {
            var modDir = Path.Combine(root, modName);
            Directory.CreateDirectory(modDir);
            File.WriteAllText(
                Path.Combine(modDir, "mod.json"),
                $$"""
                {
                  "name": "{{modName}}",
                  "version": "1.0.0",
                  "description": "test",
                  "main": "",
                  "priority": {{priority}},
                  "dependencies": {{dependenciesJson ?? "{}"}}
                }
                """);
            return modDir;
        }

        private static void WriteLaunchGraph(string graphPath, string planFingerprint, string[] orderedModIds, string plannedModsJson)
        {
            var orderedIdsJson = string.Join(
                "," + Environment.NewLine + "    ",
                orderedModIds.Select(id => $"\"{id}\""));
            File.WriteAllText(
                graphPath,
                $$"""
                {
                  "schemaVersion": 1,
                  "generatedAtUtc": "2026-04-01T00:00:00.0000000Z",
                  "planFingerprint": "{{planFingerprint}}",
                  "orderedModIds": [
                    {{orderedIdsJson}}
                  ],
                  "plannedMods": {{plannedModsJson}}
                }
                """);
        }

        private static void WriteBootstrap(string bootstrapPath, string fingerprint)
        {
            File.WriteAllText(
                bootstrapPath,
                $$"""
                {
                  "LaunchGraphPath": "raylib.launch.graph.json",
                  "PlanFingerprint": "{{fingerprint}}",
                  "PlanSchemaVersion": 1
                }
                """);
        }

        private static void AssertCapabilityStandardPlan(
            LauncherLaunchPlan plan,
            string expectedRootModId,
            string expectedStartupMapId,
            string[] allowedModIds,
            string[]? requiredModIds = null)
        {
            Assert.That(plan.RootModIds, Is.EqualTo(new[] { expectedRootModId }));
            Assert.That(plan.OrderedModIds, Does.Contain(expectedRootModId));
            Assert.That(plan.OrderedModIds, Is.SubsetOf(allowedModIds));
            if (requiredModIds is not null)
            {
                foreach (var requiredModId in requiredModIds)
                {
                    Assert.That(plan.OrderedModIds, Does.Contain(requiredModId));
                }
            }

            Assert.That(plan.OrderedModIds, Does.Not.Contain("PerformerBlacksmithShowcaseMod"));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("PerformerBlacksmithScatterHudTextBenchmarkEntryMod"));

            var startupMapSetting = plan.Diagnostics.Settings.First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
            Assert.That(startupMapSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo(expectedStartupMapId));
            Assert.That(startupMapSetting.EffectiveSource, Does.Contain(expectedRootModId));
        }

        private static void AssertAiShowcasePlan(
            LauncherLaunchPlan plan,
            string expectedRootModId,
            string expectedStartupMapId,
            string[] allowedModIds)
        {
            Assert.That(plan.RootModIds, Is.EqualTo(new[] { expectedRootModId }));
            Assert.That(plan.OrderedModIds, Is.SubsetOf(allowedModIds));
            Assert.That(plan.OrderedModIds, Does.Contain(expectedRootModId));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("AIDemoMod"));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("RtsDemoMod"));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("RelationshipShowcaseMod"));
            Assert.That(plan.OrderedModIds, Does.Not.Contain("FourXAssociationShowcaseMod"));

            var startupMapSetting = plan.Diagnostics.Settings.First(setting => string.Equals(setting.Key, "startupMapId", StringComparison.Ordinal));
            Assert.That(startupMapSetting.EffectiveValue?.GetValue<string>(), Is.EqualTo(expectedStartupMapId));
            Assert.That(startupMapSetting.EffectiveSource, Does.Contain(expectedRootModId));
        }

        private static FileSnapshot CaptureFile(string path)
        {
            return File.Exists(path)
                ? new FileSnapshot(true, File.ReadAllText(path))
                : new FileSnapshot(false, string.Empty);
        }

        private static void RestoreFile(string path, FileSnapshot snapshot)
        {
            if (snapshot.Exists)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, snapshot.Contents);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                var candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        private readonly record struct FileSnapshot(bool Exists, string Contents);
    }
}
