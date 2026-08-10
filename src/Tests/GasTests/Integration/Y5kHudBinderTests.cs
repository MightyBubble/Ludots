using System.IO;
using Arch.Core;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Providers;
using Ludots.Core.Gameplay.Tasks;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;
using Ludots.WebUI.PanelKit;
using NUnit.Framework;
using UiRegionsMod.Runtime;

namespace Ludots.Tests.GAS.Integration
{
    [TestFixture]
    public sealed class Y5kHudBinderTests
    {
        [Test]
        public void Y5kHudManifest_LoadsAndBinds_AllDeclaredPanels()
        {
            string manifestPath = Path.Combine(
                FindRepoRoot(),
                "mods/showcases/y5k_grand_strategy/Y5kGrandStrategyMod/assets/PanelKit/y5k_hud_manifest.json");
            Assert.That(File.Exists(manifestPath), Is.True);

            using World world = World.Create();
            var providers = new ProviderServices(registerDefaultGaps: false, allowTestDomainOverride: true);
            var tasks = new TaskRuntimeService(world, new TaskDefinitionRegistry(), providers, new TaskPresentationBuffer());
            var activityPresentation = new ActivityPresentationBuffer();
            var activities = new ActivityRuntimeService(
                world,
                new ActivityDefinitionRegistry(),
                providers,
                activityPresentation);

            using var dataPlane = new WebUiDataPlaneRuntime();
            dataPlane.RegisterTopic(new TaskObjectiveTopicProducer("y5k.topic.objective", tasks));
            dataPlane.RegisterTopic(new ActivityModalTopicProducer("y5k.topic.activity", activities, activityPresentation));
            foreach (string topic in new[]
                     {
                         "y5k.topic.time",
                         "y5k.topic.filter",
                         "y5k.topic.notification",
                         "y5k.topic.minimap",
                         "y5k.topic.entity-insight",
                         "y5k.topic.production",
                         "y5k.topic.entity-list",
                         "y5k.topic.command",
                     })
            {
                dataPlane.RegisterTopic(new StaticHudTopicProducer(topic, "panel", () => new { ok = true }));
            }

            WebUiPanelKitReferenceCatalog catalog = UiRegionsCatalogFactory.Create(dataPlane.IsTopicRegistered);
            WebUiPanelKitManifest manifest = WebUiPanelKitManifestLoader.LoadFromFile(manifestPath, catalog);
            Assert.That(manifest.Panels, Has.Count.EqualTo(10));

            UIRoot root = CreateRoot(out UiSurfaceHost host);
            using var binder = new WebUiPanelKitSurfaceBinder(host, manifest);
            binder.Bind();

            Assert.That(binder.BoundPanelIds, Has.Count.EqualTo(10));
            Assert.That(binder.BrowserSubscriptionTopics, Does.Contain("y5k.topic.minimap"));
            Assert.That(root.Scene!.FindByElementId("panel-kit-y5k.hud.minimap"), Is.Not.Null);
            Assert.That(root.Scene.FindByElementId("panel-kit-y5k.hud.command-deck"), Is.Not.Null);
            Assert.That(root.Scene.FindByElementId("panel-kit-y5k.hud.activity-modal"), Is.Not.Null);
            Assert.That(root.Scene.FindByElementId("panel-kit-y5k.hud.entity-info"), Is.Not.Null);
        }

        private static UIRoot CreateRoot(out UiSurfaceHost host)
        {
            var root = new UIRoot(new NullUiRenderer());
            host = new UiSurfaceHost(root, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
            return root;
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("repo root not found");
        }

        private sealed class NullUiRenderer : IUiRenderer
        {
            public void Render(UiScene scene, float width, float height)
            {
            }
        }
    }
}
