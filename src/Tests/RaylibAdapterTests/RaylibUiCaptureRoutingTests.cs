using System.Numerics;
using Ludots.Adapter.Raylib;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibUiCaptureRoutingTests
{
    [Test]
    public void FocusedBrowserCanvasWithoutPointerCapture_DoesNotBlockWorldPointer()
    {
        bool captured = RaylibHostLoop.ShouldCaptureWorldPointer(
            pointerCaptured: false,
            wheelCaptured: false,
            inputHandled: false);

        Assert.That(captured, Is.False);
    }

    [Test]
    public void FocusedBrowserCanvasWithoutHit_DoesNotRouteWorldMouseDownToUi()
    {
        bool routed = RaylibHostLoop.ShouldRouteMouseDownToUi(
            hitInteractiveUi: false,
            uiPointerCaptured: false);

        Assert.That(routed, Is.False);
    }

    [Test]
    public void InteractiveUiHit_RoutesMouseDownToUi()
    {
        bool routed = RaylibHostLoop.ShouldRouteMouseDownToUi(
            hitInteractiveUi: true,
            uiPointerCaptured: false);

        Assert.That(routed, Is.True);
    }

    [Test]
    public void ExistingUiPointerCapture_KeepsRoutingMouseDownToUi()
    {
        bool routed = RaylibHostLoop.ShouldRouteMouseDownToUi(
            hitInteractiveUi: false,
            uiPointerCaptured: true);

        Assert.That(routed, Is.True);
    }

    [Test]
    public void PointerCapturedByUi_BlocksWorldPointerEvenWhenCanvasIsFocused()
    {
        bool captured = RaylibHostLoop.ShouldCaptureWorldPointer(
            pointerCaptured: true,
            wheelCaptured: false,
            inputHandled: false);

        Assert.That(captured, Is.True);
    }

    [Test]
    public void WheelCapturedByUi_BlocksWorldPointerZoom()
    {
        bool captured = RaylibHostLoop.ShouldCaptureWorldPointer(
            pointerCaptured: false,
            wheelCaptured: true,
            inputHandled: false);

        Assert.That(captured, Is.True);
    }

    [Test]
    public void PlainUiHandledInput_BlocksWorldPointerWhenNoCanvasOwnsKeyboardFocus()
    {
        bool captured = RaylibHostLoop.ShouldCaptureWorldPointer(
            pointerCaptured: false,
            wheelCaptured: false,
            inputHandled: true);

        Assert.That(captured, Is.True);
    }

    [Test]
    public void UiHandledPointerInput_BlocksWorldPointerEvenWhenCanvasStaysFocused()
    {
        bool captured = RaylibHostLoop.ShouldCaptureWorldPointer(
            pointerCaptured: false,
            wheelCaptured: false,
            inputHandled: true);

        Assert.That(captured, Is.True);
    }

    [Test]
    public void WorldSelectionReleasePending_PreservesWorldPointerRelease()
    {
        bool captured = RaylibHostLoop.ShouldCaptureWorldPointer(
            pointerCaptured: true,
            wheelCaptured: false,
            inputHandled: true,
            worldSelectionReleasePending: true);

        Assert.That(captured, Is.False);
    }

    [Test]
    public void WorldSelectionReleasePending_DoesNotLeakCapturedWheelInput()
    {
        bool captured = RaylibHostLoop.ShouldCaptureWorldPointer(
            pointerCaptured: true,
            wheelCaptured: true,
            inputHandled: true,
            worldSelectionReleasePending: true);

        Assert.That(captured, Is.True);
    }

    [Test]
    public void CapturedPointerButtonNoLongerDown_ReleasesUiPointerCaptureWithoutReleaseEdge()
    {
        bool releaseCapture = RaylibHostLoop.ShouldReleaseUiPointerCapture(
            windowFocused: true,
            capturedPointerButtonHasValue: true,
            capturedButtonDown: false,
            capturedButtonReleased: false);

        Assert.That(releaseCapture, Is.True);
    }

    [Test]
    public void CapturedPointerButtonStillDown_KeepsUiPointerCapture()
    {
        bool releaseCapture = RaylibHostLoop.ShouldReleaseUiPointerCapture(
            windowFocused: true,
            capturedPointerButtonHasValue: true,
            capturedButtonDown: true,
            capturedButtonReleased: false);

        Assert.That(releaseCapture, Is.False);
    }

    [Test]
    public void CapturedPointerButtonNoLongerDown_ForwardsSyntheticUiPointerUp()
    {
        bool forwardUp = RaylibHostLoop.ShouldForwardUiPointerUp(
            windowFocused: true,
            capturedPointerButtonHasValue: true,
            capturedButtonDown: false,
            capturedButtonReleased: false);

        Assert.That(forwardUp, Is.True);
    }

    [Test]
    public void ActiveWorldSelectionWithNoMouseButtonsDown_PreservesWorldPointerRelease()
    {
        bool releasePending = RaylibHostLoop.HasWorldSelectionReleasePending(
            selectionDragActive: true,
            anyMouseButtonReleased: false,
            anyMouseButtonDown: false);

        Assert.That(releasePending, Is.True);
    }

    [Test]
    public void ConsumedScreenOverlayBuffer_DoesNotRetainSelectionRectIntoNextFrame()
    {
        var screenHud = new ScreenHudBatchBuffer(8);
        var screenOverlay = new ScreenOverlayBuffer();
        var builder = new PresentationOverlaySceneBuilder(screenHud, null, null, null, screenOverlay);
        var scene = new PresentationOverlayScene(32);

        screenOverlay.AddRect(
            100,
            120,
            240,
            160,
            new Vector4(0.18f, 0.55f, 0.95f, 0.12f),
            new Vector4(0.38f, 0.78f, 1f, 0.92f));

        RaylibHostLoop.BuildOverlaySceneAndClearConsumedBuffer(builder, scene, screenOverlay);

        Assert.That(screenOverlay.Count, Is.EqualTo(0));
        Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect).Length, Is.EqualTo(1));

        RaylibHostLoop.BuildOverlaySceneAndClearConsumedBuffer(builder, scene, screenOverlay);

        Assert.That(scene.GetLaneSpan(PresentationOverlayLayer.TopMost, PresentationOverlayItemKind.Rect).Length, Is.EqualTo(0));
        Assert.That(scene.ContainsLayer(PresentationOverlayLayer.TopMost), Is.False);
    }

    [Test]
    public void ClearingRasterTopOverlay_RefreshesCompositeTexture()
    {
        bool refresh = RaylibOverlayCompositor.ShouldRefreshComposite(
            underlayCanvasChanged: false,
            refreshUiLayer: false,
            refreshTopOverlay: true,
            rasterTopOverlay: false,
            topOverlayHadContentBeforeRefresh: true,
            hasCompositeContent: true,
            compositeHadContent: true);

        Assert.That(refresh, Is.True);
    }

    [Test]
    public void UnchangedCompositeInputs_DoNotRefreshCompositeTexture()
    {
        bool refresh = RaylibOverlayCompositor.ShouldRefreshComposite(
            underlayCanvasChanged: false,
            refreshUiLayer: false,
            refreshTopOverlay: false,
            rasterTopOverlay: false,
            topOverlayHadContentBeforeRefresh: false,
            hasCompositeContent: true,
            compositeHadContent: true);

        Assert.That(refresh, Is.False);
    }
}
