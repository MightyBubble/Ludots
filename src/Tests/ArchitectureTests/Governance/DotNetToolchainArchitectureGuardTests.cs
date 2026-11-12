using System.Text.Json;
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
    public void OfflineFeedPinsDownlevelTargetingPacksAgainstFloatingSdk()
    {
        string repoRoot = FindRepoRoot();
        string rootTargets = Path.Combine(repoRoot, "Directory.Build.targets");
        XDocument document = XDocument.Load(rootTargets);
        XElement pin = document
            .Descendants("Target")
            .Single(element => string.Equals(
                (string?)element.Attribute("Name"),
                "PinOfflineDownlevelTargetingPacks",
                StringComparison.Ordinal));

        string[] expectedVersions = ["net8.0=8.0.31", "net7.0=7.0.20", "net6.0=6.0.36"];
        string[] packNames =
        [
            "Microsoft.NETCore.App",
            "Microsoft.AspNetCore.App",
            "Microsoft.WindowsDesktop.App"
        ];

        var pinned = pin
            .Descendants("KnownFrameworkReference")
            .Select(element => (
                Name: (string?)element.Attribute("Update"),
                Framework: (string?)element.Attribute("Condition"),
                Version: element.Element("TargetingPackVersion")?.Value))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That((string?)pin.Attribute("BeforeTargets"), Is.EqualTo("ProcessFrameworkReferences"));
            foreach (string packName in packNames)
            {
                foreach (string expected in expectedVersions)
                {
                    string[] parts = expected.Split('=');
                    Assert.That(pinned, Has.Some.Matches<(string? Name, string? Framework, string? Version)>(entry =>
                        entry.Name == packName &&
                        entry.Framework != null &&
                        entry.Framework.Contains($"'{parts[0]}'", StringComparison.Ordinal) &&
                        entry.Version == parts[1]));
                }
            }
        });

        foreach (string version in new[] { "8.0.31", "7.0.20", "6.0.36" })
        {
            foreach (string packageId in new[]
                     {
                         "microsoft.netcore.app.ref",
                         "microsoft.aspnetcore.app.ref",
                         "microsoft.windowsdesktop.app.ref"
                     })
            {
                string nupkg = Path.Combine(repoRoot, "external", "nuget", $"{packageId}.{version}.nupkg");
                Assert.That(File.Exists(nupkg), Is.True, nupkg);
            }
        }

        string[] shadowed = Directory
            .EnumerateFiles(repoRoot, "Directory.Build.targets", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !string.Equals(path, rootTargets, StringComparison.Ordinal))
            .ToArray();
        Assert.That(shadowed, Is.Not.Empty);
        Assert.That(
            shadowed.Select(File.ReadAllText),
            Has.All.Contains("GetPathOfFileAbove('Directory.Build.targets'"));
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
