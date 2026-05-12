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

        [Test]
        public void Mount_RejectsMissingContractData()
        {
            var vfs = new VirtualFileSystem();

            Assert.That(() => vfs.Mount("", CreateTempDir()), Throws.TypeOf<ArgumentException>());
            Assert.That(() => vfs.Mount("ModA", ""), Throws.TypeOf<ArgumentException>());
            Assert.That(() => vfs.Mount("ModA", Path.Combine(Path.GetTempPath(), "ludots_missing_" + Guid.NewGuid().ToString("N"))), Throws.TypeOf<DirectoryNotFoundException>());
        }

        [Test]
        public void Mount_UsesCaseExactModIds()
        {
            var root = CreateTempDir();
            try
            {
                File.WriteAllText(Path.Combine(root, "inside.txt"), "ok");

                var vfs = new VirtualFileSystem();
                vfs.Mount("ModA", root);

                Assert.That(vfs.TryResolveFullPath("ModA:inside.txt", out _), Is.True);
                Assert.That(vfs.TryResolveFullPath("moda:inside.txt", out _), Is.False);
            }
            finally
            {
                TryDelete(root);
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
