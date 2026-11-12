using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[Category("ci-gate")]
[Category("arch-guard")]
public sealed class DotNetToolchainArchitectureGuardTests
{
    [Test]
    public void RepositoryPinsSupportedDotNet9Sdk()
    {
        string repoRoot = FindRepoRoot();
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repoRoot, "global.json")));
        JsonElement sdk = document.RootElement.GetProperty("sdk");

        Assert.Multiple(() =>
        {
            Assert.That(sdk.GetProperty("version").GetString(), Is.EqualTo("9.0.100"));
            Assert.That(sdk.GetProperty("rollForward").GetString(), Is.EqualTo("latestFeature"));
        });
    }

    [Test]
    public void WorkflowsDoNotOverrideRepositorySdkSelection()
    {
        string workflowsDirectory = Path.Combine(FindRepoRoot(), ".github", "workflows");
        string[] offenders = Directory
            .EnumerateFiles(workflowsDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("dotnet new globaljson", StringComparison.Ordinal))
            .Select(path => new FileInfo(path).Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(offenders, Is.Empty,
            "CI must consume the repository global.json instead of creating a competing SDK selection.");
    }

    [Test]
    public void ProjectsTargetNet9AndOfflineFeedHasOneDeclaration()
    {
        string repoRoot = FindRepoRoot();
        string[] tfmOffenders = EnumerateProjectFiles(repoRoot)
            .Where(path => HasRetiredTargetFramework(path) || HasDuplicateTargetFrameworkDeclaration(path))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        XDocument feed = XDocument.Load(Path.Combine(repoRoot, "nuget.config"));
        XElement packageSources = feed.Descendants("packageSources").Single();
        XElement[] feedAdds = packageSources.Elements("add").ToArray();
        string[] nugetConfigs = Directory
            .EnumerateFiles(repoRoot, "nuget.config", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(repoRoot, "NuGet.Config", SearchOption.AllDirectories))
            .Where(path => !IsIgnoredBuildOutput(path))
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .Where(path => !string.Equals(path, "nuget.config", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string workflowsDirectory = Path.Combine(repoRoot, ".github", "workflows");
        string[] workflowText = Directory
            .EnumerateFiles(workflowsDirectory, "*.yml", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(tfmOffenders, Is.Empty);
            Assert.That(nugetConfigs, Is.Empty);
            Assert.That(packageSources.Elements("clear").Count(), Is.EqualTo(1));
            Assert.That(feedAdds.Select(element => (string?)element.Attribute("key")).ToArray(),
                Is.EqualTo(new[] { "LudotsOffline" }));
            Assert.That(feedAdds.Select(element => (string?)element.Attribute("value")).ToArray(),
                Is.EqualTo(new[] { "external/nuget" }));
            Assert.That(File.ReadAllText(Path.Combine(repoRoot, "nuget.config")), Does.Not.Contain("nuget.org"));
            Assert.That(File.Exists(Path.Combine(repoRoot, "Directory.Build.targets")), Is.False);
            Assert.That(workflowText.Where(text => text.Contains("setup-dotnet@", StringComparison.Ordinal)),
                Has.All.Contains("global-json-file: global.json"));
            Assert.That(workflowText, Has.None.Contains("9.0.x"));
        });
    }

    [Test]
    public void PackageFeedChangesTriggerConsumingWorkflows()
    {
        string repoRoot = FindRepoRoot();
        string[] workflows =
        {
            ".github/workflows/solution-verify.yml",
            ".github/workflows/camera-baseline.yml",
            ".github/workflows/ci-acceptance.yml",
            ".github/workflows/mass-navigation-10k-evidence.yml",
        };

        Assert.Multiple(() =>
        {
            foreach (string workflow in workflows)
            {
                string text = File.ReadAllText(Path.Combine(repoRoot, workflow));
                List<string[]> blocks = ReadPathFilters(text);
                Assert.That(blocks, Is.Not.Empty, workflow);
                foreach (string[] entries in blocks)
                {
                    Assert.That(entries, Does.Contain("nuget.config"), workflow);
                    Assert.That(entries, Does.Contain("external/nuget/**"), workflow);
                }
            }
        });
    }

    [Test]
    public void WorkflowDotNetCommandsCheckExitCode()
    {
        string workflowsDirectory = Path.Combine(FindRepoRoot(), ".github", "workflows");
        Assert.Multiple(() =>
        {
            foreach (string path in Directory.EnumerateFiles(workflowsDirectory, "*.yml"))
            {
                AssertDotNetCommandsCheckExitCode(Path.GetFileName(path), File.ReadAllText(path));
            }
        });
    }

    [Test]
    public void CefWin64RuntimePackageUsesGitLfs()
    {
        string repoRoot = FindRepoRoot();
        const string relativePath = "external/nuget/chromiumembeddedframework.runtime.win-x64.148.0.9.nupkg";
        string pointer = GitShow(repoRoot, relativePath);
        long declaredSize = ReadLfsPointerSize(pointer);
        string solutionVerify = File.ReadAllText(Path.Combine(
            repoRoot, ".github", "workflows", "solution-verify.yml"));
        string[] offenders = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "external", "nuget"), "*.nupkg")
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Where(path => !CommittedNupkgIsAcceptable(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(pointer, Does.StartWith("version https://git-lfs.github.com/spec/v1"));
            Assert.That(declaredSize, Is.GreaterThan(100L * 1024 * 1024));
            Assert.That(GitAttributesCover(repoRoot, relativePath), Is.True, relativePath);
            Assert.That(solutionVerify, Does.Contain("lfs: true"));
            Assert.That(offenders, Is.Empty);
        });
    }

    [Test]
    public void SvgGeneratorUsesVersionedAnalyzerDependencies()
    {
        string repoRoot = FindRepoRoot();
        XDocument project = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "Libraries",
            "Svg.Skia",
            "externals",
            "SVG",
            "Generators",
            "Svg.Generators.csproj"));

        XElement packageReference = project
            .Descendants("PackageReference")
            .Single(element => string.Equals(
                (string?)element.Attribute("Include"),
                "Microsoft.CodeAnalysis.CSharp",
                StringComparison.Ordinal));
        XElement[] compilerReferences = project
            .Descendants("Reference")
            .Where(element => ((string?)element.Attribute("Include"))
                ?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(project.Descendants("TargetFramework").Single().Value, Is.EqualTo("netstandard2.0"));
            Assert.That((string?)packageReference.Attribute("Version"), Is.EqualTo("4.11.0"));
            Assert.That((string?)packageReference.Attribute("PrivateAssets"), Is.EqualTo("all"));
            Assert.That(project.Descendants("RoslynBinariesPath"), Is.Empty);
            Assert.That(compilerReferences, Is.Empty);
        });
    }

    [Test]
    public void SvgCustomConsumesGeneratorThroughAnalyzerProjectReference()
    {
        string repoRoot = FindRepoRoot();
        XDocument project = XDocument.Load(Path.Combine(
            repoRoot,
            "src",
            "Libraries",
            "Svg.Skia",
            "Svg.Custom",
            "Svg.Custom.csproj"));

        XElement generatorReference = project
            .Descendants("ProjectReference")
            .Single(element => ((string?)element.Attribute("Include"))
                ?.EndsWith("Svg.Generators.csproj", StringComparison.Ordinal) == true);

        Assert.Multiple(() =>
        {
            Assert.That((string?)generatorReference.Attribute("OutputItemType"), Is.EqualTo("Analyzer"));
            Assert.That((string?)generatorReference.Attribute("ReferenceOutputAssembly"), Is.EqualTo("false"));
            Assert.That(project.Descendants("Target").Any(element =>
                string.Equals((string?)element.Attribute("Name"), "BuildSvgGenerators", StringComparison.Ordinal)),
                Is.False);
            Assert.That(project.Descendants("Analyzer"), Is.Empty);
        });
    }

    private static IEnumerable<string> EnumerateProjectFiles(string repoRoot)
    {
        return Directory
            .EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsIgnoredBuildOutput(path));
    }

    private static bool IsIgnoredBuildOutput(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool HasRetiredTargetFramework(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .Any(element => Regex.IsMatch(element.Value, @"net(6|7|8|10)\.0"));
    }

    private static bool HasDuplicateTargetFrameworkDeclaration(string projectPath)
    {
        XDocument document = XDocument.Load(projectPath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement element in document.Descendants())
        {
            if (element.Name.LocalName is not ("TargetFramework" or "TargetFrameworks"))
            {
                continue;
            }

            foreach (string token in element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.StartsWith("$(", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!seen.Add(token))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<string[]> ReadPathFilters(string workflowText)
    {
        string[] lines = workflowText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<string[]>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!string.Equals(lines[i].Trim(), "paths:", StringComparison.Ordinal))
            {
                continue;
            }

            var entries = new List<string>();
            for (int j = i + 1; j < lines.Length; j++)
            {
                string trimmed = lines[j].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    break;
                }

                entries.Add(trimmed[2..].Trim().Trim('\'').Trim('"'));
            }

            blocks.Add(entries.ToArray());
        }

        return blocks;
    }

    private static void AssertDotNetCommandsCheckExitCode(string workflowName, string workflowText)
    {
        string[] lines = workflowText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        bool pending = false;
        int pendingLine = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("run:", StringComparison.Ordinal))
            {
                trimmed = trimmed["run:".Length..].Trim();
            }

            if (trimmed.StartsWith("dotnet build", StringComparison.Ordinal) ||
                trimmed.StartsWith("dotnet test", StringComparison.Ordinal))
            {
                if (pending)
                {
                    Assert.Fail($"{workflowName}:{pendingLine} runs dotnet without checking $LASTEXITCODE");
                }

                pending = true;
                pendingLine = i + 1;
                continue;
            }

            if (trimmed.StartsWith("if ($LASTEXITCODE -ne 0)", StringComparison.Ordinal))
            {
                pending = false;
            }
        }

        if (pending)
        {
            Assert.Fail($"{workflowName}:{pendingLine} runs dotnet without checking $LASTEXITCODE");
        }
    }

    private static bool CommittedNupkgIsAcceptable(string repoRoot, string relativePath)
    {
        long blobSize = GitCatFileSize(repoRoot, relativePath);
        if (blobSize > 100L * 1024 * 1024)
        {
            return false;
        }

        if (blobSize > 4096)
        {
            return true;
        }

        string blob = GitShow(repoRoot, relativePath);
        if (blob.StartsWith("version https://git-lfs.github.com/spec/v1", StringComparison.Ordinal))
        {
            return GitAttributesCover(repoRoot, relativePath);
        }

        return blob.Length >= 2 && blob[0] == 'P' && blob[1] == 'K';
    }

    private static long GitCatFileSize(string repoRoot, string relativePath)
    {
        string output = RunGit(repoRoot, "cat-file", "-s", $"HEAD:{relativePath}");
        return long.Parse(output.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GitShow(string repoRoot, string relativePath)
    {
        return RunGit(repoRoot, "show", $"HEAD:{relativePath}");
    }

    private static string RunGit(string repoRoot, params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(stderr.Trim());
        }

        return stdout;
    }

    private static bool GitAttributesCover(string repoRoot, string relativePath)
    {
        foreach (string raw in File.ReadAllLines(Path.Combine(repoRoot, ".gitattributes")))
        {
            string trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                parts.Contains("filter=lfs") &&
                GitAttributeMatches(parts[0], relativePath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GitAttributeMatches(string pattern, string relativePath)
    {
        if (!pattern.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(pattern, relativePath, StringComparison.Ordinal);
        }

        string regex = "^" + Regex.Escape(pattern).Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(relativePath, regex);
    }

    private static long ReadLfsPointerSize(string pointer)
    {
        foreach (string raw in pointer.Split('\n'))
        {
            string trimmed = raw.Trim();
            if (trimmed.StartsWith("size ", StringComparison.Ordinal) &&
                long.TryParse(trimmed["size ".Length..], out long size))
            {
                return size;
            }
        }

        return -1;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
