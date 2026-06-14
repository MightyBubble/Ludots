using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public enum ProceduralMeshUsageHint : byte
    {
        Static = 0,
        Dynamic = 1,
        Streamed = 2,
    }

    public readonly struct ProceduralSubmeshDescriptor
    {
        public ProceduralSubmeshDescriptor(int indexStart, int indexCount, int materialAssetId)
        {
            if (indexStart < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(indexStart));
            }

            if (indexCount <= 0 || (indexCount % 3) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(indexCount), "Procedural submesh indexCount must be a positive triangle list.");
            }

            if (materialAssetId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(materialAssetId));
            }

            IndexStart = indexStart;
            IndexCount = indexCount;
            MaterialAssetId = materialAssetId;
        }

        public int IndexStart { get; }

        public int IndexCount { get; }

        public int MaterialAssetId { get; }
    }

    public readonly struct ProceduralMeshBounds
    {
        public ProceduralMeshBounds(in Vector3 center, in Vector3 extents)
        {
            if (extents.X < 0f || extents.Y < 0f || extents.Z < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(extents), "Procedural mesh extents must be non-negative.");
            }

            Center = center;
            Extents = extents;
        }

        public Vector3 Center { get; }

        public Vector3 Extents { get; }

        public Vector3 Min => Center - Extents;

        public Vector3 Max => Center + Extents;
    }

    /// <summary>
    /// Core-owned procedural mesh payload. Owners mutate backing arrays and publish a validated snapshot through Commit.
    /// </summary>
    public sealed class ProceduralMeshAssetData
    {
        public ProceduralMeshAssetData(
            int maxVertexCount,
            int maxIndexCount,
            int maxSubmeshCount = 1,
            bool includeUv1 = false,
            bool includeColors32 = false)
        {
            if (maxVertexCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxVertexCount));
            }

            if (maxIndexCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxIndexCount));
            }

            if (maxSubmeshCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSubmeshCount));
            }

            Positions = new float[maxVertexCount * 3];
            Normals = new float[maxVertexCount * 3];
            Tangents = new float[maxVertexCount * 4];
            Uv0 = new float[maxVertexCount * 2];
            Uv1 = includeUv1 ? new float[maxVertexCount * 2] : null;
            Colors32 = includeColors32 ? new byte[maxVertexCount * 4] : null;
            Indices = new int[maxIndexCount];
            Submeshes = new ProceduralSubmeshDescriptor[maxSubmeshCount];
        }

        public float[] Positions { get; }

        public float[] Normals { get; }

        public float[] Tangents { get; }

        public float[] Uv0 { get; }

        public float[]? Uv1 { get; }

        public byte[]? Colors32 { get; }

        public int[] Indices { get; }

        public ProceduralSubmeshDescriptor[] Submeshes { get; }

        public int VertexCapacity => Positions.Length / 3;

        public int IndexCapacity => Indices.Length;

        public int SubmeshCapacity => Submeshes.Length;

        public int VertexCount { get; private set; }

        public int IndexCount { get; private set; }

        public int SubmeshCount { get; private set; }

        public ProceduralMeshUsageHint UsageHint { get; private set; }

        public ProceduralMeshBounds LocalBounds { get; private set; }

        public int Generation { get; private set; }

        public void Commit(
            int vertexCount,
            int indexCount,
            ReadOnlySpan<ProceduralSubmeshDescriptor> submeshes,
            in ProceduralMeshBounds localBounds,
            ProceduralMeshUsageHint usageHint)
        {
            ValidateCommit(vertexCount, indexCount, submeshes, in localBounds);

            VertexCount = vertexCount;
            IndexCount = indexCount;
            SubmeshCount = submeshes.Length;
            UsageHint = usageHint;
            LocalBounds = localBounds;
            for (int i = 0; i < submeshes.Length; i++)
            {
                Submeshes[i] = submeshes[i];
            }

            Generation++;
        }

        private void ValidateCommit(
            int vertexCount,
            int indexCount,
            ReadOnlySpan<ProceduralSubmeshDescriptor> submeshes,
            in ProceduralMeshBounds localBounds)
        {
            if ((uint)vertexCount > (uint)VertexCapacity || vertexCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            }

            if ((uint)indexCount > (uint)IndexCapacity || indexCount <= 0 || (indexCount % 3) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(indexCount), "Procedural mesh indexCount must be a positive triangle list.");
            }

            if (submeshes.Length == 0 || submeshes.Length > SubmeshCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(submeshes));
            }

            if (localBounds.Extents == Vector3.Zero)
            {
                throw new InvalidOperationException("Procedural mesh must provide non-zero local bounds.");
            }

            int requiredPositionFloats = vertexCount * 3;
            int requiredTangentFloats = vertexCount * 4;
            int requiredUvFloats = vertexCount * 2;
            ValidateRequiredData(Positions, requiredPositionFloats, nameof(Positions));
            ValidateRequiredData(Normals, requiredPositionFloats, nameof(Normals));
            ValidateRequiredData(Tangents, requiredTangentFloats, nameof(Tangents));
            ValidateRequiredData(Uv0, requiredUvFloats, nameof(Uv0));
            ValidateVector3Data(Positions, vertexCount, nameof(Positions));
            ValidateVector3Data(Normals, vertexCount, nameof(Normals));
            ValidateTangentData(Tangents, vertexCount);
            ValidateUvData(Uv0, vertexCount);

            for (int i = 0; i < indexCount; i++)
            {
                if ((uint)Indices[i] >= (uint)vertexCount)
                {
                    throw new InvalidOperationException($"Procedural mesh index[{i}] references vertex {Indices[i]} outside committed vertex range.");
                }
            }

            int expectedIndexStart = 0;
            for (int i = 0; i < submeshes.Length; i++)
            {
                ProceduralSubmeshDescriptor submesh = submeshes[i];
                if (submesh.IndexStart != expectedIndexStart)
                {
                    throw new InvalidOperationException($"Procedural mesh submesh[{i}] must start at contiguous index {expectedIndexStart}.");
                }

                if (submesh.IndexStart + submesh.IndexCount > indexCount)
                {
                    throw new InvalidOperationException($"Procedural mesh submesh[{i}] exceeds committed index range.");
                }

                expectedIndexStart += submesh.IndexCount;
            }

            if (expectedIndexStart != indexCount)
            {
                throw new InvalidOperationException("Procedural mesh submeshes must fully cover committed indices without gaps.");
            }
        }

        private static void ValidateRequiredData(Array array, int requiredCount, string fieldName)
        {
            if (array == null || array.Length < requiredCount)
            {
                throw new InvalidOperationException($"Procedural mesh commit requires populated {fieldName} data.");
            }
        }

        private static void ValidateVector3Data(float[] data, int vertexCount, string fieldName)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * 3;
                float x = data[offset + 0];
                float y = data[offset + 1];
                float z = data[offset + 2];
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                {
                    throw new InvalidOperationException($"Procedural mesh commit requires finite {fieldName} data at vertex {i}.");
                }

                if (fieldName != nameof(Positions) && ((x * x) + (y * y) + (z * z)) <= 1e-10f)
                {
                    throw new InvalidOperationException($"Procedural mesh commit requires non-zero {fieldName} data at vertex {i}.");
                }
            }
        }

        private static void ValidateTangentData(float[] data, int vertexCount)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * 4;
                float x = data[offset + 0];
                float y = data[offset + 1];
                float z = data[offset + 2];
                float w = data[offset + 3];
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(w))
                {
                    throw new InvalidOperationException($"Procedural mesh commit requires finite Tangents data at vertex {i}.");
                }

                if (((x * x) + (y * y) + (z * z)) <= 1e-10f)
                {
                    throw new InvalidOperationException($"Procedural mesh commit requires non-zero Tangents data at vertex {i}.");
                }
            }
        }

        private static void ValidateUvData(float[] data, int vertexCount)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                int offset = i * 2;
                if (!float.IsFinite(data[offset + 0]) || !float.IsFinite(data[offset + 1]))
                {
                    throw new InvalidOperationException($"Procedural mesh commit requires finite Uv0 data at vertex {i}.");
                }
            }
        }
    }
}
