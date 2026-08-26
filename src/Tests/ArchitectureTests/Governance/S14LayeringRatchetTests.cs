using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[Category("ci-gate")]
[Category("arch-guard")]
public sealed class S14LayeringRatchetTests
{
    public const int MaxGetEngineCalls = 205;
    public const int MaxUndeclaredModRegisterSystemCalls = 100;
    public const int MaxProductionStaticRegistryClearCalls = 9;
    public const int MaxModProjectsReferencingFacadeGameEngine = 147;
    public const int MaxModGraphIdRegistryClearCalls = 5;

    private static readonly Regex GetEngineCall = new(@"\.GetEngine\s*\(", RegexOptions.Compiled);
    private static readonly Regex RegisterSystemCall = new(@"\.RegisterSystem\s*\(", RegexOptions.Compiled);
    private static readonly Regex CapabilityDeclaration = new(
        @"SystemCapability\s*\(|capabilityId\s*:",
        RegexOptions.Compiled);
    private static readonly Regex StaticRegistryClear = new(
        @"\b[A-Z][A-Za-z0-9]*Registry\.Clear\s*\(",
        RegexOptions.Compiled);
    private static readonly Regex GraphIdRegistryClear = new(
        @"GraphIdRegistry\.Clear\s*\(",
        RegexOptions.Compiled);

    [Test]
    public void GetEngine_IsObsolete_AndCallSitesDoNotIncrease()
    {
        MethodInfo? method = typeof(ScriptContextExtensions).GetMethod(
            "GetEngine",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        Assert.That(Attribute.IsDefined(method!, typeof(ObsoleteAttribute)), Is.True);

        int actual = CountMatches(EnumerateProductionAndModCsFiles(), GetEngineCall);
        AssertRatchet(actual, MaxGetEngineCalls, "GetEngine invocations");
    }

    [Test]
    public void Mods_DirectRegisterSystemWithoutCapability_DoesNotIncrease()
    {
        int actual = 0;
        foreach (string file in EnumerateCsFiles("mods"))
        {
            string text = File.ReadAllText(file);
            foreach (Match match in RegisterSystemCall.Matches(text))
            {
                int lineStart = text.LastIndexOf('\n', match.Index) + 1;
                int priorStart = lineStart;
                for (int i = 0; i < 15 && priorStart > 0; i++)
                {
                    int previousBreak = text.LastIndexOf('\n', priorStart - 2);
                    priorStart = previousBreak < 0 ? 0 : previousBreak + 1;
                }

                int invocationEnd = text.IndexOf(';', match.Index);
                if (invocationEnd < 0)
                {
                    invocationEnd = Math.Min(text.Length, match.Index + 160);
                }

                string window = text[priorStart..invocationEnd];
                if (!CapabilityDeclaration.IsMatch(window))
                {
                    actual++;
                }
            }
        }

        AssertRatchet(actual, MaxUndeclaredModRegisterSystemCalls, "mods undeclared RegisterSystem invocations");
    }

    [Test]
    public void Production_StaticRegistryClear_DoesNotIncrease()
    {
        int actual = 0;
        foreach (string file in EnumerateProductionCsFiles())
        {
            actual += StaticRegistryClear.Matches(File.ReadAllText(file)).Count;
        }

        AssertRatchet(actual, MaxProductionStaticRegistryClearCalls, "production static Registry.Clear invocations");
    }

    [Test]
    public void Mods_GraphIdRegistryClear_DoesNotIncrease()
    {
        int actual = CountMatches(EnumerateCsFiles("mods"), GraphIdRegistryClear);
        AssertRatchet(actual, MaxModGraphIdRegistryClearCalls, "mods GraphIdRegistry.Clear invocations");
    }

    [Test]
    public void ModProjects_ReferencingFacadeGameEngine_DoNotIncrease()
    {
        string modsRoot = Path.Combine(FindRepoRoot(), "mods");
        int actual = 0;
        foreach (string projectPath in Directory.EnumerateFiles(modsRoot, "*.csproj", SearchOption.AllDirectories))
        {
            XDocument document = XDocument.Load(projectPath);
            bool referencesFacade = document
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .OfType<string>()
                .Any(include =>
                    include.Contains("Ludots.Core.csproj", StringComparison.Ordinal) ||
                    include.Contains("Ludots.Engine", StringComparison.Ordinal));
            if (referencesFacade)
            {
                actual++;
            }
        }

        AssertRatchet(actual, MaxModProjectsReferencingFacadeGameEngine, "mod projects referencing facade GameEngine");
    }

    private static void AssertRatchet(int actual, int maximum, string label)
    {
        Assert.That(
            actual,
            Is.LessThanOrEqualTo(maximum),
            $"{label} actual {actual} exceeded ratchet {maximum}. Constants may only decrease.");
    }

    private static int CountMatches(IEnumerable<string> files, Regex pattern)
    {
        int total = 0;
        foreach (string file in files)
        {
            total += pattern.Matches(File.ReadAllText(file)).Count;
        }

        return total;
    }

    private static IEnumerable<string> EnumerateCsFiles(params string[] relativeRoots)
    {
        string repoRoot = FindRepoRoot();
        foreach (string relativeRoot in relativeRoots)
        {
            string root = Path.Combine(repoRoot, relativeRoot);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (IsIgnoredBuildArtifact(file))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static IEnumerable<string> EnumerateProductionAndModCsFiles()
    {
        foreach (string file in EnumerateCsFiles("mods", "src"))
        {
            string relative = ToRepoRelativePath(FindRepoRoot(), file);
            if (relative.StartsWith("src/Tests/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateProductionCsFiles()
    {
        foreach (string file in EnumerateCsFiles("mods", "src"))
        {
            string relative = ToRepoRelativePath(FindRepoRoot(), file);
            if (relative.StartsWith("src/Tests/", StringComparison.Ordinal) ||
                relative.StartsWith("src/Libraries/", StringComparison.Ordinal) ||
                relative.StartsWith("src/Tools/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    private static bool IsIgnoredBuildArtifact(string path)
    {
        string[] parts = path.Replace('\\', '/').Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] is "bin" or "obj")
            {
                return true;
            }
        }

        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "assets")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
    }

    private static string ToRepoRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
    }
}
