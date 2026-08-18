using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Presentation.Navigation;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    /// <summary>
    /// Raylib consumer for the Core-owned baked NavMesh presentation buffer.
    /// Keeps one native GPU mesh per resident tile and allocates no managed memory after cache warmup.
    /// Vertices come exclusively from authoritative NavTile VertexXcm/Ycm/Zcm; no visual-heightmap draping,
    /// because draping would replace authoritative baked height with an unrelated authored surface.
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

        // areaId 0 keeps the style fill; other ids cycle a deterministic tint so distinct
        // walkability areas stay visually separable in the debug overlay.
        private static readonly Color[] AreaTints =
        {
            new(255, 120, 90, 255),
            new(120, 200, 255, 255),
            new(190, 140, 255, 255),
            new(140, 255, 170, 255),
            new(255, 220, 110, 255),
            new(255, 160, 220, 255),
            new(110, 240, 240, 255),
            new(200, 200, 120, 255),
        };

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

        public void Draw(NavMeshPresentationBuffer buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ThrowIfDisposed();

            DrawnTileCountLastFrame = 0;
            DrawnTriangleCountLastFrame = 0;
            RebuiltTileCountLastFrame = 0;

            ReadOnlySpan<NavTile> tiles = buffer.Tiles;
            ulong frame = buffer.FrameGeneration;

            // Reconcile against fixed capacity before creating anything: mark cached slots
            // whose keys are still published this frame, then evict every slot that is
            // missing. A full cache whose tile IDs changed between frames must free the stale
            // slots instead of throwing on capacity, and an empty frame evicts the whole
            // cache without drawing or fabricating any slot.
            MarkPresentCachedSlots(tiles, buffer, frame);
            EvictMissing(frame);

            if (tiles.IsEmpty)
            {
                return;
            }

            NavMeshPresentationStyle style = buffer.Style;
            bool fillPass = style.DrawFill;
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
                    bool dirty = _checksums[slot] != buffer.TileChecksums[i] ||
                                 _stateRevisions[slot] != buffer.StateRevision ||
                                 _originXcm[slot] != tile.OriginXcm ||
                                 _originZcm[slot] != tile.OriginZcm ||
                                 _vertexCounts[slot] != tile.VertexCount ||
                                 _triangleCounts[slot] != tile.TriangleCount;
                    if (dirty)
                    {
                        RebuildSlot(slot, tile, buffer.TileChecksums[i], buffer.StateRevision, in style);
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

            if (style.DrawEdges)
            {
                Color edgeColor = ToColor(style.EdgeColor);
                float heightOffsetMeters = style.HeightOffsetMeters;
                for (int i = 0; i < tiles.Length; i++)
                {
                    DrawEdges(tiles[i], edgeColor, heightOffsetMeters);
                }
            }
        }

        private void MarkPresentCachedSlots(ReadOnlySpan<NavTile> tiles, NavMeshPresentationBuffer buffer, ulong frame)
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                NavTile tile = tiles[i] ?? throw new InvalidOperationException($"NavMesh presentation tile[{i}] is null.");
                var key = new CacheKey(buffer.Layer, buffer.Profile, tile.TileId);
                if (_slotByKey.TryGetValue(key, out int slot))
                {
                    _lastSeenFrames[slot] = frame;
                }
            }
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

        /// <summary>
        /// Pure, native-free projection of one NavTile vertex to world meters using authoritative
        /// VertexXcm/Ycm/Zcm plus tile origin. Validates the index. Exposed so contract tests can
        /// assert real geometry consumption without a display.
        /// </summary>
        public static Vector3 ProjectVertex(NavTile tile, int index, float heightOffsetMeters)
        {
            if (tile == null)
            {
                throw new ArgumentNullException(nameof(tile));
            }

            if ((uint)index >= (uint)tile.VertexCount)
            {
                throw new InvalidOperationException(
                    $"NavTile {tile.TileId} contains vertex index {index} outside VertexCount {tile.VertexCount}.");
            }

            int xCm = checked(tile.OriginXcm + tile.VertexXcm[index]);
            int zCm = checked(tile.OriginZcm + tile.VertexZcm[index]);
            return new Vector3(
                xCm * CmToMeters,
                tile.VertexYcm[index] * CmToMeters + heightOffsetMeters,
                zCm * CmToMeters);
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
            ReadOnlySpan<int> triA = tile.TriA;
            ReadOnlySpan<int> triB = tile.TriB;
            ReadOnlySpan<int> triC = tile.TriC;
            ReadOnlySpan<byte> areaIds = tile.TriAreaIds;
            byte red = ToByte(fillColor.Red);
            byte green = ToByte(fillColor.Green);
            byte blue = ToByte(fillColor.Blue);
            byte alpha = ToByte(fillColor.Alpha);

            for (int triangle = 0; triangle < tile.TriangleCount; triangle++)
            {
                Vector3 a = ProjectVertex(tile, triA[triangle], heightOffsetMeters);
                Vector3 b = ProjectVertex(tile, triB[triangle], heightOffsetMeters);
                Vector3 c = ProjectVertex(tile, triC[triangle], heightOffsetMeters);
                Vector3 normal = Vector3.Cross(b - a, c - a);
                normal = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : Vector3.UnitY;
                int vertexOffset = triangle * 9;
                WriteVector(vertices, vertexOffset, in a);
                WriteVector(vertices, vertexOffset + 3, in b);
                WriteVector(vertices, vertexOffset + 6, in c);
                WriteVector(normals, vertexOffset, in normal);
                WriteVector(normals, vertexOffset + 3, in normal);
                WriteVector(normals, vertexOffset + 6, in normal);

                byte triangleRed = red;
                byte triangleGreen = green;
                byte triangleBlue = blue;
                if (triangle < areaIds.Length && areaIds[triangle] != 0)
                {
                    Color tint = AreaTints[areaIds[triangle] % AreaTints.Length];
                    triangleRed = tint.r;
                    triangleGreen = tint.g;
                    triangleBlue = tint.b;
                }

                int colorOffset = triangle * 12;
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    int offset = colorOffset + vertex * 4;
                    colors[offset] = triangleRed;
                    colors[offset + 1] = triangleGreen;
                    colors[offset + 2] = triangleBlue;
                    colors[offset + 3] = alpha;
                }
            }

            Rl.UploadMesh(ref mesh, false);
            return mesh;
        }

        private void DrawEdges(NavTile tile, Color color, float heightOffsetMeters)
        {
            ReadOnlySpan<int> triA = tile.TriA;
            ReadOnlySpan<int> triB = tile.TriB;
            ReadOnlySpan<int> triC = tile.TriC;
            ReadOnlySpan<int> n0 = tile.N0;
            ReadOnlySpan<int> n1 = tile.N1;
            ReadOnlySpan<int> n2 = tile.N2;
            for (int triangle = 0; triangle < tile.TriangleCount; triangle++)
            {
                Vector3 a = ProjectVertex(tile, triA[triangle], heightOffsetMeters);
                Vector3 b = ProjectVertex(tile, triB[triangle], heightOffsetMeters);
                Vector3 c = ProjectVertex(tile, triC[triangle], heightOffsetMeters);
                if (n0[triangle] < 0 || triangle < n0[triangle]) Rl.DrawLine3D(a, b, color);
                if (n1[triangle] < 0 || triangle < n1[triangle]) Rl.DrawLine3D(b, c, color);
                if (n2[triangle] < 0 || triangle < n2[triangle]) Rl.DrawLine3D(c, a, color);
            }
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
