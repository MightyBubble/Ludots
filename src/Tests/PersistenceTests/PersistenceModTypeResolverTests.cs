using System.Diagnostics;
using System.Reflection;
using Arch.Core;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
[NonParallelizable]
public sealed class PersistenceModTypeResolverTests
{
    [Test]
    public void SerializerCreatedFromModLoaderSnapshotResolvesLoadedModComponentAfterDefaultCacheWasBuilt()
    {
        string tempRoot = CreateTempDir();
        ModLoader? loader = null;
        try
        {
            LudotsCorePersistenceFormatters.ResetCacheForTests();
            _ = LudotsCorePersistenceFormatters.GetFormatterComponentTypes();

            string repoRoot = FindRepoRoot();
            string coreProjectPath = Path.Combine(repoRoot, "src", "Core", "Ludots.Core.csproj");
            string modName = "Issue611PersistenceProbe" + Guid.NewGuid().ToString("N") + "Mod";
            string modDir = CreatePersistenceProbeMod(tempRoot, modName, coreProjectPath);
            BuildProject(Path.Combine(modDir, modName + ".csproj"));

            loader = new ModLoader(
                new VirtualFileSystem(),
                new FunctionRegistry(),
                new TriggerManager());
            loader.LoadMods(new[] { modDir });

            Assembly modAssembly = loader.LoadedAssemblies.Single(assembly =>
                string.Equals(assembly.GetName().Name, modName, StringComparison.Ordinal));
            Type componentType = modAssembly.GetType(
                $"{modName}.Issue611SaveMarker",
                throwOnError: true)!;
            object component = Activator.CreateInstance(componentType, 611)!;

            using World world = World.Create();
            Entity entity = world.Create();
            world.Add(entity, component);

            LudotsBinaryWorldSerializer serializer = LudotsPersistenceSerializerFactory.Create(loader);

            byte[] bytes = serializer.Serialize(world);
            using World restored = serializer.Deserialize(bytes);

            object restoredComponent = FindSingleComponent(restored, componentType);

            Assert.That(componentType.GetField("Value")!.GetValue(restoredComponent), Is.EqualTo(611));
        }
        finally
        {
            loader?.UnloadAll();
            LudotsCorePersistenceFormatters.ResetCacheForTests();
            TryDelete(tempRoot);
        }
    }

    private static object FindSingleComponent(World world, Type componentType)
    {
        object? result = null;
        int count = 0;
        var query = QueryDescription.Null;
        world.Query(in query, entity =>
        {
            Signature signature = world.GetSignature(entity);
            foreach (ComponentType component in signature.Components)
            {
                if (component.Type != componentType)
                {
                    continue;
                }

                result = world.Get(entity, component);
                count++;
            }
        });

        Assert.That(count, Is.EqualTo(1), $"Expected exactly one component '{componentType.FullName}'.");
        return result!;
    }

    private static string CreatePersistenceProbeMod(
        string tempRoot,
        string modName,
        string coreProjectPath)
    {
        string modDir = Path.Combine(tempRoot, modName);
        Directory.CreateDirectory(modDir);

        string projectXml = $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net9.0</TargetFramework>
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

        public readonly struct Issue611SaveMarker
        {
            public Issue611SaveMarker(int value)
            {
                Value = value;
            }

            public readonly int Value;
        }

        public sealed class {{modName}}Entry : IMod
        {
            public void OnLoad(IModContext context)
            {
            }

            public void OnUnload()
            {
            }
        }
        """;

        string manifestJson = $$"""
        {
          "name": "{{modName}}",
          "version": "1.0.0",
          "description": "Issue 611 persistence type resolver regression mod",
          "main": "bin/net9.0/{{modName}}.dll",
          "priority": 0,
          "dependencies": {}
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
            Arguments = $"build \"{projectPath}\" -c Debug --nologo -p:BuildInParallel=false -m:1 -nodeReuse:false",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start dotnet build process.");
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
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
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
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
        string path = Path.Combine(Path.GetTempPath(), "ludots_issue611_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
}
