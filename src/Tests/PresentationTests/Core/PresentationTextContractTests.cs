using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Registry;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI.Runtime;
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

            WriteFile("TestMod", "assets/Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.current"", ""argCount"": 1 }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root, new[] { "TestMod" });
            var loader = new PresentationTextCatalogLoader(pipeline);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("duplicate id 'hud.current'"));
        }

        [Test]
        public void PresenterDefinitionConfigLoader_ResolvesTextTokenBindings_ToStableIds()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/presenters.json",
                @"[
  {
    ""id"": ""entity_world_text"",
    ""behaviors"": [
      {
        ""slot"": ""body"",
        ""kind"": ""WorldText"",
        ""activeByDefault"": true,
        ""worldText"": {
          ""textToken"": ""hud.current_over_base"",
          ""mode"": ""AttributeCurrentOverBase""
        }
      }
    ],
    ""bindings"": [
      { ""paramKey"": ""worldText.tokenId"", ""source"": ""textToken"", ""textToken"": ""hud.current_over_base"" }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => string.Equals(key, "hud.current_over_base", StringComparison.Ordinal) ? 42 : 0);

            loader.Load(catalog);

            int defId = registry.GetId("entity_world_text");
            Assert.That(defId, Is.GreaterThan(0));
            Assert.That(registry.TryGet(defId, out var definition), Is.True);

            bool found = false;
            int textTokenParamKey = PresenterParamKeyRegistry.Register("worldText.tokenId");
            for (int i = 0; i < definition.Bindings.Length; i++)
            {
                if (definition.Bindings[i].ParamKey != textTokenParamKey)
                {
                    continue;
                }

                found = true;
                Assert.That(definition.Bindings[i].Value.Source, Is.EqualTo(ValueSourceKind.Constant));
                Assert.That(definition.Bindings[i].Value.ConstantValue, Is.EqualTo(42f));
            }

            Assert.That(found, Is.True, "Expected WorldText token binding to resolve into a stable text token id.");
        }

        [Test]
        public void PresenterDefinitionConfigLoader_ParsesAssetAndAttributeBindingBehaviors()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/presenters.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/presenters.json",
                @"[
  {
    ""id"": ""scorch_decal"",
    ""behaviors"": [
      {
        ""slot"": ""body"",
        ""kind"": ""AssetBinding"",
        ""activeByDefault"": true,
        ""assetBinding"": {
          ""assetKind"": ""Decal"",
          ""assetId"": ""decal.scorch"",
          ""materialId"": ""mat.scorch"",
          ""renderPath"": ""StaticMesh"",
          ""mobility"": ""Movable"",
          ""colorParamKey"": ""decal.tint""
        }
      },
      {
        ""slot"": ""attribute"",
        ""kind"": ""AttributeBinding"",
        ""activeByDefault"": true,
        ""attributeBinding"": {
          ""attributeId"": ""burn"",
          ""targetParamKey"": ""decal.intensity"",
          ""mode"": ""Attribute""
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PresenterDefinitionRegistry();
            var loader = new PresenterDefinitionConfigLoader(
                pipeline,
                registry,
                resolveAttributeName: key => string.Equals(key, "burn", StringComparison.Ordinal) ? 9 : -1,
                resolveMaterialId: key => string.Equals(key, "mat.scorch", StringComparison.Ordinal) ? 23 : 0,
                resolveBehaviorAssetId: (kind, key) =>
                    kind == AssetKind.Decal && string.Equals(key, "decal.scorch", StringComparison.Ordinal) ? 17 : 0);

            loader.Load(catalog);

            int defId = registry.GetId("scorch_decal");
            Assert.That(registry.TryGet(defId, out var def), Is.True);
            Assert.That(def.Behaviors, Has.Length.EqualTo(2));
            Assert.That(def.Behaviors[0].SlotIndex, Is.EqualTo(0));
            Assert.That(def.Behaviors[0].Kind, Is.EqualTo(BehaviorKind.AssetBinding));
            Assert.That(def.Behaviors[0].ActiveByDefault, Is.True);
            Assert.That(def.Behaviors[0].AssetBinding.AssetKind, Is.EqualTo(AssetKind.Decal));
            Assert.That(def.Behaviors[0].AssetBinding.AssetId, Is.EqualTo(17));
            Assert.That(def.Behaviors[0].AssetBinding.MaterialId, Is.EqualTo(23));
            Assert.That(def.Behaviors[0].AssetBinding.RenderPath, Is.EqualTo(VisualRenderPath.StaticMesh));
            Assert.That(def.Behaviors[0].AssetBinding.Mobility, Is.EqualTo(VisualMobility.Movable));
            Assert.That(
                def.Behaviors[0].AssetBinding.ColorParamKey,
                Is.EqualTo(PresenterParamKeyRegistry.Register("decal.tint")));
            Assert.That(def.Bindings, Is.Empty);
            Assert.That(def.Behaviors[1].Kind, Is.EqualTo(BehaviorKind.AttributeBinding));
            Assert.That(def.Behaviors[1].AttributeBinding.AttributeId, Is.EqualTo(9));
            Assert.That(def.Behaviors[1].AttributeBinding.TargetParamKey, Is.EqualTo(PresenterParamKeyRegistry.Register("decal.intensity")));
            Assert.That(def.Behaviors[1].AttributeBinding.Mode, Is.EqualTo(ValueSourceKind.Attribute));
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
            var strings = new WorldHudStringTable(textCatalog, selection, runtimeStringCapacity: 4);

            int tokenId = textCatalog.GetTokenId("hud.static_label");
            int runtimeStringId = strings.Register("runtime-only");

            Assert.That(strings.TryGet(tokenId), Is.EqualTo("Static Label"));
            Assert.That(runtimeStringId, Is.GreaterThan(tokenId));
            Assert.That(strings.TryGet(runtimeStringId), Is.EqualTo("runtime-only"));

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
                var expectedText = PresentationTextPacket.FromWorldHudValueMode(
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
        public void Overlay_ReformatsNestedTitleWhenLocaleChangesWithoutReemit()
        {
            var world = World.Create();
            try
            {
                PresentationTextCatalog textCatalog = CreatePlateCatalog();
                var selection = new PresentationTextLocaleSelection(textCatalog);
                int plateId = textCatalog.GetTokenId("hud.hero.plate");
                int nameId = textCatalog.GetTokenId("entity.guan");
                var packet = PresentationTextPacket.FromToken(plateId);
                packet.SetArg(0, PresentationTextArg.FromInt32(10));
                packet.SetArg(1, PresentationTextArg.FromTextToken(nameId));

                var worldHud = new WorldHudBatchBuffer(4);
                var screenHud = new ScreenHudBatchBuffer(4);
                var builder = new PresentationOverlaySceneBuilder(screenHud, null, textCatalog, selection, screenOverlay: null);
                var scene = new PresentationOverlayScene(8);
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = 77,
                    DirtySerial = 11,
                    Kind = WorldHudItemKind.Text,
                    WorldPosition = new Vector3(10f, 2f, 0f),
                    FontSize = 16,
                    Text = packet,
                });

                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: new FixedProjector(new Vector2(320f, 240f)),
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud);

                system.Update(0f);
                builder.Build(scene);
                Assert.That(SceneText(scene), Is.EqualTo("Lv10 Guan Yu"));

                selection.SetActiveLocale("zh-CN");
                builder.Build(scene);
                Assert.That(SceneText(scene), Is.EqualTo("Lv10 关羽"));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void WorldHudToScreenSystem_SubmitsInCameraLargeBars()
        {
            var world = World.Create();
            try
            {
                var worldHud = new WorldHudBatchBuffer(4);
                var screenHud = new ScreenHudBatchBuffer(4);
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = 901,
                    DirtySerial = 902,
                    Kind = WorldHudItemKind.Bar,
                    WorldPosition = new Vector3(10f, 2f, 0f),
                    Width = 1024f,
                    Height = 24f,
                    Value0 = 0.75f,
                    Color0 = new Vector4(0.1f, 0.1f, 0.1f, 1f),
                    Color1 = new Vector4(0.2f, 0.8f, 0.2f, 1f),
                });

                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: new FixedProjector(new Vector2(960f, 360f)),
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud);

                system.Update(0f);

                Assert.That(screenHud.BarCount, Is.EqualTo(1),
                    "screen-visible HUD bars must not be rejected by readability, density, or size caps");
                Assert.That(screenHud.DroppedTotal, Is.EqualTo(0));
                ref readonly var item = ref screenHud.GetBarSpan()[0];
                Assert.That(item.Width, Is.EqualTo(1024f));
                Assert.That(item.StableId, Is.EqualTo(901));
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
        public void WorldHudToScreenSystem_RebuildsWhenOwnerCullVisibilityChanges()
        {
            var world = World.Create();
            try
            {
                var worldHud = new WorldHudBatchBuffer(4);
                var screenHud = new ScreenHudBatchBuffer(4);
                var cullingDebug = new CameraCullingDebugState();
                Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = 991,
                    DirtySerial = 1,
                    Owner = owner,
                    Kind = WorldHudItemKind.Bar,
                    WorldPosition = new Vector3(10f, 2f, 0f),
                    Width = 80f,
                    Height = 8f,
                    Value0 = 0.75f,
                });

                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: new FixedProjector(new Vector2(320f, 240f)),
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud,
                    cullingDebug: cullingDebug);

                system.Update(0f);
                Assert.That(screenHud.BarCount, Is.EqualTo(1));

                ref CullState ownerCull = ref world.Get<CullState>(owner);
                ownerCull.IsVisible = false;
                ownerCull.LOD = LODLevel.Low;
                cullingDebug.VisibilityRevision++;
                system.Update(0f);

                Assert.That(screenHud.BarCount, Is.EqualTo(0),
                    "retained HUD projection must remove items when owner CullState changes even if projection and content are unchanged");
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void WorldHudToScreenSystem_RemovesProjectedStableItem_WhenWorldHudStableItemIsRemoved()
        {
            var world = World.Create();
            try
            {
                var worldHud = new WorldHudBatchBuffer(4);
                var screenHud = new ScreenHudBatchBuffer(4);
                var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, screenOverlay: null);
                var scene = new PresentationOverlayScene(8);
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = 1301,
                    DirtySerial = 1,
                    Kind = WorldHudItemKind.Text,
                    WorldPosition = new Vector3(10f, 2f, 0f),
                    Width = 48f,
                    Height = 16f,
                    FontSize = 16,
                    Value0 = 7f,
                    Id1 = (int)WorldHudValueMode.Constant,
                });

                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: new FixedProjector(new Vector2(320f, 240f)),
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud);

                system.Update(0f);
                builder.Build(scene);

                Assert.That(screenHud.TextCount, Is.EqualTo(1));
                Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text).Length, Is.EqualTo(1));

                worldHud.Remove(1301);
                system.Update(0f);
                builder.Build(scene);

                Assert.That(screenHud.TextCount, Is.EqualTo(0),
                    "Removing a world HUD stable id must clear the authoritative screen HUD projection before adapters draw.");
                Assert.That(screenHud.GetSpan().Length, Is.EqualTo(0),
                    "Flattened screen HUD reads must not retain compacted projected items.");
                Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text).Length, Is.EqualTo(0),
                    "Overlay deltas must receive the projected HUD removal.");
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void WorldHudToScreenSystem_ReusesOwnerProjection_ForNonAdjacentHudItems()
        {
            var world = World.Create();
            try
            {
                var worldHud = new WorldHudBatchBuffer(8);
                var screenHud = new ScreenHudBatchBuffer(8);
                Entity owner = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
                var sharedPosition = new Vector3(10f, 2f, 0f);
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = 1201,
                    DirtySerial = 1,
                    Owner = owner,
                    Kind = WorldHudItemKind.Bar,
                    WorldPosition = sharedPosition,
                    Width = 80f,
                    Height = 8f,
                    Value0 = 0.75f,
                });
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = 1202,
                    DirtySerial = 1,
                    Owner = Entity.Null,
                    Kind = WorldHudItemKind.Bar,
                    WorldPosition = new Vector3(12f, 2f, 0f),
                    Width = 80f,
                    Height = 8f,
                    Value0 = 0.5f,
                });
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = 1203,
                    DirtySerial = 1,
                    Owner = owner,
                    Kind = WorldHudItemKind.Text,
                    WorldPosition = sharedPosition,
                    Width = 80f,
                    Height = 16f,
                    FontSize = 16,
                });

                var projector = new CountingProjector(new Vector2(320f, 240f));
                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: projector,
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud,
                    cullingDebug: new CameraCullingDebugState());

                system.Update(0f);

                Assert.That(screenHud.BarCount, Is.EqualTo(2));
                Assert.That(screenHud.TextCount, Is.EqualTo(1));
                Assert.That(projector.CallCount, Is.EqualTo(2),
                    "The same owner/world-position pair should be projected once per frame even when HUD items are not adjacent.");
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
        public void PresentationTextFormatter_ThrowsWhenTemplateArgumentExceedsPacketArguments()
        {
            var packet = PresentationTextPacket.FromToken(1);
            var plainTemplate = new PresentationTextTemplate(
                "{0}",
                new[]
                {
                    new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Argument, string.Empty, 0)
                });
            var styledTemplate = new PresentationTextTemplate(
                "<b>{0}</b>",
                new[]
                {
                    new PresentationTextTemplatePart(
                        PresentationTextTemplatePartKind.Argument,
                        string.Empty,
                        0,
                        PresentationTextStyleOverride.CreateBold())
                });

            Assert.That(
                () => PresentationTextFormatter.Format(plainTemplate, in packet),
                Throws.InvalidOperationException.With.Message.Contains("index 0")
                    .And.Message.Contains("count 0"));
            Assert.That(
                () => PresentationTextFormatter.FormatRuns(styledTemplate, in packet),
                Throws.InvalidOperationException.With.Message.Contains("index 0")
                    .And.Message.Contains("count 0"));
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
        public void PresentationTextFormatter_FormatsStringArgsFromCatalogPool()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""story.line.combo"", ""argCount"": 2 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""zh-CN"",
  ""locales"": {
    ""zh-CN"": {
      ""story.line.combo"": ""{0}：{1}""
    },
    ""en-US"": {
      ""story.line.combo"": ""{0}: {1}""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = loader.Load(catalog);
            int tokenId = textCatalog.GetTokenId("story.line.combo");

            var packet = PresentationTextPacket.FromToken(tokenId);
            packet.SetArg(0, PresentationTextArg.FromString(textCatalog.StringPool, "守望者"));
            packet.SetArg(1, PresentationTextArg.FromString(textCatalog.StringPool, "灯还亮着"));

            Assert.That(PresentationTextFormatter.TryFormat(textCatalog, textCatalog.DefaultLocaleId, in packet, out string zhText), Is.True);
            Assert.That(zhText, Is.EqualTo("守望者：灯还亮着"));

            int enLocaleId = textCatalog.GetLocaleId("en-US");
            Assert.That(PresentationTextFormatter.TryFormat(textCatalog, enLocaleId, in packet, out string enText), Is.True);
            Assert.That(enText, Is.EqualTo("守望者: 灯还亮着"));
        }

        [Test]
        public void PresentationTextFormatter_InsertsZeroArgTokenInActiveLocale()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.hero.plate"", ""argCount"": 2 },
  { ""id"": ""entity.guan"", ""argCount"": 0 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.hero.plate"": ""Lv{0} {1}"",
      ""entity.guan"": ""Guan Yu""
    },
    ""zh-CN"": {
      ""hud.hero.plate"": ""Lv{0} {1}"",
      ""entity.guan"": ""关羽""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            PresentationTextCatalog textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
            int plateId = textCatalog.GetTokenId("hud.hero.plate");
            int nameId = textCatalog.GetTokenId("entity.guan");
            var packet = PresentationTextPacket.FromToken(plateId);
            packet.SetArg(0, PresentationTextArg.FromInt32(10));
            packet.SetArg(1, PresentationTextArg.FromTextToken(nameId));

            Assert.That(PresentationTextFormatter.TryFormat(textCatalog, textCatalog.DefaultLocaleId, in packet, out string enText), Is.True);
            Assert.That(enText, Is.EqualTo("Lv10 Guan Yu"));

            int zhLocaleId = textCatalog.GetLocaleId("zh-CN");
            Assert.That(PresentationTextFormatter.TryFormat(textCatalog, zhLocaleId, in packet, out string zhText), Is.True);
            Assert.That(zhText, Is.EqualTo("Lv10 关羽"));
        }

        [Test]
        public void PresentationTextFormatter_RejectsNestedTokenThatStillHasArguments()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""hud.hero.plate"", ""argCount"": 1 },
  { ""id"": ""entity.guan"", ""argCount"": 1 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""hud.hero.plate"": ""{0}"",
      ""entity.guan"": ""{0}""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            PresentationTextCatalog textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
            var packet = PresentationTextPacket.FromToken(textCatalog.GetTokenId("hud.hero.plate"));
            packet.SetArg(0, PresentationTextArg.FromTextToken(textCatalog.GetTokenId("entity.guan")));

            Assert.That(
                () => PresentationTextFormatter.TryFormat(textCatalog, textCatalog.DefaultLocaleId, in packet, out _),
                Throws.InvalidOperationException.With.Message.Contains("zero-argument"));
        }

        [Test]
        public void PresentationTextStringPool_Throws_WhenResolvingAcrossPools()
        {
            var poolA = new PresentationTextStringPool();
            var poolB = new PresentationTextStringPool();
            PresentationTextArg arg = PresentationTextArg.FromString(poolA, "witness");

            Assert.That(poolA.Get(in arg), Is.EqualTo("witness"));
            Assert.That(
                () => poolB.Get(in arg),
                Throws.InvalidOperationException.With.Message.Contains("pool identity"));
        }

        [Test]
        public void PresentationTextCatalogLoader_ParsesRestrictedMarkup_IntoStyledParts()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""story.warden.warn"", ""argCount"": 0 },
  { ""id"": ""story.line.wrap_arg"", ""argCount"": 1 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""zh-CN"",
  ""locales"": {
    ""zh-CN"": {
      ""story.warden.warn"": ""灯还亮着，<b>别走神</b>，山谷在等<color=#FFF6C56B>见证者</color>"",
      ""story.line.wrap_arg"": ""称呼：<b>{0}</b>""
    },
    ""en-US"": {
      ""story.warden.warn"": ""Lanterns still burn. Stay <i>focused</i>."",
      ""story.line.wrap_arg"": ""Call me <b>{0}</b>""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = loader.Load(catalog);

            int warnId = textCatalog.GetTokenId("story.warden.warn");
            Assert.That(textCatalog.TryGetTemplate(textCatalog.DefaultLocaleId, warnId, out var warnTemplate), Is.True);
            Assert.That(warnTemplate.HasStyledParts, Is.True);

            var packet = PresentationTextPacket.FromToken(warnId);
            Assert.That(PresentationTextFormatter.TryFormat(textCatalog, textCatalog.DefaultLocaleId, in packet, out string plain), Is.True);
            Assert.That(plain, Is.EqualTo("灯还亮着，别走神，山谷在等见证者"));

            Assert.That(PresentationTextFormatter.TryFormatRuns(textCatalog, textCatalog.DefaultLocaleId, in packet, out var runs), Is.True);
            Assert.That(runs.Count, Is.EqualTo(4));
            Assert.That(runs[0].Text, Is.EqualTo("灯还亮着，"));
            Assert.That(runs[0].Style.IsEmpty, Is.True);
            Assert.That(runs[1].Text, Is.EqualTo("别走神"));
            Assert.That(runs[1].Style.Bold, Is.True);
            Assert.That(runs[2].Text, Is.EqualTo("，山谷在等"));
            Assert.That(runs[3].Text, Is.EqualTo("见证者"));
            Assert.That(runs[3].Style.HasColor, Is.True);
            Assert.That(runs[3].Style.A, Is.EqualTo(0xFF));
            Assert.That(runs[3].Style.R, Is.EqualTo(0xF6));
            Assert.That(runs[3].Style.G, Is.EqualTo(0xC5));
            Assert.That(runs[3].Style.B, Is.EqualTo(0x6B));

            int wrapId = textCatalog.GetTokenId("story.line.wrap_arg");
            var wrapPacket = PresentationTextPacket.FromToken(wrapId);
            wrapPacket.SetArg(0, PresentationTextArg.FromString(textCatalog.StringPool, "米蕾勒"));
            Assert.That(PresentationTextFormatter.TryFormatRuns(textCatalog, textCatalog.DefaultLocaleId, in wrapPacket, out var wrapRuns), Is.True);
            Assert.That(wrapRuns.Count, Is.EqualTo(2));
            Assert.That(wrapRuns[0].Text, Is.EqualTo("称呼："));
            Assert.That(wrapRuns[1].Text, Is.EqualTo("米蕾勒"));
            Assert.That(wrapRuns[1].Style.Bold, Is.True);
        }

        [Test]
        public void PresentationTextCatalogLoader_FailsClosed_OnIllegalMarkup()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""story.bad.unclosed"", ""argCount"": 0 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""zh-CN"",
  ""locales"": {
    ""zh-CN"": {
      ""story.bad.unclosed"": ""未闭合 <b>词""
    },
    ""en-US"": {
      ""story.bad.unclosed"": ""ok""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            Assert.That(
                () => loader.Load(catalog),
                Throws.InvalidOperationException.With.Message.Contains("story.bad.unclosed"));
        }

        [Test]
        public void PresentationTextCatalogLoader_FailsClosed_OnBadColorAndNesting()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""story.bad.color"", ""argCount"": 0 },
  { ""id"": ""story.bad.nest"", ""argCount"": 0 }
]");

            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""zh-CN"",
  ""locales"": {
    ""zh-CN"": {
      ""story.bad.color"": ""坏色 <color=#ZZ>x</color>"",
      ""story.bad.nest"": ""ok""
    },
    ""en-US"": {
      ""story.bad.color"": ""ok"",
      ""story.bad.nest"": ""ok""
    }
  }
}");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var loader = new PresentationTextCatalogLoader(pipeline);
            Assert.That(
                () => loader.Load(catalog),
                Throws.InvalidOperationException.With.Message.Contains("story.bad.color"));

            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""zh-CN"",
  ""locales"": {
    ""zh-CN"": {
      ""story.bad.color"": ""ok"",
      ""story.bad.nest"": ""嵌套 <b>外<i>内</i></b>""
    },
    ""en-US"": {
      ""story.bad.color"": ""ok"",
      ""story.bad.nest"": ""ok""
    }
  }
}");

            Assert.That(
                () => loader.Load(catalog),
                Throws.InvalidOperationException.With.Message.Contains("nested"));
        }

        [Test]
        public void UiStyledTextRunNormalization_MergesMidWordStyleBoundaryIntoLaterRun()
        {
            var runs = new[]
            {
                UiStyledTextRun.Plain("Hel"),
                new UiStyledTextRun("lo world", Bold: true),
            };

            IReadOnlyList<UiStyledTextRun> normalized = UiStyledTextRunNormalization.NormalizeWordBoundaries(runs);
            Assert.That(normalized.Count, Is.EqualTo(1));
            Assert.That(normalized[0].Text, Is.EqualTo("Hello world"));
            Assert.That(normalized[0].Bold, Is.True);
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

        [Test]
        public void HudOwnerFrameSnapshot_AlignsAttributeColumnsWithRowCapacity()
        {
            var world = World.Create();
            try
            {
                const int health = 3;
                const int stamina = 4;
                var snapshot = new HudOwnerFrameSnapshot();

                snapshot.RegisterTrackedAttribute(health);
                Entity first = CreateAttributedOwner(world, health, current: 40f, baseValue: 150f);
                snapshot.Rebuild(world);
                AssertAttributeRow(snapshot, world, first, health);
                Assert.That(snapshot.TryGetAttributeValues(1, health, out _, out _), Is.False);

                snapshot.RegisterTrackedAttribute(stamina);
                AssertAttributeRow(snapshot, first, stamina, current: 0f, baseValue: 0f);

                SetAttribute(world, first, health, current: 12f, baseValue: 80f);
                SetAttribute(world, first, stamina, current: 9f, baseValue: 30f);
                snapshot.Rebuild(world);
                AssertAttributeRow(snapshot, world, first, health);
                AssertAttributeRow(snapshot, world, first, stamina);

                Entity cullOnly = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
                snapshot.Rebuild(world);
                AssertAttributeRow(snapshot, world, first, health);
                AssertAttributeRow(snapshot, world, first, stamina);
                AssertAttributeRow(snapshot, cullOnly, health, current: 0f, baseValue: 0f);
                AssertAttributeRow(snapshot, cullOnly, stamina, current: 0f, baseValue: 0f);

                world.Destroy(first);
                world.Destroy(cullOnly);
                Entity replacement = world.Create(new CullState { IsVisible = false, LOD = LODLevel.Low });
                snapshot.Rebuild(world);
                Assert.That(snapshot.TryGetRow(first, out _), Is.False);
                AssertAttributeRow(snapshot, replacement, health, current: 0f, baseValue: 0f);
                AssertAttributeRow(snapshot, replacement, stamina, current: 0f, baseValue: 0f);
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void HudOwnerFrameSnapshot_AlignsAttributeColumn_WhenRegisteredWithNoLiveOwners()
        {
            var world = World.Create();
            try
            {
                const int health = 3;
                var snapshot = new HudOwnerFrameSnapshot();
                Entity first = CreateAttributedOwner(world, health, current: 40f, baseValue: 150f);
                snapshot.Rebuild(world);
                world.Destroy(first);
                snapshot.Rebuild(world);

                snapshot.RegisterTrackedAttribute(health);
                Entity returned = CreateAttributedOwner(world, health, current: 7f, baseValue: 20f);
                snapshot.Rebuild(world);
                AssertAttributeRow(snapshot, world, returned, health);
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void HudOwnerFrameSnapshot_GrowsAttributeColumnsPastFirstCapacityBlock()
        {
            var world = World.Create();
            try
            {
                const int health = 3;
                const int firstBlock = 64;
                var snapshot = new HudOwnerFrameSnapshot();
                var owners = new Entity[firstBlock];
                for (int i = 0; i < owners.Length; i++)
                {
                    owners[i] = CreateAttributedOwner(world, health, current: i, baseValue: 100f);
                }

                snapshot.Rebuild(world);
                snapshot.RegisterTrackedAttribute(health);
                Entity extra = world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
                snapshot.Rebuild(world);

                AssertAttributeRow(snapshot, world, owners[0], health);
                AssertAttributeRow(snapshot, world, owners[firstBlock - 1], health);
                AssertAttributeRow(snapshot, extra, health, current: 0f, baseValue: 0f);
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void WorldHudToScreenSystem_RefreshesBoundText_WhenCullOnlyOwnerAppearsInsideCapacity()
        {
            var world = World.Create();
            try
            {
                const int health = 3;
                var worldHud = new WorldHudBatchBuffer(4);
                var screenHud = new ScreenHudBatchBuffer(4);
                Entity owner = CreateAttributedOwner(world, health, current: 40f, baseValue: 150f);
                worldHud.TryAdd(new WorldHudItem
                {
                    StableId = 42,
                    DirtySerial = 1,
                    Owner = owner,
                    Kind = WorldHudItemKind.Text,
                    WorldPosition = new Vector3(10f, 2f, 0f),
                    FontSize = 16,
                    Value0 = 0f,
                    Value1 = 0f,
                    Id1 = (int)WorldHudValueMode.AttributeCurrentOverBase,
                    ValueBound = 1,
                    BoundAttributeId = health,
                });

                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: new FixedProjector(new Vector2(320f, 240f)),
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud);

                system.Update(0f);
                Assert.That(screenHud.TextCount, Is.EqualTo(1));
                Assert.That(screenHud.HasAttributeBoundTexts, Is.True);

                system.Update(0f);
                Assert.That(screenHud.GetTextSpan()[0].Value0, Is.EqualTo(40f));
                Assert.That(screenHud.GetTextSpan()[0].Value1, Is.EqualTo(150f));

                world.Create(new CullState { IsVisible = true, LOD = LODLevel.High });
                SetAttribute(world, owner, health, current: 55f, baseValue: 150f);
                system.Update(0f);

                Assert.That(screenHud.TextCount, Is.EqualTo(1));
                Assert.That(screenHud.GetTextSpan()[0].Value0, Is.EqualTo(55f));
                Assert.That(screenHud.GetTextSpan()[0].Value1, Is.EqualTo(150f));
            }
            finally
            {
                World.Destroy(world);
            }
        }

        [Test]
        public void WorldHudToScreenSystem_DoesNotTrackAttributes_ForUnboundBar()
        {
            var world = World.Create();
            try
            {
                var worldHud = new WorldHudBatchBuffer(4);
                var screenHud = new ScreenHudBatchBuffer(4);
                EmitWorldHudBar(worldHud);
                var system = new WorldHudToScreenSystem(
                    world,
                    worldHud,
                    strings: null,
                    projector: new FixedProjector(new Vector2(320f, 240f)),
                    view: new FixedViewController(new Vector2(1920f, 1080f)),
                    screenHud: screenHud);

                system.Update(0f);
                system.Update(0f);

                Assert.That(screenHud.BarCount, Is.EqualTo(1));
                Assert.That(screenHud.HasAttributeBoundTexts, Is.False);
            }
            finally
            {
                World.Destroy(world);
            }
        }

        private static (VirtualFileSystem vfs, ModLoader modLoader, ConfigPipeline pipeline, ConfigCatalog catalog)
            BuildPipeline(string root, string[]? modIds = null)
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
            string dir = Path.Combine(_root, modId, Path.GetDirectoryName(relativePath) ?? string.Empty);
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

        private static Entity CreateAttributedOwner(World world, int attributeId, float current, float baseValue)
        {
            Entity owner = world.Create(
                new AttributeBuffer(),
                new CullState { IsVisible = true, LOD = LODLevel.High });
            SetAttribute(world, owner, attributeId, current, baseValue);
            return owner;
        }

        private static void SetAttribute(World world, Entity owner, int attributeId, float current, float baseValue)
        {
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(owner);
            attributes.SetBase(attributeId, baseValue);
            attributes.SetCurrent(attributeId, current);
        }

        private static void AssertAttributeRow(
            HudOwnerFrameSnapshot snapshot,
            World world,
            Entity owner,
            int attributeId)
        {
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(owner);
            AssertAttributeRow(
                snapshot,
                owner,
                attributeId,
                attributes.GetCurrent(attributeId),
                attributes.GetBase(attributeId));
        }

        private static void AssertAttributeRow(
            HudOwnerFrameSnapshot snapshot,
            Entity owner,
            int attributeId,
            float current,
            float baseValue)
        {
            Assert.That(snapshot.TryGetRow(owner, out int row), Is.True, $"owner {owner.Id} must be in the frame snapshot");
            Assert.That(
                snapshot.TryGetAttributeValues(row, attributeId, out float actualCurrent, out float actualBase),
                Is.True,
                $"attribute {attributeId} must be readable for owner {owner.Id}");
            Assert.That(actualCurrent, Is.EqualTo(current));
            Assert.That(actualBase, Is.EqualTo(baseValue));
        }

        private static string SceneText(PresentationOverlayScene scene)
        {
            var span = scene.GetLaneSpan(PresentationOverlayLayer.UnderUi, PresentationOverlayItemKind.Text);
            Assert.That(span.Length, Is.EqualTo(1));
            return span[0].Text ?? string.Empty;
        }

        private static PresentationTextCatalog CreatePlateCatalog()
        {
            var tokenIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int plateId = tokenIds.Register("hud.hero.plate");
            int nameId = tokenIds.Register("entity.guan");
            var tokens = new PresentationTextTokenDefinition[tokenIds.Count + 1];
            tokens[plateId] = new PresentationTextTokenDefinition { TokenId = plateId, Key = "hud.hero.plate", ArgCount = 2 };
            tokens[nameId] = new PresentationTextTokenDefinition { TokenId = nameId, Key = "entity.guan", ArgCount = 0 };

            var localeIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            int en = localeIds.Register("en-US");
            int zh = localeIds.Register("zh-CN");
            var enTemplates = new PresentationTextTemplate[tokenIds.Count + 1];
            var zhTemplates = new PresentationTextTemplate[tokenIds.Count + 1];
            enTemplates[plateId] = Plate("Lv", " ");
            zhTemplates[plateId] = Plate("Lv", " ");
            enTemplates[nameId] = Literal("Guan Yu");
            zhTemplates[nameId] = Literal("关羽");
            var locales = new PresentationTextLocaleTable[localeIds.Count + 1];
            locales[en] = new PresentationTextLocaleTable(en, "en-US", enTemplates);
            locales[zh] = new PresentationTextLocaleTable(zh, "zh-CN", zhTemplates);
            return new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: en);
        }

        private static PresentationTextTemplate Literal(string text)
        {
            return new PresentationTextTemplate(
                text,
                new[] { new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, text, -1) });
        }

        private static PresentationTextTemplate Plate(string prefix, string gap)
        {
            return new PresentationTextTemplate(
                prefix + "{0}" + gap + "{1}",
                new[]
                {
                    new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, prefix, -1),
                    new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Argument, string.Empty, 0),
                    new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Literal, gap, -1),
                    new PresentationTextTemplatePart(PresentationTextTemplatePartKind.Argument, string.Empty, 1),
                });
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

        private sealed class CountingProjector : IScreenProjector
        {
            private readonly Vector2 _screen;

            public CountingProjector(Vector2 screen)
            {
                _screen = screen;
            }

            public int CallCount { get; private set; }

            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                CallCount++;
                return _screen;
            }
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
