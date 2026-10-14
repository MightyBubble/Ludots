using System.Text;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class RetiredInputNameGuardTests
{
    private static readonly string[] Forbidden =
    {
        "CoreInputMod",
        "Default_Gameplay",
        "CoreInputCollectionKeys",
        "startupInputContexts",
        "collection.command.source",
        "SkillBarOverlaySystem",
        "targetKinds",
    };

    private static readonly string[] Roots =
    {
        "mods",
        "src",
        "gitbook",
        "docs/architecture",
        "docs/diagrams",
        "docs/reference",
        "docs/conventions",
    };

    private static readonly HashSet<string> ExcludedRelativePaths = new(StringComparer.Ordinal)
    {
        "gitbook/architecture/graph-capability-status.md",
        "docs/reference/champion_skill_sandbox_delivery_plan.md",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".csproj",
        ".sln",
        ".json",
        ".md",
        ".html",
        ".props",
        ".targets",
        ".txt",
        ".yml",
        ".yaml",
        ".xml",
        ".ps1",
        ".sh",
        ".svg",
    };

    [Test]
    public void ModsSrcGitbookAndCurrentDocs_DoNotContainRetiredInputNames()
    {
        string repoRoot = FindRepoRoot();
        var hits = new List<string>();
        foreach (string rootName in Roots)
        {
            string root = Path.Combine(repoRoot, rootName);
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) ||
                    relative.Contains("/obj/", StringComparison.Ordinal) ||
                    relative.EndsWith("RetiredInputNameGuardTests.cs", StringComparison.Ordinal) ||
                    ExcludedRelativePaths.Contains(relative))
                {
                    continue;
                }

                if (!TextExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                string text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                int line = 1;
                int start = 0;
                for (int i = 0; i <= text.Length; i++)
                {
                    if (i != text.Length && text[i] != '\n')
                    {
                        continue;
                    }

                    int length = i - start;
                    if (length > 0)
                    {
                        foreach (string token in Forbidden)
                        {
                            if (text.IndexOf(token, start, length, StringComparison.Ordinal) >= 0)
                            {
                                hits.Add($"{relative}:{line}: {token}");
                            }
                        }
                    }

                    line++;
                    start = i + 1;
                }
            }
        }

        Assert.That(
            hits,
            Is.Empty,
            "Retired input names must stay out of mods, src, gitbook, docs/architecture, docs/diagrams, docs/reference, and docs/conventions:\n" + string.Join("\n", hits));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj.");
    }
}
