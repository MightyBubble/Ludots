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
}
