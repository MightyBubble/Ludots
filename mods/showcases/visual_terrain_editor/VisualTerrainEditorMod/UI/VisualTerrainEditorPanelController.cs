using Ludots.Platform.Abstractions;
using System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;
using VisualTerrainEditorMod.Runtime;

namespace VisualTerrainEditorMod.UI;

internal sealed class VisualTerrainEditorPanelController
{
    private static readonly UiColor PanelBorder = Color("#2E4153");
    private static readonly UiColor PanelBackground = Color("#E6101820");
    private static readonly UiColor MutedText = Color("#95A1AA");
    private static readonly UiColor PrimaryText = Color("#F2F5F7");
    private static readonly UiColor Accent = Color("#53C5A5");
    private static readonly UiColor Warning = Color("#F1C96B");

    private readonly VisualTerrainEditorRuntime _runtime;
    private readonly VisualTerrainEditorDocument _document;
    private ReactivePage<VisualTerrainEditorPanelState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public VisualTerrainEditorPanelController(VisualTerrainEditorRuntime runtime, VisualTerrainEditorDocument document)
    {
        _runtime = runtime;
        _document = document;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine, VisualTerrainEditorPanelState state)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
        {
            return;
        }

        _engine = engine;
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<VisualTerrainEditorPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
        }
        else
        {
            _page.SetState(_ => state);
        }

        surfaceHost.PublishReactivePage(
            ref _lease,
            new UiSurfaceLeaseRequest("Showcase.VisualTerrainEditor.Panel", UiSurfaceSegment.Overlay, priority: 40),
            _page);
    }

    public void ClearIfOwned(UIRoot root)
    {
        if (_lease.IsValid &&
            _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.ReleaseLease(ref _lease);
        }

        _engine = null;
    }

    public void InvalidateIfMounted(GameEngine engine)
    {
        if (_lease.IsValid &&
            engine.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
        {
            surfaceHost.InvalidateLease(_lease);
        }
    }

    private UiElementBuilder BuildRoot(ReactiveContext<VisualTerrainEditorPanelState> context)
    {
        VisualTerrainEditorPanelState state = context.State;
        float minimapLeft = MathF.Max(16f, state.ViewportWidth - 16f - 288f);
        float brushLeft = MathF.Max(16f, state.ViewportWidth - 16f - 336f);
        // Anchor the brush/terrain controls to the top-right, just below the minimap,
        // and bound their height with a scroll view so added rows never fall off screen.
        float minimapBottom = 16f + 300f;
        float brushTop = MathF.Max(16f, minimapBottom + 12f);
        float brushMaxHeight = MathF.Max(240f, state.ViewportHeight - brushTop - 16f);

        return Ui.Column(
                BuildInfoPanel(state)
                    .Width(416f)
                    .Absolute(16f, 16f),
                BuildMinimapPanel(state)
                    .Width(288f)
                    .Absolute(minimapLeft, 16f),
                BuildBrushPanel(state)
                    .Width(336f)
                    .Height(brushMaxHeight)
                    .Overflow(UiOverflow.Scroll)
                    .Absolute(brushLeft, brushTop))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(40);
    }

    private UiElementBuilder BuildInfoPanel(VisualTerrainEditorPanelState state)
    {
        return BuildPanelCard(
            Ui.Text("Visual Terrain Editor")
                .FontSize(22f)
                .Bold()
                .Color(PrimaryText),
            Ui.Text("使用笔刷动作直接在 3D 世界绘制。左上只放地图和视图状态；右下是笔刷；右上是 chunk 小地图。")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"Asset: {state.AssetName}")
                .FontSize(12f)
                .Color(PrimaryText),
            Ui.Text($"Id: {state.AssetId}")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"Dirty: {(state.IsDirty ? "Yes" : "No")} | Status: {state.StatusText}")
                .FontSize(12f)
                .Color(state.IsDirty ? Warning : Accent)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(string.IsNullOrWhiteSpace(state.SavePath) ? "Save Path: not saved yet" : $"Save Path: {state.SavePath}")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"Chunks: {state.ChunkColumns}x{state.ChunkRows} | Loaded: {state.LoadedChunkCount} | Edited: {state.EditedChunkCount}")
                .FontSize(12f)
                .Color(MutedText),
            Ui.Text($"Data: {state.SampleColumns}x{state.SampleRows} | Render: {state.RenderColumns}x{state.RenderRows}")
                .FontSize(12f)
                .Color(MutedText),
            Ui.Text($"Per Chunk: {state.SamplesPerChunkColumn}x{state.SamplesPerChunkRow} data | {state.RenderColumnsPerChunk}x{state.RenderRowsPerChunk} render")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"World: {state.WorldWidthMeters:0}m x {state.WorldHeightMeters:0}m | Height: {_document.MinHeightCm:0}cm ~ {_document.MaxHeightCm:0}cm")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            BuildButtonGroup(
                "Map",
                BuildActionButton("New 4K", _ => _runtime.CreateSmallMap()),
                BuildActionButton("New 8K", _ => _runtime.CreateMediumMap()),
                BuildActionButton("New 16K", _ => _runtime.CreateLargeMap()),
                BuildActionButton("Save Map", _ => _runtime.SaveCurrentMap())),
            BuildButtonGroup(
                "View",
                BuildModeButton("Base", state.ViewMode == TerrainViewMode.Base, _ => _runtime.SetViewMode(TerrainViewMode.Base)),
                BuildModeButton("Eroded", state.ViewMode == TerrainViewMode.Eroded, _ => _runtime.SetViewMode(TerrainViewMode.Eroded)),
                BuildModeButton("Ridges", state.ViewMode == TerrainViewMode.Ridges, _ => _runtime.SetViewMode(TerrainViewMode.Ridges))));
    }

    private UiElementBuilder BuildBrushPanel(VisualTerrainEditorPanelState state)
    {
        string cursorText = "Cursor: off world";
        if (_runtime.TryGetPointerWorld(out var worldCm))
        {
            cursorText = _runtime.TryGetHoveredChunk(out int chunkX, out int chunkY)
                ? $"Cursor: ({worldCm.X}, {worldCm.Y}) cm | Chunk: ({chunkX}, {chunkY})"
                : $"Cursor: ({worldCm.X}, {worldCm.Y}) cm";
        }

        return BuildPanelCard(
            Ui.Text("Brush")
                .FontSize(20f)
                .Bold()
                .Color(PrimaryText),
            Ui.Text(cursorText)
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            BuildButtonGroup(
                "Mode",
                BuildModeButton("Raise", !state.LowerBrush, _ => _runtime.SetBrushMode(false)),
                BuildModeButton("Lower", state.LowerBrush, _ => _runtime.SetBrushMode(true)),
                BuildActionButton("Radius -", _ => _runtime.AdjustBrushRadius(-5f)),
                BuildActionButton("Radius +", _ => _runtime.AdjustBrushRadius(5f))),
            BuildButtonGroup(
                "Output",
                BuildModeButton("Pure", !state.ApplyErosion, _ => _runtime.SetApplyErosion(false)),
                BuildModeButton("Erode", state.ApplyErosion, _ => _runtime.SetApplyErosion(true)),
                BuildModeButton("Terrain", state.DisplayColorMode == VisualHeightmapRenderColorMode.TerrainRamp, _ => _runtime.SetDisplayColorMode(VisualHeightmapRenderColorMode.TerrainRamp)),
                BuildModeButton("Heightmap", state.DisplayColorMode == VisualHeightmapRenderColorMode.HeightmapGrayscale, _ => _runtime.SetDisplayColorMode(VisualHeightmapRenderColorMode.HeightmapGrayscale)),
                BuildModeButton("Flat", state.DisplayFlatOverview, _ => _runtime.SetDisplayFlatOverview(true)),
                BuildModeButton("3D", !state.DisplayFlatOverview, _ => _runtime.SetDisplayFlatOverview(false)),
                BuildActionButton("Height -", _ => _runtime.AdjustDisplayHeightScale(-50f)),
                BuildActionButton("Height +", _ => _runtime.AdjustDisplayHeightScale(50f)),
                BuildActionButton("Contrast -", _ => _runtime.AdjustDisplayColorContrast(-0.20f)),
                BuildActionButton("Contrast +", _ => _runtime.AdjustDisplayColorContrast(0.20f))),
            BuildButtonGroup(
                "Vertical Exaggeration",
                BuildActionButton("1x", _ => _runtime.SetDisplayHeightScale(1f)),
                BuildActionButton("100x", _ => _runtime.SetDisplayHeightScale(100f)),
                BuildActionButton("500x", _ => _runtime.SetDisplayHeightScale(500f)),
                BuildActionButton("1000x", _ => _runtime.SetDisplayHeightScale(1000f)),
                BuildActionButton("2000x", _ => _runtime.SetDisplayHeightScale(2000f)),
                BuildActionButton("5000x", _ => _runtime.SetDisplayHeightScale(5000f))),
            BuildMetricCard("Brush Radius", $"{state.BrushRadiusMeters:0.0} m"),
            BuildMetricCard("Display Height", $"{state.DisplayHeightScale:0.00}x"),
            BuildMetricCard("Color Contrast", $"{state.DisplayColorContrast:0.00}x"),
            BuildStepperCard("Scale", $"{state.Scale:0.00}", _ => _runtime.AdjustScale(-0.01f), _ => _runtime.AdjustScale(0.01f)),
            BuildStepperCard("Strength", $"{state.Strength:0.00}", _ => _runtime.AdjustStrength(-0.01f), _ => _runtime.AdjustStrength(0.01f)),
            BuildStepperCard("Gully Weight", $"{state.GullyWeight:0.00}", _ => _runtime.AdjustGullyWeight(-0.05f), _ => _runtime.AdjustGullyWeight(0.05f)),
            BuildStepperCard("Detail", $"{state.Detail:0.00}", _ => _runtime.AdjustDetail(-0.10f), _ => _runtime.AdjustDetail(0.10f)),
            BuildStepperCard("Octaves", state.Octaves.ToString(), _ => _runtime.AdjustOctaves(-1), _ => _runtime.AdjustOctaves(1)),
            BuildActionButton("Reset Terrain", _ => _runtime.ResetDocument())
                .Padding(12f, 10f)
                .Background("#8A4334")
                .Color(PrimaryText)
                .Bold());
    }

    private UiElementBuilder BuildMinimapPanel(VisualTerrainEditorPanelState state)
    {
        _runtime.GetVisibleChunkWindow(out int centerChunkX, out int centerChunkY, out int minChunkX, out int maxChunkX, out int minChunkY, out int maxChunkY);
        string focusText = centerChunkX >= 0
            ? $"Focus Chunk: ({centerChunkX}, {centerChunkY}) | Window: [{minChunkX}-{maxChunkX}] x [{minChunkY}-{maxChunkY}]"
            : "Focus Chunk: n/a";

        return BuildPanelCard(
            Ui.Text("Chunk Minimap")
                .FontSize(20f)
                .Bold()
                .Color(PrimaryText),
            Ui.Text("灰: 未加载  蓝灰: 已加载  绿: 已编辑  青框: 镜头窗口  黄点: 相机焦点  白框: 当前笔刷 chunk")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(focusText)
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            BuildChunkGrid(state));
    }

    private UiElementBuilder BuildChunkGrid(VisualTerrainEditorPanelState state)
    {
        VisualTerrainAssetDescriptor asset = _document.Asset;
        const float maxGridWidth = 224f;
        const float maxGridHeight = 224f;
        float worldAspect = (asset.ChunkColumns * asset.ChunkWorldWidthCm) /
                            (float)Math.Max(1, asset.ChunkRows * asset.ChunkWorldHeightCm);
        float gridWidth = worldAspect >= 1f ? maxGridWidth : maxGridWidth * worldAspect;
        float gridHeight = worldAspect >= 1f ? maxGridHeight / worldAspect : maxGridHeight;
        float cellWidth = MathF.Max(2f, gridWidth / asset.ChunkColumns);
        float cellHeight = MathF.Max(2f, gridHeight / asset.ChunkRows);
        gridWidth = cellWidth * asset.ChunkColumns;
        gridHeight = cellHeight * asset.ChunkRows;

        var rows = new UiElementBuilder[asset.ChunkRows];
        _runtime.GetVisibleChunkWindow(out int centerChunkX, out int centerChunkY, out int minChunkX, out int maxChunkX, out int minChunkY, out int maxChunkY);
        bool hasHover = _runtime.TryGetHoveredChunk(out int hoverChunkX, out int hoverChunkY);

        for (int chunkY = 0; chunkY < asset.ChunkRows; chunkY++)
        {
            var cells = new UiElementBuilder[asset.ChunkColumns];
            for (int chunkX = 0; chunkX < asset.ChunkColumns; chunkX++)
            {
                _document.GetChunkStatus(chunkX, chunkY, out bool loaded, out bool edited);
                bool inWindow = chunkX >= minChunkX && chunkX <= maxChunkX && chunkY >= minChunkY && chunkY <= maxChunkY;
                bool isCenter = chunkX == centerChunkX && chunkY == centerChunkY;
                bool isHover = hasHover && chunkX == hoverChunkX && chunkY == hoverChunkY;

                string background = edited
                    ? "#40A87C"
                    : loaded
                        ? "#384A56"
                        : "#121A20";
                if (isCenter)
                {
                    background = "#F1C96B";
                }

                UiColor borderColor = inWindow ? Color("#49D0E0") : Color("#1B2A34");
                float borderWidth = inWindow ? 1f : 0f;
                if (isHover)
                {
                    borderColor = Color("#FFFFFF");
                    borderWidth = 1f;
                }

                cells[chunkX] = Ui.Text(" ")
                    .Width(cellWidth)
                    .Height(cellHeight)
                    .Background(background)
                    .Border(borderWidth, borderColor);
            }

            rows[chunkY] = Ui.Row(cells).Gap(0f);
        }

        return Ui.Column(rows)
            .Gap(0f)
            .Width(gridWidth)
            .Height(gridHeight)
            .Padding(8f)
            .Radius(12f)
            .Background("#081017")
            .Border(1f, PanelBorder);
    }

    private UiElementBuilder BuildPanelCard(params UiElementBuilder[] children)
    {
        return Ui.Card(children)
            .Padding(16f)
            .Gap(10f)
            .Radius(18f)
            .Background(PanelBackground)
            .Border(1f, PanelBorder)
            .BoxShadow(0f, 12f, 28f, Color("#66000000"));
    }

    private UiElementBuilder BuildButtonGroup(string title, params UiElementBuilder[] buttons)
    {
        return Ui.Column(
                Ui.Text(title)
                    .FontSize(12f)
                    .Bold()
                    .Color(MutedText),
                Ui.Row(buttons)
                    .Gap(8f)
                    .Wrap())
            .Padding(12f)
            .Gap(8f)
            .Radius(12f)
            .Background("#162029")
            .Border(1f, PanelBorder);
    }

    private UiElementBuilder BuildStepperCard(string title, string value, Action<UiActionContext> onDecrease, Action<UiActionContext> onIncrease)
    {
        return Ui.Column(
                Ui.Text(title)
                    .FontSize(12f)
                    .Bold()
                    .Color(MutedText),
                Ui.Row(
                        BuildActionButton("-", onDecrease),
                        Ui.Text(value)
                            .FontSize(18f)
                            .Bold()
                            .Color(PrimaryText)
                            .Width(120f),
                        BuildActionButton("+", onIncrease))
                    .Gap(8f)
                    .Align(UiAlignItems.Center))
            .Padding(12f)
            .Gap(8f)
            .Radius(12f)
            .Background("#162029")
            .Border(1f, PanelBorder);
    }

    private UiElementBuilder BuildMetricCard(string title, string value)
    {
        return Ui.Column(
                Ui.Text(title)
                    .FontSize(12f)
                    .Bold()
                    .Color(MutedText),
                Ui.Text(value)
                    .FontSize(18f)
                    .Bold()
                    .Color(PrimaryText))
            .Padding(12f)
            .Gap(8f)
            .Radius(12f)
            .Background("#162029")
            .Border(1f, PanelBorder);
    }

    private UiElementBuilder BuildModeButton(string label, bool active, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(10f, 8f)
            .Radius(10f)
            .Background(active ? Accent : Color("#1D262D"))
            .Color(active ? Color("#071013") : PrimaryText)
            .Bold();
    }

    private UiElementBuilder BuildActionButton(string label, Action<UiActionContext> onClick)
    {
        return Ui.Button(label, onClick)
            .Padding(10f, 8f)
            .Radius(10f)
            .Background("#1D262D")
            .Color(PrimaryText)
            .Border(1f, PanelBorder);
    }

    private static UiColor Color(string hex)
    {
        if (!UiColor.TryParse(hex, out UiColor parsed))
        {
            throw new InvalidOperationException($"Unsupported color literal '{hex}'.");
        }

        return parsed;
    }
}
