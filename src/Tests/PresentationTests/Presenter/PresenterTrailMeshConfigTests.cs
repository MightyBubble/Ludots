using System;
using System.IO;
using System.Numerics;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresenterTrailMeshConfigTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_TrailMeshConfig", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
            TagRegistry.Clear();

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failures in test teardown.
            }
        }

        [Test]
        public void Load_ParsesTrailMeshBehavior_WithAllFields()
        {
            WritePresenters(
                """
                [
                  {
                    "id": "blade",
                    "behaviors": [
                      {
                        "slot": "trail",
                        "kind": "TrailMesh",
                        "activeByDefault": false,
                        "trailMesh": {
                          "baseOffset": [0, 0, 0.1],
                          "tipOffset": [0, 0, 1.2],
                          "maxSamples": 20,
                          "sampleIntervalSeconds": 0.012,
                          "sampleLifetimeSeconds": 0.35,
                          "headColor": [0.7, 0.9, 1.0, 0.9],
                          "tailColor": [0.2, 0.4, 1.0, 0.0]
                        }
                      }
                    ]
                  }
                ]
                """);

            PresenterDefinitionRegistry registry = Load();

            PresenterDefinition definition = registry.Get(registry.GetId("blade"));
            Assert.That(definition.Behaviors.Length, Is.EqualTo(1));
            ref readonly BehaviorSlot slot = ref definition.Behaviors[0];
            Assert.That(slot.SlotIndex, Is.EqualTo(17));
            Assert.That(slot.Kind, Is.EqualTo(BehaviorKind.TrailMesh));
            Assert.That(slot.ActiveByDefault, Is.False);
            Assert.That(slot.TrailMesh.BaseOffset, Is.EqualTo(new Vector3(0f, 0f, 0.1f)));
            Assert.That(slot.TrailMesh.TipOffset, Is.EqualTo(new Vector3(0f, 0f, 1.2f)));
            Assert.That(slot.TrailMesh.MaxSamples, Is.EqualTo(20));
            Assert.That(slot.TrailMesh.SampleIntervalSeconds, Is.EqualTo(0.012f));
            Assert.That(slot.TrailMesh.SampleLifetimeSeconds, Is.EqualTo(0.35f));
            Assert.That(slot.TrailMesh.HeadColor, Is.EqualTo(new Vector4(0.7f, 0.9f, 1.0f, 0.9f)));
            Assert.That(slot.TrailMesh.TailColor, Is.EqualTo(new Vector4(0.2f, 0.4f, 1.0f, 0.0f)));
            Assert.That(definition.TickBehaviorIndices.Length, Is.EqualTo(1), "trail behavior must join the per-frame tick lane");
        }

        [Test]
        public void Load_TrailMeshDefaults_WhenOptionalFieldsOmitted()
        {
            WritePresenters(
                """
                [
                  {
                    "id": "blade",
                    "behaviors": [
                      {
                        "slot": "trail",
                        "kind": "TrailMesh",
                        "trailMesh": {
                          "tipOffset": [0, 0, 1.2]
                        }
                      }
                    ]
                  }
                ]
                """);

            PresenterDefinitionRegistry registry = Load();

            ref readonly TrailMeshConfig config = ref registry.Get(registry.GetId("blade")).Behaviors[0].TrailMesh;
            Assert.That(config.BaseOffset, Is.EqualTo(Vector3.Zero));
            Assert.That(config.MaxSamples, Is.EqualTo(24));
            Assert.That(config.SampleIntervalSeconds, Is.EqualTo(0f));
            Assert.That(config.SampleLifetimeSeconds, Is.EqualTo(0.3f));
            Assert.That(config.HeadColor, Is.EqualTo(Vector4.One));
            Assert.That(config.TailColor, Is.EqualTo(new Vector4(1f, 1f, 1f, 0f)));
        }

        [Test]
        public void Load_TrailMeshRejectsUnknownField()
        {
            WritePresenters(TrailPresenter(""" "tipOffset": [0, 0, 1.2], "textureId": 7 """));

            Assert.Throws<InvalidOperationException>(() => Load());
        }

        [Test]
        public void Load_TrailMeshRejectsOutOfRangeSampleCounts()
        {
            WritePresenters(TrailPresenter(""" "tipOffset": [0, 0, 1.2], "maxSamples": 1 """));
            Assert.Throws<InvalidOperationException>(() => Load());

            WritePresenters(TrailPresenter($""" "tipOffset": [0, 0, 1.2], "maxSamples": {TrailMeshBuffer.MaxSamplesPerTrail + 1} """));
            Assert.Throws<InvalidOperationException>(() => Load());
        }

        [Test]
        public void Load_TrailMeshRejectsNonPositiveLifetimeAndNegativeInterval()
        {
            WritePresenters(TrailPresenter(""" "tipOffset": [0, 0, 1.2], "sampleLifetimeSeconds": 0 """));
            Assert.Throws<InvalidOperationException>(() => Load());

            WritePresenters(TrailPresenter(""" "tipOffset": [0, 0, 1.2], "sampleIntervalSeconds": -0.1 """));
            Assert.Throws<InvalidOperationException>(() => Load());
        }

        [Test]
        public void Load_TrailMeshRejectsZeroLengthSegment()
        {
            WritePresenters(TrailPresenter(""" "baseOffset": [0, 0, 1.2], "tipOffset": [0, 0, 1.2] """));

            Assert.Throws<InvalidOperationException>(() => Load());
        }

        [TestCase("Sound")]
        [TestCase("InstancedBatch")]
        [TestCase("MinimapMarker")]
        [TestCase("Animator")]
        [TestCase("Grounding")]
        public void Load_NonTrailMeshBehaviorRejectsTrailMeshScopedField(string kind)
        {
            // trailMesh 是 TrailMesh 行为专属字段：出现在其它行为 kind 上必须在配置加载期
            // fail-fast（RejectBehaviorScopedFields），而不是被静默忽略后让运行时缺样采样。
            WritePresenters(
                $"""
                [
                  {{
                    "id": "blade",
                    "behaviors": [
                      {{
                        "slot": "gfx",
                        "kind": "{kind}",
                        "trailMesh": {{ "tipOffset": [0, 0, 1.2] }}
                      }}
                    ]
                  }}
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load());
            Assert.That(ex.Message, Does.Contain("field 'trailMesh' is not valid for this behavior kind"));
        }

        private static string TrailPresenter(string trailMeshFields)
        {
            return
                "[ { \"id\": \"blade\", \"behaviors\": [ { \"slot\": \"trail\", \"kind\": \"TrailMesh\", " +
                "\"trailMesh\": { " + trailMeshFields + " } } ] } ]";
        }

        private PresenterDefinitionRegistry Load()
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            var registry = new PresenterDefinitionRegistry();
            new PresenterDefinitionConfigLoader(pipeline, registry).Load(catalog);
            return registry;
        }

        private void WritePresenters(string content)
        {
            WriteFile("config_catalog.json", @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Presentation/presenters.json", content);
        }

        private void WriteFile(string relativePath, string content)
        {
            string dir = Path.Combine(_root, "Core", Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }
    }
}
