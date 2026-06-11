using System;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
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
        public void PerformerDefinitionConfigLoader_ResolvesWorldTextDefaultTextId_ToStableId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""entity_world_text"",
    ""visualKind"": ""WorldText"",
    ""defaultTextId"": ""hud.current_over_base"",
    ""worldTextValueMode"": ""AttributeCurrentOverBase"",
    ""bindings"": [
      { ""paramKey"": 0, ""source"": ""Constant"", ""constantValue"": 7 }
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

            Assert.That(definition.DefaultTextId, Is.EqualTo(42));
            Assert.That(definition.WorldTextValueMode, Is.EqualTo(WorldHudValueMode.AttributeCurrentOverBase));
            Assert.That(definition.Bindings.Length, Is.EqualTo(1));
            Assert.That(definition.Bindings[0].ParamKey, Is.EqualTo(0));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenWorldTextUsesTextTokenBinding()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""entity_world_text"",
    ""visualKind"": ""WorldText"",
    ""bindings"": [
      { ""paramKey"": 15, ""source"": ""TextToken"", ""textToken"": ""hud.current_over_base"" }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("defaultTextId"));
        }

        [TestCase(15, "defaultTextId")]
        [TestCase(16, "worldTextValueMode")]
        public void PerformerDefinitionConfigLoader_Fails_WhenWorldTextBindingTargetsReservedTokenOrModeParam(
            int paramKey,
            string expectedMessage)
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""entity_world_text"",
    ""visualKind"": ""WorldText"",
    ""defaultTextId"": ""hud.current_over_base"",
    ""bindings"": [
      { ""paramKey"": " + paramKey + @", ""source"": ""Constant"", ""constantValue"": 42 }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveTextTokenId: key => string.Equals(key, "hud.current_over_base", StringComparison.Ordinal) ? 42 : 0);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain(expectedMessage));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenLegacyEntityFilterFieldsArePresent()
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
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("entityScope"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenLegacyMaxVisibilityDistanceIsPresent()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""distance_culled_marker"",
    ""visualKind"": ""Marker3D"",
    ""maxVisibilityDistanceCm"": 5000
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("maxVisibilityDistanceCm"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenCommandUsesRemovedFields()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""legacy_command_marker"",
    ""visualKind"": ""GroundOverlay"",
    ""rules"": [
      {
        ""event"": { ""kind"": ""EntitySpawned"" },
        ""command"": { ""commandKind"": ""CreatePerformer"", ""performerDefinitionId"": ""legacy_command_marker"" }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("commandKind"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenScopedCommandOmitsScopeSource()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""scoped_marker"",
    ""visualKind"": ""GroundOverlay"",
    ""rules"": [
      {
        ""event"": { ""kind"": ""EntitySpawned"" },
        ""command"": { ""kind"": ""CreatePerformer"", ""definitionId"": ""scoped_marker"" }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("scopeSource"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenSetParamOmitsParamLane()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""implicit_set_param"",
    ""visualKind"": ""GroundOverlay"",
    ""rules"": [
      {
        ""event"": { ""kind"": ""EffectApplied"" },
        ""command"": { ""kind"": ""SetParam"", ""paramKey"": 4, ""paramValue"": 1.0 }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("paramLane"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenParamDefaultInfersLaneOrUsesRemovedValue()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""implicit_param_default"",
    ""visualKind"": ""GroundOverlay"",
    ""paramDefaults"": [
      { ""paramKey"": 1, ""value"": 4.0 }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("value"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenInheritedBindingOmitsParamKey()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""base_overlay"",
    ""visualKind"": ""GroundOverlay"",
    ""bindings"": [
      { ""paramKey"": 4, ""source"": ""Constant"", ""constantValue"": 1.0 }
    ]
  },
  {
    ""id"": ""child_overlay"",
    ""extends"": ""base_overlay"",
    ""bindings"": [
      { ""source"": ""Constant"", ""constantValue"": 0.5 }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("paramKey"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenInheritedBehaviorOmitsSlot()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""base_marker"",
    ""visualKind"": ""Marker3D"",
    ""meshOrShapeId"": ""mesh.cube"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""AssetBinding"",
        ""activeByDefault"": true,
        ""assetBinding"": {
          ""assetKind"": ""Mesh"",
          ""assetId"": ""mesh.cube"",
          ""materialId"": ""default_surface"",
          ""renderPath"": ""StaticMesh"",
          ""mobility"": ""Static""
        }
      }
    ]
  },
  {
    ""id"": ""child_marker"",
    ""extends"": ""base_marker"",
    ""behaviors"": [
      { ""kind"": ""Material"", ""activeByDefault"": true, ""material"": { ""baseMaterialId"": ""default_surface"" } }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMeshId: key => string.Equals(key, "mesh.cube", StringComparison.Ordinal) ? 1 : 0,
                resolveBehaviorAssetId: (_, key) => string.Equals(key, "mesh.cube", StringComparison.Ordinal) ? 1 : 0,
                resolveMaterialId: key => string.Equals(key, "default_surface", StringComparison.Ordinal) ? 1 : 0,
                resolveMaterialDomain: id => id == 1 ? MaterialAssetDomain.Mesh : null);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("slot"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_ParsesFacingRadiansBinding()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""line_overlay"",
    ""visualKind"": ""GroundOverlay"",
    ""bindings"": [
      { ""paramKey"": 3, ""source"": ""FacingRadians"" }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            int defId = registry.GetId("line_overlay");
            Assert.That(defId, Is.GreaterThan(0));
            Assert.That(registry.TryGet(defId, out var def), Is.True);
            Assert.That(def.Bindings.Length, Is.EqualTo(1));
            Assert.That(def.Bindings[0].ParamKey, Is.EqualTo(3));
            Assert.That(def.Bindings[0].Value.Source, Is.EqualTo(ValueSourceKind.FacingRadians));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_ParsesEntityColorChannelBinding()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""team_tint"",
    ""visualKind"": ""GroundOverlay"",
    ""bindings"": [
      { ""paramKey"": 4, ""source"": ""EntityColor"", ""channel"": ""Red"" },
      { ""paramKey"": 5, ""source"": ""EntityColor"", ""channel"": ""Green"" },
      { ""paramKey"": 6, ""source"": ""EntityColor"", ""channel"": ""Blue"" },
      { ""paramKey"": 7, ""source"": ""EntityColor"", ""channel"": ""Alpha"" }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            int defId = registry.GetId("team_tint");
            Assert.That(registry.TryGet(defId, out var def), Is.True);
            Assert.That(def.Bindings.Length, Is.EqualTo(4));
            Assert.That(def.Bindings[0].Value.SourceId, Is.EqualTo(0));
            Assert.That(def.Bindings[1].Value.SourceId, Is.EqualTo(1));
            Assert.That(def.Bindings[2].Value.SourceId, Is.EqualTo(2));
            Assert.That(def.Bindings[3].Value.SourceId, Is.EqualTo(3));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_ParsesGraphBindingProgramId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""graph_driven_overlay"",
    ""visualKind"": ""GroundOverlay"",
    ""bindings"": [
      { ""paramKey"": 0, ""source"": ""Graph"", ""graphProgramId"": 17 }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            int defId = registry.GetId("graph_driven_overlay");
            Assert.That(registry.TryGet(defId, out var def), Is.True);
            Assert.That(def.Bindings[0].Value.Source, Is.EqualTo(ValueSourceKind.Graph));
            Assert.That(def.Bindings[0].Value.SourceId, Is.EqualTo(17));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenBindingSourceCasingDoesNotMatchContract()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""line_overlay"",
    ""visualKind"": ""GroundOverlay"",
    ""bindings"": [
      { ""paramKey"": 3, ""source"": ""facingradians"" }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("case-sensitive"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenMeshReferenceUsesNumericId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""numeric_mesh_marker"",
    ""visualKind"": ""Marker3D"",
    ""meshOrShapeId"": 1
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("meshId must be a registered string key"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenAttributeReferenceUsesNumericId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""numeric_attribute_bar"",
    ""visualKind"": ""WorldBar"",
    ""bindings"": [
      { ""paramKey"": 0, ""source"": ""Attribute"", ""sourceId"": 1 }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("attribute reference must be a registered string key"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenAttributeNameIsUnknown()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""unknown_attribute_bar"",
    ""visualKind"": ""WorldBar"",
    ""bindings"": [
      { ""paramKey"": 0, ""source"": ""Attribute"", ""attributeName"": ""Tests.Presentation.Semantic.DoesNotExist"" }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("unknown attributeName"));
            Assert.That(ex.Message, Does.Contain("Tests.Presentation.Semantic.DoesNotExist"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenEntityColorUsesRemovedSourceId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""numeric_color_channel"",
    ""visualKind"": ""GroundOverlay"",
    ""bindings"": [
      { ""paramKey"": 4, ""source"": ""EntityColor"", ""sourceId"": 0 }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("EntityColor binding sourceId was removed"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenGraphUsesRemovedSourceId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""numeric_graph_binding"",
    ""visualKind"": ""GroundOverlay"",
    ""bindings"": [
      { ""paramKey"": 0, ""source"": ""Graph"", ""sourceId"": 17 }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("Graph binding sourceId was removed"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenEventUsesLegacyKeyId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""legacy_event_keyid"",
    ""visualKind"": ""Marker3D"",
    ""meshOrShapeId"": ""mesh.cube"",
    ""rules"": [
      {
        ""event"": { ""kind"": ""EntitySpawned"", ""keyId"": 1 },
        ""command"": { ""kind"": ""DestroyPerformerScope"" }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveMeshId: key => string.Equals(key, "mesh.cube", StringComparison.Ordinal) ? 1 : 0);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("keyId was removed"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_ParsesTagBindingTagId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""tagid_binding"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""TagBinding"",
        ""activeByDefault"": true,
        ""tagBinding"": {
          ""tagId"": ""Tests.Presentation.Working"",
          ""targetParamKey"": 1
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            loader.Load(catalog);

            int defId = registry.GetId("tagid_binding");
            Assert.That(registry.TryGet(defId, out var def), Is.True);
            Assert.That(def.Behaviors.Length, Is.EqualTo(1));
            Assert.That(def.Behaviors[0].TagBinding.TagId, Is.EqualTo(TagRegistry.GetId("Tests.Presentation.Working")));
            Assert.That(def.Behaviors[0].TagBinding.TargetParamKey, Is.EqualTo(1));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenTagBindingUsesLegacyTag()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""legacy_tag_binding"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""TagBinding"",
        ""activeByDefault"": true,
        ""tagBinding"": {
          ""tag"": ""Tests.Presentation.Working"",
          ""targetParamKey"": 1
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("TagBinding.tag was removed"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenTagBindingTagIdUsesNumericId()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""numeric_tagid_binding"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""TagBinding"",
        ""activeByDefault"": true,
        ""tagBinding"": {
          ""tagId"": 1,
          ""targetParamKey"": 1
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("not numeric id"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenRawPerformerIdsDifferOnlyByCase()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  { ""id"": ""case_marker"", ""visualKind"": ""GroundOverlay"" }
]");

            string directDir = Path.Combine(_root, "Core", "Presentation");
            Directory.CreateDirectory(directDir);
            File.WriteAllText(Path.Combine(directDir, "performers.json"),
                @"[
  { ""id"": ""Case_Marker"", ""visualKind"": ""GroundOverlay"" }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = new PerformerDefinitionConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("differs only by case"));
        }

        [Test]
        public void PerformerAssetKindContract_DefinesHostOwnedSurfaceAsStableSemanticKind()
        {
            AssetKind[] values = Enum.GetValues<AssetKind>();

            Assert.That(values.Length, Is.EqualTo(10));
            Assert.That(values, Does.Contain(AssetKind.Surface));
            Assert.That((byte)AssetKind.Surface, Is.EqualTo(10));
            Assert.That(VisualRenderPath.Surface.IsSurfaceLane(), Is.True);
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenMaterialCustomDataIsNotArray()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""bad_custom_data_shape"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""AssetBinding"",
        ""activeByDefault"": true,
        ""assetBinding"": {
          ""assetKind"": ""Mesh"",
          ""assetId"": ""mesh.cube"",
          ""materialId"": ""mat.mesh"",
          ""renderPath"": ""StaticMesh"",
          ""mobility"": ""Static"",
          ""materialCustomData"": { ""slot"": 0 }
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = CreatePerformerLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("materialCustomData must be an array"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenMaterialDoesNotDeclareCustomDataSupport()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""mesh_custom_data_without_material_capability"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""AssetBinding"",
        ""activeByDefault"": true,
        ""assetBinding"": {
          ""assetKind"": ""Mesh"",
          ""assetId"": ""mesh.cube"",
          ""materialId"": ""mat.mesh"",
          ""renderPath"": ""StaticMesh"",
          ""mobility"": ""Static"",
          ""materialCustomData"": [
            { ""slot"": 0, ""defaultValue"": [1, 2, 3, 4] }
          ]
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = CreatePerformerLoader(pipeline, registry, materialSupportsCustomData: false);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("SupportsPerInstanceCustomData"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenUnsupportedAssetKindDeclaresCustomData()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""spline_custom_data_is_invalid"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""AssetBinding"",
        ""activeByDefault"": true,
        ""assetBinding"": {
          ""assetKind"": ""Spline"",
          ""assetId"": ""spline.path"",
          ""renderPath"": ""StaticMesh"",
          ""mobility"": ""Static"",
          ""materialCustomData"": [
            { ""slot"": 0, ""defaultValue"": [1, 0, 0, 1] }
          ]
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = CreatePerformerLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("cannot consume materialCustomData"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenSurfaceUsesNonSurfaceRenderPath()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""surface_on_static_lane"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""AssetBinding"",
        ""activeByDefault"": true,
        ""assetBinding"": {
          ""assetKind"": ""Surface"",
          ""assetId"": ""surface.heightfield"",
          ""materialId"": ""mat.surface"",
          ""renderPath"": ""StaticMesh"",
          ""mobility"": ""Static"",
          ""surfaceLayerKey"": ""terrain.visual""
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = CreatePerformerLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("requires renderPath 'Surface'"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_Fails_WhenNonSurfaceDeclaresSurfaceMetadata()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""mesh_with_surface_metadata"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""AssetBinding"",
        ""activeByDefault"": true,
        ""assetBinding"": {
          ""assetKind"": ""Mesh"",
          ""assetId"": ""mesh.cube"",
          ""materialId"": ""mat.mesh"",
          ""renderPath"": ""InstancedStaticMesh"",
          ""mobility"": ""Static"",
          ""surfaceLayerKey"": ""terrain.visual"",
          ""sortId"": 7
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = CreatePerformerLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("surfaceLayerKey is only valid for Surface"));
        }

        [Test]
        public void PerformerDefinitionConfigLoader_ParsesSurfaceAssetBinding_AsHostOwnedSurfaceLane()
        {
            WriteFile("Core", "config_catalog.json",
                @"[{ ""Path"": ""Presentation/performers.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }]");
            WriteFile("Core", "Presentation/performers.json",
                @"[
  {
    ""id"": ""terrain_surface"",
    ""behaviors"": [
      {
        ""slot"": 0,
        ""kind"": ""AssetBinding"",
        ""activeByDefault"": true,
        ""assetBinding"": {
          ""assetKind"": ""Surface"",
          ""assetId"": ""surface.heightfield"",
          ""materialId"": ""mat.surface"",
          ""renderPath"": ""Surface"",
          ""mobility"": ""Static"",
          ""surfaceLayerKey"": ""terrain.visual"",
          ""sortId"": 7
        }
      }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PerformerDefinitionRegistry();
            var loader = CreatePerformerLoader(pipeline, registry);

            loader.Load(catalog);

            int defId = registry.GetId("terrain_surface");
            Assert.That(registry.TryGet(defId, out var definition), Is.True);
            Assert.That(definition.Behaviors.Length, Is.EqualTo(1));
            ref readonly var binding = ref definition.Behaviors[0].AssetBinding;
            Assert.That(binding.AssetKind, Is.EqualTo(AssetKind.Surface));
            Assert.That(binding.RenderPath, Is.EqualTo(VisualRenderPath.Surface));
            Assert.That(binding.SurfaceLayerKey, Is.EqualTo("terrain.visual"));
            Assert.That(binding.SortId, Is.EqualTo(7));
        }

        [Test]
        public void PresentationHostAssetConfigLoader_BindsBackendMaterialUris_FromHostAssetsOnly()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/material_assets.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/host_assets.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");
            WriteFile("Core", "Presentation/material_assets.json",
                @"[
  {
    ""id"": ""mat.surface"",
    ""domain"": ""Surface"",
    ""flags"": [""SupportsPerInstanceCustomData""]
  }
]");
            WriteFile("Core", "Presentation/host_assets.json",
                @"[
  {
    ""id"": ""mat.surface.raylib"",
    ""backendId"": ""raylib"",
    ""assetKind"": ""Material"",
    ""assetId"": ""mat.surface"",
    ""sourceUris"": [""raylib.material:mat.surface""]
  },
  {
    ""id"": ""mat.surface.ue5"",
    ""backendId"": ""ue5"",
    ""assetKind"": ""Material"",
    ""assetId"": ""mat.surface"",
    ""sourceUris"": [""ue5.material:/Game/Ludots/Materials/M_Surface""]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var materials = new PresentationMaterialRegistry();

            new PresentationMaterialConfigLoader(pipeline, materials).Load(catalog);
            int materialId = materials.GetId("mat.surface");
            Assert.That(materials.TryGet(materialId, out var semanticDescriptor), Is.True);
            Assert.That(semanticDescriptor.SourceUris, Is.Empty);

            new PresentationHostAssetConfigLoader(pipeline, materials, "ue5").Load(catalog);

            Assert.That(materials.TryGet(materialId, out var backendDescriptor), Is.True);
            Assert.That(backendDescriptor.Domain, Is.EqualTo(MaterialAssetDomain.Surface));
            Assert.That(
                backendDescriptor.Flags & MaterialAssetFlags.SupportsPerInstanceCustomData,
                Is.EqualTo(MaterialAssetFlags.SupportsPerInstanceCustomData));
            Assert.That(backendDescriptor.SourceUris, Is.EqualTo(new[] { "ue5.material:/Game/Ludots/Materials/M_Surface" }));
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
            var strings = new WorldHudStringTable(textCatalog, selection, dynamicCapacity: 4);

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
                var expectedText = PresentationTextPacket.FromWorldHudValue(
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
        public void PresentationSemanticCatalogLoader_LoadsAttributesAndMappings_AndFormatsSemanticValues()
        {
            int healthId = AttributeRegistry.Register("Tests.Presentation.Semantic.Health");

            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" },
  { ""Path"": ""Presentation/semantic_attributes.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/semantic_mappings.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""semantic.health.label"", ""argCount"": 0 },
  { ""id"": ""semantic.health.current"", ""argCount"": 1 },
  { ""id"": ""semantic.health.current_over_base"", ""argCount"": 2 },
  { ""id"": ""semantic.health.constant"", ""argCount"": 1 },
  { ""id"": ""semantic.unit.hp"", ""argCount"": 0 },
  { ""id"": ""semantic.relationship.label"", ""argCount"": 0 },
  { ""id"": ""semantic.relationship.friendly"", ""argCount"": 0 },
  { ""id"": ""semantic.relationship.hostile"", ""argCount"": 0 },
  { ""id"": ""semantic.relationship.neutral"", ""argCount"": 0 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""semantic.health.label"": ""Health"",
      ""semantic.health.current"": ""{0}"",
      ""semantic.health.current_over_base"": ""{0}/{1}"",
      ""semantic.health.constant"": ""{0}"",
      ""semantic.unit.hp"": ""HP"",
      ""semantic.relationship.label"": ""Relationship"",
      ""semantic.relationship.friendly"": ""Friendly"",
      ""semantic.relationship.hostile"": ""Hostile"",
      ""semantic.relationship.neutral"": ""Neutral""
    }
  }
}");
            WriteFile("Core", "Presentation/semantic_attributes.json",
                @"[
  {
    ""id"": ""unit.health"",
    ""attribute"": ""Tests.Presentation.Semantic.Health"",
    ""labelToken"": ""semantic.health.label"",
    ""currentFormatToken"": ""semantic.health.current"",
    ""currentOverBaseFormatToken"": ""semantic.health.current_over_base"",
    ""constantFormatToken"": ""semantic.health.constant"",
    ""unitToken"": ""semantic.unit.hp""
  }
]");
            WriteFile("Core", "Presentation/semantic_mappings.json",
                @"[
  {
    ""id"": ""team.relationship"",
    ""labelToken"": ""semantic.relationship.label"",
    ""values"": {
      ""team.relationship.friendly"": ""semantic.relationship.friendly"",
      ""team.relationship.hostile"": ""semantic.relationship.hostile"",
      ""team.relationship.neutral"": ""semantic.relationship.neutral""
    }
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var textLoader = new PresentationTextCatalogLoader(pipeline);
            PresentationTextCatalog textCatalog = textLoader.Load(catalog);
            var localeSelection = new PresentationTextLocaleSelection(textCatalog);
            var semanticLoader = new PresentationSemanticCatalogLoader(pipeline, textCatalog);
            PresentationSemanticCatalog semanticCatalog = semanticLoader.Load(catalog);
            var resolver = new PresentationSemanticResolver(textCatalog, localeSelection, semanticCatalog);

            Assert.That(semanticCatalog.TryGetAttribute("unit.health", out var attribute), Is.True);
            Assert.That(attribute.AttributeId, Is.EqualTo(healthId));
            Assert.That(semanticCatalog.TryGetAttribute(healthId, out var byIdAttribute), Is.True);
            Assert.That(byIdAttribute.SemanticKey, Is.EqualTo("unit.health"));
            Assert.That(semanticCatalog.TryGetMapping(WellKnownPresentationSemanticMappingKeys.TeamRelationship, out var mapping), Is.True);
            Assert.That(mapping.TryGetValueTokenId(WellKnownPresentationSemanticMappingKeys.TeamRelationshipFriendly, out int friendlyTokenId), Is.True);
            Assert.That(textCatalog.GetTokenKey(friendlyTokenId), Is.EqualTo("semantic.relationship.friendly"));

            Assert.That(resolver.ResolveAttributeLabelRequired("unit.health"), Is.EqualTo("Health"));
            Assert.That(
                resolver.FormatAttributeValueRequired("unit.health", PresentationAttributeValueDisplayKind.CurrentOverBase, 75f, 100f),
                Is.EqualTo("75/100 HP"));
            Assert.That(resolver.ResolveMappingLabelRequired(WellKnownPresentationSemanticMappingKeys.TeamRelationship), Is.EqualTo("Relationship"));
            Assert.That(
                resolver.ResolveMappedValueRequired(
                    WellKnownPresentationSemanticMappingKeys.TeamRelationship,
                    WellKnownPresentationSemanticMappingKeys.TeamRelationshipFriendly),
                Is.EqualTo("Friendly"));
        }

        [Test]
        public void PresentationSemanticCatalogLoader_ResolvesAttributesRegisteredFromGameConstants()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/text_tokens.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" },
  { ""Path"": ""Presentation/text_locales.json"", ""Policy"": ""DeepObject"" },
  { ""Path"": ""Presentation/semantic_attributes.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");
            WriteFile("Core", "Presentation/text_tokens.json",
                @"[
  { ""id"": ""semantic.health.label"", ""argCount"": 0 },
  { ""id"": ""semantic.health.current"", ""argCount"": 1 },
  { ""id"": ""semantic.health.current_over_base"", ""argCount"": 2 },
  { ""id"": ""semantic.health.constant"", ""argCount"": 1 }
]");
            WriteFile("Core", "Presentation/text_locales.json",
                @"{
  ""defaultLocale"": ""en-US"",
  ""locales"": {
    ""en-US"": {
      ""semantic.health.label"": ""Health"",
      ""semantic.health.current"": ""{0}"",
      ""semantic.health.current_over_base"": ""{0}/{1}"",
      ""semantic.health.constant"": ""{0}""
    }
  }
}");
            WriteFile("Core", "Presentation/semantic_attributes.json",
                @"[
  {
    ""id"": ""unit.health"",
    ""attribute"": ""Tests.Presentation.Semantic.FromConfig"",
    ""labelToken"": ""semantic.health.label"",
    ""currentFormatToken"": ""semantic.health.current"",
    ""currentOverBaseFormatToken"": ""semantic.health.current_over_base"",
    ""constantFormatToken"": ""semantic.health.constant""
  }
]");

            int healthId = AttributeRegistry.Register("Tests.Presentation.Semantic.FromConfig");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var textCatalog = new PresentationTextCatalogLoader(pipeline).Load(catalog);
            var semanticCatalog = new PresentationSemanticCatalogLoader(pipeline, textCatalog).Load(catalog);

            Assert.That(semanticCatalog.TryGetAttribute("unit.health", out var attribute), Is.True);
            Assert.That(attribute.AttributeId, Is.EqualTo(healthId));
        }

        [Test]
        public void PresentationImageConfigLoader_LoadsImageAssets_AndResolvesVfsLocatorToAbsolutePath()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/image_assets.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");
            WriteFile("Core", "Presentation/image_assets.json",
                @"[
  {
    ""id"": ""portrait.commander"",
    ""assetKind"": ""Portrait2D"",
    ""locators"": [
      { ""backendId"": ""raylib"", ""assetRef"": ""Core:assets/Presentation/portraits/commander.svg"" }
    ]
  }
]");

            string portraitDir = Path.Combine(_root, "Core", "assets", "Presentation", "portraits");
            Directory.CreateDirectory(portraitDir);
            string portraitPath = Path.Combine(portraitDir, "commander.svg");
            File.WriteAllText(portraitPath, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 8 8\"><rect width=\"8\" height=\"8\" fill=\"#123456\"/></svg>");

            var (vfs, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PresentationImageRegistry();
            var loader = new PresentationImageConfigLoader(pipeline, registry);
            loader.Load(catalog);

            int imageAssetId = registry.GetId("portrait.commander");
            Assert.That(imageAssetId, Is.GreaterThan(0));
            Assert.That(registry.TryGet(imageAssetId, out var image), Is.True);
            Assert.That(image.AssetKind, Is.EqualTo(PresentationImageAssetKind.Portrait2D));

            var resolver = new PresentationImageSourceResolver(registry, vfs, "raylib");
            string resolved = resolver.ResolveRequiredSource(imageAssetId);
            Assert.That(resolved, Is.EqualTo(Path.GetFullPath(portraitPath)));
            Assert.That(File.Exists(resolved), Is.True);
        }

        [Test]
        public void PresentationImageConfigLoader_Fails_WhenLegacyFallbackFieldIsPresent()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/image_assets.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");
            WriteFile("Core", "Presentation/image_assets.json",
                @"[
  {
    ""id"": ""portrait.commander"",
    ""assetKind"": ""Portrait2D"",
    ""fallbackGlyph"": ""CM"",
    ""locators"": [
      { ""backendId"": ""raylib"", ""assetRef"": ""Core:assets/Presentation/portraits/commander.svg"" }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PresentationImageRegistry();
            var loader = new PresentationImageConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("removed field 'fallbackGlyph'"));
        }

        [Test]
        public void PresentationImageConfigLoader_Fails_WhenAssetKindIsMissing()
        {
            WriteFile("Core", "config_catalog.json",
                @"[
  { ""Path"": ""Presentation/image_assets.json"", ""Policy"": ""ArrayById"", ""IdField"": ""id"" }
]");
            WriteFile("Core", "Presentation/image_assets.json",
                @"[
  {
    ""id"": ""portrait.commander"",
    ""locators"": [
      { ""backendId"": ""raylib"", ""assetRef"": ""Core:assets/Presentation/portraits/commander.svg"" }
    ]
  }
]");

            var (_, _, pipeline, catalog) = BuildPipeline(_root);
            var registry = new PresentationImageRegistry();
            var loader = new PresentationImageConfigLoader(pipeline, registry);

            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load(catalog));
            Assert.That(ex!.Message, Does.Contain("missing required 'assetKind'"));
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

        private static PerformerDefinitionConfigLoader CreatePerformerLoader(
            ConfigPipeline pipeline,
            PerformerDefinitionRegistry registry,
            bool materialSupportsCustomData = true)
        {
            return new PerformerDefinitionConfigLoader(
                pipeline,
                registry,
                resolveBehaviorAssetId: (kind, key) => key switch
                {
                    "mesh.cube" when kind == AssetKind.Mesh => 10,
                    "spline.path" when kind == AssetKind.Spline => 20,
                    "surface.heightfield" when kind == AssetKind.Surface => 30,
                    _ => 0,
                },
                resolveMaterialId: key => key switch
                {
                    "mat.mesh" => 100,
                    "mat.surface" => 200,
                    _ => 0,
                },
                materialSupportsCustomData: materialId => materialSupportsCustomData && materialId == 100,
                resolveMaterialDomain: materialId => materialId switch
                {
                    100 => MaterialAssetDomain.Mesh,
                    200 => MaterialAssetDomain.Surface,
                    _ => null,
                });
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
