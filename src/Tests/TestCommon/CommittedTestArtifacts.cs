using System;
using System.IO;
using NUnit.Framework;

namespace Ludots.Tests;

/// <summary>
/// 测试产物的写路径。默认写到 NUnit 工作目录；只有环境变量
/// <c>LUDOTS_UPDATE_COMMITTED_TEST_ARTIFACTS=1</c> 时才按仓库相对路径写入库树。
/// </summary>
internal static class CommittedTestArtifacts
{
    public const string UpdateSwitch = "LUDOTS_UPDATE_COMMITTED_TEST_ARTIFACTS";

    public static bool UpdatesRepository =>
        string.Equals(Environment.GetEnvironmentVariable(UpdateSwitch), "1", StringComparison.Ordinal);

    public static string Resolve(string repositoryRelativePath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRelativePath))
        {
            throw new ArgumentException("Repository-relative artifact path is required.", nameof(repositoryRelativePath));
        }

        string relative = repositoryRelativePath.Replace('\\', '/').TrimStart('/');
        if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(repositoryRelativePath))
        {
            throw new ArgumentException(
                "Artifact path must be a repository-relative path without parent segments.",
                nameof(repositoryRelativePath));
        }

        string root = UpdatesRepository
            ? FindRepoRoot()
            : TestContext.CurrentContext.WorkDirectory;
        return Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "showcase.registry.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from the test host.");
    }
}
