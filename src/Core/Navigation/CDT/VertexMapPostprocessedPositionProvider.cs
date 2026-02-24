using System;
using System.Numerics;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Navigation.CDT
{
    public sealed class VertexMapPostprocessedPositionProvider : INavMeshPositionProvider
    {
        private readonly VertexMap _map;
        private readonly int _mapWidth;
        private readonly int _mapHeight;

        public VertexMapPostprocessedPositionProvider(VertexMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _mapWidth = map.WidthInChunks * VertexChunk.ChunkSize;
            _mapHeight = map.HeightInChunks * VertexChunk.ChunkSize;
        }

        public Vector3 GetVertexPosition(int col, int row, float heightScale)
        {
            byte h = GetHeight(col, row);
            float x = HexCoordinates.HexWidth * (col + 0.5f * (row & 1));
            float z = HexCoordinates.RowSpacing * row;
            float y = h * heightScale;
            return new Vector3(x, y, z);
        }

        public bool TryGetCliffSplit(int cA, int rA, int cB, int rB, float heightScale, out Vector3 highExt, out Vector3 lowExt)
        {
            highExt = default;
            lowExt = default;

            byte hA = GetHeight(cA, rA);
            byte hB = GetHeight(cB, rB);
            if (hA == hB) return false;

            int highC, highR;
            int lowC, lowR;
            byte highH, lowH;
            if (hA > hB)
            {
                highC = cA; highR = rA; highH = hA;
                lowC = cB; lowR = rB; lowH = hB;
            }
            else
            {
                highC = cB; highR = rB; highH = hB;
                lowC = cA; lowR = rA; lowH = hA;
            }

            if (!GetCliffStraighten(highC, highR, lowC, lowR)) return false;

            var highPos = GetVertexPosition(highC, highR, heightScale);
            var lowPos = GetVertexPosition(lowC, lowR, heightScale);
            float midZ = (highPos.Z + lowPos.Z) * 0.5f;

            float dirX = MathF.Sign(lowPos.X - highPos.X);
            float smoothedX = HexCoordinates.HexWidth * (highC + 0.25f);
            float bias = HexCoordinates.HexWidth * 0.5f;
            if (dirX != 0f) smoothedX += dirX * bias;

            highExt = new Vector3(smoothedX, highPos.Y, midZ);
            lowExt = new Vector3(smoothedX, lowPos.Y, midZ);
            return true;
        }

        private byte GetHeight(int c, int r)
        {
            if ((uint)c >= (uint)_mapWidth || (uint)r >= (uint)_mapHeight) return 0;
            var chunk = _map.GetChunk(c, r, false);
            if (chunk == null) return 0;
            return chunk.GetHeight(c & VertexChunk.ChunkSizeMask, r & VertexChunk.ChunkSizeMask);
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
    }
}

