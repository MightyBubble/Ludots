using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Scripting;
using NUnit.Framework;
using VirtualCameraBaselineMod;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class VirtualCameraBaselineModTests
    {
        [Test]
        public void VirtualCameraBaselineMod_EntryMap_ActivatesIntroFocusShot()
        {
            using var engine = CreateEngine();

            Tick(engine, frames: 5);

            var registry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry);
            Assert.That(registry, Is.Not.Null, "VirtualCameraRegistry should be available from GameEngine services.");
            Assert.That(registry!.TryGet(VirtualCameraBaselineIds.IntroFocusCameraId, out var definition), Is.True);
            Assert.That(definition.Id, Is.EqualTo(VirtualCameraBaselineIds.IntroFocusCameraId));

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.HasActiveCamera, Is.True);
            Assert.That(brain.ActiveCameraId, Is.EqualTo(VirtualCameraBaselineIds.IntroFocusCameraId));
            Assert.That(engine.GameSession.Camera.ActivePreset?.Id, Is.EqualTo("TPS"));
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.TopDown));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(6400f, 3200f)));
        }

        [Test]
        public void VirtualCameraBaselineMod_ClearRequest_RestoresMapDefaultFollow()
        {
            using var engine = CreateEngine();

            Tick(engine, frames: 5);
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Clear = true
            });
            Tick(engine, frames: 5);

            var brain = engine.GameSession.Camera.VirtualCameraBrain;
            Assert.That(brain, Is.Not.Null);
            Assert.That(brain!.HasActiveCamera, Is.False);
            Assert.That(engine.GameSession.Camera.ActivePreset?.Id, Is.EqualTo("TPS"));
            Assert.That(engine.GameSession.Camera.State.RigKind, Is.EqualTo(CameraRigKind.ThirdPerson));
            Assert.That(engine.GameSession.Camera.State.TargetCm, Is.EqualTo(new Vector2(1200f, 800f)));
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.EqualTo(new Vector2(1200f, 800f)));
            Assert.That(engine.GameSession.Camera.State.IsFollowing, Is.True);

            Entity hero = FindEntityByName(engine.World, "BaselineHero");
            Assert.That(hero, Is.Not.EqualTo(Entity.Null));
            Assert.That(engine.World.Get<WorldPositionCm>(hero).Value.ToVector2(), Is.EqualTo(new Vector2(1200f, 800f)));
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "mods");

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                new()
                {
                    Path.Combine(modsRoot, "LudotsCoreMod"),
                    Path.Combine(modsRoot, "VirtualCameraBaselineMod")
                },
                assetsRoot);

            engine.Start();
            engine.LoadMap(engine.MergedConfig.StartupMapId);
            return engine;
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
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
