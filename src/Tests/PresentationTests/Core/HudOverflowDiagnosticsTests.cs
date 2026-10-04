using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// HUD 溢出可观测化验收：容量边界上的行为必须显式——
    /// 脏内容增量窗口溢出要留下计数，运行期字符串表满要报错，
    /// paramDefaults 超 lane 容量要在注册期失败（样本资产在
    /// mods/fixtures/hud_overflow_diagnostics），不允许任何静默丢弃。
    /// </summary>
    [TestFixture]
    public sealed class HudOverflowDiagnosticsTests
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
            _root = Path.Combine(Path.GetTempPath(), "Ludots_HudOverflowDiagnostics", Guid.NewGuid().ToString("N"));
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
        public void WorldHud_DirtyContentWindowOverflow_IsCounted()
        {
            var buffer = new WorldHudBatchBuffer(capacity: 4);
            for (int i = 0; i < 4; i++)
            {
                Assert.That(buffer.TryAdd(MakeBar(stableId: i + 1, value0: 1f)), Is.True);
            }

            for (int i = 0; i < 4; i++)
            {
                Assert.That(buffer.TryAdd(MakeBar(stableId: i + 1, value0: 2f)), Is.True);
            }

            Assert.That(buffer.DirtyContentDrops, Is.EqualTo(0));

            // 第 5 笔内容脏进 4 槽窗口：被丢弃但必须留下计数，而不是静默 return。
            Assert.That(buffer.TryAdd(MakeBar(stableId: 1, value0: 3f)), Is.True);
            Assert.That(buffer.DirtyContentDrops, Is.EqualTo(1));

            buffer.Clear();
            Assert.That(buffer.DirtyContentDrops, Is.EqualTo(0));
        }

        [Test]
        public void ScreenHud_DirtyWindowOverflow_IsCounted()
        {
            var screen = new ScreenHudBatchBuffer(capacity: 8);
            for (int i = 0; i < 8; i++)
            {
                Assert.That(screen.TryAddBar(MakeScreenBar(stableId: i + 1, value0: 1f)), Is.True);
            }

            Assert.That(screen.DirtyContentDrops, Is.EqualTo(0));

            // 8 槽脏窗口已满：第 9 笔内容变化被丢弃但必须留下计数。
            Assert.That(screen.TryUpsertBar(MakeScreenBar(stableId: 1, value0: 2f)), Is.True);
            Assert.That(screen.DirtyContentDrops, Is.EqualTo(1));
        }

        [Test]
        public void WorldHudStringTable_RuntimeCapacityExhausted_Throws()
        {
            var table = CreateStringTable(runtimeStringCapacity: 2);

            Assert.That(table.Register("first"), Is.GreaterThan(0));
            Assert.That(table.Register("second"), Is.GreaterThan(0));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => table.Register("third"))!;
            Assert.That(ex.Message, Does.Contain("runtimeStringCapacity"));
        }

        [Test]
        public void ParamDefaults_OverLaneCapacity_FailsAtRegistration()
        {
            string samplePath = Path.Combine(
                RepoRoot,
                "mods", "fixtures", "hud_overflow_diagnostics", "HudOverflowDiagnosticsMod",
                "assets", "Presentation", "strictness", "param_defaults_seventeen_floats.json");

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

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog))!;
            Assert.That(ex.Message, Does.Contain("overflow_param_defaults"));
            Assert.That(ex.Message, Does.Contain("17 float"));
            Assert.That(registry.RegisteredIds, Is.Empty);
        }

        private static WorldHudStringTable CreateStringTable(int runtimeStringCapacity)
        {
            var tokenIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            tokenIds.Register("hud.overflow");
            tokenIds.Freeze();

            var localeIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            localeIds.Register("en-US");
            localeIds.Freeze();

            var tokens = new PresentationTextTokenDefinition[2];
            tokens[1] = new PresentationTextTokenDefinition { TokenId = 1, Key = "hud.overflow", ArgCount = 0 };
            var templates = new PresentationTextTemplate[2];
            templates[1] = new PresentationTextTemplate("{0}", Array.Empty<PresentationTextTemplatePart>());
            var locales = new PresentationTextLocaleTable[2];
            locales[1] = new PresentationTextLocaleTable(1, "en-US", templates);

            var catalog = new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: 1);
            var selection = new PresentationTextLocaleSelection(catalog);
            return new WorldHudStringTable(catalog, selection, runtimeStringCapacity);
        }

        private static WorldHudItem MakeBar(int stableId, float value0)
        {
            // 投影相关字段保持一致，只有内容（Value0）变化，走内容脏通道。
            return new WorldHudItem
            {
                StableId = stableId,
                Kind = WorldHudItemKind.Bar,
                WorldPosition = new System.Numerics.Vector3(1f, 1f, 0f),
                Width = 10f,
                Height = 4f,
                Value0 = value0,
            };
        }

        private static ScreenHudBarItem MakeScreenBar(int stableId, float value0)
        {
            return new ScreenHudBarItem
            {
                StableId = stableId,
                ScreenX = 100f,
                ScreenY = 50f,
                Width = 10f,
                Height = 4f,
                Value0 = value0,
            };
        }
    }
}
