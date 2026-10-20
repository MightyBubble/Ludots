using NUnit.Framework;

namespace Ludots.Tests.Architecture.Governance;

[Category("arch-guard")]
public sealed class AimSyncRetirementArchitectureGuardTests
{
    private static readonly string[] Roots = ["src", "mods", "assets"];

    private static readonly string[] Forbidden =
    {
        "AbilityExec" + "AimSync",
        "InputInteraction" + "ContextAccessor",
        "core_input." + "ability_aim",
    };

    [Test]
    public void RetiredAimSyncAndCoreAimPresenters_AreAbsent()
    {
        string repoRoot = FindRepoRoot();
        var hits = new List<string>();
        foreach (string root in Roots)
        {
            string directory = Path.Combine(repoRoot, root);
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (IsSkipped(file))
                {
                    continue;
                }

                string text = File.ReadAllText(file);
                foreach (string token in Forbidden)
                {
                    int index = text.IndexOf(token, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        continue;
                    }

                    int line = 1;
                    for (int i = 0; i < index; i++)
                    {
                        if (text[i] == '\n')
                        {
                            line++;
                        }
                    }

                    hits.Add($"{Path.GetRelativePath(repoRoot, file)}:{line}: {token}");
                }
            }
        }

        Assert.That(hits, Is.Empty, "Retired aim sync and core aim presenters must not remain:\n" + string.Join("\n", hits));
    }

    private static bool IsSkipped(string file)
    {
        string normalized = file.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal) ||
               normalized.Contains("/obj/", StringComparison.Ordinal);
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
