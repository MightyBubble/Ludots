using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Client.WebGpu.Runtime;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Adapter.WebGpu;

/// <summary>
/// Formal world-anchored WebGPU HUD store consuming <see cref="WorldHudBatchBuffer"/> directly.
/// Static bar/glyph lanes retain content; movement updates only compact world anchors.
/// Camera projection is performed on GPU from <see cref="WebGpuCameraFrame"/>.
/// </summary>
public sealed class WorldAnchoredWebGpuHudStore
{
    private readonly PresentationTextGlyphCompiler _textCompiler;
    private readonly Dictionary<int, int> _barIndexByStableId = new(1024);
    private readonly Dictionary<int, int> _textIndexByStableId = new(1024);

    private RetainedBarSlot[] _bars = Array.Empty<RetainedBarSlot>();
    private RetainedTextSlot[] _texts = Array.Empty<RetainedTextSlot>();
    private WebGpuWorldBarInstance[] _barInstances = Array.Empty<WebGpuWorldBarInstance>();
    private WebGpuWorldHudAnchor[] _anchors = Array.Empty<WebGpuWorldHudAnchor>();
    private WebGpuWorldGlyphInstance[] _glyphInstances = Array.Empty<WebGpuWorldGlyphInstance>();

    private int _barCount;
    private int _textCount;
    private int _barInstanceCount;
    private int _anchorCount;
    private int _glyphInstanceCount;
    private int _lastContentRevision = -1;
    private int _lastProjectionRevision = -1;
    private bool _built;

    private int _barDirtyStart = int.MaxValue;
    private int _barDirtyEnd = -1;
    private int _anchorDirtyStart = int.MaxValue;
    private int _anchorDirtyEnd = -1;
    private int _glyphDirtyStart = int.MaxValue;
    private int _glyphDirtyEnd = -1;
    private bool _barFullUpload;
    private bool _anchorFullUpload;
    private bool _glyphFullUpload;

    private WebGpuHudBuildDiagnostics _buildDiagnostics;
    private WebGpuHudUploadPlan _uploadPlan;

    public WorldAnchoredWebGpuHudStore(PresentationTextGlyphCompiler textCompiler)
    {
        _textCompiler = textCompiler ?? throw new ArgumentNullException(nameof(textCompiler));
    }

    public ReadOnlySpan<WebGpuWorldBarInstance> BarInstances =>
        _barInstances.AsSpan(0, _barInstanceCount);

    public ReadOnlySpan<WebGpuWorldHudAnchor> Anchors =>
        _anchors.AsSpan(0, _anchorCount);

    public ReadOnlySpan<WebGpuWorldGlyphInstance> GlyphInstances =>
        _glyphInstances.AsSpan(0, _glyphInstanceCount);

    public WebGpuHudUploadPlan UploadPlan => _uploadPlan;

    public WebGpuHudBuildDiagnostics BuildDiagnostics => _buildDiagnostics;

    public int BarCount => _barCount;

    public int TextCount => _textCount;

    public int AnchorCount => _anchorCount;

    public int GlyphCount => _glyphInstanceCount;

    public void Sync(WorldHudBatchBuffer worldHud)
    {
        if (worldHud is null)
        {
            throw new ArgumentNullException(nameof(worldHud));
        }

        ResetFrameCounters();
        long frameStart = Stopwatch.GetTimestamp();

        if (!_built)
        {
            FullRebuild(worldHud);
        }
        else if (worldHud.ContentRevision == _lastContentRevision)
        {
            _buildDiagnostics.Path = WebGpuHudBuildPath.NoChange;
        }
        else if (!TryApplyDeltas(worldHud))
        {
            FullRebuild(worldHud);
        }

        _buildDiagnostics.TotalMilliseconds = ElapsedMilliseconds(frameStart);
        _buildDiagnostics.HudCpuBuildMilliseconds = _buildDiagnostics.TotalMilliseconds;
        _buildDiagnostics.BarInstanceCount = _barInstanceCount;
        _buildDiagnostics.LabelAnchorCount = _anchorCount;
        _buildDiagnostics.GlyphCount = _glyphInstanceCount;
        _buildDiagnostics.BarResidentBytes = checked(_barInstanceCount * Unsafe.SizeOf<WebGpuWorldBarInstance>());
        _buildDiagnostics.AnchorResidentBytes = checked(_anchorCount * Unsafe.SizeOf<WebGpuWorldHudAnchor>());
        _buildDiagnostics.GlyphResidentBytes = checked(_glyphInstanceCount * Unsafe.SizeOf<WebGpuWorldGlyphInstance>());
        PopulateUploadPlan();
    }

