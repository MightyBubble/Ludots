using System;
using System.Numerics;

namespace Ludots.Platform.Abstractions
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
}
