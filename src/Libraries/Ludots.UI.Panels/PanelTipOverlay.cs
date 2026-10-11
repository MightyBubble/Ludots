using System;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace Ludots.UI.Panels;

/// <summary>
/// Hover tooltip overlay for the panel line: subscribes to the surface host scene's hover
/// events and publishes the hovered control's declared tip (<c>tip</c> on any panel
/// template control) as a top-priority surface contribution — one mechanism for every
/// skin, content authored as data (literal or bound title/text), never per-feature code.
/// The overlay owns no state beyond the lease: scene hover is the truth, the tip appears
/// and disappears with it.
/// </summary>
public sealed class PanelTipOverlay : IDisposable
{
    private const string OwnerId = "panel-tip-overlay";
    private const int TipPriority = 100_000;

    private readonly UiSurfaceHost _host;
    private bool _subscribed;
    private bool _hasLease;
    private UiSurfaceLeaseHandle _lease;

    public PanelTipOverlay(UiSurfaceHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Scene mounts lazily on the host; call once per presentation tick.</summary>
    public void Update()
    {
        if (!_subscribed && _host.Scene is { } scene)
        {
            scene.HoverPointChanged += OnHoverPointChanged;
            _subscribed = true;
        }
    }

    public void Dispose()
    {
        Hide();
        if (_subscribed && _host.Scene is { } scene)
        {
            scene.HoverPointChanged -= OnHoverPointChanged;
            _subscribed = false;
        }
    }

    private void OnHoverPointChanged(UiNode? node, float x, float y)
    {
        if (!UiScene.TryReadNodeTip(node, out string? title, out string? text))
        {
            Hide();
            return;
        }

        _lease = _host.Acquire(new UiSurfaceLeaseRequest(OwnerId, UiSurfaceSegment.Main, priority: TipPriority));
        _hasLease = true;
        float tipX = x + 14f;
        float tipY = y + 18f;
        _host.Publish(_lease, UiSurfaceContribution.FromBuilder(
            () => BuildTip(title, text, tipX, tipY),
            styleSheets: new[] { PanelDefaultStyles.Load() }));
    }

    private static UiElementBuilder BuildTip(string? title, string? text, float x, float y)
    {
        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Container)
            .Column()
            .Class("ui-tip")
            .Absolute(x, y)
            .ZIndex(1_000_000);
        if (!string.IsNullOrEmpty(title))
        {
            builder = builder.Child(new UiElementBuilder(UiNodeKind.Text).Class("ui-tip-title").Text(title!));
        }

        if (!string.IsNullOrEmpty(text))
        {
            builder = builder.Child(new UiElementBuilder(UiNodeKind.Text).Class("ui-tip-text").Text(text!));
        }

        return builder;
    }

    private void Hide()
    {
        if (!_hasLease)
        {
            return;
        }

        _host.Release(_lease);
        _hasLease = false;
    }
}
