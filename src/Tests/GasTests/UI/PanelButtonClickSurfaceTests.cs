using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Input;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Panels;
using System.Text.Json.Nodes;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    /// <summary>
    /// Retained-surface click chain for Button controls: the composed builder carries the
    /// binder's OnClick into the materialized scene as an action handle, the host's pointer
    /// down/up at the button's hit point dispatches it, and the binder's baked payload is
    /// what reaches the event sink — the same chain the raylib host router drives.
    /// </summary>
    [TestFixture]
    public sealed class PanelButtonClickSurfaceTests
    {
        private const string TemplateId = "tests.panel.click";

        private World _world = null!;
        private Entity _scope;

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
            _world = World.Create();
            _scope = _world.Create();
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            AttributeRegistry.Clear();
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void PointerDownUpOnButton_FiresBinderWithBakedPayload()
        {
            PanelTemplate template = PanelTemplateLoader.Load(TemplateJson());
            PanelLayoutControl button = FindButton(template);
            var values = new PanelVariableSet(TemplateId, new Dictionary<string, float>(), revision: 0);
            var scope = new PanelBindingScope(values);

            var fired = new List<(string EventId, int Option)>();
            var composer = new PanelLayoutComposer();
            UiElementBuilder root = composer.ComposeControls(
                template.Layout!.Controls,
                scope,
                static _ => throw new InvalidOperationException("no images expected"),
                interactionBinder: (control, builder, bindScope) =>
                {
                    if (control.Type != PanelLayoutControlType.Button)
                    {
                        return;
                    }

                    JsonObject payload = PanelButtonPayloadBaker.Bake(template, template.Events[0], control, bindScope);
                    builder.OnClick(_ => fired.Add((template.Events[0].EventId, payload["option"]!.GetValue<int>())));
                });

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(800f, 600f);
            var surfaceHost = new UiSurfaceHost(uiRoot, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
            UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                "test:button-panel", UiSurfaceSegment.Main, priority: 0));
            surfaceHost.Publish(lease, UiSurfaceContribution.FromBuilder(
                () => new UiElementBuilder(UiNodeKind.Container)
                    .Width(300f)
                    .Height(120f)
                    .Absolute(0f, 0f)
                    .Children(root)));

            // Layout runs on input; a benign move forces the scene to lay out before scanning.
            uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Move,
                X = 1f,
                Y = 1f,
            });
            (float x, float y) = FindButtonPoint(uiRoot);

            bool down = uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                Button = PointerButton.Left,
                X = x,
                Y = y,
            });
            uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Up,
                Button = PointerButton.Left,
                X = x,
                Y = y,
            });

            Assert.That(down, Is.True, "the button must claim the pointer down (interactive node)");
            Assert.That(fired.Count, Is.EqualTo(1));
            Assert.That(fired[0].EventId, Is.EqualTo("Ui.DialogChoose"));
            Assert.That(fired[0].Option, Is.EqualTo(2), "payload baked at compose time rides the click");
        }

        [Test]
        public void PointerOnNonButtonArea_DoesNotFire()
        {
            PanelTemplate template = PanelTemplateLoader.Load(TemplateJson());
            var values = new PanelVariableSet(TemplateId, new Dictionary<string, float>(), revision: 0);
            var scope = new PanelBindingScope(values);
            var fired = 0;
            var composer = new PanelLayoutComposer();
            UiElementBuilder root = composer.ComposeControls(
                template.Layout!.Controls,
                scope,
                static _ => throw new InvalidOperationException("no images expected"),
                interactionBinder: (control, builder, _) =>
                {
                    if (control.Type == PanelLayoutControlType.Button)
                    {
                        builder.OnClick(_ => fired++);
                    }
                });

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(800f, 600f);
            var surfaceHost = new UiSurfaceHost(uiRoot, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
            UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                "test:button-panel-miss", UiSurfaceSegment.Main, priority: 0));
            surfaceHost.Publish(lease, UiSurfaceContribution.FromBuilder(
                () => new UiElementBuilder(UiNodeKind.Container)
                    .Width(300f)
                    .Height(400f)
                    .Absolute(0f, 0f)
                    .Children(root)));

            uiRoot.HandleInput(new PointerEvent
            {
                DeviceType = InputDeviceType.Mouse,
                PointerId = 0,
                Action = PointerAction.Down,
                Button = PointerButton.Left,
                X = 40f,
                Y = 380f,
            });

            Assert.That(fired, Is.Zero, "clicks off the button must not reach the binder");
        }

        [Test]
        public void HoverOverButtonWithTip_PublishesTipOverlay_NodeDisappearsOnLeave()
        {
            string json = TipTemplateJson();
            var composer = new PanelLayoutComposer();
            UiElementBuilder root = composer.ComposeControls(
                PanelTemplateLoader.Load(json).Layout!.Controls,
                new PanelBindingScope(new PanelVariableSet("tests.panel.tip", new Dictionary<string, float> { ["title"] = 0f }, revision: 0)),
                static _ => throw new InvalidOperationException("no images expected"));

            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(800f, 600f);
            var surfaceHost = new UiSurfaceHost(uiRoot, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
            UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                "test:tip-panel", UiSurfaceSegment.Main, priority: 0));
            surfaceHost.Publish(lease, UiSurfaceContribution.FromBuilder(
                () => new UiElementBuilder(UiNodeKind.Container)
                    .Width(300f)
                    .Height(120f)
                    .Absolute(0f, 0f)
                    .Children(root)));

            using var overlay = new PanelTipOverlay(surfaceHost);
            overlay.Update();

            // Force layout, then find the button and hover it.
            uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Move, X = 1f, Y = 1f });
            (float x, float y) = FindButtonPoint(uiRoot);
            uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Move, X = x, Y = y });
            uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Move, X = x + 1f, Y = y + 1f });

            UiNode? tipTitle = FindNodeByClass(uiRoot.Scene, "ui-tip-title");
            Assert.That(tipTitle, Is.Not.Null, "hovering a tipped control publishes the tip overlay");
            Assert.That(tipTitle!.TextContent, Is.EqualTo("火球术"));

            uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Move, X = 500f, Y = 500f });
            uiRoot.HandleInput(new PointerEvent { DeviceType = InputDeviceType.Mouse, PointerId = 0, Action = PointerAction.Move, X = 501f, Y = 501f });
            Assert.That(FindNodeByClass(uiRoot.Scene, "ui-tip"), Is.Null, "leaving the control releases the tip");
        }

        private static UiNode? FindNodeByClass(UiScene? scene, string className)
        {
            return scene == null ? null : FindByClass(scene.Root, className);
        }

        private static UiNode? FindByClass(UiNode? node, string className)
        {
            if (node == null)
            {
                return null;
            }

            for (int i = 0; i < node.ClassNames.Count; i++)
            {
                if (string.Equals(node.ClassNames[i], className, StringComparison.Ordinal))
                {
                    return node;
                }
            }

            for (int i = 0; i < node.Children.Count; i++)
            {
                UiNode? hit = FindByClass(node.Children[i], className);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        private static string TipTemplateJson() => """
        {
          "id": "tests.panel.tip",
          "graph": "tests.graph.tip",
          "pins": [
            { "name": "title", "key": "tests.panel.tip.title", "mode": "realtime", "default": 0 }
          ],
          "events": [
            { "eventId": "Ui.DialogChoose", "control": "choose", "gesture": "tap", "payload": { "option": "Int" } }
          ],
          "layout": {
            "controls": [
              { "type": "button", "control": "choose", "text": "选项", "payload": { "option": "2" },
                "tip": { "title": "火球术", "textBind": "title" } }
            ]
          }
        }
        """;

        private static PanelLayoutControl FindButton(PanelTemplate template)
        {
            foreach (PanelLayoutControl control in template.Layout!.Controls)
            {
                if (control.Type == PanelLayoutControlType.Button)
                {
                    return control;
                }
            }

            throw new AssertionException("no button in template");
        }

        private static (float X, float Y) FindButtonPoint(UIRoot root)
        {
            UiScene? scene = root.Scene;
            Assert.That(scene, Is.Not.Null, "surface host must have mounted the scene");
            for (float y = 4f; y < 400f; y += 4f)
            {
                for (float x = 4f; x < 300f; x += 4f)
                {
                    UiNode? hit = scene!.HitTest(x, y);
                    for (UiNode? node = hit; node != null; node = node.Parent)
                    {
                        if (node.Kind == UiNodeKind.Button)
                        {
                            return (x, y);
                        }
                    }
                }
            }

            throw new AssertionException("no button hit point found in the mounted scene");
        }

        private static string TemplateJson() => """
        {
          "id": "tests.panel.click",
          "graph": "tests.graph.click",
          "pins": [
            { "name": "title", "key": "tests.panel.click.title", "mode": "realtime", "default": 0 }
          ],
          "events": [
            { "eventId": "Ui.DialogChoose", "control": "choose", "gesture": "tap", "payload": { "option": "Int" } }
          ],
          "layout": {
            "controls": [
              { "type": "label", "text": "选一个" },
              { "type": "button", "control": "choose", "text": "选项", "payload": { "option": "2" } }
            ]
          }
        }
        """;
    }
}
