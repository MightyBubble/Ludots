using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Ludots.Tests;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// 验收证据写路径合同：默认模式证据只落 NUnit 工作目录，ResetArtifactDirectory 只清理
/// 默认解析出的目录；验收中途抛错也不影响入库证据树。只有 LUDOTS_UPDATE_COMMITTED_TEST_ARTIFACTS=1
/// 时 Resolve 才指向入库树。
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("acceptance")]
[Category("ci-gate")]
public sealed class AcceptanceEvidenceArtifactPathContractTests
{
    private const string CommittedShowcaseDir = "artifacts/acceptance/gold-market-showcase";

    [Test]
    public void DefaultMode_ResetArtifactDirectory_CleansOnlyTestWorkDirectory()
    {
        using (WithUpdateSwitch(null))
        {
            string repoRoot = FindRepoRoot();
            string committedShowcaseRoot = Path.Combine(repoRoot, CommittedShowcaseDir.Replace('/', Path.DirectorySeparatorChar));
            string committedBefore = HashDirectory(committedShowcaseRoot);

            string artifactDir = CommittedTestArtifacts.Resolve(CommittedShowcaseDir);
            string screensDir = Path.Combine(artifactDir, "screens");
            AssertIsInsideDirectory(artifactDir, Path.GetFullPath(TestContext.CurrentContext.WorkDirectory));
            AssertIsOutsideDirectory(artifactDir, TrackedArtifactsRoot(repoRoot));

            Directory.CreateDirectory(screensDir);
            string stalePng = Path.Combine(screensDir, "000_stale_frame.png");
            string staleTrace = Path.Combine(artifactDir, "trace.jsonl");
            string keptFile = Path.Combine(artifactDir, "notes-not-in-reset-families.txt");
            File.WriteAllBytes(stalePng, MinimalPngBytes());
            File.WriteAllText(staleTrace, "stale");
            File.WriteAllText(keptFile, "reset 只清理自身声明的文件族");

            AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);

            Assert.That(File.Exists(stalePng), Is.False, "ResetArtifactDirectory 必须清掉解析目录里的旧截图。");
            Assert.That(File.Exists(staleTrace), Is.False, "ResetArtifactDirectory 必须清掉解析目录里的旧 trace。");
            Assert.That(File.Exists(keptFile), Is.True, "ResetArtifactDirectory 的清理范围只有 png 与四个固定证据文件。");
            Assert.That(HashDirectory(committedShowcaseRoot), Is.EqualTo(committedBefore), "入库证据树在默认模式下必须逐字节不变。");
        }
    }

    [Test]
    public void DefaultMode_FailingAcceptanceLeavesCommittedTreeUntouchedAndWorkDirectoryEvidenceIntact()
    {
        using (WithUpdateSwitch(null))
        {
            string repoRoot = FindRepoRoot();
            string committedShowcaseRoot = Path.Combine(repoRoot, CommittedShowcaseDir.Replace('/', Path.DirectorySeparatorChar));
            string committedBefore = HashDirectory(committedShowcaseRoot);

            string artifactDir = CommittedTestArtifacts.Resolve(CommittedShowcaseDir);
            string screensDir = Path.Combine(artifactDir, "screens");
            string capturedPng = Path.Combine(screensDir, "001_captured_step.png");
            string capturedTrace = Path.Combine(artifactDir, "trace.jsonl");
            string capturedReport = Path.Combine(artifactDir, "battle-report.md");

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            {
                AcceptanceUiEvidenceWriter.ResetArtifactDirectory(artifactDir, screensDir);
                File.WriteAllBytes(capturedPng, MinimalPngBytes());
                File.WriteAllText(capturedTrace, "{\"step\":1}");
                File.WriteAllText(capturedReport, "# captured before failure");
                throw new InvalidOperationException("simulated acceptance failure after evidence capture");
            });

            Assert.That(thrown.Message, Is.EqualTo("simulated acceptance failure after evidence capture"));
            Assert.That(HashDirectory(committedShowcaseRoot), Is.EqualTo(committedBefore), "验收失败不得牵连入库证据树。");
            Assert.That(File.Exists(capturedPng), Is.True, "失败前捕获的证据必须保留在测试工作目录。");
            Assert.That(File.Exists(capturedTrace), Is.True);
            Assert.That(File.Exists(capturedReport), Is.True);
        }
    }

    [Test]
    public void Resolve_RejectsRootedAndParentSegmentPaths()
    {
        using (WithUpdateSwitch(null))
        {
            Assert.That(
                () => CommittedTestArtifacts.Resolve(""),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => CommittedTestArtifacts.Resolve("   "),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => CommittedTestArtifacts.Resolve(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "C:/"),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => CommittedTestArtifacts.Resolve("../outside-repo"),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => CommittedTestArtifacts.Resolve("artifacts/../../outside-repo"),
                Throws.TypeOf<ArgumentException>());
        }
    }

    [Test]
    public void ExplicitUpdateSwitch_ResolvesIntoCommittedTree()
    {
        using (WithUpdateSwitch("1"))
        {
            string repoRoot = FindRepoRoot();
            string resolved = Path.GetFullPath(CommittedTestArtifacts.Resolve(CommittedShowcaseDir));
            string expected = Path.GetFullPath(Path.Combine(repoRoot, CommittedShowcaseDir.Replace('/', Path.DirectorySeparatorChar)));
            Assert.That(resolved, Is.EqualTo(expected));
            AssertIsInsideDirectory(resolved, TrackedArtifactsRoot(repoRoot));
        }
    }

    private static string TrackedArtifactsRoot(string repoRoot)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, "artifacts"));
    }

    private static void AssertIsInsideDirectory(string path, string directory)
    {
        string normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;
        Assert.That(
            path.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase),
            Is.True,
            $"{path} 必须落在 {normalizedDirectory} 内。");
    }

    private static void AssertIsOutsideDirectory(string path, string directory)
    {
        string normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;
        Assert.That(
            path.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase),
            Is.False,
            $"{path} 不得落入被跟踪的入库目录 {normalizedDirectory}。");
    }

    private static byte[] MinimalPngBytes()
    {
        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    }

    private static string HashDirectory(string root)
    {
        Assert.That(Directory.Exists(root), Is.True, $"缺少入库证据目录 {root}。");
        using var sha = SHA256.Create();
        var builder = new StringBuilder();
        List<string> files = new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        files.Sort(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            builder.Append(relative).Append('\n');
            builder.Append(Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(file)))).Append('\n');
        }

        return builder.ToString();
    }

    private static IDisposable WithUpdateSwitch(string? value)
    {
        string? original = Environment.GetEnvironmentVariable(CommittedTestArtifacts.UpdateSwitch);
        Environment.SetEnvironmentVariable(CommittedTestArtifacts.UpdateSwitch, value);
        return new RestoreSwitch(original);
    }

    private sealed class RestoreSwitch(string? original) : IDisposable
    {
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(CommittedTestArtifacts.UpdateSwitch, original);
        }
    }

    private static string FindRepoRoot()
    {
        string? current = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "launcher.config.json")) &&
                Directory.Exists(Path.Combine(current, "mods")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Ludots repo root.");
    }
}
