using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    /// <summary>
    /// The shared collection name is retired. Mods and engine code author the
    /// <c>commandSource</c> role, and <c>Input/collection_keys.json</c> binds it.
    /// </summary>
    [TestFixture]
    public sealed class CommandSourceCollectionKeyGuardTests
    {
        [Test]
        public void ModsAndSrc_DoNotContainRetiredCommandSourceCollectionLiteral()
        {
            string repoRoot = FindRepoRoot();
            string needle = string.Concat("collection.command", ".source");
            byte[] pattern = Encoding.ASCII.GetBytes(needle);
            var offenders = new List<string>();
            foreach (string rootName in new[] { "mods", "src" })
            {
                string root = Path.Combine(repoRoot, rootName);
                Assert.That(Directory.Exists(root), Is.True, $"{root} missing");
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    string normalized = file.Replace('\\', '/');
                    if (normalized.Contains("/bin/", StringComparison.Ordinal) ||
                        normalized.Contains("/obj/", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    byte[] bytes = File.ReadAllBytes(file);
                    if (IndexOf(bytes, pattern) >= 0)
                    {
                        offenders.Add(Path.GetRelativePath(repoRoot, file));
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "The retired shared collection key must not appear under mods/ or src/. Offenders:\n" +
                string.Join("\n", offenders));
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length)
            {
                return -1;
            }

            int last = haystack.Length - needle.Length;
            for (int i = 0; i <= last; i++)
            {
                int j = 0;
                for (; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        break;
                    }
                }

                if (j == needle.Length)
                {
                    return i;
                }
            }

            return -1;
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

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }
    }
}
