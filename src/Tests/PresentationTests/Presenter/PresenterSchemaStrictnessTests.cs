using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// Schema 严格化验收：presenters.json 中已出现但形状错误的数值数组
    /// （分量缺失/多余/为 null）必须在加载期报错并带字段名，不允许静默替换默认值。
    /// 样本资产在 mods/fixtures/presenter_schema_strictness，逐文件对应一个失败口径。
    /// </summary>
    [TestFixture]
    public sealed class PresenterSchemaStrictnessTests
    {
        private string _root = string.Empty;

        private static string RepoRoot
        {
            get
            {
                string? dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    dir = Path.GetDirectoryName(dir);
                }

                return dir ?? throw new InvalidOperationException("repo root not found");
            }
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresenterSchemaStrictness", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        [Test]
        public void AnchorOffset_TwoComponents_FailsWithExactComponentCount()
        {
            InvalidOperationException? ex = LoadSample("anchor_offset_two_components.json").Error;
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Message, Does.Contain("exactly 3"));
            Assert.That(ex.Message, Does.Contain("offset"));
        }

        [Test]
        public void AnchorOffset_ExtraComponent_FailsWithExactComponentCount()
        {
            InvalidOperationException? ex = LoadSample("anchor_offset_extra_component.json").Error;
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Message, Does.Contain("exactly 3"));
            Assert.That(ex.Message, Does.Contain("offset"));
        }

        [Test]
        public void LocalScale_TwoComponents_FailsWithExactComponentCount()
        {
            InvalidOperationException? ex = LoadSample("local_scale_two_components.json").Error;
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Message, Does.Contain("exactly 3"));
            Assert.That(ex.Message, Does.Contain("localScale"));
        }

        [Test]
        public void LocalRotation_NullComponent_FailsInsteadOfSilentIdentity()
        {
            InvalidOperationException? ex = LoadSample("local_rotation_null_component.json").Error;
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Message, Does.Contain("null component"));
            Assert.That(ex.Message, Does.Contain("localRotation"));
        }

        [Test]
        public void MinimapColor_MissingAlpha_FailsInsteadOfSilentWhite()
        {
            InvalidOperationException? ex = LoadSample("minimap_color_missing_alpha.json").Error;
            Assert.That(ex, Is.Not.Null);
            Assert.That(ex!.Message, Does.Contain("exactly 4"));
            Assert.That(ex.Message, Does.Contain("color"));
        }

        [Test]
        public void AbsentOptionalArrays_StillLoadWithDefaults()
        {
            var (registry, ex) = LoadSample("valid_absent_defaults.json");
            Assert.That(ex, Is.Null);
            Assert.That(registry.TryGet(registry.GetId("strict_valid_absent"), out PresenterDefinition definition), Is.True);
            Assert.That(definition.Behaviors.Length, Is.EqualTo(1));
        }

        private (PresenterDefinitionRegistry Registry, InvalidOperationException? Error) LoadSample(string sampleFile)
        {
            string samplePath = Path.Combine(
                RepoRoot,
                "mods", "fixtures", "presenter_schema_strictness", "PresenterSchemaStrictnessMod",
                "assets", "Presentation", "strictness", sampleFile);

            var vfs = new VirtualFileSystem();
            Directory.CreateDirectory(Path.Combine(_root, "Core", "Presentation"));
            File.WriteAllText(
                Path.Combine(_root, "Core", "config_catalog.json"),
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            File.WriteAllText(
                Path.Combine(_root, "Core", "Presentation", "presenters.json"),
                File.ReadAllText(samplePath));
            vfs.Mount("Core", Path.Combine(_root, "Core"));

            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (_, _) => 1);

            InvalidOperationException? error;
            try
            {
                loader.Load(catalog);
                error = null;
            }
            catch (InvalidOperationException ex)
            {
                error = ex;
            }

            return (registry, error);
        }
    }
}
