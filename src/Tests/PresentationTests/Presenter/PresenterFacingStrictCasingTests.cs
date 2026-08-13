using System.IO;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Presenters;
using NUnit.Framework;
using PresenterBlacksmithShowcaseMod;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterFacingStrictCasingTests
    {
        [SetUp]
        public void SetUp()
        {
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
        }

        [Test]
        public void PresenterParamKeyRegistry_ResolvesOnlyExactCasing()
        {
            Assert.That(PresenterParamKeyRegistry.TryGetId("worldText.tokenId", out int wellKnownId), Is.True);
            Assert.That(wellKnownId, Is.EqualTo(WellKnownPresenterParamKeys.TextTokenId));
            Assert.That(PresenterParamKeyRegistry.TryGetId("WorldText.tokenId", out _), Is.False);

            int customId = PresenterParamKeyRegistry.Register("semantic.health.ratio");

            Assert.That(PresenterParamKeyRegistry.TryGetId("semantic.health.ratio", out int resolvedCustomId), Is.True);
            Assert.That(resolvedCustomId, Is.EqualTo(customId));
            Assert.That(PresenterParamKeyRegistry.TryGetId("Semantic.Health.Ratio", out _), Is.False);
        }

        [Test]
        public void PresenterParamKeyRegistry_RejectsLeadingAndTrailingWhitespace()
        {
            Assert.That(
                () => PresenterParamKeyRegistry.Register(" semantic.health.ratio "),
                Throws.ArgumentException.With.Message.Contains("leading or trailing whitespace"));
            Assert.That(PresenterParamKeyRegistry.TryGetId(" semantic.health.ratio ", out _), Is.False);
        }

        [Test]
        public void PresenterScopeTagRegistry_ResolvesOnlyExactCasing()
        {
            int workingId = PresenterScopeTagRegistry.Register("working");

            Assert.That(PresenterScopeTagRegistry.GetId("working"), Is.EqualTo(workingId));
            Assert.That(PresenterScopeTagRegistry.GetId("Working"), Is.EqualTo(0));
            Assert.That(
                () => PresenterScopeTagRegistry.Register(" working "),
                Throws.ArgumentException.With.Message.Contains("leading or trailing whitespace"));
        }

        [Test]
        public void PresentationAssetRegistries_ResolveOnlyExactCasing()
        {
            var definitions = new PresenterDefinitionRegistry();
            int definitionId = definitions.Register("selectionMarker", new PresenterDefinition { Key = "selectionMarker" });

            Assert.That(definitions.GetId("selectionMarker"), Is.EqualTo(definitionId));
            Assert.That(definitions.GetId("SelectionMarker"), Is.EqualTo(0));

            var behaviors = new PresentationBehaviorRegistry();
            int behaviorId = behaviors.Register("worldTextBehavior", default);

            Assert.That(behaviors.GetId("worldTextBehavior"), Is.EqualTo(behaviorId));
            Assert.That(behaviors.GetId("WorldTextBehavior"), Is.EqualTo(0));

            var materials = new PresentationMaterialRegistry();
            int defaultMaterialId = materials.GetId(PresentationMaterialRegistry.DefaultSurfaceKey);
            int workerMaterialId = materials.Register(
                "workerMetal",
                MaterialAssetDomain.Surface,
                new[] { "materials/workerMetal.mat" },
                MaterialAssetFlags.None);

            Assert.That(defaultMaterialId, Is.GreaterThan(0));
            Assert.That(materials.GetId(PresentationMaterialRegistry.DefaultSurfaceKey), Is.EqualTo(defaultMaterialId));
            Assert.That(materials.GetId("DEFAULT_SURFACE"), Is.EqualTo(0));
            Assert.That(materials.GetId("workerMetal"), Is.EqualTo(workerMaterialId));
            Assert.That(materials.GetId("WorkerMetal"), Is.EqualTo(0));
        }

        [Test]
        public void BlacksmithShowcaseMapChecks_ResolveOnlyExactCasing()
        {
            Assert.That(PresenterBlacksmithShowcaseIds.IsShowcaseMap(PresenterBlacksmithShowcaseIds.ShowcaseMapId), Is.True);
            Assert.That(PresenterBlacksmithShowcaseIds.IsShowcaseMap("PRESENTER_BLACKSMITH_SHOWCASE"), Is.False);

            Assert.That(
                PresenterBlacksmithShowcaseIds.IsDynamicWorkerBenchmarkMap(PresenterBlacksmithShowcaseIds.DynamicWorkerLargeWorldBenchmarkMapId),
                Is.True);
            Assert.That(
                PresenterBlacksmithShowcaseIds.IsDynamicWorkerBenchmarkMap("PRESENTER_BLACKSMITH_DYNAMIC_WORKER_LARGE_WORLD_BENCHMARK"),
                Is.False);

            Assert.That(
                PresenterBlacksmithShowcaseIds.IsMinimapMarkerLargeWorldShowcaseMap(PresenterBlacksmithShowcaseIds.MinimapMarkerLargeWorldShowcaseMapId),
                Is.True);
            Assert.That(
                PresenterBlacksmithShowcaseIds.IsMinimapMarkerLargeWorldShowcaseMap("PRESENTER_BLACKSMITH_MINIMAP_MARKER_LARGE_WORLD_SHOWCASE"),
                Is.False);
        }

        [Test]
        public void CorePresentationConfigLoaders_DoNotUseCaseInsensitiveEnumParsing()
        {
            string configRoot = Path.Combine(FindRepoRoot(), "src", "Core", "Presentation", "Config");
            string[] files = Directory.GetFiles(configRoot, "*.cs", SearchOption.TopDirectoryOnly);

            for (int i = 0; i < files.Length; i++)
            {
                string source = File.ReadAllText(files[i]);
                Assert.That(
                    source,
                    Does.Not.Contain("ignoreCase: true"),
                    $"Presentation config loader must keep strict enum casing: {files[i]}");
            }
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Ludots.sln")) || Directory.Exists(Path.Combine(dir, "mods")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate Ludots repository root.");
        }
    }
}
