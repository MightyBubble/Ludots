using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
[Category("ci-gate")]
public sealed class CorePointerVocabularyGuardTests
{
    private static readonly string[] Forbidden =
    {
        "MinimapInputConsumer",
        "PointerInputCaptured",
        "InteractionActionBindings",
        "interactionActions",
        "GraphAimSourceRuntime",
        "IGraphAimSourceRuntime",
        "BindAimSource",
    };

    [Test]
    public void Core_DoesNotContainRetiredPointerNames()
    {
        string repoRoot = FindRepoRoot();
        string core = Path.Combine(repoRoot, "src", "Core");
        var hits = new List<string>();
        foreach (string file in Directory.EnumerateFiles(core, "*", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.Ordinal) ||
                normalized.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

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

        Assert.That(hits, Is.Empty, "Retired pointer names must not remain in Core:\n" + string.Join("\n", hits));
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

        throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
    }
}
