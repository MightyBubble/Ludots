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
            PanelTemplate template = _templates.Require(info.TemplateId);
            int stackIndex = anchorStack.TryGetValue(anchorKey, out int count) ? count : 0;
            anchorStack[anchorKey] = stackIndex + 1;

            string key = $"{info.TemplateId}#{info.Handle.Id}:{info.Handle.Generation}";
            if (!_mounted.TryGetValue(key, out MountedPanel? mounted))
            {
                UiRect rect = ResolvePanelRect(anchorKey, stackIndex, template.Layout);
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
        PanelLayoutMetrics metrics = ResolvePanelMetrics(template.Layout);
        var accent = new UiColor(skin.AccentR, skin.AccentG, skin.AccentB);
        var dim = new UiColor(136, 136, 136);

        var builder = new UiElementBuilder(UiNodeKind.Container).Column()
            .Class("panel")
            .Class(TemplateClassToken(template.Id))
            .Class($"layout-{template.Layout}")
            .Background(new UiColor(20, 20, 35, 220))
            .Border(2, accent)
            .Radius(8)
            .Padding(12)
            .Width(metrics.Width)
            .Gap(4)
            .Absolute(rect.X, rect.Y)
            .Children(
                BuildLayoutBody(template, values, accent),
                new UiElementBuilder(UiNodeKind.Text)
                    .Class("hint")
                    .Text($"[{skin.Label}]")
                    .FontSize(11)
                    .Color(dim));
        return builder;
    }

    private static UiElementBuilder BuildLayoutBody(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        return template.Layout switch
        {
            PanelLayoutCatalog.CompactStatus => BuildCompactStatus(template, values, accent),
            PanelLayoutCatalog.ControlStrip => BuildControlStrip(template, values, accent),
            PanelLayoutCatalog.Radar => BuildRadar(template, values, accent),
            PanelLayoutCatalog.SelectionCard => BuildSelectionCard(template, values, accent),
            PanelLayoutCatalog.EntityLedger => BuildEntityLedger(template, values, accent),
            PanelLayoutCatalog.EventFeed => BuildEventFeed(template, values, accent),
            PanelLayoutCatalog.CommandDeck => BuildCommandDeck(template, values, accent),
            PanelLayoutCatalog.SubsystemNav => BuildSubsystemNav(template, values, accent),
            _ => throw new InvalidOperationException($"Unsupported panel layout '{template.Layout}'."),
        };
    }

    private static UiElementBuilder BuildCompactStatus(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        return Frame(template, accent)
            .Children(Title(template, accent),
                Metric(template, values, "day", "DAY", 28, accent),
                Row("status-meta", Metric(template, values, "speed", "SPEED", 13, UiColor.White), Metric(template, values, "viewMode", "VIEW", 13, UiColor.White)));
    }

    private static UiElementBuilder BuildControlStrip(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        return Frame(template, accent)
            .Children(Title(template, accent),
                Row("control-strip", ControlCell("X", Get(template, values, "cameraX"), accent), ControlCell("Y", Get(template, values, "cameraY"), accent), ControlCell("ZOOM", Get(template, values, "zoom"), accent)));
    }

    private static UiElementBuilder BuildRadar(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        var radar = new UiElementBuilder(UiNodeKind.Container).Column().Class("radar-grid")
            .Width(240).Height(182).Padding(8).Gap(4)
            .Background(new UiColor(10, 32, 38, 220)).Border(1, accent)
            .Children(new UiElementBuilder(UiNodeKind.Text).Text("N   ·   ·   ·   ·   E").FontSize(11).Color(accent),
                new UiElementBuilder(UiNodeKind.Text).Text("·     ◇     ·").FontSize(24).Color(new UiColor(230, 230, 230)),
                new UiElementBuilder(UiNodeKind.Text).Text("·   ·   ·   ·   ·").FontSize(11).Color(new UiColor(100, 180, 190)),
                Row("radar-coords", Metric(template, values, "cameraX", "X", 12, UiColor.White), Metric(template, values, "cameraY", "Y", 12, UiColor.White), Metric(template, values, "zoom", "Z", 12, UiColor.White)));
        return Frame(template, accent).Children(Title(template, accent), radar);
    }

    private static UiElementBuilder BuildSelectionCard(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        return Frame(template, accent).Children(Title(template, accent),
            Row("selection-summary", new UiElementBuilder(UiNodeKind.Text).Text("SELECTED UNIT").FontSize(13).Bold().Color(UiColor.White), Badge("x" + Get(template, values, "selectedCount").ToString("F0"), accent)),
            BarMetric(template, values, "selectedHealth", "HEALTH", new UiColor(82, 205, 115), 100),
            BarMetric(template, values, "selectedAttack", "ATTACK", new UiColor(239, 161, 74), 100));
    }

    private static UiElementBuilder BuildEntityLedger(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        return Frame(template, accent).Children(Title(template, accent),
            Row("ledger-row", LedgerCell("TOTAL", Get(template, values, "entityCount"), accent), LedgerCell("ALLY", Get(template, values, "allyCount"), new UiColor(82, 205, 115)), LedgerCell("HOSTILE", Get(template, values, "hostileCount"), new UiColor(239, 100, 100))));
    }

    private static UiElementBuilder BuildEventFeed(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        return Frame(template, accent).Children(Title(template, accent),
            Row("event-alert", Badge("ALERT " + Get(template, values, "alertLevel").ToString("F0"), new UiColor(239, 100, 100)), Metric(template, values, "eventCount", "EVENTS", 13, UiColor.White)),
            new UiElementBuilder(UiNodeKind.Container).Column().Class("event-feed").Gap(3)
                .Children(FeedLine("Latest signal", Get(template, values, "lastEvent"), accent), FeedLine("Events resolved", Get(template, values, "eventCount"), new UiColor(160, 170, 190))));
    }

    private static UiElementBuilder BuildCommandDeck(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        return Frame(template, accent).Children(Title(template, accent),
            Row("command-state", Badge(Get(template, values, "commandReady") > 0 ? "READY" : "LOCKED", accent), Metric(template, values, "selectedUnits", "UNITS", 13, UiColor.White)),
            BarMetric(template, values, "actionPoints", "ACTION POINTS", accent, 10),
            Row("command-actions", ActionCell("MOVE"), ActionCell("ATTACK"), ActionCell("BUILD")));
    }

    private static UiElementBuilder BuildSubsystemNav(PanelTemplate template, PanelVariableSet values, UiColor accent)
    {
        return Frame(template, accent).Children(Title(template, accent),
            NavLine("DIPLOMACY", Get(template, values, "diplomacy"), new UiColor(86, 170, 238)),
            NavLine("RESEARCH", Get(template, values, "research"), new UiColor(180, 120, 238)),
            NavLine("LOGISTICS", Get(template, values, "logistics"), new UiColor(238, 170, 86)));
    }

    private static UiElementBuilder Frame(PanelTemplate template, UiColor accent) => new UiElementBuilder(UiNodeKind.Container).Column().Class("panel-body").Gap(6);

    private static UiElementBuilder Title(PanelTemplate template, UiColor accent) => new UiElementBuilder(UiNodeKind.Text).Class("title").Text(DisplayTitle(template.Id)).FontSize(16).Bold().Color(accent);

    private static UiElementBuilder Row(string className, params UiElementBuilder[] children) => new UiElementBuilder(UiNodeKind.Container).Row().Class(className).Gap(8).Align(UiAlignItems.Center).Justify(UiJustifyContent.SpaceBetween).Children(children);

    private static UiElementBuilder Metric(PanelTemplate template, PanelVariableSet values, string pin, string label, float size, UiColor color) => new UiElementBuilder(UiNodeKind.Text).Class($"metric-{pin}").Text($"{label}  {Get(template, values, pin):F0}").FontSize(size).Color(color);

    private static UiElementBuilder ControlCell(string label, float value, UiColor accent) => new UiElementBuilder(UiNodeKind.Container).Column().Class("control-cell").Padding(8).Background(new UiColor(26, 40, 62, 220)).Border(1, accent).Children(new UiElementBuilder(UiNodeKind.Text).Text(label).FontSize(11).Color(accent), new UiElementBuilder(UiNodeKind.Text).Text(value.ToString("F0")).FontSize(18).Bold().Color(UiColor.White));

    private static UiElementBuilder Badge(string text, UiColor color) => new UiElementBuilder(UiNodeKind.Text).Class("badge").Text(text).FontSize(11).Bold().Color(color).Background(new UiColor(35, 35, 50, 220)).Padding(6, 3).Radius(4);

    private static UiElementBuilder BarMetric(PanelTemplate template, PanelVariableSet values, string pin, string label, UiColor color, float maximum)
    {
        float percent = Math.Clamp(Get(template, values, pin) / maximum * 100f, 0f, 100f);
        return new UiElementBuilder(UiNodeKind.Container).Column().Class($"bar-metric-{pin}").Gap(2).Children(
            Row("bar-label", new UiElementBuilder(UiNodeKind.Text).Text(label).FontSize(11).Color(new UiColor(170, 180, 195)), new UiElementBuilder(UiNodeKind.Text).Text(Get(template, values, pin).ToString("F0")).FontSize(11).Color(UiColor.White)),
            new UiElementBuilder(UiNodeKind.Container).Height(7).WidthPercent(100).Background(new UiColor(45, 50, 65)).Children(new UiElementBuilder(UiNodeKind.Container).HeightPercent(100).WidthPercent(percent).Background(color).Radius(3)));
    }

    private static UiElementBuilder LedgerCell(string label, float value, UiColor color) => new UiElementBuilder(UiNodeKind.Container).Column().Class("ledger-cell").Align(UiAlignItems.Center).Children(new UiElementBuilder(UiNodeKind.Text).Text(value.ToString("F0")).FontSize(22).Bold().Color(color), new UiElementBuilder(UiNodeKind.Text).Text(label).FontSize(10).Color(new UiColor(170, 180, 195)));

    private static UiElementBuilder FeedLine(string label, float value, UiColor color) => new UiElementBuilder(UiNodeKind.Text).Class("feed-line").Text($"• {label}  {value:F0}").FontSize(12).Color(color);

    private static UiElementBuilder ActionCell(string label) => new UiElementBuilder(UiNodeKind.Container).Class("action-cell").Padding(5).Background(new UiColor(34, 43, 62, 220)).Border(1, new UiColor(90, 110, 140)).Children(new UiElementBuilder(UiNodeKind.Text).Text(label).FontSize(10).Color(UiColor.White));

    private static UiElementBuilder NavLine(string label, float value, UiColor color) => Row("nav-line", new UiElementBuilder(UiNodeKind.Text).Text("›  " + label).FontSize(13).Bold().Color(UiColor.White), new UiElementBuilder(UiNodeKind.Text).Text(value.ToString("F0") + "%").FontSize(13).Color(color));

    private static float Get(PanelTemplate template, PanelVariableSet values, string name) => template.FindPin(name) == null ? 0f : values.Get(name);

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
                "Supported anchors: screen.topLeft, screen.topCenter, screen.topRight, screen.middleLeft, screen.middleRight, screen.bottomLeft, screen.bottomCenter, screen.bottomRight.");
        }

        bool top = anchorKey.Contains("top", StringComparison.OrdinalIgnoreCase);
        bool middle = anchorKey.Contains("middle", StringComparison.OrdinalIgnoreCase);
        float x = left
            ? AnchorMargin
            : right
                ? MathF.Max(AnchorMargin, _root.Width - PanelWidth - AnchorMargin)
                : MathF.Max(AnchorMargin, (_root.Width - PanelWidth) * 0.5f);
        float stackOffset = stackIndex * (PanelChromeHeight + (3 * RowHeight) + PanelStackGap);
        float panelHeight = PanelChromeHeight + (3 * RowHeight);
        float y = middle
            ? MathF.Max(AnchorMargin, (_root.Height - panelHeight) * 0.5f + stackOffset)
            : top
                ? AnchorMargin + stackOffset
                : MathF.Max(AnchorMargin, _root.Height - panelHeight - AnchorMargin - stackOffset);
        return new UiRect(x, y, PanelWidth, PanelChromeHeight + (3 * RowHeight));
    }

    private sealed record MountedPanel(UiSurfaceLeaseHandle Lease);
}
