using NUnit.Framework;

namespace Ludots.Tests.Architecture;

/// <summary>
/// 链接候选烘焙参数只给烘焙器和进图核对用：共享导航装载器收下它，群体装载器推导掩码，
/// NavSurfaceContract 把它算进 bakeInputsDigest 比对资产。建链接时按每行的 depart / arrive 过滤，不读烘焙掩码。
/// </summary>
[TestFixture]
public sealed class LinkCandidateBakeRuntimeIsolationTests
{
    private static readonly HashSet<string> AllowedFiles = new(StringComparer.Ordinal)
    {
        "src/Core/Navigation/NavMesh/Config/NavMeshBakeConfig.cs",
        "src/Core/Navigation/NavMesh/Config/NavMeshBakeConfigLoader.cs",
        "src/Core/Navigation/NavMesh/Config/NavSolverDeclaration.cs",
        "src/Core/CrowdSimulation/Config/CrowdSimulationConfigLoader.cs",
        "src/Core/CrowdSimulation/Config/CrowdSimulationRuntimeConfig.cs",
        "src/Core/CrowdSimulation/World/NavSurfaceContract.cs",
    };

    [Test]
    public void OnlyLoadersAndDigestCheck_ReadLinkCandidateBake()
    {
        string repo = FindRepoRoot();
        var hits = new List<string>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(repo, "src", "Core"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(repo, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            if (text.Contains("LinkCandidateBake", StringComparison.OrdinalIgnoreCase) && !AllowedFiles.Contains(relative))
            {
                hits.Add(relative);
            }

            if (text.Contains("jumpLinkBake", StringComparison.OrdinalIgnoreCase)
                && relative != "src/Core/Navigation/NavMesh/Config/NavMeshBakeConfigLoader.cs")
            {
                hits.Add(relative + "（旧键 jumpLinkBake 只允许出现在共享装载器的删除报错里）");
            }
        }

        Assert.That(hits, Is.Empty, "这些文件读了链接候选烘焙参数：\n" + string.Join("\n", hits));
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
