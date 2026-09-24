using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Modding;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class EmitterAssetConfigLoaderTests
    {
        private const int TestRuntimeFormatVersion = 1810;
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_EmitterAssetConfig", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Configs", "Presentation"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public void Load_RegistersEveryConcreteEmitterKind()
        {
            WriteEmitters(
                $$"""
                [
                  { "id": "cast.spark", "assetKind": "SpriteEmitter", "runtimeFormatVersion": {{TestRuntimeFormatVersion}}, "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
                  { "id": "slash.trail", "assetKind": "RibbonEmitter", "runtimeFormatVersion": {{TestRuntimeFormatVersion}}, "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
                  { "id": "debris.chunk", "assetKind": "ModelEmitter", "runtimeFormatVersion": {{TestRuntimeFormatVersion}}, "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc" },
                  { "id": "beam.core", "assetKind": "TrackEmitter", "runtimeFormatVersion": {{TestRuntimeFormatVersion}}, "sha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd" },
                  { "id": "portal.ring", "assetKind": "RingEmitter", "runtimeFormatVersion": {{TestRuntimeFormatVersion}}, "sha256": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" }
                ]
                """);

            var emitters = new EmitterAssetRegistry();
            new EmitterAssetConfigLoader(BuildPipeline(), emitters).Load(BuildCatalog());

            AssertDescriptor(emitters, "cast.spark", AssetKind.SpriteEmitter);
            AssertDescriptor(emitters, "slash.trail", AssetKind.RibbonEmitter);
            AssertDescriptor(emitters, "debris.chunk", AssetKind.ModelEmitter);
            AssertDescriptor(emitters, "beam.core", AssetKind.TrackEmitter);
            AssertDescriptor(emitters, "portal.ring", AssetKind.RingEmitter);
            Assert.That(emitters.Count, Is.EqualTo(5));
        }

        [Test]
        public void Load_WhenRequiredCatalogIsEmpty_LeavesRegistryEmpty()
        {
            WriteEmitters("[]");

            var emitters = new EmitterAssetRegistry();
            new EmitterAssetConfigLoader(BuildPipeline(), emitters).Load(BuildCatalog());

            Assert.That(emitters.Count, Is.Zero);
        }

        [Test]
        public void Load_NormalizesUppercaseSha256ToLowercase()
        {
            WriteEmitters(
                $$"""
                [
                  {
                    "id": "beam.core",
                    "assetKind": "TrackEmitter",
                    "runtimeFormatVersion": {{TestRuntimeFormatVersion}},
                    "sha256": "ABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCDEFABCD"
                  }
                ]
                """);

            var emitters = new EmitterAssetRegistry();
            new EmitterAssetConfigLoader(BuildPipeline(), emitters).Load(BuildCatalog());

            Assert.That(emitters.TryGetDescriptor(emitters.GetId("beam.core"), out EmitterAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.Sha256, Is.EqualTo("abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd"));
            Assert.That(descriptor.RuntimeFormatVersion, Is.EqualTo(TestRuntimeFormatVersion));
        }

        [TestCase("VFX")]
        [TestCase("ParticleEmitter")]
        [TestCase("RibbonTrail")]
        [TestCase("Ring")]
        [TestCase("Mesh")]
        [TestCase("13")]
        public void Load_RejectsAnythingExceptConcreteEmitterKinds(string invalidKind)
        {
            WriteEmitters($$"""
                [ { "id": "invalid.asset", "assetKind": "{{invalidKind}}" } ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new EmitterAssetConfigLoader(BuildPipeline(), new EmitterAssetRegistry()).Load(BuildCatalog()))!;

            Assert.That(ex.Message, Does.Contain("invalid concrete emitter assetKind"));
            Assert.That(ex.Message, Does.Contain(invalidKind));
        }

        [Test]
        public void Load_RejectsRuntimeSourceFieldsFromSemanticCatalog()
        {
            WriteEmitters(
                $$"""
                [
                  {
                    "id": "beam.core",
                    "assetKind": "TrackEmitter",
                    "runtimeFormatVersion": {{TestRuntimeFormatVersion}},
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    "sourceUris": [ "Mod:assets/Presentation/beam.efkefc" ]
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new EmitterAssetConfigLoader(BuildPipeline(), new EmitterAssetRegistry()).Load(BuildCatalog()))!;

            Assert.That(ex.Message, Does.Contain("unsupported field 'sourceUris'"));
        }

        [TestCase(1800)]
        [TestCase(1806)]
        public void Load_PreservesPositiveRuntimeFormatForAdapterCompatibilityCheck(int runtimeFormatVersion)
        {
            WriteEmitters($$"""
                [
                  {
                    "id": "beam.core",
                    "assetKind": "TrackEmitter",
                    "runtimeFormatVersion": {{runtimeFormatVersion}},
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                  }
                ]
                """);

            var emitters = new EmitterAssetRegistry();
            new EmitterAssetConfigLoader(BuildPipeline(), emitters).Load(BuildCatalog());

            Assert.That(emitters.TryGetDescriptor(emitters.GetId("beam.core"), out EmitterAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.RuntimeFormatVersion, Is.EqualTo(runtimeFormatVersion));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void Load_RejectsNonPositiveRuntimeFormatVersion(int runtimeFormatVersion)
        {
            WriteEmitters($$"""
                [
                  {
                    "id": "beam.core",
                    "assetKind": "TrackEmitter",
                    "runtimeFormatVersion": {{runtimeFormatVersion}},
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new EmitterAssetConfigLoader(BuildPipeline(), new EmitterAssetRegistry()).Load(BuildCatalog()))!;

            Assert.That(ex.Message, Does.Contain("positive integer"));
        }

        [TestCase("")]
        [TestCase("abc")]
        [TestCase("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
        public void Load_RejectsMissingOrMalformedSha256(string sha256)
        {
            WriteEmitters($$"""
                [
                  {
                    "id": "beam.core",
                    "assetKind": "TrackEmitter",
                    "runtimeFormatVersion": {{TestRuntimeFormatVersion}},
                    "sha256": "{{sha256}}"
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new EmitterAssetConfigLoader(BuildPipeline(), new EmitterAssetRegistry()).Load(BuildCatalog()))!;

            Assert.That(ex.Message, Does.Contain("sha256"));
        }

        [Test]
        public void Load_RejectsMissingSha256Field()
        {
            WriteEmitters(
                $$"""
                [
                  {
                    "id": "beam.core",
                    "assetKind": "TrackEmitter",
                    "runtimeFormatVersion": {{TestRuntimeFormatVersion}}
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new EmitterAssetConfigLoader(BuildPipeline(), new EmitterAssetRegistry()).Load(BuildCatalog()))!;

            Assert.That(ex.Message, Does.Contain("field 'sha256' must be a non-empty string"));
        }

        [Test]
        public void Load_RejectsMissingRuntimeFormatVersionField()
        {
            WriteEmitters(
                """
                [
                  {
                    "id": "beam.core",
                    "assetKind": "TrackEmitter",
                    "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new EmitterAssetConfigLoader(BuildPipeline(), new EmitterAssetRegistry()).Load(BuildCatalog()))!;

            Assert.That(ex.Message, Does.Contain("field 'runtimeFormatVersion' must be a positive integer"));
        }

        [Test]
        public void Load_RequiresEmitterCatalogEntry()
        {
            WriteEmitters("[]");

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                new EmitterAssetConfigLoader(BuildPipeline(), new EmitterAssetRegistry()).Load(new ConfigCatalog()))!;

            Assert.That(ex.Message, Does.Contain(EmitterAssetConfigLoader.DefaultRelativePath));
        }

        [Test]
        public void ResolveId_RejectsKindMismatchAndUnknownAsset()
        {
            var emitters = new EmitterAssetRegistry();
            int id = emitters.Register(
                "beam.core",
                AssetKind.TrackEmitter,
                TestRuntimeFormatVersion,
                new string('a', 64));

            Assert.That(emitters.ResolveId(AssetKind.TrackEmitter, "beam.core"), Is.EqualTo(id));
            Assert.That(
                () => emitters.ResolveId(AssetKind.RibbonEmitter, "beam.core"),
                Throws.InvalidOperationException.With.Message.Contains("not requested kind 'RibbonEmitter'"));
            Assert.That(
                () => emitters.ResolveId(AssetKind.TrackEmitter, "beam.missing"),
                Throws.InvalidOperationException.With.Message.Contains("Unknown emitter asset 'beam.missing'"));
        }

        private ConfigPipeline BuildPipeline()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", _root);
            return new ConfigPipeline(vfs, modLoader: null!);
        }

        private static ConfigCatalog BuildCatalog()
        {
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry(
                EmitterAssetConfigLoader.DefaultRelativePath,
                ConfigMergePolicy.ArrayById,
                "id"));
            return catalog;
        }

        private void WriteEmitters(string json)
        {
            File.WriteAllText(
                Path.Combine(_root, "Configs", "Presentation", "emitter_assets.json"),
                json);
        }

        private static void AssertDescriptor(
            EmitterAssetRegistry emitters,
            string key,
            AssetKind expectedKind)
        {
            int id = emitters.GetId(key);
            Assert.That(id, Is.GreaterThan(0));
            Assert.That(emitters.TryGetDescriptor(id, out EmitterAssetDescriptor descriptor), Is.True);
            Assert.That(descriptor.Id, Is.EqualTo(id));
            Assert.That(descriptor.AssetKind, Is.EqualTo(expectedKind));
            Assert.That(descriptor.RuntimeFormatVersion, Is.EqualTo(TestRuntimeFormatVersion));
            Assert.That(descriptor.Sha256, Has.Length.EqualTo(64));
            Assert.That(descriptor.SourceUris, Is.Empty);
        }
    }
}
