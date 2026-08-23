using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using NavDomainShowcaseMod.Runtime;

namespace NavDomainShowcaseMod.UI;

internal sealed class NavDomainPanelController
{
    private static readonly UiColor PanelBorder = Color("#2E4153");
    private static readonly UiColor PanelBackground = Color("#E6101820");
    private static readonly UiColor MutedText = Color("#95A1AA");
    private static readonly UiColor PrimaryText = Color("#F2F5F7");
    private static readonly UiColor Accent = Color("#53C5A5");
    private static readonly UiColor Warning = Color("#F1C96B");

    private readonly NavDomainAuthoringRuntime _runtime;
    private readonly LogicTerrainDocument _document;
    private readonly NavBakeSession _bakeSession;
    private ReactivePage<NavDomainPanelState>? _page;

    public NavDomainPanelController(
        NavDomainAuthoringRuntime runtime,
        LogicTerrainDocument document,
        NavBakeSession bakeSession)
    {
        _runtime = runtime;
        _document = document;
        _bakeSession = bakeSession;
    }

    public void MountOrRefresh(UIRoot root, GameEngine engine, NavDomainPanelState state)
    {
        if (_page == null)
        {
            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<NavDomainPanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
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

    private UiElementBuilder BuildRoot(ReactiveContext<NavDomainPanelState> context)
    {
        NavDomainPanelState state = context.State;
        float rightLeft = MathF.Max(16f, state.ViewportWidth - 16f - 380f);

        return Ui.Column(
                BuildMainPanel(state)
                    .Width(420f)
                    .Absolute(16f, 16f),
                BuildBakePanel(state)
                    .Width(380f)
                    .Absolute(rightLeft, 16f))
            .WidthPercent(100f)
            .HeightPercent(100f)
            .Absolute(0f, 0f)
            .ZIndex(40);
    }

    private UiElementBuilder BuildMainPanel(NavDomainPanelState state)
    {
        string cursorText = "Cursor: off world";
        if (_runtime.TryGetPointerWorld(out var worldCm))
        {
            cursorText = _runtime.TryGetHoveredChunk(out int chunkX, out int chunkY)
                ? $"Cursor: ({worldCm.X}, {worldCm.Y}) cm | Chunk: ({chunkX}, {chunkY})"
                : $"Cursor: ({worldCm.X}, {worldCm.Y}) cm";
        }

        return BuildPanelCard(
            Ui.Text("Nav Domain Authoring")
                .FontSize(22f)
                .Bold()
                .Color(PrimaryText),
            Ui.Text("左键绘制 logic terrain；dirty chunk 标黄；Estimate 看预算，Bake 走 NavBakeService (CDT)；烘焙出的 NavTile 以青色网格显示。")
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"Status: {state.StatusText}")
                .FontSize(12f)
                .Color(state.DirtyChunkCount > 0 ? Warning : Accent)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text($"Terrain: {state.ChunkColumns}x{state.ChunkRows} chunks | Painted: {state.PaintedChunkCount} | Dirty: {state.DirtyChunkCount}")
                .FontSize(12f)
                .Color(MutedText),
            Ui.Text(cursorText)
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            BuildButtonGroup(
                "Brush Mode",
                BuildModeButton("Raise", state.BrushMode == TerrainBrushMode.RaiseHeight, _ => _runtime.SetBrushMode(TerrainBrushMode.RaiseHeight)),
                BuildModeButton("Lower", state.BrushMode == TerrainBrushMode.LowerHeight, _ => _runtime.SetBrushMode(TerrainBrushMode.LowerHeight)),
                BuildModeButton("Block", state.BrushMode == TerrainBrushMode.Block, _ => _runtime.SetBrushMode(TerrainBrushMode.Block)),
                BuildModeButton("Unblock", state.BrushMode == TerrainBrushMode.Unblock, _ => _runtime.SetBrushMode(TerrainBrushMode.Unblock)),
                BuildActionButton("Radius -", _ => _runtime.AdjustBrushRadius(-2f)),
                BuildActionButton("Radius +", _ => _runtime.AdjustBrushRadius(2f))),
            BuildMetricCard("Brush Radius", $"{state.BrushRadiusMeters:0.0} m"),
            BuildActionButton("Reset Terrain", _ => _runtime.ResetTerrain())
                .Padding(12f, 10f)
                .Background("#8A4334")
                .Color(PrimaryText)
                .Bold(),
            BuildChunkGrid(state));
    }

    private UiElementBuilder BuildBakePanel(NavDomainPanelState state)
    {
        var estimate = state.Estimate;
        string estimateText = estimate == null
            ? "No estimate yet. Paint terrain, then press Estimate."
            : $"Targets: {estimate.TargetTileCount}/{estimate.FullTileCount} | Ops: {estimate.BakeOperationCount} | Work units: {estimate.BudgetWorkUnitCount}\n" +
              $"Status: {estimate.BudgetStatusText} | Est: {estimate.EstimatedSecondsLow:0.0}-{estimate.EstimatedSecondsHigh:0.0} s\n" +
              $"Hash: {estimate.EstimateHash[..Math.Min(16, estimate.EstimateHash.Length)]}";

        var outcome = state.Outcome;
        string outcomeText = outcome == null
            ? "No bake yet."
            : $"ok={outcome.OkCount} empty={outcome.EmptyCount} fail={outcome.FailCount} | tris={outcome.TriangleCount} | {outcome.ElapsedMs:0} ms";

        return BuildPanelCard(
            Ui.Text("Bake")
                .FontSize(20f)
                .Bold()
                .Color(PrimaryText),
            Ui.Text(estimateText)
                .FontSize(12f)
                .Color(MutedText)
                .WhiteSpace(UiWhiteSpace.Normal),
            Ui.Text(outcomeText)
                .FontSize(12f)
                .Color(PrimaryText)
                .WhiteSpace(UiWhiteSpace.Normal),
            BuildButtonGroup(
                "Actions",
                BuildActionButton("Estimate Dirty", _ => _runtime.EstimateDirty()),
                BuildActionButton("Bake Dirty", _ => _runtime.BakeDirty()),
                BuildActionButton("Bake All", _ => _runtime.BakeAll())));
    }

    private UiElementBuilder BuildChunkGrid(NavDomainPanelState state)
    {
        float cellSize = MathF.Max(6f, MathF.Floor(360f / MathF.Max(state.ChunkColumns, state.ChunkRows)));
        _runtime.GetVisibleChunkWindow(out int centerChunkX, out int centerChunkY, out int minChunkX, out int maxChunkX, out int minChunkY, out int maxChunkY);
        bool hasHover = _runtime.TryGetHoveredChunk(out int hoverChunkX, out int hoverChunkY);

        var rows = new UiElementBuilder[state.ChunkRows];
        for (int chunkY = 0; chunkY < state.ChunkRows; chunkY++)
        {
            var cells = new UiElementBuilder[state.ChunkColumns];
            for (int chunkX = 0; chunkX < state.ChunkColumns; chunkX++)
            {
                _document.GetChunkStatus(chunkX, chunkY, out bool painted, out bool dirty);
                bool baked = _bakeSession.TryGetTile(chunkX, chunkY, out _);
                bool inWindow = chunkX >= minChunkX && chunkX <= maxChunkX && chunkY >= minChunkY && chunkY <= maxChunkY;
                bool isHover = hasHover && chunkX == hoverChunkX && chunkY == hoverChunkY;

                string background = dirty
                    ? "#F1C96B"
                    : baked
                        ? "#2FA3B8"
                        : painted
                            ? "#3F5346"
                            : "#141D22";

                UiColor borderColor = isHover ? Color("#FFFFFF") : inWindow ? Color("#49D0E0") : Color("#1B2A34");
                float borderWidth = isHover || inWindow ? 1f : 0f;

                cells[chunkX] = Ui.Text(" ")
                    .Width(cellSize)
                    .Height(cellSize)
                    .Background(background)
                    .Border(borderWidth, borderColor);
            }

            rows[chunkY] = Ui.Row(cells).Gap(0f);
        }

        return Ui.Column(
                Ui.Text("Chunks   灰:未画  绿:已画  黄:dirty  青:已烘焙  青框:镜头窗口  白框:hover")
                    .FontSize(11f)
                    .Color(MutedText)
                    .WhiteSpace(UiWhiteSpace.Normal),
                Ui.Column(rows).Gap(0f))
            .Gap(6f);
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
