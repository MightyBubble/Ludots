using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class JumpLinkBakeRuntimeIsolationTests
{
    [Test]
    public void CrowdSimulationRuntime_DoesNotReadJumpLinkBake()
    {
        string repo = FindRepoRoot();
        string core = Path.Combine(repo, "src", "Core");
        var hits = new List<string>();
        foreach (string file in Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name is "NavMeshBakeConfig.cs" or "NavMeshBakeConfigLoader.cs")
            {
                continue;
            }

            string text = File.ReadAllText(file);
            if (text.Contains("jumpLinkBake", StringComparison.Ordinal) ||
                text.Contains("JumpLinkBake", StringComparison.Ordinal))
            {
                hits.Add(Path.GetRelativePath(repo, file));
            }
        }

        Assert.That(hits, Is.Empty, "运行时代码读了 jumpLinkBake：\n" + string.Join("\n", hits));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
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
