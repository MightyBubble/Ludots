using System.Linq;
using Ludots.App.RaylibEngineGallery;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter
{
    [Category("raylib-field")]
    public sealed class RaylibEngineGalleryTests
    {
        private static readonly string[] CanonicalSceneIds =
        {
            "skybox",
            "sky_daynight",
            "water",
            "terrain_surface",
            "terrain_heightmap",
            "atmosphere_fog",
            "frame_lighting",
            "postprocess",
            "gpu_skinning",
            "instancing",
            "particles",
            "decal_projection",
            "vegetation_cutout",
            "material_binding",
            "ribbon_overlay",
            "skia_overlay",
            "debug_draw",
            "primitives",
            "lighting",
            "crowd_anim",
        };

        [Test]
        public void SceneCatalog_CoversAllEngineCapabilities_ExactlyOnce()
        {
            Assert.That(SceneCatalog.Ids, Is.EquivalentTo(CanonicalSceneIds));
            Assert.That(SceneCatalog.Ids.Distinct().Count(), Is.EqualTo(SceneCatalog.Ids.Count));
        }

        [Test]
        public void SceneCatalog_DescriptorsAreReadable()
        {
            var descriptors = SceneCatalog.Descriptors;
            Assert.That(descriptors.Count, Is.EqualTo(CanonicalSceneIds.Length));
            foreach (var descriptor in descriptors)
            {
                Assert.That(descriptor.Id, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Title, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Summary, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void SceneCatalog_FactoryConstructsEveryScene()
        {
            foreach (string id in CanonicalSceneIds)
            {
                IEngineScene scene = SceneCatalog.Create(id);
                Assert.That(scene, Is.Not.Null, id);
                Assert.That(scene.Id, Is.EqualTo(id));
                scene.Dispose();
            }
        }
    }
}