    private bool TryApplyDeltas(WorldHudBatchBuffer worldHud)
    {
        ReadOnlySpan<WorldHudItem> dirtyContent = worldHud.GetDirtyContentSpan();
        ReadOnlySpan<int> removedStableIds = worldHud.GetRemovedStableIdSpan();
        bool projectionChanged = worldHud.ProjectionRevision != _lastProjectionRevision;

        if (dirtyContent.Length == 0 &&
            removedStableIds.Length == 0 &&
            !projectionChanged)
        {
            return false;
        }

        if (removedStableIds.Length > 0 ||
            !CountsMatch(worldHud.GetSpan()))
        {
            // Structural add/remove requires deterministic anchor-index rebuild for glyph bindings.
            FullRebuild(worldHud);
            return true;
        }

        for (int i = 0; i < dirtyContent.Length; i++)
        {
            ref readonly WorldHudItem item = ref dirtyContent[i];
            if (item.Kind == WorldHudItemKind.Bar)
            {
                UpsertBar(in item);
                _buildDiagnostics.ContentDirtyBars++;
            }
            else if (item.Kind == WorldHudItemKind.Text)
            {
                UpsertText(in item);
                _buildDiagnostics.ContentDirtyTexts++;
            }
        }

        if (projectionChanged)
        {
            ApplyProjectionUpdates(worldHud.GetSpan());
        }

        _buildDiagnostics.Path = WebGpuHudBuildPath.Delta;
        _lastContentRevision = worldHud.ContentRevision;
        _lastProjectionRevision = worldHud.ProjectionRevision;
        worldHud.ClearContentDeltas();
        return true;
    }

