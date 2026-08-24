using System;
using System.Collections.Generic;
using System.Text;
using Arch.System;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace Ludots.UI.Panels;

/// <summary>
/// Engine-side default panel presentation (#1011 author topology fix): every visible
/// <see cref="PanelHost"/> instance is rendered by the built-in auto-layout skin with
/// zero mod code. Visibility truth is <see cref="UiPanelActivationStore"/> (contract
/// five); values flow exclusively through <see cref="PanelHost.TryGetValues"/> — this
/// system never queries the world.
/// </summary>
public sealed class PanelPresentationSystem : ISystem<float>
{
    private const float AnchorMargin = 24f;
    private const float PanelWidth = 260f;
    private const float RowHeight = 22f;
    private const float PanelChromeHeight = 66f;
    private const float PanelStackGap = 8f;

    private readonly PanelHost _panelHost;
    private readonly PanelTemplateRegistry _templates;
    private readonly UiPanelActivationStore _activation;
    private readonly IUiSurfaceHost _surfaceHost;
    private readonly UIRoot _root;
    private readonly string? _globalSkin;
    private readonly UiStyleSheet? _themeSheet;

    private readonly Dictionary<string, MountedPanel> _mounted = new(StringComparer.Ordinal);
    private bool _disposed;

    public PanelPresentationSystem(
        PanelHost panelHost,
        PanelTemplateRegistry templates,
        UiPanelActivationStore activation,
        IUiSurfaceHost surfaceHost,
        UIRoot root,
        string? globalSkin,
        UiStyleSheet? themeSheet = null)
    {
        _panelHost = panelHost ?? throw new ArgumentNullException(nameof(panelHost));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _surfaceHost = surfaceHost ?? throw new ArgumentNullException(nameof(surfaceHost));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _globalSkin = globalSkin;
        _themeSheet = themeSheet;
    }

    public void Initialize() { }

    public void BeforeUpdate(in float dt) { }

    public void AfterUpdate(in float dt) { }

