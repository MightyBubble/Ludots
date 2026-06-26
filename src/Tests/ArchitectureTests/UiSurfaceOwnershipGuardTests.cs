using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Ludots.Tests.Architecture;

[TestFixture]
public sealed class UiSurfaceOwnershipGuardTests
{
    [Test]
    public void ProductionCode_DoesNotDirectlyMountOrClearUiRootScenes()
    {
        string repoRoot = FindRepoRoot();
        string[] directories =
        {
            Path.Combine(repoRoot, "mods"),
            Path.Combine(repoRoot, "src", "Adapters"),
            Path.Combine(repoRoot, "src", "Libraries", "Ludots.UI.HtmlEngine")
        };
        string[] forbidden =
        {
            ".MountScene(",
            ".ClearScene("
        };
        var hits = new List<string>();

        for (int i = 0; i < directories.Length; i++)
        {
            string directory = directories[i];
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    for (int tokenIndex = 0; tokenIndex < forbidden.Length; tokenIndex++)
                    {
                        string token = forbidden[tokenIndex];
                        if (line.Contains(token, StringComparison.Ordinal))
                        {
                            hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineIndex + 1}: {token}: {line.Trim()}");
                        }
                    }
                }
            }
        }

        Assert.That(
            hits,
            Is.Empty,
            "Production retained UI must publish through UiSurfaceHost leases. Direct UIRoot.Scene ownership is forbidden:\n" +
            string.Join("\n", hits));
    }

    [Test]
    public void UIRoot_DoesNotExposePublicSceneMutationMethods()
    {
        string repoRoot = FindRepoRoot();
        string source = File.ReadAllText(Path.Combine(repoRoot, "src", "Libraries", "Ludots.UI", "UIRoot.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("public void MountScene("));
            Assert.That(source, Does.Not.Contain("public void ClearScene("));
        });
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

    private static string ToRepoRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace('\\', '/');
    }
}