    private bool CountsMatch(ReadOnlySpan<WorldHudItem> items)
    {
        int bars = 0;
        int texts = 0;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].Kind == WorldHudItemKind.Bar)
            {
                bars++;
            }
            else if (items[i].Kind == WorldHudItemKind.Text)
            {
                texts++;
            }
        }

        return bars == _barCount && texts == _textCount;
    }

    private void ApplyProjectionUpdates(ReadOnlySpan<WorldHudItem> items)
    {
        long barStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly WorldHudItem item = ref items[i];
            if (item.Kind == WorldHudItemKind.Bar)
            {
                if (!TryResolveBarIndex(item.StableId, preferredIndex: -1, out int barIndex))
                {
                    UpsertBar(in item);
                    continue;
                }

                if (_bars[barIndex].DirtySerial != item.DirtySerial)
                {
                    UpsertBar(in item);
                }
                else if (_bars[barIndex].WorldPosition != item.WorldPosition)
                {
                    UpdateBarAnchor(barIndex, item.WorldPosition);
                }
            }
            else if (item.Kind == WorldHudItemKind.Text)
            {
                if (!TryResolveTextIndex(item.StableId, preferredIndex: -1, out int textIndex))
                {
                    UpsertText(in item);
                    continue;
                }

                if (_texts[textIndex].DirtySerial != item.DirtySerial)
                {
                    UpsertText(in item);
                }
                else if (_texts[textIndex].WorldPosition != item.WorldPosition)
                {
                    UpdateTextAnchor(textIndex, item.WorldPosition);
                }
            }
        }

        _buildDiagnostics.BarBuildMilliseconds += ElapsedMilliseconds(barStart);
    }

    private void FullRebuild(WorldHudBatchBuffer worldHud)
    {
        long barStart = Stopwatch.GetTimestamp();
        ReadOnlySpan<WorldHudItem> items = worldHud.GetSpan();
        _barIndexByStableId.Clear();
        _textIndexByStableId.Clear();
        _barCount = 0;
        _textCount = 0;
        _barInstanceCount = 0;
        _anchorCount = 0;
        _glyphInstanceCount = 0;

        int barCapacity = 0;
        int textCapacity = 0;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].Kind == WorldHudItemKind.Bar)
            {
                barCapacity++;
            }
            else if (items[i].Kind == WorldHudItemKind.Text)
            {
                textCapacity++;
            }
        }

        EnsureBarCapacity(barCapacity);
        EnsureTextCapacity(textCapacity);
        EnsureBarInstanceCapacity(barCapacity);
        EnsureAnchorCapacity(checked(barCapacity + textCapacity));

        int estimatedGlyphs = 0;
        double resolveMilliseconds = 0d;
        for (int i = 0; i < items.Length; i++)
        {
            ref readonly WorldHudItem item = ref items[i];
            if (item.Kind == WorldHudItemKind.Bar)
            {
                int slot = _barCount++;
                int anchorIndex = AppendAnchor(item.WorldPosition);
                _bars[slot] = CreateBarSlot(in item, anchorIndex);
                _barIndexByStableId[item.StableId] = slot;
                WriteBarInstance(slot, in _bars[slot]);
                _buildDiagnostics.BarInstancesBuilt++;
            }
            else if (item.Kind == WorldHudItemKind.Text)
            {
                long resolveStart = Stopwatch.GetTimestamp();
                WebGpuGlyphRun run = _textCompiler.Resolve(in item);
                resolveMilliseconds += ElapsedMilliseconds(resolveStart);
                _buildDiagnostics.TextsResolved++;

                int slot = _textCount++;
                int anchorIndex = AppendAnchor(item.WorldPosition);
                _texts[slot] = CreateTextSlot(in item, run, anchorIndex, glyphStart: estimatedGlyphs);
                _textIndexByStableId[item.StableId] = slot;
                estimatedGlyphs = checked(estimatedGlyphs + run.GlyphCount);
            }
        }

        EnsureGlyphCapacity(estimatedGlyphs);
        long glyphStart = Stopwatch.GetTimestamp();
        for (int i = 0; i < _textCount; i++)
        {
            WriteTextGlyphs(i, markDirty: false);
        }

        _buildDiagnostics.BarBuildMilliseconds = ElapsedMilliseconds(barStart);
        _buildDiagnostics.TextResolveMilliseconds = resolveMilliseconds;
        _buildDiagnostics.GlyphWriteMilliseconds = ElapsedMilliseconds(glyphStart);
        _buildDiagnostics.Path = WebGpuHudBuildPath.FullRebuild;
        _buildDiagnostics.FullRebuildCount = 1;
        _buildDiagnostics.RemovedCount = worldHud.GetRemovedStableIdSpan().Length;
        _barFullUpload = true;
        _anchorFullUpload = true;
        _glyphFullUpload = true;
        _built = true;
        _lastContentRevision = worldHud.ContentRevision;
        _lastProjectionRevision = worldHud.ProjectionRevision;
        worldHud.ClearContentDeltas();
    }

    private void UpsertBar(in WorldHudItem item)
    {
        RequireStableId(item.StableId, "bar");
        if (!TryResolveBarIndex(item.StableId, preferredIndex: -1, out int index))
        {
            throw new InvalidOperationException(
                $"World-anchored HUD bar stableId={item.StableId} is missing from the retained store; cold inserts require a full rebuild.");
        }

        ref RetainedBarSlot slot = ref _bars[index];
        bool contentChanged =
            slot.DirtySerial != item.DirtySerial ||
            slot.Width != item.Width ||
            slot.Height != item.Height ||
            slot.Value0 != item.Value0 ||
            slot.Color0 != item.Color0 ||
            slot.Color1 != item.Color1;
        bool positionChanged = slot.WorldPosition != item.WorldPosition;

        slot.DirtySerial = item.DirtySerial;
        slot.WorldPosition = item.WorldPosition;
        slot.Width = item.Width;
        slot.Height = item.Height;
        slot.Value0 = item.Value0;
        slot.Color0 = item.Color0;
        slot.Color1 = item.Color1;

        if (positionChanged)
        {
            WriteAnchor(slot.AnchorIndex, item.WorldPosition);
            _buildDiagnostics.PositionOnlyBarsUpdated++;
        }

        if (contentChanged)
        {
            WriteBarInstance(index, in slot);
            _buildDiagnostics.BarInstancesBuilt++;
        }
    }

    private void UpsertText(in WorldHudItem item)
    {
        RequireStableId(item.StableId, "text");
        if (!TryResolveTextIndex(item.StableId, preferredIndex: -1, out int index))
        {
            throw new InvalidOperationException(
                $"World-anchored HUD text stableId={item.StableId} is missing from the retained store; cold inserts require a full rebuild.");
        }

        ref RetainedTextSlot slot = ref _texts[index];
        bool contentChanged = slot.DirtySerial != item.DirtySerial;
        bool positionChanged = slot.WorldPosition != item.WorldPosition;
        slot.DirtySerial = item.DirtySerial;
        slot.WorldPosition = item.WorldPosition;
        slot.FontSize = item.FontSize > 0 ? item.FontSize : 16;
        slot.Color = item.Color0;

        if (positionChanged)
        {
            WriteAnchor(slot.AnchorIndex, item.WorldPosition);
            _buildDiagnostics.PositionOnlyTextsUpdated++;
        }

        if (!contentChanged)
        {
            return;
        }

        long resolveStart = Stopwatch.GetTimestamp();
        WebGpuGlyphRun run = _textCompiler.Resolve(in item);
        _buildDiagnostics.TextResolveMilliseconds += ElapsedMilliseconds(resolveStart);
        _buildDiagnostics.TextsResolved++;

        bool countChanged = slot.GlyphCount != run.GlyphCount;
        slot.Run = run;
        slot.GlyphCount = run.GlyphCount;
        if (countChanged)
        {
            RepackGlyphInstances(markFullUpload: true);
            return;
        }

        long glyphStart = Stopwatch.GetTimestamp();
        WriteTextGlyphs(index, markDirty: true);
        _buildDiagnostics.GlyphWriteMilliseconds += ElapsedMilliseconds(glyphStart);
    }

    private void UpdateBarAnchor(int index, Vector3 worldPosition)
    {
        ref RetainedBarSlot slot = ref _bars[index];
        if (slot.WorldPosition == worldPosition)
        {
            return;
        }

        slot.WorldPosition = worldPosition;
        WriteAnchor(slot.AnchorIndex, worldPosition);
        _buildDiagnostics.PositionOnlyBarsUpdated++;
    }

    private void UpdateTextAnchor(int index, Vector3 worldPosition)
    {
        ref RetainedTextSlot slot = ref _texts[index];
        if (slot.WorldPosition == worldPosition)
        {
            return;
        }

        slot.WorldPosition = worldPosition;
        WriteAnchor(slot.AnchorIndex, worldPosition);
        _buildDiagnostics.PositionOnlyTextsUpdated++;
    }

    private void RepackGlyphInstances(bool markFullUpload)
    {
        int totalGlyphs = 0;
        for (int i = 0; i < _textCount; i++)
        {
            totalGlyphs = checked(totalGlyphs + _texts[i].GlyphCount);
        }

        EnsureGlyphCapacity(totalGlyphs);
        long glyphStart = Stopwatch.GetTimestamp();
        _glyphInstanceCount = 0;
        for (int i = 0; i < _textCount; i++)
        {
            _texts[i].GlyphStart = _glyphInstanceCount;
            WriteTextGlyphs(i, markDirty: false);
        }

        _buildDiagnostics.GlyphWriteMilliseconds += ElapsedMilliseconds(glyphStart);
        if (markFullUpload)
        {
            _glyphFullUpload = true;
            MarkGlyphDirty(0, _glyphInstanceCount);
        }
    }

    private void WriteTextGlyphs(int textIndex, bool markDirty)
    {
        ref RetainedTextSlot slot = ref _texts[textIndex];
        WebGpuGlyphRun run = slot.Run
            ?? throw new InvalidOperationException(
                $"World-anchored HUD text stableId={slot.StableId} has no cached glyph run.");
        if (slot.GlyphCount != run.GlyphCount)
        {
            throw new InvalidOperationException(
                $"World-anchored HUD text stableId={slot.StableId} glyph count mismatch: slot={slot.GlyphCount}, run={run.GlyphCount}.");
        }

        EnsureGlyphCapacity(checked(slot.GlyphStart + slot.GlyphCount));
        int written = _textCompiler.WriteLocalOffsetInstances(
            run,
            slot.FontSize,
            slot.Color,
            (uint)slot.AnchorIndex,
            _glyphInstances.AsSpan(slot.GlyphStart, slot.GlyphCount));
        if (written != slot.GlyphCount)
        {
            throw new InvalidOperationException(
                $"World-anchored HUD text stableId={slot.StableId} wrote {written} glyphs but expected {slot.GlyphCount}.");
        }

        int end = checked(slot.GlyphStart + slot.GlyphCount);
        if (end > _glyphInstanceCount)
        {
            _glyphInstanceCount = end;
        }

        _buildDiagnostics.GlyphsWritten += written;
        if (markDirty)
        {
            MarkGlyphDirty(slot.GlyphStart, slot.GlyphCount);
        }
    }

    private void WriteBarInstance(int barIndex, in RetainedBarSlot bar)
    {
        EnsureBarInstanceCapacity(checked(barIndex + 1));
        float value = Math.Clamp(bar.Value0, 0f, 1f);
        _barInstances[barIndex] = new WebGpuWorldBarInstance
        {
            HalfSizePx = new Vector2(bar.Width * 0.5f, bar.Height * 0.5f),
            HealthRatio = value,
            AnchorIndex = bar.AnchorIndex,
            BackgroundColor = bar.Color0,
            ForegroundColor = bar.Color1,
            PaddingPx = new Vector2(1f, 1f),
        };
        if (barIndex + 1 > _barInstanceCount)
        {
            _barInstanceCount = barIndex + 1;
        }

        MarkBarDirty(barIndex, 1);
    }

    private int AppendAnchor(Vector3 worldPosition)
    {
        int index = _anchorCount;
        EnsureAnchorCapacity(checked(index + 1));
        _anchors[index] = new WebGpuWorldHudAnchor
        {
            WorldPosition = worldPosition,
            Visibility = 1f,
        };
        _anchorCount = checked(index + 1);
        MarkAnchorDirty(index, 1);
        return index;
    }

    private void WriteAnchor(int anchorIndex, Vector3 worldPosition)
    {
        if ((uint)anchorIndex >= (uint)_anchorCount)
        {
            throw new InvalidOperationException(
                $"World-anchored HUD anchor index {anchorIndex} is outside live count {_anchorCount}.");
        }

        _anchors[anchorIndex] = new WebGpuWorldHudAnchor
        {
            WorldPosition = worldPosition,
            Visibility = 1f,
        };
        MarkAnchorDirty(anchorIndex, 1);
    }

    private static RetainedBarSlot CreateBarSlot(in WorldHudItem item, int anchorIndex) =>
        new()
        {
            StableId = item.StableId,
            DirtySerial = item.DirtySerial,
            WorldPosition = item.WorldPosition,
            Width = item.Width,
            Height = item.Height,
            Value0 = item.Value0,
            Color0 = item.Color0,
            Color1 = item.Color1,
            AnchorIndex = anchorIndex,
        };

    private static RetainedTextSlot CreateTextSlot(in WorldHudItem item, WebGpuGlyphRun run, int anchorIndex, int glyphStart) =>
        new()
        {
            StableId = item.StableId,
            DirtySerial = item.DirtySerial,
            WorldPosition = item.WorldPosition,
            FontSize = item.FontSize > 0 ? item.FontSize : 16,
            Color = item.Color0,
            Run = run,
            AnchorIndex = anchorIndex,
            GlyphStart = glyphStart,
            GlyphCount = run.GlyphCount,
        };

    private void PopulateUploadPlan()
    {
        PopulateLaneUpload(
            _barFullUpload,
            _barDirtyStart,
            _barDirtyEnd,
            _barInstanceCount,
            out _uploadPlan.BarUploadStart,
            out _uploadPlan.BarUploadCount,
            out _uploadPlan.BarFullUpload);
        PopulateLaneUpload(
            _anchorFullUpload,
            _anchorDirtyStart,
            _anchorDirtyEnd,
            _anchorCount,
            out _uploadPlan.AnchorUploadStart,
            out _uploadPlan.AnchorUploadCount,
            out _uploadPlan.AnchorFullUpload);
        PopulateLaneUpload(
            _glyphFullUpload,
            _glyphDirtyStart,
            _glyphDirtyEnd,
            _glyphInstanceCount,
            out _uploadPlan.GlyphUploadStart,
            out _uploadPlan.GlyphUploadCount,
            out _uploadPlan.GlyphFullUpload);
    }

    private static void PopulateLaneUpload(
        bool fullUpload,
        int dirtyStart,
        int dirtyEnd,
        int liveCount,
        out int uploadStart,
        out int uploadCount,
        out bool uploadFull)
    {
        if (fullUpload)
        {
            uploadStart = 0;
            uploadCount = liveCount;
            uploadFull = true;
            return;
        }

        if (dirtyEnd >= dirtyStart)
        {
            uploadStart = dirtyStart;
            uploadCount = dirtyEnd - dirtyStart + 1;
            uploadFull = false;
            return;
        }

        uploadStart = 0;
        uploadCount = 0;
        uploadFull = false;
    }

    private void ResetFrameCounters()
    {
        _buildDiagnostics = default;
        _uploadPlan = default;
        _barDirtyStart = int.MaxValue;
        _barDirtyEnd = -1;
        _anchorDirtyStart = int.MaxValue;
        _anchorDirtyEnd = -1;
        _glyphDirtyStart = int.MaxValue;
        _glyphDirtyEnd = -1;
        _barFullUpload = false;
        _anchorFullUpload = false;
        _glyphFullUpload = false;
    }

    private void MarkBarDirty(int start, int count) => MarkDirty(ref _barDirtyStart, ref _barDirtyEnd, start, count);
    private void MarkAnchorDirty(int start, int count) => MarkDirty(ref _anchorDirtyStart, ref _anchorDirtyEnd, start, count);
    private void MarkGlyphDirty(int start, int count) => MarkDirty(ref _glyphDirtyStart, ref _glyphDirtyEnd, start, count);

    private static void MarkDirty(ref int dirtyStart, ref int dirtyEnd, int start, int count)
    {
        if (count <= 0)
        {
            return;
        }

        int end = checked(start + count - 1);
        if (start < dirtyStart)
        {
            dirtyStart = start;
        }

        if (end > dirtyEnd)
        {
            dirtyEnd = end;
        }
    }

    private bool TryResolveBarIndex(int stableId, int preferredIndex, out int index)
    {
        if ((uint)preferredIndex < (uint)_barCount && _bars[preferredIndex].StableId == stableId)
        {
            index = preferredIndex;
            return true;
        }

        return _barIndexByStableId.TryGetValue(stableId, out index);
    }

    private bool TryResolveTextIndex(int stableId, int preferredIndex, out int index)
    {
        if ((uint)preferredIndex < (uint)_textCount && _texts[preferredIndex].StableId == stableId)
        {
            index = preferredIndex;
            return true;
        }

        return _textIndexByStableId.TryGetValue(stableId, out index);
    }

    private void EnsureBarCapacity(int required)
    {
        if (_bars.Length < required)
        {
            Array.Resize(ref _bars, Math.Max(required, Math.Max(64, _bars.Length * 2)));
        }
    }

    private void EnsureTextCapacity(int required)
    {
        if (_texts.Length < required)
        {
            Array.Resize(ref _texts, Math.Max(required, Math.Max(64, _texts.Length * 2)));
        }
    }

    private void EnsureBarInstanceCapacity(int required)
    {
        if (_barInstances.Length < required)
        {
            Array.Resize(ref _barInstances, Math.Max(required, Math.Max(64, _barInstances.Length * 2)));
            _barFullUpload = true;
        }
    }

    private void EnsureAnchorCapacity(int required)
    {
        if (_anchors.Length < required)
        {
            Array.Resize(ref _anchors, Math.Max(required, Math.Max(64, _anchors.Length * 2)));
            _anchorFullUpload = true;
        }
    }

    private void EnsureGlyphCapacity(int required)
    {
        if (_glyphInstances.Length < required)
        {
            Array.Resize(ref _glyphInstances, Math.Max(required, Math.Max(256, _glyphInstances.Length * 2)));
            _glyphFullUpload = true;
        }
    }

    private static void RequireStableId(int stableId, string kind)
    {
        if (stableId <= 0)
        {
            throw new InvalidOperationException(
                $"World-anchored HUD requires StableId > 0 for under-UI {kind} items.");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ElapsedMilliseconds(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;

    private struct RetainedBarSlot
    {
        public int StableId;
        public int DirtySerial;
        public Vector3 WorldPosition;
        public float Width;
        public float Height;
        public float Value0;
        public Vector4 Color0;
        public Vector4 Color1;
        public int AnchorIndex;
    }

    private struct RetainedTextSlot
    {
        public int StableId;
        public int DirtySerial;
        public Vector3 WorldPosition;
        public int FontSize;
        public Vector4 Color;
        public WebGpuGlyphRun? Run;
        public int AnchorIndex;
        public int GlyphStart;
        public int GlyphCount;
    }
}

public enum WebGpuHudBuildPath : byte
{
    None = 0,
    NoChange = 1,
    Delta = 2,
    FullRebuild = 3,
}

public struct WebGpuHudBuildDiagnostics
{
    public WebGpuHudBuildPath Path;
    public double TotalMilliseconds;
    public double HudCpuBuildMilliseconds;
    public double BarBuildMilliseconds;
    public double TextResolveMilliseconds;
    public double GlyphWriteMilliseconds;
    public int BarInstanceCount;
    public int LabelAnchorCount;
    public int GlyphCount;
    public int BarResidentBytes;
    public int AnchorResidentBytes;
    public int GlyphResidentBytes;
    public int BarInstancesBuilt;
    public int TextsResolved;
    public int GlyphsWritten;
    public int PositionOnlyBarsUpdated;
    public int PositionOnlyTextsUpdated;
    public int ContentDirtyBars;
    public int ContentDirtyTexts;
    public int RemovedCount;
    public int FullRebuildCount;
}

public struct WebGpuHudUploadPlan
{
    public int BarUploadStart;
    public int BarUploadCount;
    public bool BarFullUpload;
    public int AnchorUploadStart;
    public int AnchorUploadCount;
    public bool AnchorFullUpload;
    public int GlyphUploadStart;
    public int GlyphUploadCount;
    public bool GlyphFullUpload;

    public readonly int BarUploadBytes =>
        checked(BarUploadCount * Unsafe.SizeOf<WebGpuWorldBarInstance>());

    public readonly int AnchorUploadBytes =>
        checked(AnchorUploadCount * Unsafe.SizeOf<WebGpuWorldHudAnchor>());

    public readonly int GlyphUploadBytes =>
        checked(GlyphUploadCount * Unsafe.SizeOf<WebGpuWorldGlyphInstance>());
}
