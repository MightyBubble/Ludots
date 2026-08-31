using System;
using System.Collections.Generic;
using System.Text;
using Arch.System;
using Ludots.Core.Client;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace Ludots.UI.Panels;

/// <summary>
/// Engine-side default panel presentation: every visible
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
    private readonly UiStyleSheet[] _styleSheets;
    private readonly IUiTextMeasurer _textMeasurer;
    private readonly IUiImageSizeProvider _imageSizeProvider;
    private readonly PresentationDisplayResolver? _displayResolver;
    private readonly ClientLocalSeatRegistry? _seats;
    private readonly PanelLayoutComposer _layoutComposer = new();

    private readonly Dictionary<string, MountedPanel> _mounted = new(StringComparer.Ordinal);
    private bool _disposed;

    public PanelPresentationSystem(
        PanelHost panelHost,
        PanelTemplateRegistry templates,
        UiPanelActivationStore activation,
        IUiSurfaceHost surfaceHost,
        UIRoot root,
        string? globalSkin,
        UiStyleSheet? themeSheet = null,
        IUiTextMeasurer? textMeasurer = null,
        IUiImageSizeProvider? imageSizeProvider = null,
        PresentationDisplayResolver? displayResolver = null,
        ClientLocalSeatRegistry? seats = null)
    {
        _panelHost = panelHost ?? throw new ArgumentNullException(nameof(panelHost));
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _surfaceHost = surfaceHost ?? throw new ArgumentNullException(nameof(surfaceHost));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _globalSkin = globalSkin;
        UiStyleSheet defaultStyles = PanelDefaultStyles.Load();
        _styleSheets = themeSheet == null
            ? new[] { defaultStyles }
            : new[] { defaultStyles, themeSheet };
        _textMeasurer = textMeasurer ?? new NullTextMeasurer();
        _imageSizeProvider = imageSizeProvider ?? new NullImageSizeProvider();
        _displayResolver = displayResolver;
        _seats = seats;
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

        List<(string SeatId, PresentBinding Binding)>? bindings = null;
        if (_seats is { PresentBindingCount: > 0 })
        {
            bindings = new List<(string SeatId, PresentBinding Binding)>();
            _seats.CopyPresentBindings(bindings);
        }

        var liveKeys = new List<string>();
        var anchorStack = new Dictionary<string, int>(StringComparer.Ordinal);
        var seatSurfaces = new List<PanelSeatSurface>();
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

            PanelTemplate template = _templates.Require(info.TemplateId);
            PanelAudience audience = PanelAudienceResolution.Effective(template, _activation);
            bool perSeat = bindings != null &&
                PanelSeatSurfacePlacement.TryResolveSeatSurfaces(
                    audience,
                    bindings,
                    _root.Width,
                    _root.Height,
                    seatSurfaces);
            if (perSeat)
            {
                for (int i = 0; i < seatSurfaces.Count; i++)
                {
                    PanelSeatSurface surface = seatSurfaces[i];
                    MountInstance(
                        info,
                        skin,
                        template,
                        surface.SeatId,
                        surface.X,
                        surface.Y,
                        surface.Width,
                        surface.Height,
                        anchorStack,
                        liveKeys);
                }
            }
            else
            {
                MountInstance(
                    info,
                    skin,
                    template,
                    seatId: null,
                    0f,
                    0f,
                    _root.Width,
                    _root.Height,
                    anchorStack,
                    liveKeys);
            }
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

    private void MountInstance(
        PanelHostInstanceInfo info,
        PanelSkinDescriptor skin,
        PanelTemplate template,
        string? seatId,
        float surfaceX,
        float surfaceY,
        float surfaceWidth,
        float surfaceHeight,
        Dictionary<string, int> anchorStack,
        List<string> liveKeys)
    {
        string anchorKey = NormalizeAnchor(info.Anchor);
        string stackKey = seatId == null ? anchorKey : $"{anchorKey}@{seatId}";
        int stackIndex = anchorStack.TryGetValue(stackKey, out int count) ? count : 0;
        anchorStack[stackKey] = stackIndex + 1;

        string key = seatId == null
            ? $"{info.TemplateId}#{info.Handle.Id}:{info.Handle.Generation}"
            : $"{info.TemplateId}#{info.Handle.Id}:{info.Handle.Generation}@{seatId}";
        bool declaredLayout = template.Layout != null;
        bool virtualized = PanelListProjector.TemplateUsesVirtualizedList(template);
        UiRect rect = ResolvePanelRect(
            info.Handle,
            anchorKey,
            stackIndex,
            template,
            surfaceX,
            surfaceY,
            surfaceWidth,
            surfaceHeight);
        if (!_mounted.TryGetValue(key, out MountedPanel? mounted))
        {
            UiSurfaceLeaseHandle lease = _surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                $"panel-skin:{key}",
                UiSurfaceSegment.Main,
                priority: info.ZOrder));
            if (virtualized)
            {
                var page = CreateVirtualListPage(info.Handle, rect, skin, info.Revision);
                _surfaceHost.Publish(lease, UiSurfaceContribution.FromReactivePage(page));
                mounted = new MountedPanel(lease, declaredLayout, page, info.Revision);
            }
            else
            {
                _surfaceHost.Publish(lease, UiSurfaceContribution.FromBuilder(
                    () => BuildPanel(info.Handle, rect, skin),
                    styleSheets: _styleSheets));
                mounted = new MountedPanel(lease, declaredLayout, page: null, info.Revision);
            }

            _mounted[key] = mounted;
        }
        else if (mounted.Page != null)
        {
            PanelSkinDescriptor skinCapture = skin;
            PanelInstanceHandle handleCapture = info.Handle;
            uint revisionCapture = info.Revision;
            mounted.Page.SetState(_ => new PanelUiState(handleCapture, rect, skinCapture, revisionCapture));
            _surfaceHost.Publish(
                mounted.Lease,
                UiSurfaceContribution.FromReactivePage(mounted.Page));
            mounted.LastRevision = info.Revision;
        }
        else if (mounted.DeclaredLayout)
        {
            _surfaceHost.Publish(mounted.Lease, UiSurfaceContribution.FromBuilder(
                () => BuildPanel(info.Handle, rect, skin),
                styleSheets: _styleSheets));
        }
        else
        {
            _surfaceHost.Invalidate(mounted.Lease);
        }

        liveKeys.Add(key);
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

    private ReactivePage<PanelUiState> CreateVirtualListPage(
        PanelInstanceHandle handle,
        UiRect rect,
        PanelSkinDescriptor skin,
        uint revision)
    {
        var initial = new PanelUiState(handle, rect, skin, revision);
        return new ReactivePage<PanelUiState>(
            _textMeasurer,
            _imageSizeProvider,
            initial,
            ComposeVirtualPanel,
            theme: null,
            _styleSheets);
    }

    private UiElementBuilder ComposeVirtualPanel(ReactiveContext<PanelUiState> context)
    {
        PanelUiState state = context.State;
        return BuildPanel(state.Handle, state.Rect, state.Skin, context);
    }

    private UiElementBuilder BuildPanel(
        PanelInstanceHandle handle,
        UiRect rect,
        PanelSkinDescriptor skin,
        ReactiveContext<PanelUiState>? reactiveContext = null)
    {
        if (!_panelHost.TryGetValues(handle, out PanelVariableSet values))
        {
            throw new InvalidOperationException(
                $"Panel presentation cannot read stale handle {handle.Id}#{handle.Generation}.");
        }

        PanelTemplate template = _templates.Require(values.TemplateId);
        _panelHost.TryGetListProjections(handle, out IReadOnlyList<PanelListProjection> lists);

        UiElementBuilder body = template.Layout != null
            ? ComposeDeclaredControls(
                template,
                template.Layout.Controls,
                values,
                lists,
                item: null,
                handle,
                reactiveContext)
            : BuildRows(template, values);

        if (template.Layout != null)
        {
            return new UiElementBuilder(UiNodeKind.Container).Column()
                .Class("panel")
                .Class(TemplateClassToken(template.Id))
                .Width(rect.Width)
                .Overflow(UiOverflow.Clip)
                .Absolute(rect.X, rect.Y)
                .Children(body);
        }

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
            .Overflow(UiOverflow.Clip)
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

    private UiElementBuilder ComposeDeclaredControls(
        PanelTemplate template,
        IReadOnlyList<PanelLayoutControl> controls,
        PanelVariableSet values,
        IReadOnlyList<PanelListProjection> lists,
        PanelListItemProjection? item,
        PanelInstanceHandle handle,
        ReactiveContext<PanelUiState>? reactiveContext)
    {
        return _layoutComposer.ComposeControls(
            controls,
            new PanelBindingScope(values, item),
            ResolvePanelImageSource,
            control => BuildList(template, control, values, lists, item, handle, reactiveContext));
    }

    private string ResolvePanelImageSource(string imageId)
    {
        if (_displayResolver == null)
        {
            throw new InvalidOperationException(
                "Panel image control requires PresentationDisplayResolver engine service.");
        }

        return _displayResolver.ResolveImageSourceOrThrow(imageId);
    }

    private UiElementBuilder BuildList(
        PanelTemplate template,
        PanelLayoutControl control,
        PanelVariableSet values,
        IReadOnlyList<PanelListProjection> lists,
        PanelListItemProjection? parentItem,
        PanelInstanceHandle handle,
        ReactiveContext<PanelUiState>? reactiveContext)
    {
        PanelCollectionBinding collection = FindCollection(template, control.Bind!)
            ?? throw new InvalidOperationException(
                $"Panel '{template.Id}' list bind '{control.Bind}' has no matching collection.");
        PanelTemplate elementTemplate = collection.Template
            ?? throw new InvalidOperationException(
                $"Panel '{template.Id}' collection '{collection.Name}' template is not bound.");
        if (elementTemplate.Layout == null)
        {
            throw new InvalidOperationException(
                $"Element template '{elementTemplate.Id}' requires layout.");
        }

        string hostId = $"panel-list-{template.Id}-{control.Bind}";
        float itemExtent = control.ItemExtent ?? ListItemHeight;
        int totalCount = 0;
        PanelListProjection? countProjection = FindList(lists, control.Bind!);
        if (countProjection != null)
        {
            totalCount = countProjection.TotalCount;
        }

        PanelListViewWindow window = PanelListViewWindow.All;
        float leading = 0f;
        float trailing = 0f;
        if (control.Virtualize)
        {
            if (reactiveContext == null)
            {
                throw new InvalidOperationException(
                    $"Panel '{template.Id}' list '{control.Bind}' virtualize requires reactive surface publishing.");
            }

            float viewportHeight = control.ViewportHeight
                ?? throw new InvalidOperationException(
                    $"Panel '{template.Id}' list '{control.Bind}' virtualize requires viewportHeight.");
            UiVirtualWindow virtualWindow = reactiveContext.GetVerticalVirtualWindow(
                hostId,
                totalCount,
                itemExtent,
                viewportHeight,
                control.Overscan);
            window = new PanelListViewWindow(virtualWindow.StartIndex, virtualWindow.EndIndexExclusive);
            leading = virtualWindow.LeadingSpacerExtent;
            trailing = virtualWindow.TrailingSpacerExtent;
        }

        PanelListProjection projection;
        if (control.Virtualize)
        {
            if (parentItem != null)
            {
                throw new InvalidOperationException(
                    $"Element template '{template.Id}' nested list '{control.Bind}' cannot be virtualized.");
            }

            if (!_panelHost.TryProjectListWindow(handle, control.Bind!, window, out projection))
            {
                throw new InvalidOperationException(
                    $"Panel '{template.Id}' list '{control.Bind}' window projection failed for handle {handle.Id}#{handle.Generation}.");
            }
        }
        else if (parentItem == null)
        {
            PanelListViewWindow projectionWindow = control.Present == PanelPresentMode.Aggregate
                ? new PanelListViewWindow(0, 1)
                : PanelListViewWindow.All;
            if (!_panelHost.TryProjectListWindow(
                    handle,
                    control.Bind!,
                    projectionWindow,
                    out projection))
            {
                throw new InvalidOperationException(
                    $"Panel '{template.Id}' list '{control.Bind}' projection failed for handle {handle.Id}#{handle.Generation}.");
            }
        }
        else
        {
            projection = FindList(lists, control.Bind!)
                ?? throw new InvalidOperationException(
                    $"Panel '{template.Id}' list '{control.Bind}' has no projection.");
        }

        var rows = new List<UiElementBuilder>();
        if (leading > 0.01f)
        {
            rows.Add(Ui.Spacer(leading));
        }

        int projectedItemCount = control.Present == PanelPresentMode.Aggregate
            ? Math.Min(1, projection.Items.Count)
            : projection.Items.Count;
        float cellExtent = control.ItemExtent ?? ListItemHeight;
        for (int i = 0; i < projectedItemCount; i++)
        {
            int absoluteIndex = projection.StartIndex + i;
            PanelListItemProjection item = projection.Items[i];
            var itemChildren = new List<UiElementBuilder>(2)
            {
                ComposeDeclaredControls(
                    elementTemplate,
                    elementTemplate.Layout.Controls,
                    values,
                    item.NestedLists,
                    item,
                    handle,
                    reactiveContext: null)
            };
            if (control.Present == PanelPresentMode.Aggregate)
            {
                PanelAggregateCountSpec countSpec = control.AggregateCount
                    ?? throw new InvalidOperationException(
                        $"Panel '{template.Id}' list '{control.Bind}' present=aggregate missing aggregate.count.");
                itemChildren.Add(new UiElementBuilder(UiNodeKind.Text)
                    .Class("aggregate-count")
                    .Text($"{countSpec.Prefix}{projection.TotalCount}"));
            }

            string presentClass = control.Present switch
            {
                PanelPresentMode.Aggregate => "list-item-aggregate",
                PanelPresentMode.Grid => "list-item-grid",
                PanelPresentMode.Column => "list-item-column",
                _ => "list-item-list",
            };

            UiElementBuilder cell = new UiElementBuilder(UiNodeKind.Container)
                .Column()
                .Id($"{hostId}-row-{absoluteIndex}")
                .Class("list-item")
                .Class($"list-item-{absoluteIndex}")
                .Class(presentClass)
                .Gap(2)
                .Padding(4)
                .Height(cellExtent)
                .Overflow(UiOverflow.Clip)
                .Background(new UiColor(28, 28, 48, 180))
                .Radius(4)
                .Children(itemChildren.ToArray());

            if (control.Present == PanelPresentMode.Grid && control.Columns is int gridColumns && gridColumns > 0)
            {
                // MinWidth 0 overrides flex min-content so cells can shrink into the column budget.
                cell = cell.FlexGrow(1f).FlexShrink(1f).FlexBasisPercent(100f / gridColumns).MinWidth(0f);
            }
            else if (control.Present == PanelPresentMode.Column)
            {
                // Floor keeps unreadably-crushed chips from stacking forever; Overflow.Scroll
                // takes over when the floor sum exceeds the panel content width.
                cell = cell.FlexGrow(1f).FlexShrink(1f).FlexBasis(0f).MinWidth(48f);
            }

            rows.Add(cell);
        }

        if (trailing > 0.01f)
        {
            rows.Add(Ui.Spacer(trailing));
        }

        UiElementBuilder listBody;
        if (control.Present == PanelPresentMode.Column)
        {
            listBody = new UiElementBuilder(UiNodeKind.Container)
                .Row()
                .Class("control-list")
                .Class("control-list-column")
                .Class(control.ClassName ?? "list")
                .Class($"list-{control.Bind}")
                .WidthPercent(100f)
                .Height(cellExtent)
                .Overflow(UiOverflow.Scroll)
                .Gap(4f)
                .Children(rows.ToArray());
        }
        else if (control.Present == PanelPresentMode.Grid)
        {
            int columns = control.Columns
                ?? throw new InvalidOperationException(
                    $"Panel '{template.Id}' list '{control.Bind}' present=grid missing columns.");
            var gridRows = new List<UiElementBuilder>();
            for (int offset = 0; offset < rows.Count; offset += columns)
            {
                int take = Math.Min(columns, rows.Count - offset);
                var slice = new UiElementBuilder[take];
                for (int c = 0; c < take; c++)
                {
                    slice[c] = rows[offset + c];
                }

                gridRows.Add(new UiElementBuilder(UiNodeKind.Container)
                    .Row()
                    .Class("control-list-grid-row")
                    .Gap(4f)
                    .Children(slice));
            }

            listBody = new UiElementBuilder(UiNodeKind.Container)
                .Column()
                .Class("control-list")
                .Class("control-list-grid")
                .Class(control.ClassName ?? "list")
                .Class($"list-{control.Bind}")
                .Gap(4f)
                .Children(gridRows.ToArray());
        }
        else
        {
            listBody = new UiElementBuilder(UiNodeKind.Container)
                .Column()
                .Class("control-list")
                .Class(control.ClassName ?? "list")
                .Class($"list-{control.Bind}")
                .Gap(control.Virtualize ? 0f : 4f)
                .Children(rows.ToArray());
        }

        if (control.ViewportHeight.HasValue)
        {
            return Ui.ScrollView(listBody)
                .Id(hostId)
                .Class("control-list-scroll")
                .Class(control.ClassName ?? "list")
                .Height(control.ViewportHeight.Value)
                .Padding(2f);
        }

        return listBody;
    }

    private static PanelCollectionBinding? FindCollection(PanelTemplate template, string name)
    {
        for (int i = 0; i < template.Collections.Count; i++)
        {
            if (string.Equals(template.Collections[i].Name, name, StringComparison.Ordinal))
            {
                return template.Collections[i];
            }
        }

        return null;
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

    private UiRect ResolvePanelRect(
        PanelInstanceHandle handle,
        string anchorKey,
        int stackIndex,
        PanelTemplate template,
        float surfaceX,
        float surfaceY,
        float surfaceWidth,
        float surfaceHeight)
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

        float contentHeight = PanelChromeHeight + 3 * RowHeight;
        if (template.Layout != null)
        {
            float listBudget = 0f;
            for (int i = 0; i < template.Layout.Controls.Count; i++)
            {
                PanelLayoutControl control = template.Layout.Controls[i];
                if (control.Type != PanelLayoutControlType.List)
                {
                    continue;
                }

                if (control.ViewportHeight.HasValue)
                {
                    listBudget += control.ViewportHeight.Value + 24f;
                    continue;
                }

                int itemCount = control.Present == PanelPresentMode.Aggregate ? 0 : 3;
                if (_panelHost.TryGetListProjections(handle, out IReadOnlyList<PanelListProjection> lists))
                {
                    for (int li = 0; li < lists.Count; li++)
                    {
                        if (string.Equals(lists[li].Name, control.Bind, StringComparison.Ordinal))
                        {
                            itemCount = control.Present == PanelPresentMode.Aggregate
                                ? (lists[li].TotalCount > 0 ? 1 : 0)
                                : Math.Max(itemCount, lists[li].TotalCount);
                        }
                    }
                }

                float itemExtent = control.ItemExtent ?? ListItemHeight;
                int visualRows = control.Present switch
                {
                    PanelPresentMode.Column => itemCount > 0 ? 1 : 0,
                    PanelPresentMode.Grid when control.Columns is int cols && cols > 0
                        => (itemCount + cols - 1) / cols,
                    PanelPresentMode.Aggregate => itemCount > 0 ? 1 : 0,
                    _ => itemCount,
                };
                listBudget += visualRows * itemExtent;
            }

            contentHeight = PanelChromeHeight + Math.Max(3 * RowHeight, listBudget);
        }

        bool top = anchorKey.Contains("top", StringComparison.OrdinalIgnoreCase);
        float panelWidth = template.Width > 0f ? template.Width : PanelWidth;
        float x = left
            ? surfaceX + AnchorMargin
            : right
                ? MathF.Max(surfaceX + AnchorMargin, surfaceX + surfaceWidth - panelWidth - AnchorMargin)
                : MathF.Max(surfaceX + AnchorMargin, surfaceX + (surfaceWidth - panelWidth) * 0.5f);
        float stackOffset = stackIndex * (contentHeight + PanelStackGap);
        float y = top
            ? surfaceY + AnchorMargin + stackOffset
            : MathF.Max(
                surfaceY + AnchorMargin,
                surfaceY + surfaceHeight - contentHeight - AnchorMargin - stackOffset);
        return new UiRect(x, y, panelWidth, contentHeight);
    }

    private sealed class MountedPanel
    {
        public MountedPanel(
            UiSurfaceLeaseHandle lease,
            bool declaredLayout,
            ReactivePage<PanelUiState>? page,
            uint lastRevision)
        {
            Lease = lease;
            DeclaredLayout = declaredLayout;
            Page = page;
            LastRevision = lastRevision;
        }

        public UiSurfaceLeaseHandle Lease { get; }
        public bool DeclaredLayout { get; }
        public ReactivePage<PanelUiState>? Page { get; }
        public uint LastRevision { get; set; }
    }

    private sealed class PanelUiState
    {
        public PanelUiState(PanelInstanceHandle handle, UiRect rect, PanelSkinDescriptor skin, uint revision)
        {
            Handle = handle;
            Rect = rect;
            Skin = skin;
            Revision = revision;
        }

        public PanelInstanceHandle Handle { get; }
        public UiRect Rect { get; }
        public PanelSkinDescriptor Skin { get; }
        public uint Revision { get; }
    }

    private sealed class NullTextMeasurer : IUiTextMeasurer
    {
        public UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth)
        {
            float width = MeasureWidth(text, style);
            float height = Math.Max(1f, style.FontSize);
            return new UiTextLayoutResult(new[] { text ?? string.Empty }, width, height, height);
        }

        public float MeasureWidth(string? text, UiStyle style)
        {
            int length = string.IsNullOrEmpty(text) ? 0 : text.Length;
            return length * Math.Max(1f, style.FontSize) * 0.55f;
        }
    }

    private sealed class NullImageSizeProvider : IUiImageSizeProvider
    {
        public bool TryGetSize(string? source, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            return false;
        }
    }
}
