using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    /// <summary>
    /// ScreenRect presenter behavior: config loading (fail-fast on unknown fields, missing
    /// corner param keys, kind-scoped fields) and the per-frame emit lane resolving the two
    /// param-driven corners into ScreenOverlayBuffer rect items.
    /// </summary>
    [TestFixture]
    public sealed class PresenterScreenRectTests
    {
        private const float Dt = 1f / 60f;
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Ludots_ScreenRect", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PresenterParamKeyRegistry.ClearCustomKeysForTests();
            PresenterScopeTagRegistry.Clear();

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
        public void Load_ParsesScreenRectBehavior_WithCornerParamKeysAndColors()
        {
            WritePresenters(
                """
                [
                  {
                    "id": "marquee",
                    "behaviors": [
                      {
                        "slot": "screenRect",
                        "kind": "ScreenRect",
                        "activeByDefault": true,
                        "screenRect": {
                          "corner0XParamKey": "rect.pressX",
                          "corner0YParamKey": "rect.pressY",
                          "corner1XParamKey": "rect.pointerX",
                          "corner1YParamKey": "rect.pointerY",
                          "fill": [0.3, 0.6, 1.0, 0.2],
                          "border": [0.3, 0.6, 1.0, 1.0]
                        }
                      }
                    ]
                  }
                ]
                """);

            PresenterDefinitionRegistry registry = Load();

            PresenterDefinition definition = registry.Get(registry.GetId("marquee"));
            Assert.That(definition.HasScreenRectBehavior, Is.True);
            Assert.That(definition.ScreenRectWorkItems.Length, Is.EqualTo(1));
            ref readonly BehaviorSlot slot = ref definition.Behaviors[0];
            Assert.That(slot.Kind, Is.EqualTo(BehaviorKind.ScreenRect));
            Assert.That(slot.SlotIndex, Is.EqualTo(18));
            Assert.That(slot.ScreenRect.Corner0XParamKey, Is.EqualTo(KeyId("rect.pressX")));
            Assert.That(slot.ScreenRect.Corner0YParamKey, Is.EqualTo(KeyId("rect.pressY")));
            Assert.That(slot.ScreenRect.Corner1XParamKey, Is.EqualTo(KeyId("rect.pointerX")));
            Assert.That(slot.ScreenRect.Corner1YParamKey, Is.EqualTo(KeyId("rect.pointerY")));
            Assert.That(slot.ScreenRect.FillColor, Is.EqualTo(new Vector4(0.3f, 0.6f, 1.0f, 0.2f)));
            Assert.That(slot.ScreenRect.BorderColor, Is.EqualTo(new Vector4(0.3f, 0.6f, 1.0f, 1.0f)));
        }

        [Test]
        public void Load_ScreenRectRequiresAllFourCornerParamKeys()
        {
            WritePresenters(
                """
                [
                  {
                    "id": "marquee",
                    "behaviors": [
                      {
                        "slot": "screenRect",
                        "kind": "ScreenRect",
                        "screenRect": {
                          "corner0XParamKey": "rect.pressX",
                          "corner0YParamKey": "rect.pressY",
                          "corner1XParamKey": "rect.pointerX"
                        }
                      }
                    ]
                  }
                ]
                """);

            Assert.Throws<InvalidOperationException>(() => Load());
        }

        [Test]
        public void Load_ScreenRectRejectsUnknownField()
        {
            WritePresenters(RectPresenter(""" "corner0XParamKey": "a", "borderWidth": 2 """));
            Assert.Throws<InvalidOperationException>(() => Load());
        }

        [TestCase("AssetBinding")]
        [TestCase("MinimapMarker")]
        [TestCase("AttributeBinding")]
        public void Load_NonScreenRectBehaviorRejectsScreenRectScopedField(string kind)
        {
            WritePresenters(
                $$"""
                [
                  {
                    "id": "marquee",
                    "behaviors": [
                      {
                        "slot": "screenRect",
                        "kind": "{{kind}}",
                        "screenRect": { "corner0XParamKey": "a", "corner0YParamKey": "b", "corner1XParamKey": "c", "corner1YParamKey": "d" }
                      }
                    ]
                  }
                ]
                """);

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => Load());
            Assert.That(ex.Message, Does.Contain("field 'screenRect' is not valid for this behavior kind"));
        }

        [Test]
        public void Emit_WritesNormalizedRectFromParamCorners_AndFollowsParamChanges()
        {
            (World world, PresenterDefinitionRegistry definitions, Entity presenter, ScreenOverlayBuffer overlay, PresenterScreenRectSystem system) =
                BuildEmitHarness(corner0: new Vector2(100f, 40f), corner1: new Vector2(40f, 200f));

            system.Update(Dt);

            Assert.That(overlay.Count, Is.EqualTo(1), "an active ScreenRect behavior emits one rect item per frame");
            ref readonly ScreenOverlayItem item = ref overlay.GetSpan()[0];
            Assert.That(item.Kind, Is.EqualTo(ScreenOverlayItemKind.Rect));
            Assert.That(item.X, Is.EqualTo(40), "corners normalize: min X of the two corners");
            Assert.That(item.Y, Is.EqualTo(40));
            Assert.That(item.Width, Is.EqualTo(60));
            Assert.That(item.Height, Is.EqualTo(160));
            Assert.That(item.BackgroundColor, Is.EqualTo(new Vector4(0.3f, 0.6f, 1.0f, 0.2f)));
            Assert.That(item.Color, Is.EqualTo(new Vector4(0.3f, 0.6f, 1.0f, 1.0f)));

            // 数据侧改角点 → 下一帧矩形跟随（presenter 只渲染）。
            ref PresenterFloatParams floats = ref world.Get<PresenterFloatParams>(presenter);
            floats.Set(KeyId("rect.pointerX"), 360f);
            floats.Set(KeyId("rect.pointerY"), 240f);
            system.Update(Dt);

            ref readonly ScreenOverlayItem follow = ref overlay.GetSpan()[1];
            Assert.That(follow.X, Is.EqualTo(100));
            Assert.That(follow.Y, Is.EqualTo(40));
            Assert.That(follow.Width, Is.EqualTo(260));
            Assert.That(follow.Height, Is.EqualTo(200));

            world.Dispose();
        }

        [Test]
        public void Emit_SkipsWhenBehaviorInactive_MissingCornerParam_OrDegenerateRect()
        {
            (World world, PresenterDefinitionRegistry definitions, Entity presenter, ScreenOverlayBuffer overlay, PresenterScreenRectSystem system) =
                BuildEmitHarness(corner0: new Vector2(10f, 10f), corner1: new Vector2(10.4f, 10.4f));

            system.Update(Dt);
            Assert.That(overlay.Count, Is.EqualTo(0), "a sub-pixel rect renders nothing");

            ref PresenterFloatParams floats = ref world.Get<PresenterFloatParams>(presenter);
            floats.Set(KeyId("rect.pointerX"), 400f);
            floats.Set(KeyId("rect.pointerY"), 300f);
            floats.Set(KeyId("rect.pressX"), float.NaN);
            system.Update(Dt);
            Assert.That(overlay.Count, Is.EqualTo(0), "an unresolvable corner param renders nothing (fail closed)");

            world.Dispose();
        }

        private (World world, PresenterDefinitionRegistry definitions, Entity presenter, ScreenOverlayBuffer overlay, PresenterScreenRectSystem system) BuildEmitHarness(
            Vector2 corner0,
            Vector2 corner1)
        {
            var world = World.Create();
            var definitions = new PresenterDefinitionRegistry();
            int defId = definitions.Register("rect.marquee", new PresenterDefinition
            {
                Behaviors =
                [
                    new BehaviorSlot
                    {
                        SlotIndex = 18,
                        Kind = BehaviorKind.ScreenRect,
                        ActiveByDefault = true,
                        ScreenRect = new ScreenRectConfig
                        {
                            FillColor = new Vector4(0.3f, 0.6f, 1.0f, 0.2f),
                            BorderColor = new Vector4(0.3f, 0.6f, 1.0f, 1.0f),
                            Corner0XParamKey = PresenterParamKeyRegistry.Register("rect.pressX"),
                            Corner0YParamKey = PresenterParamKeyRegistry.Register("rect.pressY"),
                            Corner1XParamKey = PresenterParamKeyRegistry.Register("rect.pointerX"),
                            Corner1YParamKey = PresenterParamKeyRegistry.Register("rect.pointerY"),
                        },
                    },
                ],
            });

            var runtime = new PresenterEntityRuntime(world);
            runtime.BindDefinitions(definitions);
            Entity owner = world.Create();
            Entity presenter = runtime.CreateHierarchy(
                definitions, defId, owner, scopeId: 1, PresentationAnchorKind.Entity,
                worldPosition: Vector3.Zero, stableId: 9100, parent: Entity.Null,
                definitions.Get(defId));

            ref PresenterFloatParams floats = ref world.Get<PresenterFloatParams>(presenter);
            floats.Set(KeyId("rect.pressX"), corner0.X);
            floats.Set(KeyId("rect.pressY"), corner0.Y);
            floats.Set(KeyId("rect.pointerX"), corner1.X);
            floats.Set(KeyId("rect.pointerY"), corner1.Y);

            var overlay = new ScreenOverlayBuffer();
            var seats = new ClientLocalSeatRegistry();
            seats.Add(new ClientLocalSeat("test"));
            seats.SetPossession("test", 1, owner);
            var globals = new Dictionary<string, object>
            {
                [CoreServiceKeys.ClientLocalSeatRegistry.Name] = seats,
            };
            var system = new PresenterScreenRectSystem(world, definitions, overlay, globals);
            return (world, definitions, presenter, overlay, system);
        }

        private static int KeyId(string key)
        {
            return PresenterParamKeyRegistry.TryGetId(key, out int id) ? id : PresenterParamKeyRegistry.Register(key);
        }

        private static string RectPresenter(string screenRectFields)
        {
            return
                "[ { \"id\": \"marquee\", \"behaviors\": [ { \"slot\": \"screenRect\", \"kind\": \"ScreenRect\", " +
                "\"screenRect\": { " + screenRectFields + " } } ] } ]";
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
