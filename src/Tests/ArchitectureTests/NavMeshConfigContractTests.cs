using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Navigation.NavMesh.Config;
using NUnit.Framework;

namespace Ludots.Tests.Architecture
{
    [TestFixture]
    public sealed class NavMeshConfigContractTests
    {
        [Test]
        public void NavMeshBakeConfigPath_UsesRelativeConfigContract()
        {
            Assert.That(NavMeshConfigPaths.BakeConfigPath, Is.EqualTo("Navigation/navmesh.json"));
        }

        [Test]
        public void NavMeshBakeConfigLoader_LoadsThroughCoreConfigPipelineContract()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", assetsRoot);

            var pipeline = new ConfigPipeline(vfs, modLoader: null!);
            var config = new NavMeshBakeConfigLoader(pipeline).Load();

            Assert.That(config.Profiles, Is.Not.Null.And.Not.Empty);
            Assert.That(config.Layers, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void NavMeshBakeConfigLoader_LoadFromRepoRoot_UsesSameRelativeContract()
        {
            string repoRoot = FindRepoRoot();
            var config = NavMeshBakeConfigLoader.LoadFromRepoRoot(repoRoot);

            Assert.That(config.Profiles, Is.Not.Null.And.Not.Empty);
            Assert.That(config.Layers, Is.Not.Null.And.Not.Empty);
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
