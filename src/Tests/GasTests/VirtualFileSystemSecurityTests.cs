using System;
using System.IO;
using NUnit.Framework;
using Ludots.Core.Modding;

namespace GasTests
{
    [TestFixture]
    public class VirtualFileSystemSecurityTests
    {
        [Test]
        public void TryResolveFullPath_WhenPathEscapesMount_ReturnsFalse()
        {
            var root = CreateTempDir();
            var outside = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(root, "inside.txt"), "ok");
                File.WriteAllText(Path.Combine(outside, "outside.txt"), "x");

                var vfs = new VirtualFileSystem();
                vfs.Mount("ModA", root);

                Assert.That(vfs.TryResolveFullPath("ModA:inside.txt", out var inPath), Is.True);
                Assert.That(inPath, Does.EndWith("inside.txt"));

                Assert.That(vfs.TryResolveFullPath("ModA:../outside.txt", out _), Is.False);
                Assert.That(() => vfs.GetStream("ModA:../outside.txt"), Throws.TypeOf<UnauthorizedAccessException>());
            }
            finally
            {
                TryDelete(root);
                TryDelete(outside);
            }
        }

        private static string CreateTempDir()
        {
            var path = Path.Combine(Path.GetTempPath(), "ludots_vfs_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
