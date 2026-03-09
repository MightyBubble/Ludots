using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Diff;
using Ludots.UI.Runtime.Events;
using NUnit.Framework;

namespace Ludots.Tests.UIRuntime;

[TestFixture]
public sealed class UiSceneTests
{
    [Test]
    public void UiScene_CanMountRootNode()
    {
        UiScene scene = new();
        UiNode rootNode = new(new UiNodeId(1), UiNodeKind.Container);

        scene.Mount(rootNode);

        Assert.That(scene.Root, Is.SameAs(rootNode));
        Assert.That(scene.Version, Is.EqualTo(1));
        Assert.That(scene.IsDirty, Is.True);
    }

    [Test]
    public void UiScene_CanExportFullSnapshot()
    {
        UiScene scene = new();
        UiNode childNode = new(new UiNodeId(2), UiNodeKind.Text, textContent: "HP");
        UiNode rootNode = new(new UiNodeId(1), UiNodeKind.Container, children: new[] { childNode });
        scene.Mount(rootNode);

        UiSceneDiff diff = scene.CreateFullDiff();

        Assert.That(diff.Kind, Is.EqualTo(UiSceneDiffKind.FullSnapshot));
        Assert.That(diff.Snapshot.Version, Is.EqualTo(1));
        Assert.That(diff.Snapshot.Root, Is.Not.Null);
        Assert.That(diff.Snapshot.Root!.Children.Count, Is.EqualTo(1));
        Assert.That(diff.Snapshot.Root.Children[0].TextContent, Is.EqualTo("HP"));
        Assert.That(scene.IsDirty, Is.False);
    }

    [Test]
    public void UiScene_CanDispatchPointerEvent()
    {
        UiDispatcher dispatcher = new();
        UiActionHandle handle = dispatcher.Register(_ => { });
        UiNode targetNode = new(new UiNodeId(2), UiNodeKind.Button, actionHandles: new[] { handle });
        UiNode rootNode = new(new UiNodeId(1), UiNodeKind.Container, children: new[] { targetNode });
        UiScene scene = new(dispatcher);
        scene.Mount(rootNode);

        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Down, 0, 10f, 20f, targetNode.Id));

        Assert.That(result.Handled, Is.True);
    }

    [Test]
    public void UiScene_ClickPath_ProducesHandledResult()
    {
        UiDispatcher dispatcher = new();
        int actionCount = 0;
        UiActionHandle handle = dispatcher.Register(_ => actionCount++);

        UiNode targetNode = new(new UiNodeId(2), UiNodeKind.Button, actionHandles: new[] { handle });
        UiNode rootNode = new(new UiNodeId(1), UiNodeKind.Container, children: new[] { targetNode });
        UiScene scene = new(dispatcher);
        scene.Mount(rootNode);

        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 1, 32f, 48f, targetNode.Id));

        Assert.That(result.Handled, Is.True);
        Assert.That(actionCount, Is.EqualTo(1));
    }

    [Test]
    public void UiScene_ReturnsUnhandled_WhenTargetNodeCannotBeResolved()
    {
        UiScene scene = new();
        scene.Mount(new UiNode(new UiNodeId(1), UiNodeKind.Container));

        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Click, 0, 0f, 0f, new UiNodeId(99)));

        Assert.That(result, Is.EqualTo(UiEventResult.Unhandled));
    }

    [Test]
    public void UiScene_ScrollEvent_UpdatesScrollOffset()
    {
        (UiScene scene, UiNode host, UiNode[] _) = BuildScrollFixture();

        float centerX = host.LayoutRect.X + (host.LayoutRect.Width / 2f);
        float centerY = host.LayoutRect.Y + (host.LayoutRect.Height / 2f);
        UiEventResult result = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Scroll, 0, centerX, centerY, host.Id, 0f, 36f));

        Assert.That(result.Handled, Is.True);
        Assert.That(host.ScrollOffsetY, Is.GreaterThan(0f));
    }

    [Test]
    public void UiScene_HitTest_AccountsForAncestorScrollOffset()
    {
        (UiScene scene, UiNode host, UiNode[] items) = BuildScrollFixture();
        scene.Dispatch(new UiPointerEvent(UiPointerEventType.Scroll, 0, host.LayoutRect.X + 8f, host.LayoutRect.Y + 8f, host.Id, 0f, 30f));

        UiNode target = items[2];
        float sampleX = host.LayoutRect.X + 12f;
        float sampleY = target.LayoutRect.Y - host.ScrollOffsetY + 8f;
        UiNode? hit = scene.HitTest(sampleX, sampleY);

        Assert.That(hit, Is.SameAs(target));
    }

    [Test]
    public void UiScene_ScrollThumbDrag_ClearsOnPointerUp()
    {
        (UiScene scene, UiNode host, UiNode[] _) = BuildScrollFixture();
        UiRect thumb = UiScrollGeometry.GetVerticalThumbRect(host);
        float pointerX = thumb.X + (thumb.Width / 2f);
        float pointerY = thumb.Y + (thumb.Height / 2f);

        UiEventResult down = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Down, 0, pointerX, pointerY, host.Id));
        UiEventResult move = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Move, 0, pointerX, pointerY + 14f, host.Id));
        float offsetAfterDrag = host.ScrollOffsetY;
        UiEventResult up = scene.Dispatch(new UiPointerEvent(UiPointerEventType.Up, 0, pointerX, pointerY + 14f, host.Id));
        scene.Dispatch(new UiPointerEvent(UiPointerEventType.Move, 0, pointerX, pointerY + 32f, host.Id));

        Assert.That(down.Handled, Is.True);
        Assert.That(move.Handled, Is.True);
        Assert.That(up.Handled, Is.True);
        Assert.That(offsetAfterDrag, Is.GreaterThan(0f));
        Assert.That(host.ScrollOffsetY, Is.EqualTo(offsetAfterDrag));
    }

    private static (UiScene Scene, UiNode Host, UiNode[] Items) BuildScrollFixture()
    {
        UiNode[] items = Enumerable.Range(0, 8)
            .Select(index => new UiNode(
                new UiNodeId(index + 3),
                UiNodeKind.Button,
                style: UiStyle.Default with
                {
                    Height = UiLength.Px(28f)
                },
                textContent: $"Item {index + 1}"))
            .ToArray();

        UiNode host = new(
            new UiNodeId(2),
            UiNodeKind.Column,
            style: UiStyle.Default with
            {
                Width = UiLength.Px(140f),
                Height = UiLength.Px(72f),
                Padding = UiThickness.All(6f),
                Gap = 6f,
                RowGap = 6f,
                ColumnGap = 6f,
                Overflow = UiOverflow.Scroll,
                BackgroundColor = SkiaSharp.SKColor.Parse("#283349"),
                BorderWidth = 1f,
                BorderColor = SkiaSharp.SKColor.Parse("#4c5976")
            },
            children: items);

        UiNode root = new(
            new UiNodeId(1),
            UiNodeKind.Container,
            style: UiStyle.Default with
            {
                Width = UiLength.Px(240f),
                Height = UiLength.Px(180f)
            },
            children: new[] { host });

        UiScene scene = new();
        scene.Mount(root);
        scene.Layout(240f, 180f);
        return (scene, host, items);
    }
}
