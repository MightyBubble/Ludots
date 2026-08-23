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
