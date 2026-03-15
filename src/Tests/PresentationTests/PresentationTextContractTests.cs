using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class PresentationTextContractTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_PresentationText", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_root, recursive: true); } catch { }
        }

        [Test]
        public void PresentationTextCatalogLoader_LoadsLocales_AndSupportsSelectionSwitch()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.ready"", ""argCount"": 0 },
  { ""id"": ""hud.current"", ""argCount"": 1 },
  { ""id"": ""hud.current_over_base"", ""argCount"": 2 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.ready"": ""READY"",
      ""hud.current"": ""{0}"",
      ""hud.current_over_base"": ""{0}/{1}""
    },
    ""zh-CN"": {
      ""hud.ready"": ""READY-CN"",
      ""hud.current"": ""当前 {0}"",
      ""hud.current_over_base"": ""当前 {0} / {1}""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = loader.Load(catalog);

            int readyTokenId = textCatalog.GetTokenId("hud.ready");
            int tokenId = textCatalog.GetTokenId("hud.current_over_base");
            Assert.That(readyTokenId, Is.GreaterThan(0));
            Assert.That(tokenId, Is.GreaterThan(0));
            Assert.That(textCatalog.GetLocaleKey(textCatalog.DefaultLocaleId), Is.EqualTo("en-US"));
            Assert.That(textCatalog.TryGetTemplate(textCatalog.DefaultLocaleId, readyTokenId, out var readyTemplate), Is.True);
            Assert.That(readyTemplate.Source, Is.EqualTo("READY"));
            Assert.That(textCatalog.TryGetTemplate(textCatalog.DefaultLocaleId, tokenId, out var template), Is.True);
            Assert.That(template.Source, Is.EqualTo("{0}/{1}"));

            var parts = template.GetParts().ToArray();
            Assert.That(parts.Length, Is.EqualTo(3));
            Assert.That(parts[0].Kind, Is.EqualTo(PresentationTextTemplatePartKind.Argument));
            Assert.That(parts[0].ArgIndex, Is.EqualTo(0));
            Assert.That(parts[1].Kind, Is.EqualTo(PresentationTextTemplatePartKind.Literal));
            Assert.That(parts[1].Literal, Is.EqualTo("/"));
            Assert.That(parts[2].Kind, Is.EqualTo(PresentationTextTemplatePartKind.Argument));
            Assert.That(parts[2].ArgIndex, Is.EqualTo(1));

            var selection = new PresentationTextLocaleSelection(textCatalog);
            Assert.That(selection.ActiveLocaleKey, Is.EqualTo("en-US"));
            Assert.That(selection.TrySetActiveLocale("zh-CN"), Is.True);
            Assert.That(selection.ActiveLocaleKey, Is.EqualTo("zh-CN"));
            Assert.That(textCatalog.TryGetTemplate(selection.ActiveLocaleId, readyTokenId, out readyTemplate), Is.True);
            Assert.That(readyTemplate.Source, Is.EqualTo("READY-CN"));
        }

        [Test]
        public void PresentationTextCatalogLoader_AssignsStableSortedTokenAndLocaleIds()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.zed"", ""argCount"": 0 },
  { ""id"": ""hud.alpha"", ""argCount"": 0 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""zh-CN"": {
      ""hud.alpha"": ""A"",
      ""hud.zed"": ""Z""
    },
    ""en-US"": {
      ""hud.alpha"": ""A"",
      ""hud.zed"": ""Z""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = loader.Load(catalog);

            Assert.That(textCatalog.GetTokenId("hud.alpha"), Is.EqualTo(1));
            Assert.That(textCatalog.GetTokenId("hud.zed"), Is.EqualTo(2));
            Assert.That(textCatalog.GetLocaleId("en-US"), Is.EqualTo(1));
            Assert.That(textCatalog.GetLocaleId("zh-CN"), Is.EqualTo(2));
        }

        [Test]
        public void PresentationTextCatalogLoader_Fails_WhenLocaleEntryIsMissing()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.current"", ""argCount"": 1 },
  { ""id"": ""hud.current_over_base"", ""argCount"": 2 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.current"": ""{0}""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("missing token 'hud.current_over_base'"));
        }

        [Test]
        public void PresentationTextCatalogLoader_Fails_WhenPlaceholderDoesNotCoverDeclaredArgCount()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.current_over_base"", ""argCount"": 2 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.current_over_base"": ""{0}""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("does not reference placeholder {1}"));
        }

        [Test]
        public void PresentationTextCatalogLoader_Fails_OnDuplicateTokenId_InSingleSourceFragment()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.current"", ""argCount"": 1 },
  { ""id"": ""hud.current"", ""argCount"": 1 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.current"": ""{0}""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("duplicate id 'hud.current'"));
        }

        [Test]
        public void PresentationTextCatalogLoader_Fails_OnDuplicateTokenId_AcrossSources()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.current"", ""argCount"": 1 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.current"": ""{0}""
    }
  }
}");

            string coreDirectDir = Path.Combine(_root, "Core", "Presentation");
            Directory.CreateDirectory(coreDirectDir);
            File.WriteAllText(Path.Combine(coreDirectDir, "text_tokens.json"),
                @"[
  { ""id"": ""hud.current"", ""argCount"": 1 }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("duplicate id 'hud.current'"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_ResolvesTextTokenBindings_ToStableIds()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""entity_world_text"",
    ""visualKind"": ""WorldText"",
    ""bindings"": [
      { ""paramKey"": 15, ""source"": ""textToken"", ""textToken"": ""hud.current_over_base"" },
      { ""paramKey"": 16, ""source"": ""constant"", ""constantValue"": 1 }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => string.Equals(key, "hud.current_over_base", StringComparison.Ordinal) ? 42 : 0);

            loader.Load(catalog);

            int defId = registry.GetId("entity_world_text");
            Assert.That(defId, Is.GreaterThan(0));
            Assert.That(registry.TryGet(defId, out var definition), Is.True);

            bool found = false;
            for (int i = 0; i < definition.Bindings.Length; i++)
            {
                if (definition.Bindings[i].ParamKey != 15)
                {
                    continue;
                }

                found = true;
                Assert.That(definition.Bindings[i].Value.Source, Is.EqualTo(ValueSourceKind.Constant));
                Assert.That(definition.Bindings[i].Value.ConstantValue, Is.EqualTo(42f));
            }

            Assert.That(found, Is.True, "Expected WorldText binding paramKey=15 to resolve into a stable text token id.");
        }

        [Test]
        public void PerformerDefinitionConfigLoader_ParsesNewFilterFields()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""filtered_bar"",
    ""visualKind"": ""WorldBar"",
    ""entityScope"": ""AllWithAttributes"",
    ""requiredTemplate"": ""moba_hero""
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTemplateId: key => string.Equals(key, "moba_hero", StringComparison.Ordinal) ? 42 : 0);

            loader.Load(catalog);

            int defId = registry.GetId("filtered_bar");
            Assert.That(defId, Is.GreaterThan(0));
            Assert.That(registry.TryGet(defId, out var def), Is.True);
            Assert.That(def.RequiredTemplateId, Is.EqualTo(42));
        }

        [Test]
        public void WorldHudStringTable_BridgesStaticTokens_WithoutCollidingWithLegacyRegistrations()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.static_label"", ""argCount"": 0 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.static_label"": ""Static Label""
    },
    ""zh-CN"": {
      ""hud.static_label"": ""Static Label ZH""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = loader.Load(catalog);
            var selection = new PresentationTextLocaleSelection(textCatalog);
            var strings = new WorldHudStringTable(textCatalog, selection, legacyCapacity: 4);

            int tokenId = textCatalog.GetTokenId("hud.static_label");
            int legacyId = strings.Register("runtime-only");

            Assert.That(strings.TryGet(tokenId), Is.EqualTo("Static Label"));
            Assert.That(legacyId, Is.GreaterThan(tokenId));
            Assert.That(strings.TryGet(legacyId), Is.EqualTo("runtime-only"));

            selection.SetActiveLocale("zh-CN");
            Assert.That(strings.TryGet(tokenId), Is.EqualTo("Static Label ZH"));
        }

        [Test]
        public void WorldHudToScreenSystem_CopiesPresentationTextPacket()
        {
            var world = World.Create();
            try
            {
                var worldHud = new WorldHudBatchBuffer(4);
                var screenHud = new ScreenHudBatchBuffer(4);
                var expectedText = PresentationTextPacket.FromLegacyWorldHud(
                    tokenId: 17,
                    mode: WorldHudValueMode.AttributeCurrentOverBase,
                    value0: 100f,
                    value1: 150f);

                worldHud.TryAdd(new WorldHudItem
                {
                    Kind = WorldHudItemKind.Text,
                    WorldPosition = new Vector3(10f, 2f, 0f),
                    Width = 40f,
                    Height = 10f,
                    FontSize = 12,
                    Value0 = 100f,
                    Value1 = 150f,
                    Id1 = (int)WorldHudValueMode.AttributeCurrentOverBase,
                    Text = expectedText,
                });

                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: new FixedProjector(new Vector2(320f, 240f)),
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud);

                system.Update(0f);

                Assert.That(screenHud.Count, Is.EqualTo(1));
                ref readonly var item = ref screenHud.GetSpan()[0];
                Assert.That(item.Text.TokenId, Is.EqualTo(17));
                Assert.That(item.Text.ArgCount, Is.EqualTo(2));
                Assert.That(item.Text.GetArg(0).Type, Is.EqualTo(PresentationTextArgType.Int32));
                Assert.That(item.Text.GetArg(0).AsInt32(), Is.EqualTo(100));
                Assert.That(item.Text.GetArg(1).AsInt32(), Is.EqualTo(150));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void WorldHudToScreenSystem_RoundsStationaryProjectionJitter_ForRetainedOverlay()
        {
            var world = World.Create();
            try
            {
                var worldHud = new WorldHudBatchBuffer(4);
                var screenHud = new ScreenHudBatchBuffer(4);
                var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, screenOverlay: null);
                var scene = new PresentationOverlayScene(8);
                var projector = new SequenceProjector(
                    new Vector2(320.49f, 240.49f),
                    new Vector2(320.48f, 240.48f));
                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: projector,
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud);

                EmitWorldHudBar(worldHud);
                system.Update(0f);
                builder.Build(scene);

                Assert.That(screenHud.BarCount, Is.EqualTo(1));
                ref readonly var firstBar = ref screenHud.GetBarSpan()[0];
                float firstX = firstBar.ScreenX;
                float firstY = firstBar.ScreenY;
                int firstLayerVersion = scene.GetLayerVersion(PresentationOverlayLayer.UnderUi);

                worldHud.Clear();
                EmitWorldHudBar(worldHud);
                system.Update(0f);
                builder.Build(scene);

                Assert.That(screenHud.BarCount, Is.EqualTo(1));
                ref readonly var secondBar = ref screenHud.GetBarSpan()[0];
                Assert.That(secondBar.ScreenX, Is.EqualTo(firstX));
                Assert.That(secondBar.ScreenY, Is.EqualTo(firstY));
                Assert.That(scene.DirtyLaneCount, Is.EqualTo(0),
                    "sub-pixel stationary jitter should not dirty retained overlay lanes");
                Assert.That(scene.GetLayerVersion(PresentationOverlayLayer.UnderUi), Is.EqualTo(firstLayerVersion));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void PresentationTextFormatter_FormatsPacketAgainstLocaleTemplate()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.damage"", ""argCount"": 2 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.damage"": ""DMG {0} / {1}""
    },
    ""zh-CN"": {
      ""hud.damage"": ""伤害 {0} / {1}""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = loader.Load(catalog);
            int tokenId = textCatalog.GetTokenId("hud.damage");

            var packet = PresentationTextPacket.FromToken(tokenId);
            packet.SetArg(0, PresentationTextArg.FromInt32(42));
            packet.SetArg(1, PresentationTextArg.FromFloat32(3.5f, PresentationTextArgFormat.Fixed1));

            Assert.That(PresentationTextFormatter.TryFormat(textCatalog, textCatalog.DefaultLocaleId, in packet, out string enText), Is.True);
            Assert.That(enText, Is.EqualTo("DMG 42 / 3.5"));

            int zhLocaleId = textCatalog.GetLocaleId("zh-CN");
            Assert.That(PresentationTextFormatter.TryFormat(textCatalog, zhLocaleId, in packet, out string zhText), Is.True);
            Assert.That(zhText, Is.EqualTo("伤害 42 / 3.5"));
        }

        [Test]
        public void PresentationTextFormatter_PreservesEscapedBraces()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.literal"", ""argCount"": 1 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.literal"": ""{{{0}}}""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = loader.Load(catalog);
            int tokenId = textCatalog.GetTokenId("hud.literal");

            var packet = PresentationTextPacket.FromToken(tokenId);
            packet.SetArg(0, PresentationTextArg.FromInt32(99));

            Assert.That(PresentationTextFormatter.TryFormat(textCatalog, textCatalog.DefaultLocaleId, in packet, out string text), Is.True);
            Assert.That(text, Is.EqualTo("{99}"));
        }

        [Test]
        public void GameEngine_RegistersPresentationTextCatalogServices()
        {
            using var engine = CreateEngine("LudotsCoreMod", "CoreInputMod");

            var catalog = engine.GetService(CoreServiceKeys.PresentationTextCatalog);
            var selection = engine.GetService(CoreServiceKeys.PresentationTextLocaleSelection);

            Assert.That(catalog, Is.Not.Null);
            Assert.That(selection, Is.Not.Null);
            Assert.That(catalog!.GetTokenId("hud.attribute.current_over_base"), Is.GreaterThan(0));
            Assert.That(selection!.ActiveLocaleKey, Is.EqualTo("en-US"));
            Assert.That(selection.TrySetActiveLocale("zh-CN"), Is.True);
            Assert.That(selection.ActiveLocaleKey, Is.EqualTo("zh-CN"));
        }

        private static (VirtualFileSystem vfs, ModLoader modLoader, ConfigPipeline pipeline, ConfigCatalog catalog)
            BuildPipeline(string root, string[] modIds = null)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            if (modIds != null)
            {
                for (int i = 0; i < modIds.Length; i++)
                {
                    string modPath = Path.Combine(root, modIds[i]);
                    vfs.Mount(modIds[i], modPath);
                    modLoader.LoadedModIds.Add(modIds[i]);
                }
            }

            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = ConfigCatalogLoader.Load(pipeline);
            return (vfs, modLoader, pipeline, catalog);
        }

        private void WriteFile(string modId, string relativePath, string content)
        {
            string dir = Path.Combine(_root, modId, "Configs", Path.GetDirectoryName(relativePath) ?? string.Empty);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Path.GetFileName(relativePath)), content);
        }

        private static GameEngine CreateEngine(params string[] modIds)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, modIds);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            engine.Start();
            return engine;
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private static void EmitWorldHudBar(WorldHudBatchBuffer worldHud)
        {
            worldHud.TryAdd(new WorldHudItem
            {
                StableId = 101,
                DirtySerial = 202,
                Kind = WorldHudItemKind.Bar,
                WorldPosition = new Vector3(10f, 2f, 0f),
                Width = 10f,
                Height = 3f,
                Value0 = 0.5f,
                Color0 = new Vector4(0.1f, 0.1f, 0.1f, 1f),
                Color1 = new Vector4(0.2f, 0.8f, 0.2f, 1f),
            });
        }

        private sealed class FixedProjector : IScreenProjector
        {
            private readonly Vector2 _screen;

            public FixedProjector(Vector2 screen)
            {
                _screen = screen;
            }

            public Vector2 WorldToScreen(Vector3 worldPosition) => _screen;
        }

        private sealed class SequenceProjector : IScreenProjector
        {
            private readonly Vector2[] _screens;
            private int _index;

            public SequenceProjector(params Vector2[] screens)
            {
                if (screens == null || screens.Length == 0)
                {
                    throw new ArgumentException("At least one screen position is required.", nameof(screens));
                }

                _screens = screens;
            }

            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                int currentIndex = Math.Min(_index, _screens.Length - 1);
                if (_index < _screens.Length - 1)
                {
                    _index++;
                }

                return _screens[currentIndex];
            }
        }

        private sealed class FixedViewController : IViewController
        {
            public FixedViewController(Vector2 resolution)
            {
                Resolution = resolution;
            }

            public Vector2 Resolution { get; }

            public float Fov => 60f;

            public float AspectRatio => Resolution.Y <= 0 ? 1f : Resolution.X / Resolution.Y;
        }
    }
}
