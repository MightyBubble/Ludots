using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public readonly struct RaylibFieldTexturePlan
    {
        public RaylibFieldTexturePlan(
            GlobalFieldVisualId id,
            IntRect boundsCells,
            int cellSizeCm,
            int textureWidth,
            int textureHeight,
            int cellCount,
            int dirtyRectCount,
            int dirtyUploadArea,
            bool fullUpload)
        {
            Id = id;
            BoundsCells = boundsCells;
            CellSizeCm = cellSizeCm;
            TextureWidth = textureWidth;
            TextureHeight = textureHeight;
            CellCount = cellCount;
            DirtyRectCount = dirtyRectCount;
            DirtyUploadArea = dirtyUploadArea;
            FullUpload = fullUpload;
        }

        public readonly GlobalFieldVisualId Id;
        public readonly IntRect BoundsCells;
        public readonly int CellSizeCm;
        public readonly int TextureWidth;
        public readonly int TextureHeight;
        public readonly int CellCount;
        public readonly int DirtyRectCount;
        public readonly int DirtyUploadArea;
        public readonly bool FullUpload;
    }

    public sealed class RaylibFieldRenderPerformer : IDisposable
    {
        private readonly Dictionary<GlobalFieldVisualId, FieldTextureState> _stateById = new();
        private readonly List<FieldTextureState> _states = new();
        private RaylibFieldTexturePlan[] _plans = Array.Empty<RaylibFieldTexturePlan>();
        private bool _disposed;

        public float FogOverlayY { get; set; } = 0.08f;

        public int LastFieldTextureCount { get; private set; }
        public int LastFieldCellCount { get; private set; }
        public int LastDirtyUploadCount { get; private set; }
        public int LastDirtyUploadArea { get; private set; }
        public int LastDrawCount { get; private set; }
        public int LastUnsupportedFieldCount { get; private set; }

        public ReadOnlySpan<RaylibFieldTexturePlan> BuildTexturePlan(GlobalFieldVisualBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            ThrowIfDisposed();
            ReadOnlySpan<GlobalFieldVisualRecord> records = buffer.GetRecords();
            EnsurePlanCapacity(records.Length);
            LastFieldTextureCount = 0;
            LastFieldCellCount = 0;
            LastDirtyUploadCount = 0;
            LastDirtyUploadArea = 0;
            LastDrawCount = 0;
            LastUnsupportedFieldCount = 0;

            int planCount = 0;
            for (int recordIndex = 0; recordIndex < records.Length; recordIndex++)
            {
                ref readonly GlobalFieldVisualRecord record = ref records[recordIndex];
                if (!record.IsActive)
                {
                    continue;
                }

                GlobalFieldVisualDescriptor descriptor = record.Descriptor;
                if (descriptor.Id.Kind is not (GlobalFieldVisualKind.Fog or GlobalFieldVisualKind.Influence))
                {
                    LastUnsupportedFieldCount++;
                    throw new InvalidOperationException(
                        $"Raylib Global Field renderer does not support field kind '{descriptor.Id.Kind}' yet. Publish through the shared buffer, then add an explicit Raylib renderer contract for that kind.");
                }

                if (descriptor.ValueKind != GlobalFieldVisualValueKind.Byte)
                {
                    throw new InvalidOperationException(
                        $"Raylib field renderer requires byte-valued cells, but field '{descriptor.Id}' published {descriptor.ValueKind}.");
                }

                if (descriptor.BoundsCells.Width <= 0 || descriptor.BoundsCells.Height <= 0)
                {
                    continue;
                }

                FieldTextureState state = GetOrCreateState(descriptor.Id);
                state.Kind = descriptor.Id.Kind;
                bool fullUpload = EnsureStateSize(state, descriptor.BoundsCells);
                ReadOnlySpan<GlobalFieldVisualCell> cells = buffer.GetCells(record);
                ReadOnlySpan<IntRect> dirtyRects = buffer.GetDirtyRects(record);

                if (fullUpload)
                {
                    if (descriptor.Id.Kind == GlobalFieldVisualKind.Fog)
                    {
                        FillRect(state.Pixels, state.Width, new IntRect(0, 0, state.Width, state.Height), FogVisibilityUnseen);
                    }
                    else
                    {
                        FillRectRgba(state.Pixels, state.Width, new IntRect(0, 0, state.Width, state.Height), 0, 0, 0, 0);
                    }

                    ApplyCells(state, cells);
                    SetSingleDirtyRect(state, new IntRect(0, 0, state.Width, state.Height));
                }
                else
                {
                    BuildDirtyRects(state, dirtyRects);
                    if (state.DirtyRectCount > 0)
                    {
                        ClearDirtyRects(state);
                        ApplyCells(state, cells);
                    }
                }

                int dirtyArea = CalculateDirtyArea(state);
                LastFieldTextureCount++;
                LastFieldCellCount += cells.Length;
                LastDirtyUploadCount += state.DirtyRectCount;
                LastDirtyUploadArea += dirtyArea;
                _plans[planCount++] = new RaylibFieldTexturePlan(
                    descriptor.Id,
                    descriptor.BoundsCells,
                    descriptor.CellSizeCm,
                    state.Width,
                    state.Height,
                    cells.Length,
                    state.DirtyRectCount,
                    dirtyArea,
                    fullUpload);
            }

            return _plans.AsSpan(0, planCount);
        }

        public bool TryGetStagedPixel(GlobalFieldVisualId id, FieldCell2D cell, out Color color)
        {
            if (_stateById.TryGetValue(id, out FieldTextureState? state) &&
                state.TryCellToTexture(cell, out int x, out int y))
            {
                int pixel = ((y * state.Width) + x) * 4;
                color = new Color(
                    state.Pixels[pixel],
                    state.Pixels[pixel + 1],
                    state.Pixels[pixel + 2],
                    state.Pixels[pixel + 3]);
                return true;
            }

            color = default;
            return false;
        }

        public void Draw(GlobalFieldVisualBuffer buffer)
        {
            ReadOnlySpan<RaylibFieldTexturePlan> plans = BuildTexturePlan(buffer);
            if (plans.IsEmpty)
            {
                return;
            }

            Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
            Rl.rlDisableDepthMask();

            for (int i = 0; i < plans.Length; i++)
            {
                ref readonly RaylibFieldTexturePlan plan = ref plans[i];
                FieldTextureState state = _stateById[plan.Id];
                DrawCellCubes(state, plan.CellSizeCm);
                LastDrawCount++;
            }

            Rl.rlEnableDepthMask();
            Rl.EndBlendMode();
        }

        public void Dispose()
        {
            _disposed = true;
        }

        private FieldTextureState GetOrCreateState(GlobalFieldVisualId id)
        {
            if (_stateById.TryGetValue(id, out FieldTextureState? state))
            {
                return state;
            }

            state = new FieldTextureState(id);
            _stateById.Add(id, state);
            _states.Add(state);
            return state;
        }

        private static bool EnsureStateSize(FieldTextureState state, IntRect boundsCells)
        {
            int width = boundsCells.Width;
            int height = boundsCells.Height;
            bool changed = state.Width != width || state.Height != height || state.BoundsCells != boundsCells;
            if (!changed && state.Pixels.Length >= width * height * 4)
            {
                state.DirtyRectCount = 0;
                return false;
            }

            state.BoundsCells = boundsCells;
            state.Width = width;
            state.Height = height;
            int byteCount = checked(width * height * 4);
            if (state.Pixels.Length < byteCount)
            {
                state.Pixels = new byte[byteCount];
            }

            state.DirtyRectCount = 0;
            return true;
        }

        private static void ApplyCells(FieldTextureState state, ReadOnlySpan<GlobalFieldVisualCell> cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                ref readonly GlobalFieldVisualCell cell = ref cells[i];
                if (!state.TryCellToTexture(cell.Cell, out int x, out int y))
                {
                    continue;
                }

                if (state.Kind == GlobalFieldVisualKind.Influence)
                {
                    SetInfluencePixel(state.Pixels, state.Width, x, y, cell.ByteValue);
                }
                else
                {
                    SetPixel(state.Pixels, state.Width, x, y, cell.ByteValue);
                }
            }
        }

        private static void BuildDirtyRects(FieldTextureState state, ReadOnlySpan<IntRect> dirtyRects)
        {
            state.DirtyRectCount = 0;
            for (int i = 0; i < dirtyRects.Length; i++)
            {
                if (!TryClipToTexture(state.BoundsCells, dirtyRects[i], out IntRect textureRect))
                {
                    continue;
                }

                EnsureDirtyRectCapacity(state, state.DirtyRectCount + 1);
                state.DirtyRects[state.DirtyRectCount++] = textureRect;
            }
        }

        private static void ClearDirtyRects(FieldTextureState state)
        {
            for (int i = 0; i < state.DirtyRectCount; i++)
            {
                if (state.Kind == GlobalFieldVisualKind.Influence)
                {
                    FillRectRgba(state.Pixels, state.Width, state.DirtyRects[i], 0, 0, 0, 0);
                }
                else
                {
                    FillRect(state.Pixels, state.Width, state.DirtyRects[i], FogVisibilityUnseen);
                }
            }
        }

        private static int CalculateDirtyArea(FieldTextureState state)
        {
            int area = 0;
            for (int i = 0; i < state.DirtyRectCount; i++)
            {
                IntRect rect = state.DirtyRects[i];
                area += rect.Width * rect.Height;
            }

            return area;
        }

        private static void SetSingleDirtyRect(FieldTextureState state, IntRect rect)
        {
            EnsureDirtyRectCapacity(state, 1);
            state.DirtyRects[0] = rect;
            state.DirtyRectCount = 1;
        }

        private static void EnsureDirtyRectCapacity(FieldTextureState state, int required)
        {
            if (required <= state.DirtyRects.Length)
            {
                return;
            }

            int next = Math.Max(4, state.DirtyRects.Length);
            while (next < required)
            {
                next *= 2;
            }

            Array.Resize(ref state.DirtyRects, next);
        }

        private static bool TryClipToTexture(IntRect boundsCells, IntRect dirtyCells, out IntRect textureRect)
        {
            int left = Math.Max(boundsCells.Left, dirtyCells.Left);
            int top = Math.Max(boundsCells.Top, dirtyCells.Top);
            int right = Math.Min(boundsCells.Right, dirtyCells.Right);
            int bottom = Math.Min(boundsCells.Bottom, dirtyCells.Bottom);
            if (right <= left || bottom <= top)
            {
                textureRect = default;
                return false;
            }

            textureRect = new IntRect(
                left - boundsCells.X,
                top - boundsCells.Y,
                right - left,
                bottom - top);
            return true;
        }

        private static void FillRect(byte[] pixels, int textureWidth, IntRect rect, byte visibility)
        {
            ResolveFogColorBytes(visibility, out byte r, out byte g, out byte b, out byte a);
            FillRectRgba(pixels, textureWidth, rect, r, g, b, a);
        }

        private static void FillRectRgba(
            byte[] pixels,
            int textureWidth,
            IntRect rect,
            byte r,
            byte g,
            byte b,
            byte a)
        {
            int endY = rect.Y + rect.Height;
            int endX = rect.X + rect.Width;
            for (int y = rect.Y; y < endY; y++)
            {
                int row = y * textureWidth;
                for (int x = rect.X; x < endX; x++)
                {
                    int pixel = (row + x) * 4;
                    pixels[pixel] = r;
                    pixels[pixel + 1] = g;
                    pixels[pixel + 2] = b;
                    pixels[pixel + 3] = a;
                }
            }
        }

        private static void SetPixel(byte[] pixels, int textureWidth, int x, int y, byte visibility)
        {
            ResolveFogColorBytes(visibility, out byte r, out byte g, out byte b, out byte a);
            int pixel = ((y * textureWidth) + x) * 4;
            pixels[pixel] = r;
            pixels[pixel + 1] = g;
            pixels[pixel + 2] = b;
            pixels[pixel + 3] = a;
        }

        private static void SetInfluencePixel(byte[] pixels, int textureWidth, int x, int y, byte intensity)
        {
            ResolveInfluenceColorBytes(intensity, out byte r, out byte g, out byte b, out byte a);
            int pixel = ((y * textureWidth) + x) * 4;
            pixels[pixel] = r;
            pixels[pixel + 1] = g;
            pixels[pixel + 2] = b;
            pixels[pixel + 3] = a;
        }

        public static Color ResolveInfluenceColor(byte intensity)
        {
            ResolveInfluenceColorBytes(intensity, out byte r, out byte g, out byte b, out byte a);
            return new Color(r, g, b, a);
        }

        private static void ResolveInfluenceColorBytes(byte intensity, out byte r, out byte g, out byte b, out byte a)
        {
            // Warm threat heat: amber → crimson; keep mid values readable on dark terrain.
            float t = intensity / 255f;
            r = (byte)Math.Clamp((int)Math.Round(190 + (55 * t)), 0, 255);
            g = (byte)Math.Clamp((int)Math.Round(110 * (1f - (0.75f * t))), 0, 255);
            b = (byte)Math.Clamp((int)Math.Round(48 * (1f - (0.55f * t))), 0, 255);
            a = (byte)Math.Clamp((int)Math.Round(70 + (160 * t)), 0, 255);
        }

        public static Color ResolveFogColor(byte visibility)
        {
            ResolveFogColorBytes(visibility, out byte r, out byte g, out byte b, out byte a);
            return new Color(r, g, b, a);
        }

        private static void ResolveFogColorBytes(byte visibility, out byte r, out byte g, out byte b, out byte a)
        {
            switch (visibility)
            {
                case FogVisibilityVisible:
                    r = 72;
                    g = 214;
                    b = 255;
                    a = 90;
                    return;
                case FogVisibilityExplored:
                    r = 32;
                    g = 58;
                    b = 92;
                    a = 150;
                    return;
                case FogVisibilityDenied:
                    r = 224;
                    g = 34;
                    b = 82;
                    a = 178;
                    return;
                case FogVisibilityUnseen:
                    r = 0;
                    g = 0;
                    b = 0;
                    a = 190;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(visibility), visibility, "Unsupported fog visibility.");
            }
        }

        private void DrawCellCubes(FieldTextureState state, int cellSizeCm)
        {
            float cellMeters = WorldUnits.CmToM(cellSizeCm);
            float height = MathF.Max(0.02f, cellMeters * 0.08f);
            int width = state.Width;
            int heightCells = state.Height;
            byte[] pixels = state.Pixels;
            int originCellX = state.BoundsCells.X;
            int originCellY = state.BoundsCells.Y;

            for (int ty = 0; ty < heightCells; ty++)
            {
                int row = ty * width;
                for (int tx = 0; tx < width; tx++)
                {
                    int pixel = (row + tx) * 4;
                    byte a = pixels[pixel + 3];
                    if (a == 0)
                    {
                        continue;
                    }

                    var color = new Color(pixels[pixel], pixels[pixel + 1], pixels[pixel + 2], a);
                    float worldXCm = (originCellX + tx) * cellSizeCm + (cellSizeCm * 0.5f);
                    float worldYCm = (originCellY + ty) * cellSizeCm + (cellSizeCm * 0.5f);
                    Vector3 center = WorldUnits.WorldCmToVisualMeters(worldXCm, worldYCm, FogOverlayY);
                    Rl.DrawCube(center, cellMeters, height, cellMeters, color);
                }
            }
        }

        private void EnsurePlanCapacity(int required)
        {
            if (required <= _plans.Length)
            {
                return;
            }

            Array.Resize(ref _plans, NextCapacity(_plans.Length, required));
        }

        private static int NextCapacity(int current, int required)
        {
            int next = Math.Max(4, current);
            while (next < required)
            {
                next *= 2;
            }

            return next;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibFieldRenderPerformer));
            }
        }

        private const byte FogVisibilityUnseen = 0;
        private const byte FogVisibilityExplored = 1;
        private const byte FogVisibilityVisible = 2;
        private const byte FogVisibilityDenied = 3;

        private sealed class FieldTextureState
        {
            public FieldTextureState(GlobalFieldVisualId id)
            {
                Id = id;
            }

            public readonly GlobalFieldVisualId Id;
            public GlobalFieldVisualKind Kind;
            public IntRect BoundsCells;
            public int Width;
            public int Height;
            public byte[] Pixels = Array.Empty<byte>();
            public IntRect[] DirtyRects = Array.Empty<IntRect>();
            public int DirtyRectCount;

            public bool TryCellToTexture(FieldCell2D cell, out int x, out int y)
            {
                x = cell.X - BoundsCells.X;
                y = cell.Y - BoundsCells.Y;
                return (uint)x < (uint)Width && (uint)y < (uint)Height;
            }
        }
    }
}
