using System;

namespace Ludots.Core.Presentation.Assets
{
    /// <summary>
    /// Mutable runtime mesh payload owned by gameplay/editor code and consumed by adapters.
    /// Arrays are preallocated by the owner; changing <see cref="Generation"/> signals adapters to re-upload.
    /// </summary>
    public sealed class RuntimeMeshAssetData
    {
        public RuntimeMeshAssetData(int maxVertexCount)
        {
            if (maxVertexCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxVertexCount));
            }

            Vertices = new float[maxVertexCount * 3];
            Normals = new float[maxVertexCount * 3];
            Colors = new byte[maxVertexCount * 4];
        }

        public float[] Vertices { get; }

        public float[] Normals { get; }

        public byte[] Colors { get; }

        public int Capacity => Vertices.Length / 3;

        public int VertexCount { get; private set; }

        public int Generation { get; private set; }

        public void Update(int vertexCount)
        {
            if ((uint)vertexCount > (uint)Capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            }

            VertexCount = vertexCount;
            Generation++;
        }
    }
}
