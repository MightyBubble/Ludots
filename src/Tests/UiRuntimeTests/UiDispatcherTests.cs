using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Events;
using NUnit.Framework;

namespace Ludots.Tests.UIRuntime;

[TestFixture]
public sealed class UiDispatcherTests
{
    [Test]
    public void UiDispatcher_CanInvokeRegisteredAction()
    {
        UiDispatcher dispatcher = new();
        UiScene scene = new(dispatcher);
        UiNode targetNode = new(new UiNodeId(1), UiNodeKind.Button);
        UiPointerEvent evt = new(UiPointerEventType.Click, 0, 10f, 20f, targetNode.Id);

        int callCount = 0;
        UiActionHandle handle = dispatcher.Register(_ => callCount++);
        bool handled = dispatcher.Dispatch(handle, new UiActionContext(scene, evt, targetNode));

        Assert.That(handled, Is.True);
        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void UiDispatcher_ReturnsFalse_ForUnknownHandle()
    {
        UiDispatcher dispatcher = new();
        UiScene scene = new(dispatcher);
        UiNode targetNode = new(new UiNodeId(1), UiNodeKind.Button);
        UiPointerEvent evt = new(UiPointerEventType.Click, 0, 10f, 20f, targetNode.Id);

        bool handled = dispatcher.Dispatch(new UiActionHandle(999), new UiActionContext(scene, evt, targetNode));

        Assert.That(handled, Is.False);
    }
}
