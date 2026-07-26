using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Presentation.Terrain;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    /// <summary>
    /// Raylib consumer for the Core-owned baked NavMesh presentation buffer.
    /// Keeps one native GPU mesh per resident tile and allocates no managed memory after cache warmup.
    /// When a VisualHeightmap is bound, presentation verts are draped onto that surface so relief
    /// terrain cannot bury the mesh under authored visual height.
    /// </summary>
    public sealed unsafe class RaylibNavMeshPresentationRenderer : IDisposable
    {
        private const float CmToMeters = 0.01f;
        private readonly Dictionary<CacheKey, int> _slotByKey;
        private readonly CacheKey[] _keys;
        private readonly Mesh[] _meshes;
        private readonly ulong[] _checksums;
        private readonly uint[] _stateRevisions;
        private readonly int[] _originXcm;
        private readonly int[] _originZcm;
        private readonly int[] _vertexCounts;
        private readonly int[] _triangleCounts;
        private readonly ulong[] _lastSeenFrames;
        private readonly byte[] _occupied;
        private readonly int[] _freeSlots;
        private int _freeCount;
        private Material _material;
        private bool _materialLoaded;
        private bool _disposed;
        private IVisualHeightmap? _visualHeightmap;
        private int _visualHeightmapRevision = int.MinValue;
        private int _drawnDrapeRevision = int.MinValue;

        public RaylibNavMeshPresentationRenderer(int tileCapacity)
        {
            if (tileCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileCapacity), tileCapacity, "NavMesh renderer tile capacity must be > 0.");
            }

            _slotByKey = new Dictionary<CacheKey, int>(tileCapacity);
            _keys = new CacheKey[tileCapacity];
            _meshes = new Mesh[tileCapacity];
            _checksums = new ulong[tileCapacity];
            _stateRevisions = new uint[tileCapacity];
            _originXcm = new int[tileCapacity];
            _originZcm = new int[tileCapacity];
            _vertexCounts = new int[tileCapacity];
            _triangleCounts = new int[tileCapacity];
            _lastSeenFrames = new ulong[tileCapacity];
            _occupied = new byte[tileCapacity];
            _freeSlots = new int[tileCapacity];
            _freeCount = tileCapacity;
            for (int i = 0; i < tileCapacity; i++)
            {
                _freeSlots[i] = tileCapacity - 1 - i;
            }
        }

        public int CachedTileCount => _slotByKey.Count;
        public int DrawnTileCountLastFrame { get; private set; }
        public int DrawnTriangleCountLastFrame { get; private set; }
        public int RebuiltTileCountLastFrame { get; private set; }

        /// <summary>
        /// Bind the authored visual ground used for presentation draping. Null clears draping and
        /// restores VertexYcm from the NavMesh buffer. Call once per frame before <see cref="Draw"/>.
        /// </summary>
        public void BindVisualHeightmap(IVisualHeightmap? heightmap)
        {
            ThrowIfDisposed();
            _visualHeightmap = heightmap;
            _visualHeightmapRevision = heightmap is IVisualHeightmapRenderSource renderSource
                ? renderSource.Revision
                : heightmap == null ? int.MinValue : 0;
        }

        public void Draw(NavMeshPresentationBuffer buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ThrowIfDisposed();

            DrawnTileCountLastFrame = 0;
            DrawnTriangleCountLastFrame = 0;
            RebuiltTileCountLastFrame = 0;

            ReadOnlySpan<NavTile> tiles = buffer.Tiles;
            ReadOnlySpan<ulong> checksums = buffer.TileChecksums;
            NavMeshPresentationStyle style = buffer.Style;
            ulong frame = buffer.FrameGeneration;
            bool drapeChanged = _visualHeightmapRevision != _drawnDrapeRevision;
            _drawnDrapeRevision = _visualHeightmapRevision;
            bool fillPass = style.DrawFill && !tiles.IsEmpty;
            if (fillPass)
            {
                EnsureMaterial();
                Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
                Rl.rlDisableBackfaceCulling();
                Rl.rlDisableDepthMask();
            }

            try
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    NavTile tile = tiles[i] ?? throw new InvalidOperationException($"NavMesh presentation tile[{i}] is null.");
                    var key = new CacheKey(buffer.Layer, buffer.Profile, tile.TileId);
                    int slot = GetOrCreateSlot(in key);
                    _lastSeenFrames[slot] = frame;
                    bool dirty = drapeChanged ||
                                 _checksums[slot] != checksums[i] ||
                                 _stateRevisions[slot] != buffer.StateRevision ||
                                 _originXcm[slot] != tile.OriginXcm ||
                                 _originZcm[slot] != tile.OriginZcm ||
                                 _vertexCounts[slot] != tile.VertexCount ||
                                 _triangleCounts[slot] != tile.TriangleCount;
                    if (dirty)
                    {
                        RebuildSlot(slot, tile, checksums[i], buffer.StateRevision, in style);
                        RebuiltTileCountLastFrame++;
                    }

                    if (fillPass && _meshes[slot].vertexCount > 0)
                    {
                        Rl.DrawMesh(_meshes[slot], _material, RaylibMatrix.Identity);
                    }

                    DrawnTileCountLastFrame++;
                    DrawnTriangleCountLastFrame = checked(DrawnTriangleCountLastFrame + tile.TriangleCount);
                }
            }
            finally
            {
                if (fillPass)
                {
                    Rl.rlEnableDepthMask();
                    Rl.rlEnableBackfaceCulling();
                    Rl.EndBlendMode();
                }
            }

            Color edgeColor = ToColor(style.EdgeColor);
            Color boundsColor = ToColor(style.TileBoundsColor);
            NavQueryTileSpace tileSpace = buffer.TileSpace;
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTile tile = tiles[i];
                if (style.DrawEdges)
                {
                    DrawEdges(tile, edgeColor, style.HeightOffsetMeters);
                }

                if (style.DrawTileBounds)
                {
                    DrawTileBounds(tile.TileId.ChunkX, tile.TileId.ChunkY, ResolveTileMinY(tile, style.HeightOffsetMeters, in tileSpace), tileSpace, boundsColor);
                }
            }

            if (style.DrawTileStateIndication)
            {
                ReadOnlySpan<Ludots.Core.Navigation.NavMesh.Bake.NavBakeTileCoord> stateCoords = buffer.TileStateCoords;
                ReadOnlySpan<NavMeshPresentationTileState> tileStates = buffer.TileStates;
                for (int i = 0; i < stateCoords.Length; i++)
                {
                    Color stateColor = ToColor(style.ResolveTileStateColor(tileStates[i]));
                    float stateY = ResolveTileBoundsY(
                        stateCoords[i].ChunkX,
                        stateCoords[i].ChunkY,
                        in tileSpace,
                        style.HeightOffsetMeters + 0.04f);
                    DrawTileBounds(
                        stateCoords[i].ChunkX,
                        stateCoords[i].ChunkY,
                        stateY,
                        tileSpace,
                        stateColor);
                }
            }

            EvictMissing(frame);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < _occupied.Length; i++)
            {
                if (_occupied[i] != 0 && _meshes[i].vertexCount > 0)
                {
                    Rl.UnloadMesh(_meshes[i]);
                }
            }

            if (_materialLoaded)
            {
                Rl.UnloadMaterial(_material);
                _materialLoaded = false;
            }

            _slotByKey.Clear();
            _disposed = true;
        }

        private int GetOrCreateSlot(in CacheKey key)
        {
            if (_slotByKey.TryGetValue(key, out int existing))
            {
                return existing;
            }

            if (_freeCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Raylib NavMesh GPU cache capacity ({_occupied.Length}) exhausted by resident tiles.");
            }

            int slot = _freeSlots[--_freeCount];
            _keys[slot] = key;
            _occupied[slot] = 1;
            _slotByKey.Add(key, slot);
            return slot;
        }

        private void RebuildSlot(
            int slot,
            NavTile tile,
            ulong checksum,
            uint stateRevision,
            in NavMeshPresentationStyle style)
        {
            if (_meshes[slot].vertexCount > 0)
            {
                Rl.UnloadMesh(_meshes[slot]);
                _meshes[slot] = default;
            }

            if (style.DrawFill && tile.TriangleCount > 0)
            {
                _meshes[slot] = CreateTriangleMesh(tile, style.FillColor, style.HeightOffsetMeters);
            }

            _checksums[slot] = checksum;
            _stateRevisions[slot] = stateRevision;
            _originXcm[slot] = tile.OriginXcm;
            _originZcm[slot] = tile.OriginZcm;
            _vertexCounts[slot] = tile.VertexCount;
            _triangleCounts[slot] = tile.TriangleCount;
        }

        private Mesh CreateTriangleMesh(
            NavTile tile,
            NavMeshPresentationColor fillColor,
            float heightOffsetMeters)
        {
            int expandedVertexCount = checked(tile.TriangleCount * 3);
            var mesh = new Mesh
            {
                vertexCount = expandedVertexCount,
                triangleCount = tile.TriangleCount
            };
            int vectorFloatCount = checked(expandedVertexCount * 3);
            int colorByteCount = checked(expandedVertexCount * 4);
            mesh.vertices = (float*)Rl.MemAlloc(checked(sizeof(float) * vectorFloatCount));
            mesh.normals = (float*)Rl.MemAlloc(checked(sizeof(float) * vectorFloatCount));
            mesh.colors = (byte*)Rl.MemAlloc(colorByteCount);

            Span<float> vertices = new Span<float>(mesh.vertices, vectorFloatCount);
            Span<float> normals = new Span<float>(mesh.normals, vectorFloatCount);
            Span<byte> colors = new Span<byte>(mesh.colors, colorByteCount);
            ReadOnlySpan<int> triA = tile.ActiveTriA;
            ReadOnlySpan<int> triB = tile.ActiveTriB;
            ReadOnlySpan<int> triC = tile.ActiveTriC;
            byte red = ToByte(fillColor.Red);
            byte green = ToByte(fillColor.Green);
            byte blue = ToByte(fillColor.Blue);
            byte alpha = ToByte(fillColor.Alpha);

            for (int triangle = 0; triangle < tile.TriangleCount; triangle++)
            {
                Vector3 a = ResolveVertex(tile, triA[triangle], heightOffsetMeters);
                Vector3 b = ResolveVertex(tile, triB[triangle], heightOffsetMeters);
                Vector3 c = ResolveVertex(tile, triC[triangle], heightOffsetMeters);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                normal = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : Vector3.UnitY;
                int vertexOffset = triangle * 9;
                WriteVector(vertices, vertexOffset, in a);
                WriteVector(vertices, vertexOffset + 3, in b);
                WriteVector(vertices, vertexOffset + 6, in c);
                WriteVector(normals, vertexOffset, in normal);
                WriteVector(normals, vertexOffset + 3, in normal);
                WriteVector(normals, vertexOffset + 6, in normal);

                int colorOffset = triangle * 12;
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    int offset = colorOffset + vertex * 4;
                    colors[offset] = red;
                    colors[offset + 1] = green;
                    colors[offset + 2] = blue;
                    colors[offset + 3] = alpha;
                }
            }

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        private void DrawEdges(NavTile tile, Color color, float heightOffsetMeters)
        {
            ReadOnlySpan<int> triA = tile.ActiveTriA;
            ReadOnlySpan<int> triB = tile.ActiveTriB;
            ReadOnlySpan<int> triC = tile.ActiveTriC;
            ReadOnlySpan<int> n0 = tile.ActiveN0;
            ReadOnlySpan<int> n1 = tile.ActiveN1;
            ReadOnlySpan<int> n2 = tile.ActiveN2;
            for (int triangle = 0; triangle < tile.TriangleCount; triangle++)
            {
                Vector3 a = ResolveVertex(tile, triA[triangle], heightOffsetMeters);
                Vector3 b = ResolveVertex(tile, triB[triangle], heightOffsetMeters);
                Vector3 c = ResolveVertex(tile, triC[triangle], heightOffsetMeters);
                if (n0[triangle] < 0 || triangle < n0[triangle]) Rl.DrawLine3D(a, b, color);
                if (n1[triangle] < 0 || triangle < n1[triangle]) Rl.DrawLine3D(b, c, color);
                if (n2[triangle] < 0 || triangle < n2[triangle]) Rl.DrawLine3D(c, a, color);
            }
        }

        private float ResolveTileMinY(NavTile tile, float heightOffsetMeters, in NavQueryTileSpace tileSpace)
        {
            if (_visualHeightmap != null)
            {
                return ResolveTileBoundsY(tile.TileId.ChunkX, tile.TileId.ChunkY, in tileSpace, heightOffsetMeters);
            }

            ReadOnlySpan<int> y = tile.ActiveVertexYcm;
            if (y.IsEmpty)
            {
                return heightOffsetMeters;
            }

            int minYcm = y[0];
            for (int i = 1; i < y.Length; i++)
            {
                if (y[i] < minYcm) minYcm = y[i];
            }

            return minYcm * CmToMeters + heightOffsetMeters;
        }

        private float ResolveTileBoundsY(int tileX, int tileZ, in NavQueryTileSpace tileSpace, float heightOffsetMeters)
        {
            if (_visualHeightmap == null)
            {
                return heightOffsetMeters;
            }

            int minXcm = checked(tileSpace.OriginXcm + tileX * tileSpace.TileWidthCm);
            int minZcm = checked(tileSpace.OriginZcm + tileZ * tileSpace.TileHeightCm);
            int midXcm = checked(minXcm + tileSpace.TileWidthCm / 2);
            int midZcm = checked(minZcm + tileSpace.TileHeightCm / 2);
            if (!_visualHeightmap.TrySampleHeightCm(midXcm, midZcm, out float heightCm))
            {
                throw new InvalidOperationException(
                    $"NavMesh presentation draping requires VisualHeightmap coverage at tile center ({midXcm},{midZcm}).");
            }

            return heightCm * CmToMeters + heightOffsetMeters;
        }

        private static void DrawTileBounds(
            int tileX,
            int tileZ,
            float y,
            NavQueryTileSpace tileSpace,
            Color color)
        {
            int minXcm = checked(tileSpace.OriginXcm + tileX * tileSpace.TileWidthCm);
            int minZcm = checked(tileSpace.OriginZcm + tileZ * tileSpace.TileHeightCm);
            float minX = minXcm * CmToMeters;
            float minZ = minZcm * CmToMeters;
            float maxX = checked(minXcm + tileSpace.TileWidthCm) * CmToMeters;
            float maxZ = checked(minZcm + tileSpace.TileHeightCm) * CmToMeters;
            var a = new Vector3(minX, y, minZ);
            var b = new Vector3(maxX, y, minZ);
            var c = new Vector3(maxX, y, maxZ);
            var d = new Vector3(minX, y, maxZ);
            Rl.DrawLine3D(a, b, color);
            Rl.DrawLine3D(b, c, color);
            Rl.DrawLine3D(c, d, color);
            Rl.DrawLine3D(d, a, color);
        }

        private void EvictMissing(ulong frame)
        {
            for (int slot = 0; slot < _occupied.Length; slot++)
            {
                if (_occupied[slot] == 0 || _lastSeenFrames[slot] == frame)
                {
                    continue;
                }

                if (_meshes[slot].vertexCount > 0)
                {
                    Rl.UnloadMesh(_meshes[slot]);
                    _meshes[slot] = default;
                }

                if (!_slotByKey.Remove(_keys[slot]))
                {
                    throw new InvalidOperationException("Raylib NavMesh GPU cache index diverged from its occupied slot table.");
                }

                _occupied[slot] = 0;
                _checksums[slot] = 0UL;
                _stateRevisions[slot] = 0U;
                _lastSeenFrames[slot] = 0UL;
                _freeSlots[_freeCount++] = slot;
            }
        }

        private void EnsureMaterial()
        {
            if (_materialLoaded)
            {
                return;
            }

            _material = Rl.LoadMaterialDefault();
            _materialLoaded = true;
        }

        private Vector3 ResolveVertex(NavTile tile, int index, float heightOffsetMeters)
        {
            if ((uint)index >= (uint)tile.VertexCount)
            {
                throw new InvalidOperationException(
                    $"NavTile {tile.TileId} contains vertex index {index} outside VertexCount {tile.VertexCount}.");
            }

            int xCm = checked(tile.OriginXcm + tile.VertexXcm[index]);
            int zCm = checked(tile.OriginZcm + tile.VertexZcm[index]);
            float yMeters;
            if (_visualHeightmap != null)
            {
                if (!_visualHeightmap.TrySampleHeightCm(xCm, zCm, out float heightCm))
                {
                    throw new InvalidOperationException(
                        $"NavMesh presentation draping requires VisualHeightmap coverage at ({xCm},{zCm}).");
                }

                yMeters = heightCm * CmToMeters + heightOffsetMeters;
            }
            else
            {
                yMeters = tile.VertexYcm[index] * CmToMeters + heightOffsetMeters;
            }

            return new Vector3(xCm * CmToMeters, yMeters, zCm * CmToMeters);
        }

        private static void WriteVector(Span<float> destination, int offset, in Vector3 value)
        {
            destination[offset] = value.X;
            destination[offset + 1] = value.Y;
            destination[offset + 2] = value.Z;
        }

        private static Color ToColor(NavMeshPresentationColor color)
            => new Color(ToByte(color.Red), ToByte(color.Green), ToByte(color.Blue), ToByte(color.Alpha));

        private static byte ToByte(float channel) => (byte)MathF.Round(channel * 255f);

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibNavMeshPresentationRenderer));
            }
        }

        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public CacheKey(int layer, int profile, in NavTileId tileId)
            {
                Layer = layer;
                Profile = profile;
                TileId = tileId;
            }

            public int Layer { get; }
            public int Profile { get; }
            public NavTileId TileId { get; }

            public bool Equals(CacheKey other)
                => Layer == other.Layer && Profile == other.Profile && TileId.Equals(other.TileId);

            public override bool Equals(object? obj) => obj is CacheKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Layer, Profile, TileId);
        }
    }
}
