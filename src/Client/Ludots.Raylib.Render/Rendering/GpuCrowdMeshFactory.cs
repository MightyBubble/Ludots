using System.Collections.Generic;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render;

/// <summary>GPU 人群车道的网格预处理：非索引 LOD 网格焊接索引化（位置+骨骼合并，法线按面烘平）。</summary>
public static unsafe class GpuCrowdMeshFactory
{
        /// <summary>非索引网格焊接索引化：位置+骨骼属性一致的顶点合并（法线/UV 差异不参与——
        /// 蒙皮人群着色只用 tint，焊接后按三角面烘平法线，远距离不可辨）。
        /// 索引网格原样返回。焊容量按顶点数建字典，加载期一次性成本。</summary>
        /// <summary>非索引网格焊接索引化（位置+骨骼合并，法线按面烘平）——sim 场景复用。</summary>
        public static Mesh WeldToIndexed(Mesh mesh)
        {
            if (mesh.indices != null)
            {
                return mesh;
            }

            int vertexCount = mesh.vertexCount;
            if (vertexCount <= 0 || mesh.vertices == null)
            {
                throw new InvalidDataException("Weld requires a mesh with vertices.");
            }

            float* srcVerts = mesh.vertices;
            byte* srcBoneIds = mesh.boneIds;
            float* srcBoneWeights = mesh.boneWeights;

            Dictionary<string, int> weldMap = new(vertexCount);
            List<float> verts = new(vertexCount * 3);
            List<byte> boneIds = srcBoneIds != null ? new(vertexCount * 4) : new(0);
            List<float> boneWeights = srcBoneWeights != null ? new(vertexCount * 4) : new(0);
            List<ushort> indices = new(vertexCount);

            for (int v = 0; v < vertexCount; v++)
            {
                // 位置 1/1024、骨骼 id/权重（1/1024）进 key；共享位置但骨骼不同会裂开，必须参与
                int qx = (int)MathF.Round(srcVerts[v * 3 + 0] * 1024f);
                int qy = (int)MathF.Round(srcVerts[v * 3 + 1] * 1024f);
                int qz = (int)MathF.Round(srcVerts[v * 3 + 2] * 1024f);
                string key = $"{qx},{qy},{qz}";
                if (srcBoneIds != null)
                {
                    key += $",{srcBoneIds[v * 4 + 0]},{srcBoneIds[v * 4 + 1]},{srcBoneIds[v * 4 + 2]},{srcBoneIds[v * 4 + 3]}";
                }

                if (srcBoneWeights != null)
                {
                    key += $",{(int)MathF.Round(srcBoneWeights[v * 4 + 0] * 1024f)},{(int)MathF.Round(srcBoneWeights[v * 4 + 1] * 1024f)},{(int)MathF.Round(srcBoneWeights[v * 4 + 2] * 1024f)},{(int)MathF.Round(srcBoneWeights[v * 4 + 3] * 1024f)}";
                }

                if (!weldMap.TryGetValue(key, out int weldedIndex))
                {
                    weldedIndex = verts.Count / 3;
                    weldMap[key] = weldedIndex;
                    verts.Add(srcVerts[v * 3 + 0]);
                    verts.Add(srcVerts[v * 3 + 1]);
                    verts.Add(srcVerts[v * 3 + 2]);
                    if (srcBoneIds != null)
                    {
                        boneIds.Add(srcBoneIds[v * 4 + 0]);
                        boneIds.Add(srcBoneIds[v * 4 + 1]);
                        boneIds.Add(srcBoneIds[v * 4 + 2]);
                        boneIds.Add(srcBoneIds[v * 4 + 3]);
                    }

                    if (srcBoneWeights != null)
                    {
                        boneWeights.Add(srcBoneWeights[v * 4 + 0]);
                        boneWeights.Add(srcBoneWeights[v * 4 + 1]);
                        boneWeights.Add(srcBoneWeights[v * 4 + 2]);
                        boneWeights.Add(srcBoneWeights[v * 4 + 3]);
                    }
                }

                indices.Add((ushort)weldedIndex);
            }

            // 按三角面烘平法线（焊接丢掉了角点法线；远 LOD 平面着色足够）
            int weldedCount = verts.Count / 3;
            Span<float> normalsSpan = new float[weldedCount * 3];
            for (int tri = 0; tri < indices.Count; tri += 3)
            {
                int a = indices[tri] * 3, b = indices[tri + 1] * 3, c = indices[tri + 2] * 3;
                float ax = verts[a], ay = verts[a + 1], az = verts[a + 2];
                float e1x = verts[b] - ax, e1y = verts[b + 1] - ay, e1z = verts[b + 2] - az;
                float e2x = verts[c] - ax, e2y = verts[c + 1] - ay, e2z = verts[c + 2] - az;
                float nx = e1y * e2z - e1z * e2y;
                float ny = e1z * e2x - e1x * e2z;
                float nz = e1x * e2y - e1y * e2x;
                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-12f)
                {
                    nx /= len; ny /= len; nz /= len;
                }

                for (int corner = 0; corner < 3; corner++)
                {
                    int dst = indices[tri + corner] * 3;
                    normalsSpan[dst] = nx;
                    normalsSpan[dst + 1] = ny;
                    normalsSpan[dst + 2] = nz;
                }
            }

            List<float> normals = new(normalsSpan.Length);
            normals.AddRange(normalsSpan);

            Mesh welded = default;
            welded.vertexCount = weldedCount;
            welded.triangleCount = indices.Count / 3;
            welded.vertices = AllocFloats(verts);
            welded.normals = AllocFloats(normals);
            if (boneIds.Count > 0) welded.boneIds = AllocBytes(boneIds);
            if (boneWeights.Count > 0) welded.boneWeights = AllocFloats(boneWeights);
            welded.indices = AllocUshorts(indices);
            RaylibNativeResources.UploadMesh(ref welded, false);
            Console.WriteLine($"[gpu-crowd-weld] {vertexCount} verts -> {welded.vertexCount} verts ({welded.triangleCount} tris, bones={(boneIds.Count > 0 ? "yes" : "NO")})");
            return welded;
        }

        private static float* AllocFloats(List<float> values)
        {
            IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(values.Count * sizeof(float));
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values);
            fixed (float* src = span)
            {
                System.Buffer.MemoryCopy(src, (void*)block, values.Count * sizeof(float), values.Count * sizeof(float));
            }

            return (float*)block;
        }

        private static byte* AllocBytes(List<byte> values)
        {
            IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(values.Count);
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values);
            fixed (byte* src = span)
            {
                System.Buffer.MemoryCopy(src, (void*)block, values.Count, values.Count);
            }

            return (byte*)block;
        }

        private static ushort* AllocUshorts(List<ushort> values)
        {
            IntPtr block = System.Runtime.InteropServices.Marshal.AllocHGlobal(values.Count * sizeof(ushort));
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(values);
            fixed (ushort* src = span)
            {
                System.Buffer.MemoryCopy(src, (void*)block, values.Count * sizeof(ushort), values.Count * sizeof(ushort));
            }

            return (ushort*)block;
        }
}
