using System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Skia;
using SkiaSharp;
using VisualTerrainEditorMod.Runtime;

namespace VisualTerrainEditorMod.UI;

internal sealed class VisualTerrainEditorPanelController : IDisposable
{
    private static readonly SKColor MinimapBackground = SKColor.Parse("081017");
    private static readonly SKColor MinimapUnloaded = SKColor.Parse("121A20");
    private static readonly SKColor MinimapLoaded = SKColor.Parse("384A56");
    private static readonly SKColor MinimapEdited = SKColor.Parse("40A87C");
    private static readonly UiColor PanelBorder = Color("#2E4153");
    private static readonly UiColor PanelBackground = Color("#E6101820");
    private static readonly UiColor MutedText = Color("#95A1AA");
    private static readonly UiColor PrimaryText = Color("#F2F5F7");
    private static readonly UiColor Accent = Color("#53C5A5");
    private static readonly UiColor Warning = Color("#F1C96B");

    private readonly VisualTerrainEditorRuntime _runtime;
    private readonly VisualTerrainEditorDocument _document;
    private readonly UiCanvasContent _minimapCanvas;
    private readonly SKPaint _minimapFillPaint = new() { IsAntialias = false, Style = SKPaintStyle.Fill };
    private readonly SKPaint _minimapGridPaint = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = SKColor.Parse("1B2A34") };
    private readonly SKPaint _minimapWindowPaint = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = SKColor.Parse("49D0E0") };
    private readonly SKPaint _minimapHoverPaint = new() { IsAntialias = false, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = SKColors.White };
    private readonly SKPaint _minimapCenterPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColor.Parse("F1C96B") };
    private ReactivePage<VisualTerrainEditorPanelState>? _page;

    public VisualTerrainEditorPanelController(VisualTerrainEditorRuntime runtime, VisualTerrainEditorDocument document)
    {
        _runtime = runtime;
        _document = document;
        _minimapCanvas = new UiCanvasContent(DrawChunkMinimap);
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine, VisualTerrainEditorPanelState state)
    {
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

        if (!ReferenceEquals(root.Scene, _page.Scene))
        {
            root.MountScene(_page.Scene);
        }
    }

    public void ClearIfOwned(UIRoot root)
    {
        if (_page != null && ReferenceEquals(root.Scene, _page.Scene))
        {
            root.ClearScene();
        }
    }

    public void Dispose()
    {
        _minimapFillPaint.Dispose();
        _minimapGridPaint.Dispose();
        _minimapWindowPaint.Dispose();
        _minimapHoverPaint.Dispose();
        _minimapCenterPaint.Dispose();
    }

    private UiElementBuilder BuildRoot(ReactiveContext<VisualTerrainEditorPanelState> context)
    {
        VisualTerrainEditorPanelState state = context.State;
        float minimapLeft = MathF.Max(16f, state.ViewportWidth - 16f - 288f);
        float brushLeft = MathF.Max(16f, state.ViewportWidth - 16f - 336f);
        float brushTop = MathF.Max(16f, state.ViewportHeight - 16f - 452f);

        return Ui.Column(
                BuildInfoPanel(state)
                    .Width(416f)
                    .Absolute(16f, 16f),
                BuildMinimapPanel()
                    .Width(288f)
                    .Absolute(minimapLeft, 16f),
                BuildBrushPanel(state)
                    .Width(336f)
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
            Ui.Text("Left click paints directly in the 3D world. Top left is map state, bottom right is brush controls, top right is the chunk minimap.")
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
            BuildButtonGroup("Map", BuildMapButtons()),
            BuildButtonGroup(
                "View",
                BuildModeButton("Base", state.ViewMode == TerrainViewMode.Base, _ => _runtime.SetViewMode(TerrainViewMode.Base)),
                BuildModeButton("Eroded", state.ViewMode == TerrainViewMode.Eroded, _ => _runtime.SetViewMode(TerrainViewMode.Eroded)),
                BuildModeButton("Ridges", state.ViewMode == TerrainViewMode.Ridges, _ => _runtime.SetViewMode(TerrainViewMode.Ridges))));
    }

    private UiElementBuilder BuildBrushPanel(VisualTerrainEditorPanelState state)
    {
        return BuildPanelCard(
            Ui.Text("Brush")
                .FontSize(20f)
                .Bold()
                .Color(PrimaryText),
            Ui.Text("Use left click on the terrain. The in-world ring overlay is the authoritative brush indicator.")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            BuildButtonGroup(
                "Mode",
                BuildModeButton("Raise", !state.LowerBrush, _ => _runtime.SetBrushMode(false)),
                BuildModeButton("Lower", state.LowerBrush, _ => _runtime.SetBrushMode(true)),
                BuildActionButton("Radius -", _ => _runtime.AdjustBrushRadius(-5f)),
                BuildActionButton("Radius +", _ => _runtime.AdjustBrushRadius(5f))),
            BuildMetricCard("Brush Radius", $"{state.BrushRadiusMeters:0.0} m"),
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

    private UiElementBuilder BuildMinimapPanel()
    {
        return BuildPanelCard(
            Ui.Text("Chunk Minimap")
                .FontSize(20f)
                .Bold()
                .Color(PrimaryText),
            Ui.Text("Dark: unloaded. Slate: loaded. Green: edited. Cyan frame: camera window. Gold dot: focus chunk. White frame: hovered chunk.")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Canvas(_minimapCanvas)
                .Width(240f)
                .Height(240f)
                .Padding(8f)
                .Radius(12f)
                .Background("#081017")
                .Border(1f, PanelBorder));
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

    private UiElementBuilder[] BuildMapButtons()
    {
        ReadOnlySpan<VisualTerrainEditorRuntime.VisualTerrainMapPreset> presets = _runtime.GetMapPresets();
        var buttons = new UiElementBuilder[presets.Length + 1];
        for (int i = 0; i < presets.Length; i++)
        {
            string sizeLabel = presets[i].SizeLabel;
            buttons[i] = BuildActionButton($"New {sizeLabel}", _ => _runtime.CreatePresetMap(sizeLabel));
        }

        buttons[presets.Length] = BuildActionButton("Save Map", _ => _runtime.SaveCurrentMap());
        return buttons;
    }

    private void DrawChunkMinimap(SKCanvas canvas, SKRect rect)
    {
        VisualTerrainAssetDescriptor asset = _document.Asset;
        if (asset.ChunkColumns <= 0 || asset.ChunkRows <= 0)
        {
            return;
        }

        canvas.Clear(MinimapBackground);

        float cellWidth = rect.Width / asset.ChunkColumns;
        float cellHeight = rect.Height / asset.ChunkRows;
        _runtime.GetVisibleChunkWindow(out int centerChunkX, out int centerChunkY, out int minChunkX, out int maxChunkX, out int minChunkY, out int maxChunkY);
        bool hasHover = _runtime.TryGetHoveredChunk(out int hoverChunkX, out int hoverChunkY);

        for (int chunkY = 0; chunkY < asset.ChunkRows; chunkY++)
        {
            float top = rect.Top + (chunkY * cellHeight);
            float bottom = rect.Top + ((chunkY + 1) * cellHeight);
            for (int chunkX = 0; chunkX < asset.ChunkColumns; chunkX++)
            {
                _document.GetChunkStatus(chunkX, chunkY, out bool loaded, out bool edited);
                _minimapFillPaint.Color = edited
                    ? MinimapEdited
                    : loaded
                        ? MinimapLoaded
                        : MinimapUnloaded;

                float left = rect.Left + (chunkX * cellWidth);
                float right = rect.Left + ((chunkX + 1) * cellWidth);
                canvas.DrawRect(new SKRect(left, top, right, bottom), _minimapFillPaint);
            }
        }

        for (int lineX = 0; lineX <= asset.ChunkColumns; lineX++)
        {
            float x = rect.Left + (lineX * cellWidth);
            canvas.DrawLine(x, rect.Top, x, rect.Bottom, _minimapGridPaint);
        }

        for (int lineY = 0; lineY <= asset.ChunkRows; lineY++)
        {
            float y = rect.Top + (lineY * cellHeight);
            canvas.DrawLine(rect.Left, y, rect.Right, y, _minimapGridPaint);
        }

        if (minChunkX >= 0 && minChunkY >= 0 && maxChunkX >= minChunkX && maxChunkY >= minChunkY)
        {
            SKRect windowRect = new(
                rect.Left + (minChunkX * cellWidth),
                rect.Top + (minChunkY * cellHeight),
                rect.Left + ((maxChunkX + 1) * cellWidth),
                rect.Top + ((maxChunkY + 1) * cellHeight));
            canvas.DrawRect(windowRect, _minimapWindowPaint);
        }

        if (centerChunkX >= 0 && centerChunkY >= 0)
        {
            float centerX = rect.Left + ((centerChunkX + 0.5f) * cellWidth);
            float centerY = rect.Top + ((centerChunkY + 0.5f) * cellHeight);
            float radius = MathF.Max(2f, MathF.Min(cellWidth, cellHeight) * 0.3f);
            canvas.DrawCircle(centerX, centerY, radius, _minimapCenterPaint);
        }

        if (hasHover)
        {
            SKRect hoverRect = new(
                rect.Left + (hoverChunkX * cellWidth),
                rect.Top + (hoverChunkY * cellHeight),
                rect.Left + ((hoverChunkX + 1) * cellWidth),
                rect.Top + ((hoverChunkY + 1) * cellHeight));
            canvas.DrawRect(hoverRect, _minimapHoverPaint);
        }
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
