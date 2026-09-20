using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class CorePresentationSkiaPurityContractTests
    {
        private static readonly Regex SkTypeToken = new(@"\bSK[A-Z]", RegexOptions.Compiled);

        [Test]
        public void Core_Sources_And_ProjectFile_Contain_No_Skia_References()
        {
            string repoRoot = FindRepoRoot();
            var hits = new List<string>();
            foreach (string file in EnumerateCoreFiles(repoRoot))
            {
                bool isCSharp = Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase);
                foreach ((int lineNumber, string line) in SourceTextScanner.ReadCodeLines(file))
                {
                    if (line.Contains("SkiaSharp", StringComparison.Ordinal) ||
                        line.Contains("Ludots.Presentation.Skia", StringComparison.Ordinal))
                    {
                        hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineNumber}: {line.Trim()}");
                    }
                    else if (isCSharp && SkTypeToken.IsMatch(line))
                    {
                        hits.Add($"{ToRepoRelativePath(repoRoot, file)}:{lineNumber}: {line.Trim()}");
                    }
                }
            }

            Assert.That(
                hits,
                Is.Empty,
                "Core 呈现合同必须零 Skia 引用：GL 纹理句柄/图集布局/GPU 细节只能住在渲染后端库内部，" +
                "Core 一旦引入 Skia 类型或包引用，Unity/Unreal 等其它后端即被破坏：" +
                Environment.NewLine +
                string.Join(Environment.NewLine, hits));
        }

        private static IEnumerable<string> EnumerateCoreFiles(string repoRoot)
        {
            string coreRoot = Path.Combine(repoRoot, "src", "Core");
            foreach (string file in Directory.EnumerateFiles(coreRoot, "*", SearchOption.AllDirectories))
            {
                string relative = ToRepoRelativePath(repoRoot, file);
                string[] segments = relative.Split('/');
                bool excluded = false;
                for (int i = 0; i < segments.Length; i++)
                {
                    if (segments[i].Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                        segments[i].Equals("obj", StringComparison.OrdinalIgnoreCase))
                    {
                        excluded = true;
                        break;
                    }
                }

                if (excluded)
                {
                    continue;
                }

                string extension = Path.GetExtension(file);
                if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }

        private static string ToRepoRelativePath(string repoRoot, string file)
        {
            return file.Substring(repoRoot.Length + 1).Replace('\\', '/');
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                string candidate = Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found from test base directory.");
        }
    }
}
