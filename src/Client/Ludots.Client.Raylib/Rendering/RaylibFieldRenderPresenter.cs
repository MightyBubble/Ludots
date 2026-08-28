using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

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

    public sealed unsafe class RaylibFieldRenderPresenter : IDisposable
    {
        private readonly Dictionary<GlobalFieldVisualId, FieldTextureState> _stateById = new();
        private readonly List<FieldTextureState> _states = new();
        private RaylibFieldTexturePlan[] _plans = Array.Empty<RaylibFieldTexturePlan>();
        private byte[] _uploadScratch = Array.Empty<byte>();
        private Mesh _quadMesh;
        private Material _material;
        private bool _quadMeshLoaded;
        private bool _materialLoaded;
        private bool _disposed;

        public float FogOverlayY { get; set; } = 0.08f;
        public float DiscreteOwnershipOverlayY { get; set; } = 0.06f;

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
                if (descriptor.Id.Kind is not (
                    GlobalFieldVisualKind.Fog or
                    GlobalFieldVisualKind.DiscreteOwnership))
                {
                    LastUnsupportedFieldCount++;
                    throw new InvalidOperationException(
                        $"Raylib Global Field renderer does not support field kind '{descriptor.Id.Kind}' yet. Publish through the shared buffer, then add an explicit Raylib renderer contract for that kind.");
                }

                if (descriptor.Id.Kind == GlobalFieldVisualKind.Fog &&
                    descriptor.ValueKind != GlobalFieldVisualValueKind.Byte)
                {
                    throw new InvalidOperationException(
                        $"Raylib fog field renderer requires byte-valued cells, but field '{descriptor.Id}' published {descriptor.ValueKind}.");
                }

                if (descriptor.Id.Kind == GlobalFieldVisualKind.DiscreteOwnership &&
                    descriptor.ValueKind is not (
                        GlobalFieldVisualValueKind.Byte or
                        GlobalFieldVisualValueKind.Vector4))
                {
                    throw new InvalidOperationException(
                        $"Raylib discrete ownership renderer requires byte or Vector4 cells, but field '{descriptor.Id}' published {descriptor.ValueKind}.");
                }

                if (descriptor.BoundsCells.Width <= 0 || descriptor.BoundsCells.Height <= 0)
                {
                    continue;
                }

                FieldTextureState state = GetOrCreateState(descriptor.Id);
                bool fullUpload = EnsureStateContract(state, in descriptor);
                ReadOnlySpan<GlobalFieldVisualCell> cells = buffer.GetCells(record);
                ReadOnlySpan<IntRect> dirtyRects = buffer.GetDirtyRects(record);

                if (fullUpload)
                {
                    ClearRect(state, new IntRect(0, 0, state.Width, state.Height));
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

            EnsureRaylibResources();
            Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
            Rl.rlDisableBackfaceCulling();
            Rl.rlDisableDepthMask();

            for (int i = 0; i < plans.Length; i++)
            {
                ref readonly RaylibFieldTexturePlan plan = ref plans[i];
                FieldTextureState state = _stateById[plan.Id];
                UploadDirtyRects(state);
                DrawTexturePlane(state, plan.CellSizeCm);
                LastDrawCount++;
            }

            Rl.rlEnableDepthMask();
            Rl.rlEnableBackfaceCulling();
            Rl.EndBlendMode();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < _states.Count; i++)
            {
                FieldTextureState state = _states[i];
                if (state.TextureLoaded)
                {
                    Rl.UnloadTexture(state.Texture);
                    state.TextureLoaded = false;
                }
            }

            if (_quadMeshLoaded)
            {
                Rl.UnloadMesh(_quadMesh);
                _quadMeshLoaded = false;
            }

            if (_materialLoaded)
            {
                Rl.UnloadMaterial(_material);
                _materialLoaded = false;
            }

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

        private static bool EnsureStateContract(
            FieldTextureState state,
            in GlobalFieldVisualDescriptor descriptor)
        {
            IntRect boundsCells = descriptor.BoundsCells;
            int width = boundsCells.Width;
            int height = boundsCells.Height;
            bool changed =
                state.Width != width ||
                state.Height != height ||
                state.BoundsCells != boundsCells ||
                state.ValueKind != descriptor.ValueKind ||
                state.PaletteId != descriptor.PaletteId;
            if (!changed && state.Pixels.Length >= width * height * 4)
            {
                state.DirtyRectCount = 0;
                return false;
            }

            state.BoundsCells = boundsCells;
            state.Width = width;
            state.Height = height;
            state.ValueKind = descriptor.ValueKind;
            state.PaletteId = descriptor.PaletteId;
            int byteCount = checked(width * height * 4);
            if (state.Pixels.Length < byteCount)
            {
                state.Pixels = new byte[byteCount];
            }

            state.DirtyRectCount = 0;
            state.GpuUploaded = false;
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

                SetPixel(state, x, y, in cell);
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
                ClearRect(state, state.DirtyRects[i]);
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

        private static void ClearRect(FieldTextureState state, IntRect rect)
        {
            ResolveClearColorBytes(state.Id.Kind, out byte r, out byte g, out byte b, out byte a);
            int endY = rect.Y + rect.Height;
            int endX = rect.X + rect.Width;
            for (int y = rect.Y; y < endY; y++)
            {
                int row = y * state.Width;
                for (int x = rect.X; x < endX; x++)
                {
                    int pixel = (row + x) * 4;
                    state.Pixels[pixel] = r;
                    state.Pixels[pixel + 1] = g;
                    state.Pixels[pixel + 2] = b;
                    state.Pixels[pixel + 3] = a;
                }
            }
        }

        private static void SetPixel(
            FieldTextureState state,
            int x,
            int y,
            in GlobalFieldVisualCell cell)
        {
            ResolveCellColorBytes(
                state.Id.Kind,
                state.ValueKind,
                state.PaletteId,
                in cell,
                out byte r,
                out byte g,
                out byte b,
                out byte a);
            int pixel = ((y * state.Width) + x) * 4;
            state.Pixels[pixel] = r;
            state.Pixels[pixel + 1] = g;
            state.Pixels[pixel + 2] = b;
            state.Pixels[pixel + 3] = a;
        }

        public static Color ResolveFogColor(byte visibility)
        {
            ResolveFogColorBytes(visibility, out byte r, out byte g, out byte b, out byte a);
            return new Color(r, g, b, a);
        }

        public static Color ResolveDiscreteOwnershipColor(int projectedId, int paletteId = 0)
        {
            ResolveDiscreteOwnershipColorBytes(
                projectedId,
                paletteId,
                out byte r,
                out byte g,
                out byte b,
                out byte a);
            return new Color(r, g, b, a);
        }

        public static Vector4 ResolveDiscreteOwnershipColorVector(int projectedId)
        {
            ResolveDiscreteOwnershipColorBytes(
                projectedId,
                paletteId: 0,
                out byte r,
                out byte g,
                out byte b,
                out byte a);
            const float scale = 1f / byte.MaxValue;
            return new Vector4(r * scale, g * scale, b * scale, a * scale);
        }

        private static void ResolveClearColorBytes(
            GlobalFieldVisualKind kind,
            out byte r,
            out byte g,
            out byte b,
            out byte a)
        {
            if (kind == GlobalFieldVisualKind.Fog)
            {
                ResolveFogColorBytes(FogVisibilityUnseen, out r, out g, out b, out a);
                return;
            }

            r = 0;
            g = 0;
            b = 0;
            a = 0;
        }

        private static void ResolveCellColorBytes(
            GlobalFieldVisualKind kind,
            GlobalFieldVisualValueKind valueKind,
            int paletteId,
            in GlobalFieldVisualCell cell,
            out byte r,
            out byte g,
            out byte b,
            out byte a)
        {
            if (kind == GlobalFieldVisualKind.Fog)
            {
                ResolveFogColorBytes(cell.ByteValue, out r, out g, out b, out a);
                return;
            }

            if (valueKind == GlobalFieldVisualValueKind.Byte)
            {
                ResolveDiscreteOwnershipColorBytes(cell.ByteValue, paletteId, out r, out g, out b, out a);
                return;
            }

            Vector4 color = cell.FloatValue;
            r = ToColorByte(color.X);
            g = ToColorByte(color.Y);
            b = ToColorByte(color.Z);
            a = ToColorByte(color.W);
        }

        private static void ResolveDiscreteOwnershipColorBytes(
            int projectedId,
            int paletteId,
            out byte r,
            out byte g,
            out byte b,
            out byte a)
        {
            if (projectedId <= 0)
            {
                r = 0;
                g = 0;
                b = 0;
                a = 0;
                return;
            }

            uint hash = unchecked((uint)projectedId * 0x9E3779B1u) ^
                        unchecked((uint)paletteId * 0x85EBCA77u);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            r = (byte)(72 + (hash & 0x7F));
            g = (byte)(72 + ((hash >> 8) & 0x7F));
            b = (byte)(72 + ((hash >> 16) & 0x7F));
            a = 118;
        }

        private static byte ToColorByte(float value)
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            return (byte)MathF.Round(clamped * byte.MaxValue);
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

        private void UploadDirtyRects(FieldTextureState state)
        {
            EnsureTexture(state);
            if (!state.GpuUploaded && state.DirtyRectCount == 0)
            {
                SetSingleDirtyRect(state, new IntRect(0, 0, state.Width, state.Height));
            }

            for (int i = 0; i < state.DirtyRectCount; i++)
            {
                IntRect rect = state.DirtyRects[i];
                if (rect.X == 0 && rect.Y == 0 && rect.Width == state.Width && rect.Height == state.Height)
                {
                    fixed (byte* ptr = state.Pixels)
                    {
                        Rl.UpdateTexture(state.Texture, ptr);
                    }
                }
                else
                {
                    int byteCount = checked(rect.Width * rect.Height * 4);
                    EnsureUploadScratch(byteCount);
                    CopyTextureRect(state.Pixels, state.Width, rect, _uploadScratch);
                    fixed (byte* ptr = _uploadScratch)
                    {
                        Rl.UpdateTextureRec(
                            state.Texture,
                            new Rectangle(rect.X, rect.Y, rect.Width, rect.Height),
                            ptr);
                    }
                }
            }

            state.GpuUploaded = true;
        }

        private void EnsureTexture(FieldTextureState state)
        {
            if (state.TextureLoaded &&
                state.Texture.width == state.Width &&
                state.Texture.height == state.Height)
            {
                return;
            }

            if (state.TextureLoaded)
            {
                Rl.UnloadTexture(state.Texture);
                state.Texture = default;
                state.TextureLoaded = false;
            }

            Image image = Rl.GenImageColor(state.Width, state.Height, Color.BLANK);
            state.Texture = Rl.LoadTextureFromImage(image);
            Rl.UnloadImage(image);
            state.TextureLoaded = true;
            state.GpuUploaded = false;
        }

        private void DrawTexturePlane(FieldTextureState state, int cellSizeCm)
        {
            EnsureRaylibResources();
            int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
            _material.maps[albedoIndex].texture = state.Texture;
            _material.maps[albedoIndex].color = Color.WHITE;

            float widthMeters = WorldUnits.CmToM(state.Width * cellSizeCm);
            float heightMeters = WorldUnits.CmToM(state.Height * cellSizeCm);
            WorldCmInt2 originWorld = new(
                state.BoundsCells.X * cellSizeCm,
                state.BoundsCells.Y * cellSizeCm);
            float overlayY = state.Id.Kind == GlobalFieldVisualKind.DiscreteOwnership
                ? DiscreteOwnershipOverlayY
                : FogOverlayY;
            Vector3 origin = WorldUnits.WorldCmToVisualMeters(in originWorld, overlayY);
            RaylibMatrix transform = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(widthMeters, 1f, heightMeters) *
                Matrix4x4.CreateTranslation(origin));
            Rl.DrawMesh(_quadMesh, _material, transform);
        }

        private void EnsureRaylibResources()
        {
            if (!_quadMeshLoaded)
            {
                _quadMesh = CreateQuadMesh();
                _quadMeshLoaded = true;
            }

            if (!_materialLoaded)
            {
                _material = Rl.LoadMaterialDefault();
                _materialLoaded = true;
            }
        }

        private static Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh
            {
                vertexCount = 4,
                triangleCount = 2,
            };

            mesh.vertices = (float*)Rl.MemAlloc(sizeof(float) * 12);
            mesh.normals = (float*)Rl.MemAlloc(sizeof(float) * 12);
            mesh.texcoords = (float*)Rl.MemAlloc(sizeof(float) * 8);
            mesh.indices = (ushort*)Rl.MemAlloc(sizeof(ushort) * 6);

            Span<float> vertices = new(mesh.vertices, 12);
            vertices[0] = 0f; vertices[1] = 0f; vertices[2] = 0f;
            vertices[3] = 1f; vertices[4] = 0f; vertices[5] = 0f;
            vertices[6] = 1f; vertices[7] = 0f; vertices[8] = 1f;
            vertices[9] = 0f; vertices[10] = 0f; vertices[11] = 1f;

            Span<float> normals = new(mesh.normals, 12);
            for (int i = 0; i < 4; i++)
            {
                int offset = i * 3;
                normals[offset] = 0f;
                normals[offset + 1] = 1f;
                normals[offset + 2] = 0f;
            }

            Span<float> uvs = new(mesh.texcoords, 8);
            uvs[0] = 0f; uvs[1] = 0f;
            uvs[2] = 1f; uvs[3] = 0f;
            uvs[4] = 1f; uvs[5] = 1f;
            uvs[6] = 0f; uvs[7] = 1f;

            mesh.indices[0] = 0;
            mesh.indices[1] = 1;
            mesh.indices[2] = 2;
            mesh.indices[3] = 0;
            mesh.indices[4] = 2;
            mesh.indices[5] = 3;

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        private void EnsurePlanCapacity(int required)
        {
            if (required <= _plans.Length)
            {
                return;
            }

            Array.Resize(ref _plans, NextCapacity(_plans.Length, required));
        }

        private void EnsureUploadScratch(int required)
        {
            if (required <= _uploadScratch.Length)
            {
                return;
            }

            Array.Resize(ref _uploadScratch, NextCapacity(_uploadScratch.Length, required));
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

        private static void CopyTextureRect(byte[] source, int sourceWidth, IntRect rect, byte[] destination)
        {
            int rowBytes = rect.Width * 4;
            for (int y = 0; y < rect.Height; y++)
            {
                int sourceOffset = (((rect.Y + y) * sourceWidth) + rect.X) * 4;
                int destinationOffset = y * rowBytes;
                source.AsSpan(sourceOffset, rowBytes).CopyTo(destination.AsSpan(destinationOffset, rowBytes));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibFieldRenderPresenter));
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
            public IntRect BoundsCells;
            public int Width;
            public int Height;
            public GlobalFieldVisualValueKind ValueKind;
            public int PaletteId;
            public byte[] Pixels = Array.Empty<byte>();
            public IntRect[] DirtyRects = Array.Empty<IntRect>();
            public int DirtyRectCount;
            public Texture2D Texture;
            public bool TextureLoaded;
            public bool GpuUploaded;

            public bool TryCellToTexture(FieldCell2D cell, out int x, out int y)
            {
                x = cell.X - BoundsCells.X;
                y = cell.Y - BoundsCells.Y;
                return (uint)x < (uint)Width && (uint)y < (uint)Height;
            }
        }
    }
}
