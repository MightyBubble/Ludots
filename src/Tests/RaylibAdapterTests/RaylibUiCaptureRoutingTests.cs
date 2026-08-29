using Ludots.Adapter.Raylib;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibUiCaptureRoutingTests
{
    [Test]
    public void FocusedBrowserCanvasWithoutPointerCapture_DoesNotBlockWorldPointer()
    {
        bool captured = RaylibHostInputRouter.ShouldCaptureWorldPointer(
            pointerCaptured: false,
            wheelCaptured: false,
            inputHandled: false);

        Assert.That(captured, Is.False);
    }

    [Test]
    public void PointerCapturedByUi_BlocksWorldPointerEvenWhenCanvasIsFocused()
    {
        bool captured = RaylibHostInputRouter.ShouldCaptureWorldPointer(
            pointerCaptured: true,
            wheelCaptured: false,
            inputHandled: false);

        Assert.That(captured, Is.True);
    }

    [Test]
    public void WheelCapturedByUi_BlocksWorldPointerZoom()
    {
        bool captured = RaylibHostInputRouter.ShouldCaptureWorldPointer(
            pointerCaptured: false,
            wheelCaptured: true,
            inputHandled: false);

        Assert.That(captured, Is.True);
    }

    [Test]
    public void PlainUiHandledInput_BlocksWorldPointerWhenNoCanvasOwnsKeyboardFocus()
    {
        bool captured = RaylibHostInputRouter.ShouldCaptureWorldPointer(
            pointerCaptured: false,
            wheelCaptured: false,
            inputHandled: true);

        Assert.That(captured, Is.True);
    }

    [Test]
    public void UiHandledPointerInput_BlocksWorldPointerEvenWhenCanvasStaysFocused()
    {
        bool captured = RaylibHostInputRouter.ShouldCaptureWorldPointer(
            pointerCaptured: false,
            wheelCaptured: false,
            inputHandled: true);

        Assert.That(captured, Is.True);
    }
}
