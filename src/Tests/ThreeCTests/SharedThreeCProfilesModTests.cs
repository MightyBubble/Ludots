using System;
using System.IO;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class SharedThreeCProfilesModTests
    {
        [Test]
        public void SharedThreeCProfilesMod_LoadsSharedRtsMobaProfileIntoVirtualCameraRegistry()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, new[]
            {
                "LudotsCoreMod",
                "SharedThreeCProfilesMod"
            });

            using var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);

            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry);
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry!.TryGet("Shared3C.Profile.RtsMoba", out VirtualCameraDefinition definition), Is.True);
            Assert.That(definition.Id, Is.EqualTo("Shared3C.Profile.RtsMoba"));
            Assert.That(definition.RigKind, Is.EqualTo(CameraRigKind.Orbit));
            Assert.That(definition.PanMode, Is.EqualTo(CameraPanMode.KeyboardAndEdge));
            Assert.That(definition.EnableGrabDrag, Is.True);
            Assert.That(definition.ConfineTargetToWorldBounds, Is.True);
            Assert.That(definition.AllowUserInput, Is.True);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                var srcDir = Path.Combine(dir.FullName, "src");
                var assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }
    }
}
