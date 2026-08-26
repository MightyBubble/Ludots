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
/// system never queries the world. When a template declares <c>layout</c>, controls are
/// built from that tree (G12 view-projection); otherwise legacy pin rows are used.
/// </summary>
public sealed class PanelPresentationSystem : ISystem<float>
{
    private const float AnchorMargin = 24f;
    private const float PanelWidth = 280f;
    private const float RowHeight = 22f;
    private const float ListItemHeight = 48f;
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
                UiRect rect = ResolvePanelRect(info.Handle, anchorKey, stackIndex);
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
        _panelHost.TryGetListProjections(handle, out IReadOnlyList<PanelListProjection> lists);
        var accent = new UiColor(skin.AccentR, skin.AccentG, skin.AccentB);
        var dim = new UiColor(136, 136, 136);

        UiElementBuilder body = template.Layout != null
            ? BuildDeclaredControls(template.Layout.Controls, values, lists, item: null)
            : BuildRows(template, values);

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
                body,
                new UiElementBuilder(UiNodeKind.Text)
                    .Class("hint")
                    .Text($"[{skin.Label}]")
                    .FontSize(11)
                    .Color(dim));
        return builder;
    }

    private static UiElementBuilder BuildDeclaredControls(
        IReadOnlyList<PanelLayoutControl> controls,
        PanelVariableSet values,
        IReadOnlyList<PanelListProjection> lists,
        PanelListItemProjection? item)
    {
        var children = new List<UiElementBuilder>(controls.Count);
        for (int i = 0; i < controls.Count; i++)
        {
            PanelLayoutControl control = controls[i];
            UiElementBuilder? built = BuildControl(control, values, lists, item);
            if (built != null)
            {
                children.Add(built);
            }
        }

        return new UiElementBuilder(UiNodeKind.Container)
            .Column()
            .Class("layout")
            .Gap(6)
            .Children(children.ToArray());
    }

    private static UiElementBuilder? BuildControl(
        PanelLayoutControl control,
        PanelVariableSet values,
        IReadOnlyList<PanelListProjection> lists,
        PanelListItemProjection? item)
    {
        return control.Type switch
        {
            PanelLayoutControlType.Label => BuildLabel(control, values, item),
            PanelLayoutControlType.ProgressBar => BuildProgressBar(control, values, item),
            PanelLayoutControlType.Badge => BuildBadge(control, values, item),
            PanelLayoutControlType.List => BuildList(control, values, lists),
            _ => null,
        };
    }

    private static UiElementBuilder BuildLabel(
        PanelLayoutControl control,
        PanelVariableSet values,
        PanelListItemProjection? item)
    {
        string text = control.Text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(control.Bind))
        {
            text = ReadBoundText(control.Bind!, values, item);
        }

        if (!string.IsNullOrEmpty(control.Prefix))
        {
            text = control.Prefix + text;
        }

        return new UiElementBuilder(UiNodeKind.Text)
            .Class("control-label")
            .Class(control.ClassName ?? "label")
            .Text(text)
            .FontSize(14)
            .Color(new UiColor(230, 230, 230));
    }

    private static UiElementBuilder BuildProgressBar(
        PanelLayoutControl control,
        PanelVariableSet values,
        PanelListItemProjection? item)
    {
        float current = ReadBoundFloat(control.Current!, values, item);
        float max = MathF.Max(0.0001f, ReadBoundFloat(control.Max!, values, item));
        float ratio = Math.Clamp(current / max, 0f, 1f);
        float trackWidth = PanelWidth - 48f;
        float fillWidth = MathF.Max(2f, trackWidth * ratio);

        return new UiElementBuilder(UiNodeKind.Container)
            .Column()
            .Class("control-progress")
            .Class(control.ClassName ?? "progress-bar")
            .Gap(2)
            .Children(
                new UiElementBuilder(UiNodeKind.Text)
                    .Class("progress-caption")
                    .Text($"{current:F0} / {max:F0}")
                    .FontSize(11)
                    .Color(new UiColor(200, 200, 200)),
                new UiElementBuilder(UiNodeKind.Container)
                    .Row()
                    .Class("progress-track")
                    .Width(trackWidth)
                    .Height(10)
                    .Background(new UiColor(40, 40, 55, 255))
                    .Radius(4)
                    .Children(
                        new UiElementBuilder(UiNodeKind.Container)
                            .Class("progress-fill")
                            .Class("progress-fill-health")
                            .Width(fillWidth)
                            .Height(10)
                            .Background(new UiColor(255, 68, 68, 255))
                            .Radius(4)));
    }

    private static UiElementBuilder? BuildBadge(
        PanelLayoutControl control,
        PanelVariableSet values,
        PanelListItemProjection? item)
    {
        bool flag = ReadBoundBool(control.Bind ?? string.Empty, values, item);
        if (control.ShowWhen.HasValue && flag != control.ShowWhen.Value)
        {
            return null;
        }

        if (!control.ShowWhen.HasValue && !flag)
        {
            return null;
        }

        return new UiElementBuilder(UiNodeKind.Text)
            .Class("control-badge")
            .Class(control.ClassName ?? "badge")
            .Text(control.Text ?? control.Bind ?? "!")
            .FontSize(11)
            .Bold()
            .Color(new UiColor(255, 210, 80));
    }

    private static UiElementBuilder BuildList(
        PanelLayoutControl control,
        PanelVariableSet values,
        IReadOnlyList<PanelListProjection> lists)
    {
        PanelListProjection? projection = FindList(lists, control.Bind!);
        var rows = new List<UiElementBuilder>();
        if (projection != null)
        {
            for (int i = 0; i < projection.Items.Count; i++)
            {
                PanelListItemProjection item = projection.Items[i];
                rows.Add(new UiElementBuilder(UiNodeKind.Container)
                    .Column()
                    .Class("list-item")
                    .Class($"list-item-{i}")
                    .Gap(2)
                    .Padding(4)
                    .Background(new UiColor(28, 28, 48, 180))
                    .Radius(4)
                    .Children(
                        BuildDeclaredControls(control.ItemControls, values, lists, item)));
            }
        }

        return new UiElementBuilder(UiNodeKind.Container)
            .Column()
            .Class("control-list")
            .Class(control.ClassName ?? "list")
            .Class($"list-{control.Bind}")
            .Gap(4)
            .Children(rows.ToArray());
    }

    private static PanelListProjection? FindList(IReadOnlyList<PanelListProjection> lists, string name)
    {
        for (int i = 0; i < lists.Count; i++)
        {
            if (string.Equals(lists[i].Name, name, StringComparison.Ordinal))
            {
                return lists[i];
            }
        }

        return null;
    }

    private static string ReadBoundText(string bind, PanelVariableSet values, PanelListItemProjection? item)
    {
        if (item != null)
        {
            if (item.Strings.TryGetValue(bind, out string? text))
            {
                return text;
            }

            if (item.Floats.TryGetValue(bind, out float number))
            {
                return number.ToString("F0");
            }

            if (item.Bools.TryGetValue(bind, out bool flag))
            {
                return flag ? "true" : "false";
            }

            return string.Empty;
        }

        return values.TryGet(bind, out float pin) ? pin.ToString("F0") : string.Empty;
    }

    private static float ReadBoundFloat(string bind, PanelVariableSet values, PanelListItemProjection? item)
    {
        if (item != null && item.Floats.TryGetValue(bind, out float number))
        {
            return number;
        }

        return values.TryGet(bind, out float pin) ? pin : 0f;
    }

    private static bool ReadBoundBool(string bind, PanelVariableSet values, PanelListItemProjection? item)
    {
        if (item != null && item.Bools.TryGetValue(bind, out bool flag))
        {
            return flag;
        }

        return values.TryGet(bind, out float pin) && pin != 0f;
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

    private UiRect ResolvePanelRect(PanelInstanceHandle handle, string anchorKey, int stackIndex)
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

        int itemCount = 3;
        if (_panelHost.TryGetListProjections(handle, out IReadOnlyList<PanelListProjection> lists))
        {
            for (int i = 0; i < lists.Count; i++)
            {
                itemCount = Math.Max(itemCount, lists[i].Items.Count);
            }
        }

        float contentHeight = PanelChromeHeight + Math.Max(3 * RowHeight, itemCount * ListItemHeight);
        bool top = anchorKey.Contains("top", StringComparison.OrdinalIgnoreCase);
        float x = left
            ? AnchorMargin
            : right
                ? MathF.Max(AnchorMargin, _root.Width - PanelWidth - AnchorMargin)
                : MathF.Max(AnchorMargin, (_root.Width - PanelWidth) * 0.5f);
        float stackOffset = stackIndex * (contentHeight + PanelStackGap);
        float y = top ? AnchorMargin + stackOffset : MathF.Max(AnchorMargin, _root.Height - contentHeight - AnchorMargin - stackOffset);
        return new UiRect(x, y, PanelWidth, contentHeight);
    }

    private sealed record MountedPanel(UiSurfaceLeaseHandle Lease);
}
