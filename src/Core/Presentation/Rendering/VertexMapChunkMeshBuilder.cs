using System;
using System.Numerics;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Presentation.Rendering
{
    public sealed class ChunkMeshWriteBuffer
    {
        public float[] Vertices = Array.Empty<float>();
        public float[] Normals = Array.Empty<float>();
        public byte[] Colors = Array.Empty<byte>();
        public int VertexCount;

        public void Clear()
        {
            VertexCount = 0;
        }

        public void EnsureAdditionalVertices(int addVertexCount)
        {
            int required = VertexCount + addVertexCount;
            int requiredV = required * 3;
            int requiredC = required * 4;

            if (Vertices.Length < requiredV)
            {
                Array.Resize(ref Vertices, NextCapacity(requiredV));
            }

            if (Normals.Length < requiredV)
            {
                Array.Resize(ref Normals, NextCapacity(requiredV));
            }

            if (Colors.Length < requiredC)
            {
                Array.Resize(ref Colors, NextCapacity(requiredC));
            }
        }

        public void AppendVertex(in Vector3 pos, in Vector3 normal, in Vector4 color)
        {
            int vBase = VertexCount * 3;
            Vertices[vBase] = pos.X;
            Vertices[vBase + 1] = pos.Y;
            Vertices[vBase + 2] = pos.Z;

            Normals[vBase] = normal.X;
            Normals[vBase + 1] = normal.Y;
            Normals[vBase + 2] = normal.Z;

            int cBase = VertexCount * 4;
            Colors[cBase] = ToByte(color.X);
            Colors[cBase + 1] = ToByte(color.Y);
            Colors[cBase + 2] = ToByte(color.Z);
            Colors[cBase + 3] = ToByte(color.W);

            VertexCount++;
        }

        private static int NextCapacity(int required)
        {
            int cap = 256;
            while (cap < required) cap <<= 1;
            return cap;
        }

        private static byte ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f);
        }
    }

    public sealed class VertexMapChunkMeshData
    {
        public readonly ChunkMeshWriteBuffer Terrain = new ChunkMeshWriteBuffer();
        public readonly ChunkMeshWriteBuffer Water = new ChunkMeshWriteBuffer();
    }

    public sealed class VertexMapChunkMeshBuilder
    {
        private readonly struct Vtx
        {
            public readonly int C;
            public readonly int R;
            public readonly Vector3 Pos;
            public readonly float WaterY;
            public readonly byte H;
            public readonly byte W;
            public readonly bool IsRamp;
            public readonly byte Veg;
            public readonly Vector4 Color;

            public Vtx(int c, int r, Vector3 pos, float waterY, byte h, byte w, bool isRamp, byte veg, Vector4 color)
            {
                C = c;
                R = r;
                Pos = pos;
                WaterY = waterY;
                H = h;
                W = w;
                IsRamp = isRamp;
                Veg = veg;
                Color = color;
            }
        }

        private readonly VertexMap _map;
        private readonly int _mapWidth;
        private readonly int _mapHeight;

        private float _offsetX;
        private float _offsetZ;
        private float _hScale;

        public VertexMapChunkMeshBuilder(VertexMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _mapWidth = map.WidthInChunks * VertexChunk.ChunkSize;
            _mapHeight = map.HeightInChunks * VertexChunk.ChunkSize;
        }

        public void BuildChunk(int chunkX, int chunkY, float offsetX, float offsetZ, float heightScale, bool simplifiedCliffs, VertexMapChunkMeshData dst)
        {
            if (dst == null) throw new ArgumentNullException(nameof(dst));

            _offsetX = offsetX;
            _offsetZ = offsetZ;
            _hScale = heightScale;

            dst.Terrain.Clear();
            dst.Water.Clear();

            int startC = chunkX * VertexChunk.ChunkSize;
            int startR = chunkY * VertexChunk.ChunkSize;
            int endC = startC + VertexChunk.ChunkSize;
            int endR = startR + VertexChunk.ChunkSize;

            for (int r = startR; r < endR; r++)
            {
                for (int c = startC; c < endC; c++)
                {
                    if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) continue;
                    if (r >= _mapHeight - 1 || c >= _mapWidth - 1) continue;

                    bool isOdd = (r & 1) == 1;

                    Vtx v1 = GetVertex(c, r);

                    Vtx t1p1, t1p2, t1p3;
                    Vtx t2p1, t2p2, t2p3;

                    if (!isOdd)
                    {
                        t1p1 = v1;
                        t1p2 = GetVertex(c + 1, r);
                        t1p3 = GetVertex(c, r + 1);

                        t2p1 = GetVertex(c + 1, r);
                        t2p2 = GetVertex(c + 1, r + 1);
                        t2p3 = GetVertex(c, r + 1);
                    }
                    else
                    {
                        t1p1 = v1;
                        t1p2 = GetVertex(c + 1, r);
                        t1p3 = GetVertex(c + 1, r + 1);

                        t2p1 = v1;
                        t2p2 = GetVertex(c + 1, r + 1);
                        t2p3 = GetVertex(c, r + 1);
                    }

                    AddFace(t1p1, t1p3, t1p2, simplifiedCliffs, dst.Terrain);
                    AddFace(t2p1, t2p3, t2p2, simplifiedCliffs, dst.Terrain);

                    AddWaterTri(t1p1, t1p3, t1p2, dst.Water);
                    AddWaterTri(t2p1, t2p3, t2p2, dst.Water);
                }
            }
        }

        private Vtx GetVertex(int c, int r)
        {
            byte h = GetHeight(c, r);
            byte w = GetWater(c, r);
            byte biome = GetBiome(c, r);
            byte veg = GetVeg(c, r);
            bool isRamp = GetRamp(c, r);

            bool f0 = GetExtraFlag(c, r, TerrainVisualLayers.Flag0);
            bool f1 = GetExtraFlag(c, r, TerrainVisualLayers.Flag1);
            bool f2 = GetExtraFlag(c, r, TerrainVisualLayers.Flag2);
            byte b0 = GetExtraByte(c, r, TerrainVisualLayers.Byte0);

            Vector4 col = TerrainVisualRules.GetVertexColor(h, biome, f0, f1, f2, b0);

            float x = HexCoordinates.HexWidth * (c + 0.5f * (r & 1)) + _offsetX;
            float z = HexCoordinates.RowSpacing * r + _offsetZ;
            float y = h * _hScale;
            float waterY = w * _hScale;
            return new Vtx(c, r, new Vector3(x, y, z), waterY, h, w, isRamp, veg, col);
        }

        private byte GetHeight(int c, int r)
        {
            if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) return 0;
            var chunk = _map.GetChunk(c, r, false);
            if (chunk == null) return 0;
            return chunk.GetHeight(c & VertexChunk.ChunkSizeMask, r & VertexChunk.ChunkSizeMask);
        }

        private byte GetWater(int c, int r)
        {
            if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) return 0;
            var chunk = _map.GetChunk(c, r, false);
            if (chunk == null) return 0;
            return chunk.GetWaterHeight(c & VertexChunk.ChunkSizeMask, r & VertexChunk.ChunkSizeMask);
        }

        private byte GetBiome(int c, int r)
        {
            if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) return 0;
            var chunk = _map.GetChunk(c, r, false);
            if (chunk == null) return 0;
            return chunk.GetBiome(c & VertexChunk.ChunkSizeMask, r & VertexChunk.ChunkSizeMask);
        }

        private byte GetVeg(int c, int r)
        {
            if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) return 0;
            var chunk = _map.GetChunk(c, r, false);
            if (chunk == null) return 0;
            return chunk.GetVegetation(c & VertexChunk.ChunkSizeMask, r & VertexChunk.ChunkSizeMask);
        }

        private bool GetRamp(int c, int r)
        {
            if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) return false;
            var chunk = _map.GetChunk(c, r, false);
            if (chunk == null) return false;
            return chunk.GetRamp(c & VertexChunk.ChunkSizeMask, r & VertexChunk.ChunkSizeMask);
        }

        private bool GetExtraFlag(int c, int r, int layerIndex)
        {
            if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) return false;
            var chunk = _map.GetChunk(c, r, false);
            if (chunk == null) return false;
            return chunk.GetExtraFlag(c & VertexChunk.ChunkSizeMask, r & VertexChunk.ChunkSizeMask, layerIndex);
        }

        private byte GetExtraByte(int c, int r, int layerIndex)
        {
            if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) return 0;
            var chunk = _map.GetChunk(c, r, false);
            if (chunk == null) return 0;
            return chunk.GetExtraByte(c & VertexChunk.ChunkSizeMask, r & VertexChunk.ChunkSizeMask, layerIndex);
        }

        private bool GetCliffStraighten(int cA, int rA, int cB, int rB)
        {
            int baseC;
            int baseR;
            int edgeIndex;

            if (rA == rB)
            {
                if (cA + 1 == cB)
                {
                    baseC = cA;
                    baseR = rA;
                    edgeIndex = 0;
                }
                else if (cB + 1 == cA)
                {
                    baseC = cB;
                    baseR = rB;
                    edgeIndex = 0;
                }
                else
                {
                    return false;
                }
            }
            else if (rA + 1 == rB || rB + 1 == rA)
            {
                bool aUpper = rA < rB;
                int upC = aUpper ? cA : cB;
                int upR = aUpper ? rA : rB;
                int downC = aUpper ? cB : cA;
                int downR = aUpper ? rB : rA;

                if (downR != upR + 1) return false;

                bool isOdd = (upR & 1) == 1;
                int brC = isOdd ? upC + 1 : upC;
                int brR = upR + 1;
                int blC = isOdd ? upC : upC - 1;
                int blR = upR + 1;

                if (downC == brC && downR == brR)
                {
                    baseC = upC;
                    baseR = upR;
                    edgeIndex = 1;
                }
                else if (downC == blC && downR == blR)
                {
                    baseC = upC;
                    baseR = upR;
                    edgeIndex = 2;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if ((uint)baseC >= (uint)_mapWidth || (uint)baseR >= (uint)_mapHeight) return false;
            var chunk = _map.GetChunk(baseC, baseR, false);
            if (chunk == null) return false;
            return chunk.GetCliffStraightenEdge(baseC & VertexChunk.ChunkSizeMask, baseR & VertexChunk.ChunkSizeMask, edgeIndex);
        }

        private void AddFace(in Vtx p1, in Vtx p2, in Vtx p3, bool simplifiedCliffs, ChunkMeshWriteBuffer dst)
        {
            byte minH = Math.Min(p1.H, Math.Min(p2.H, p3.H));
            byte maxH = Math.Max(p1.H, Math.Max(p2.H, p3.H));

            bool p1Diff = p1.H != p2.H && p1.H != p3.H;
            bool p2Diff = p2.H != p1.H && p2.H != p3.H;
            bool p3Diff = p3.H != p1.H && p3.H != p2.H;
            if (p1Diff && p2Diff && p3Diff)
            {
                if (simplifiedCliffs)
                {
                    AppendTri(dst, p1.Pos, p2.Pos, p3.Pos, p1.Color, p2.Color, p3.Color);
                    return;
                }

                Vtx h3 = p1;
                Vtx m3 = p2;
                Vtx l3 = p3;
                if (h3.H < m3.H) (h3, m3) = (m3, h3);
                if (h3.H < l3.H) (h3, l3) = (l3, h3);
                if (m3.H < l3.H) (m3, l3) = (l3, m3);

                if (TryGetSplit(h3, m3, out var spHM) &&
                    TryGetSplit(m3, l3, out var spML) &&
                    TryGetSplit(h3, l3, out var spHL))
                {
                    AppendTri(dst, h3.Pos, spHM.HighExt, spHL.HighExt, h3.Color, h3.Color, h3.Color);
                    AppendTri(dst, m3.Pos, spML.HighExt, spHM.LowExt, m3.Color, m3.Color, m3.Color);
                    AppendTri(dst, l3.Pos, spHL.LowExt, spML.LowExt, l3.Color, l3.Color, l3.Color);

                    AppendTri(dst, spHM.HighExt, spHL.HighExt, spHM.LowExt, h3.Color, h3.Color, m3.Color);
                    AppendTri(dst, spHL.HighExt, spHM.LowExt, spHL.LowExt, h3.Color, m3.Color, l3.Color);
                    AppendTri(dst, spHM.LowExt, spML.HighExt, spHL.LowExt, m3.Color, m3.Color, l3.Color);
                    AppendTri(dst, spML.HighExt, spML.LowExt, spHL.LowExt, m3.Color, l3.Color, l3.Color);
                }
                else
                {
                    AppendTri(dst, p1.Pos, p2.Pos, p3.Pos, p1.Color, p2.Color, p3.Color);
                }

                return;
            }

            if (minH == maxH)
            {
                AppendTri(dst, p1.Pos, p2.Pos, p3.Pos, p1.Color, p2.Color, p3.Color);
                return;
            }

            bool isRamp = p1.IsRamp || p2.IsRamp || p3.IsRamp;
            if (isRamp)
            {
                AppendTri(dst, p1.Pos, p2.Pos, p3.Pos, p1.Color, p2.Color, p3.Color);
                return;
            }

            if (simplifiedCliffs)
            {
                AppendTri(dst, p1.Pos, p2.Pos, p3.Pos, p1.Color, p2.Color, p3.Color);
                return;
            }

            Vtx a = p1;
            Vtx b = p2;
            Vtx c = p3;
            if (a.H < b.H) (a, b) = (b, a);
            if (a.H < c.H) (a, c) = (c, a);
            if (b.H < c.H) (b, c) = (c, b);

            if (a.H == b.H)
            {
                Vtx h1 = a;
                Vtx h2 = b;
                Vtx l = c;

                if (TryGetSplit(h1, l, out var m1) && TryGetSplit(h2, l, out var m2))
                {
                    AppendTri(dst, h1.Pos, h2.Pos, m1.HighExt, h1.Color, h2.Color, h1.Color);
                    AppendTri(dst, h2.Pos, m2.HighExt, m1.HighExt, h2.Color, h2.Color, h1.Color);

                    AppendTri(dst, m1.HighExt, m2.HighExt, m1.LowExt, h1.Color, h2.Color, l.Color);
                    AppendTri(dst, m2.HighExt, m2.LowExt, m1.LowExt, h2.Color, l.Color, l.Color);

                    AppendTri(dst, l.Pos, m2.LowExt, m1.LowExt, l.Color, l.Color, l.Color);
                }

                return;
            }

            Vtx h = a;
            Vtx l1 = b;
            Vtx l2 = c;

            if (TryGetSplit(h, l1, out var s1) && TryGetSplit(h, l2, out var s2))
            {
                AppendTri(dst, h.Pos, s1.HighExt, s2.HighExt, h.Color, h.Color, h.Color);

                AppendTri(dst, s1.HighExt, s2.HighExt, s1.LowExt, h.Color, h.Color, l1.Color);
                AppendTri(dst, s2.HighExt, s2.LowExt, s1.LowExt, h.Color, l2.Color, l1.Color);

                AppendTri(dst, l1.Pos, l2.Pos, s1.LowExt, l1.Color, l2.Color, l1.Color);
                AppendTri(dst, l2.Pos, s2.LowExt, s1.LowExt, l2.Color, l2.Color, l1.Color);
            }
        }

        private readonly struct SplitPoints
        {
            public readonly Vector3 HighExt;
            public readonly Vector3 LowExt;

            public SplitPoints(Vector3 highExt, Vector3 lowExt)
            {
                HighExt = highExt;
                LowExt = lowExt;
            }
        }

        private bool TryGetSplit(in Vtx high, in Vtx low, out SplitPoints split)
        {
            if (high.H == low.H)
            {
                split = default;
                return false;
            }

            float midX = (high.Pos.X + low.Pos.X) * 0.5f;
            float midZ = (high.Pos.Z + low.Pos.Z) * 0.5f;

            float highExtX = midX;
            float lowExtX = midX;

            bool shouldStraighten = GetCliffStraighten(high.C, high.R, low.C, low.R);

            if (shouldStraighten)
            {
                float dirX = MathF.Sign(low.Pos.X - high.Pos.X);
                float smoothedX = HexCoordinates.HexWidth * (high.C + 0.25f) + _offsetX;
                float bias = HexCoordinates.HexWidth * 0.5f;
                if (dirX != 0f) smoothedX += dirX * bias;
                highExtX = smoothedX;
                lowExtX = smoothedX;
            }

            Vector3 highExt = new Vector3(highExtX, high.Pos.Y, midZ);
            Vector3 lowExt = new Vector3(lowExtX, low.Pos.Y, midZ);
            split = new SplitPoints(highExt, lowExt);
            return true;
        }

        private static void AppendTri(ChunkMeshWriteBuffer dst, Vector3 a, Vector3 b, Vector3 c, Vector4 ca, Vector4 cb, Vector4 cc)
        {
            Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            dst.EnsureAdditionalVertices(3);
            dst.AppendVertex(a, n, ca);
            dst.AppendVertex(b, n, cb);
            dst.AppendVertex(c, n, cc);
        }

        private static void AddWaterTri(in Vtx p1, in Vtx p2, in Vtx p3, ChunkMeshWriteBuffer dst)
        {
            if (p1.W == 0 && p2.W == 0 && p3.W == 0) return;
            if (p1.WaterY <= p1.Pos.Y && p2.WaterY <= p2.Pos.Y && p3.WaterY <= p3.Pos.Y) return;

            Vector3 a = new Vector3(p1.Pos.X, p1.WaterY + 0.003f, p1.Pos.Z);
            Vector3 b = new Vector3(p2.Pos.X, p2.WaterY + 0.003f, p2.Pos.Z);
            Vector3 c = new Vector3(p3.Pos.X, p3.WaterY + 0.003f, p3.Pos.Z);

            Vector4 col = new Vector4(0x4F / 255f, 0xC3 / 255f, 0xF7 / 255f, 0.6f);
            AppendTri(dst, a, b, c, col, col, col);
        }
    }
}
