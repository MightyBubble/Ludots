using System.Collections.Generic;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Input;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;
using Ludots.UI.Surface;
using NUnit.Framework;

namespace Ludots.Tests.UiShowcase;

[TestFixture]
public sealed class UiSurfaceHostTests
{
    [Test]
    public void Publish_SharedContributions_ComposesOnePhysicalRootScene()
    {
        UIRoot root = CreateRoot(out UiSurfaceHost host);
        UiSurfaceLeaseHandle hud = host.Acquire(new UiSurfaceLeaseRequest("test.hud", UiSurfaceSegment.Overlay, priority: 10));
        UiSurfaceLeaseHandle debug = host.Acquire(new UiSurfaceLeaseRequest("test.debug", UiSurfaceSegment.Debug, priority: 10));

        host.Publish(hud, UiSurfaceContribution.FromBuilder(() => Ui.Text("HUD").Id("test-hud")));
        host.Publish(debug, UiSurfaceContribution.FromBuilder(() => Ui.Text("Debug").Id("test-debug")));

        Assert.That(root.Scene, Is.SameAs(host.Scene));
        Assert.That(root.Scene!.FindByElementId("test-hud"), Is.Not.Null);
        Assert.That(root.Scene.FindByElementId("test-debug"), Is.Not.Null);
    }

    [Test]
    public void Publish_ExclusiveContribution_HidesAndRestoresSharedSurfaces()
    {
        UIRoot root = CreateRoot(out UiSurfaceHost host);
        UiSurfaceLeaseHandle shared = host.Acquire(new UiSurfaceLeaseRequest("test.shared", UiSurfaceSegment.Overlay, priority: 10));
        host.Publish(shared, UiSurfaceContribution.FromBuilder(() => Ui.Text("Shared").Id("test-shared")));

        UiSurfaceLeaseHandle takeover = host.Acquire(new UiSurfaceLeaseRequest("test.takeover", UiSurfaceSegment.Main, priority: 100, exclusive: true));
        host.Publish(takeover, UiSurfaceContribution.FromBuilder(() => Ui.Text("Takeover").Id("test-takeover")));

        Assert.That(root.Scene!.FindByElementId("test-shared"), Is.Null);
        Assert.That(root.Scene.FindByElementId("test-takeover"), Is.Not.Null);

        Assert.That(host.Release(takeover), Is.True);

        Assert.That(root.Scene!.FindByElementId("test-shared"), Is.Not.Null);
        Assert.That(root.Scene.FindByElementId("test-takeover"), Is.Null);
    }

    [Test]
    public void Publish_ReleasedHandle_FailsFastAsStale()
    {
        CreateRoot(out UiSurfaceHost host);
        UiSurfaceLeaseHandle handle = host.Acquire(new UiSurfaceLeaseRequest("test.stale"));

        Assert.That(host.Release(handle), Is.True);
        Assert.That(host.Release(handle), Is.False);
        Assert.Throws<InvalidOperationException>(() =>
            host.Publish(handle, UiSurfaceContribution.FromBuilder(() => Ui.Text("Stale"))));
    }

    [Test]
    public void ReactivePage_ActionUpdatesThroughHostSceneDispatcher()
    {
        UIRoot root = CreateRoot(out UiSurfaceHost host);
        root.Resize(320f, 200f);
        var page = new ReactivePage<int>(
            new SkiaTextMeasurer(),
            new SkiaImageSizeProvider(),
            0,
            context => Ui.Column(
                    Ui.Text($"Count: {context.State}").Id("counter-value"),
                    Ui.Button("Increment", _ => context.SetState(value => value + 1))
                        .Id("counter-button")
                        .Width(120f)
                        .Height(32f))
                .Width(320f)
                .Height(200f));

        UiSurfaceLeaseHandle lease = host.Acquire(new UiSurfaceLeaseRequest("test.reactive"));
        host.Publish(lease, UiSurfaceContribution.FromReactivePage(page));
        UiScene scene = root.Scene!;
        scene.Layout(320f, 200f);
        UiNode button = scene.FindByElementId("counter-button")!;

        root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Down,
            Button = PointerButton.Left,
            X = button.LayoutRect.X + 2f,
            Y = button.LayoutRect.Y + 2f
        });
        root.HandleInput(new PointerEvent
        {
            PointerId = 0,
            Action = PointerAction.Up,
            Button = PointerButton.Left,
            X = button.LayoutRect.X + 2f,
            Y = button.LayoutRect.Y + 2f
        });
        root.Scene!.Layout(320f, 200f);

        Assert.That(root.Scene.FindByElementId("counter-value")!.TextContent, Does.Contain("1"));
    }

    [Test]
    public void ReactivePage_VirtualWindow_IsCollectedIntoHostScene()
    {
        UIRoot root = CreateRoot(out UiSurfaceHost host);
        root.Resize(320f, 240f);
        var page = new ReactivePage<int>(
            new SkiaTextMeasurer(),
            new SkiaImageSizeProvider(),
            0,
            context =>
            {
                UiVirtualWindow window = context.GetVerticalVirtualWindow("test-window", 50, 20f, 100f);
                var rows = new List<UiElementBuilder>();
                for (int i = window.StartIndex; i < window.EndIndexExclusive; i++)
                {
                    rows.Add(Ui.Text($"Row {i}"));
                }

                return Ui.ScrollView(rows.ToArray())
                    .Id("test-window")
                    .Height(100f)
                    .Width(200f);
            });

        UiSurfaceLeaseHandle lease = host.Acquire(new UiSurfaceLeaseRequest("test.virtual"));
        host.Publish(lease, UiSurfaceContribution.FromReactivePage(page));
        root.Scene!.Layout(320f, 240f);

        Assert.That(root.Scene.TryGetVirtualWindow("test-window", out UiVirtualWindow window), Is.True);
        Assert.That(window.VisibleCount, Is.GreaterThan(0));
    }

    private static UIRoot CreateRoot(out UiSurfaceHost host)
    {
        var root = new UIRoot(new NullUiRenderer());
        host = new UiSurfaceHost(root, new SkiaTextMeasurer(), new SkiaImageSizeProvider());
        return root;
    }

    private sealed class NullUiRenderer : IUiRenderer
    {
        public void Render(UiScene scene, float width, float height)
        {
        }
    }
}