    public void Update(in float dt)
    {
        if (_disposed)
        {
            return;
        }

        var liveKeys = new List<string>();
        var anchorStack = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PanelHostInstanceInfo info in _panelHost.SnapshotInstances())
        {
            if (!_activation.IsVisible(info.TemplateId))
            {
                continue;
            }

            PanelSkinDescriptor skin = ResolveSkin(info);
            if (skin.Name == PanelSkinCatalog.DefaultSkinName && IsWebRouted(info))
            {
                continue;
            }

            string anchorKey = NormalizeAnchor(info.Anchor);
            int stackIndex = anchorStack.TryGetValue(anchorKey, out int count) ? count : 0;
            anchorStack[anchorKey] = stackIndex + 1;

            string key = $"{info.TemplateId}#{info.Handle.Id}:{info.Handle.Generation}";
            if (!_mounted.TryGetValue(key, out MountedPanel? mounted))
            {
                UiRect rect = ResolvePanelRect(anchorKey, stackIndex);
                UiSurfaceLeaseHandle lease = _surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                    $"panel-skin:{key}",
                    UiSurfaceSegment.Main,
                    priority: info.ZOrder));
                _surfaceHost.Publish(lease, UiSurfaceContribution.FromBuilder(
                    () => BuildPanel(info.Handle, rect, skin),
                    styleSheets: _themeSheet == null ? null : new[] { _themeSheet }));
                mounted = new MountedPanel(lease);
                _mounted[key] = mounted;
            }

            _surfaceHost.Invalidate(mounted.Lease);
            liveKeys.Add(key);
        }

        List<string>? staleKeys = null;
        foreach (KeyValuePair<string, MountedPanel> entry in _mounted)
        {
            if (liveKeys.Contains(entry.Key))
            {
                continue;
            }

            (staleKeys ??= new List<string>()).Add(entry.Key);
        }

        if (staleKeys != null)
        {
            foreach (string key in staleKeys)
            {
                _surfaceHost.Release(_mounted[key].Lease);
                _mounted.Remove(key);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (MountedPanel mounted in _mounted.Values)
        {
            _surfaceHost.Release(mounted.Lease);
        }

        _mounted.Clear();
    }

    private UiElementBuilder BuildPanel(PanelInstanceHandle handle, UiRect rect, PanelSkinDescriptor skin)
    {
        if (!_panelHost.TryGetValues(handle, out PanelVariableSet values))
        {
            throw new InvalidOperationException(
                $"Panel presentation cannot read stale handle {handle.Id}#{handle.Generation}.");
        }

        PanelTemplate template = _templates.Require(values.TemplateId);
        var accent = new UiColor(skin.AccentR, skin.AccentG, skin.AccentB);
        var dim = new UiColor(136, 136, 136);

        var builder = new UiElementBuilder(UiNodeKind.Container).Column()
            .Class("panel")
            .Class(TemplateClassToken(template.Id))
            .Background(new UiColor(20, 20, 35, 220))
            .Border(2, accent)
            .Radius(8)
            .Padding(12)
            .Width(rect.Width)
            .Gap(4)
            .Absolute(rect.X, rect.Y)
            .Children(
                new UiElementBuilder(UiNodeKind.Text)
                    .Class("title")
                    .Text(DisplayTitle(template.Id))
                    .FontSize(16)
                    .Bold()
                    .Color(accent),
                BuildRows(template, values),
                new UiElementBuilder(UiNodeKind.Text)
                    .Class("hint")
                    .Text($"[{skin.Label}]")
                    .FontSize(11)
                    .Color(dim));
        return builder;
    }

    private static UiElementBuilder BuildRows(PanelTemplate template, PanelVariableSet values)
    {
        var rows = new List<UiElementBuilder>();
        foreach (PanelPin pin in template.Pins)
        {
            bool isPairedBase = pin.Name.EndsWith("Base", StringComparison.Ordinal) &&
                HasPin(template, pin.Name[..^"Base".Length]);
            if (isPairedBase)
            {
                continue;
            }

            string text;
            var color = new UiColor(230, 230, 230);
            if (HasPin(template, pin.Name + "Base"))
            {
                float current = values.Get(pin.Name);
                float maximum = values.Get(pin.Name + "Base");
                text = $"{pin.Name.ToUpperInvariant()}  {current:F0} / {maximum:F0}";
                color = PairRowColor(pin.Name);
            }
            else
            {
                text = $"{pin.Name.ToUpperInvariant()}  {values.Get(pin.Name):F0}";
            }

            rows.Add(new UiElementBuilder(UiNodeKind.Text)
                .Class("row")
                .Class($"row-{pin.Name}")
                .Class(HasPin(template, pin.Name + "Base") ? "row-paired" : "row-single")
                .Text(text)
                .FontSize(14)
                .Color(color));
        }

        return new UiElementBuilder(UiNodeKind.Container).Column().Class("rows").Gap(4).Children(rows.ToArray());
    }

    private static bool HasPin(PanelTemplate template, string name)
    {
        foreach (PanelPin pin in template.Pins)
        {
            if (string.Equals(pin.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static UiColor PairRowColor(string variableName)
    {
        return variableName switch
        {
            "health" => new UiColor(255, 68, 68),
            "mana" => new UiColor(68, 136, 255),
            _ => new UiColor(230, 230, 230),
        };
    }

    /// <summary>
    /// Resolution chain (#1011): instance op param &gt; template skin field &gt; game.json
    /// global default &gt; "default". Skin is a per-instance render route — instances may
    /// mix native skins and web skins on one screen.
    /// </summary>
    private string? ResolvedSkinName(PanelHostInstanceInfo info)
    {
        return info.Skin ?? _templates.Require(info.TemplateId).Skin ?? _globalSkin;
    }

    private bool IsWebRouted(PanelHostInstanceInfo info)
    {
        return PanelSkinCatalog.IsBrowserStackSkin(ResolvedSkinName(info));
    }

    private PanelSkinDescriptor ResolveSkin(PanelHostInstanceInfo info)
    {
        string? name = ResolvedSkinName(info);
        if (PanelSkinCatalog.IsBrowserStackSkin(name))
        {
            // Web-routed instances are owned by the browser stack; the native renderer
            // must step aside per-instance, not per-game.
            return new PanelSkinDescriptor(PanelSkinCatalog.DefaultSkinName, "Default", 120, 120, 140);
        }

        return PanelSkinCatalog.Resolve(name);
    }

    private static string TemplateClassToken(string templateId)
    {
        return templateId.Replace('.', '-').TrimStart('-');
    }

    private static string DisplayTitle(string templateId)
    {
        string lastSegment = templateId[(templateId.LastIndexOf('.') + 1)..];
        var title = new StringBuilder(lastSegment.Length + 8);
        foreach (char c in lastSegment)
        {
            if (char.IsUpper(c) && title.Length > 0 && title[^1] != ' ')
            {
                title.Append(' ');
            }

            title.Append(char.ToUpperInvariant(c));
        }

        return title.ToString();
    }

    private static string NormalizeAnchor(string anchor)
    {
        string trimmed = anchor.Trim();
        return trimmed.StartsWith("screen.", StringComparison.Ordinal)
            ? trimmed["screen.".Length..]
            : trimmed;
    }

    private UiRect ResolvePanelRect(string anchorKey, int stackIndex)
    {
        bool left = anchorKey.Contains("left", StringComparison.OrdinalIgnoreCase);
        bool right = !left && anchorKey.Contains("right", StringComparison.OrdinalIgnoreCase);
        bool center = !left && !right && anchorKey.Contains("center", StringComparison.OrdinalIgnoreCase);
        if (!left && !right && !center)
        {
            throw new InvalidOperationException(
                $"Panel anchor '{anchorKey}' is not supported by the built-in presentation. " +
                "Supported anchors: screen.topLeft, screen.topCenter, screen.topRight, screen.bottomLeft, screen.bottomCenter, screen.bottomRight.");
        }

        bool top = anchorKey.Contains("top", StringComparison.OrdinalIgnoreCase);
        float x = left
            ? AnchorMargin
            : right
                ? MathF.Max(AnchorMargin, _root.Width - PanelWidth - AnchorMargin)
                : MathF.Max(AnchorMargin, (_root.Width - PanelWidth) * 0.5f);
        float stackOffset = stackIndex * (PanelChromeHeight + (3 * RowHeight) + PanelStackGap);
        float y = top ? AnchorMargin + stackOffset : MathF.Max(AnchorMargin, _root.Height - PanelChromeHeight - (3 * RowHeight) - AnchorMargin - stackOffset);
        return new UiRect(x, y, PanelWidth, PanelChromeHeight + (3 * RowHeight));
    }

    private sealed record MountedPanel(UiSurfaceLeaseHandle Lease);
}
