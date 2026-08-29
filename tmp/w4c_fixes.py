path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibPrimitiveRenderer.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

def member_span(sig):
    idx = t.find(sig)
    assert idx >= 0, sig[:70]
    ls = t.rfind("\n", 0, idx) + 1
    ob = t.find("{", idx)
    depth = 0
    end = None
    for i in range(ob, len(t)):
        if t[i] == "{":
            depth += 1
        elif t[i] == "}":
            depth -= 1
            if depth == 0:
                end = i + 1
                break
    while end < len(t) and t[end] == "\n":
        end += 1
    return ls, end

# 1) revert culling out of RebuildTypedLaneBatch
old = """                RaylibMatrix instance = RaylibMatrix.FromSystemNumerics(in matrix);
                // 逐实例视锥筛选（#1331）：消费 lane 已有矩阵的平移分量做保守球测试；
                // 实体级可见性（lane.Visible，源自 Core CullState）不在渲染侧重算。
                if (!IsInstanceWithinFrameFrustum(in instance, InstanceCullRadiusMeters))
                {
                    culled++;
                    continue;
                }

                batch.Add(instance);"""
new = """                batch.Add(RaylibMatrix.FromSystemNumerics(in matrix));"""
assert old in t
t = t.replace(old, new, 1)
t = t.replace("""            bool rescale = MathF.Abs(scaleMul - 1f) > 0.0001f;
            int culled = 0;
            for (int i = 0; i < lane.Count; i++)""",
"""            bool rescale = MathF.Abs(scaleMul - 1f) > 0.0001f;
            for (int i = 0; i < lane.Count; i++)""", 1)
t = t.replace("""            LastInstancedLaneCullSkippedCount += culled;
            LastInstancedMatrixBuildMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;""",
"""            LastInstancedMatrixBuildMs += (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;""", 1)

# 2) replace frustum block (BuildFrameFrustum + Extract + IsInstance) with corrected version + compaction helper
ls, e = member_span("        private void BuildFrameFrustum(in Camera3D camera)")
ls2 = t.rfind("        /// <summary>", 0, ls)
assert ls2 > 0
end_after = t.find("        private ModelInstanceBatch ResolveTypedLaneBatch(")
assert end_after > e
new_block = '''        /// <summary>
        /// 帧级视锥侧平面（#1331）：System.Numerics 行向量约定下 clip = world*(view*proj)，
        /// 平面取列组合 col1±col4 / col2±col4；只做四个侧平面的保守球筛选——近平面（near=0.05 收益可忽略）
        /// 与远平面不参与，深度约定差异（GL -w..w vs D3D 0..w）因此不构成风险。平面构建失败兜底为全可见（保守方向）。
        /// </summary>
        private void BuildFrameFrustum(in Camera3D camera)
        {
            if (_frameFrustumPlanes.Length != 4)
            {
                _frameFrustumPlanes = new Vector4[4];
            }

            float aspect = MathF.Max(0.001f, Rl.GetScreenWidth() / (float)Math.Max(1, Rl.GetScreenHeight()));
            Matrix4x4 view = Matrix4x4.CreateLookAt(camera.position, camera.target, camera.up);
            Matrix4x4 proj = camera.projection == CameraProjection.CAMERA_ORTHOGRAPHIC
                ? Matrix4x4.CreateOrthographic(camera.fovy * aspect, camera.fovy, 0.05f, 100000f)
                : Matrix4x4.CreatePerspectiveFieldOfView(
                    camera.fovy * MathF.PI / 180f,
                    aspect,
                    0.05f,
                    100000f);
            Matrix4x4 p = view * proj;
            _frameFrustumPlanes[0] = NormalizePlane(new Vector4(p.M11 + p.M14, p.M21 + p.M24, p.M31 + p.M34, p.M41 + p.M44));
            _frameFrustumPlanes[1] = NormalizePlane(new Vector4(p.M11 - p.M14, p.M21 - p.M24, p.M31 - p.M34, p.M41 - p.M44));
            _frameFrustumPlanes[2] = NormalizePlane(new Vector4(p.M12 + p.M14, p.M22 + p.M24, p.M32 + p.M34, p.M42 + p.M44));
            _frameFrustumPlanes[3] = NormalizePlane(new Vector4(p.M12 - p.M14, p.M22 - p.M24, p.M32 - p.M34, p.M42 - p.M44));
            _frameFrustumValid = true;
        }

        private static Vector4 NormalizePlane(Vector4 plane)
        {
            float length = MathF.Sqrt(Vector4.Dot(plane, plane));
            return length > 1e-9f ? plane / length : plane;
        }

        private static float InstanceRadiusMeters(in RaylibMatrix matrix, float localRadiusMeters)
        {
            float sx = MathF.Sqrt((matrix.m0 * matrix.m0) + (matrix.m1 * matrix.m1) + (matrix.m2 * matrix.m2));
            float sy = MathF.Sqrt((matrix.m4 * matrix.m4) + (matrix.m5 * matrix.m5) + (matrix.m6 * matrix.m6));
            float sz = MathF.Sqrt((matrix.m8 * matrix.m8) + (matrix.m9 * matrix.m9) + (matrix.m10 * matrix.m10));
            return localRadiusMeters * MathF.Max(sx, MathF.Max(sy, sz));
        }

        private bool IsInstanceWithinFrameFrustum(in RaylibMatrix matrix, float localRadiusMeters)
        {
            if (!_frameFrustumValid)
            {
                return true;
            }

            Vector3 position = new(matrix.m12, matrix.m13, matrix.m14);
            float radius = InstanceRadiusMeters(in matrix, localRadiusMeters);
            Span<Vector4> planes = _frameFrustumPlanes;
            for (int i = 0; i < planes.Length; i++)
            {
                Vector4 plane = planes[i];
                if (plane.X * position.X + plane.Y * position.Y + plane.Z * position.Z + plane.W < -radius)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>主颜色 pass 提交点做逐实例压缩（#1331）：全可见时零拷贝直接用原批次；
        /// 有剔除时压缩进复用 scratch。阴影 pass 不调用（光源视锥与主相机视锥不同，剔除语义不适用）。
        /// revision 矩阵缓存不受剔除结果影响（缓存存原始全量，压缩每帧独立）。</summary>
        private (RaylibMatrix[] Buffer, int Count) CompactVisibleInstances(ModelInstanceBatch batch, float localRadiusMeters)
        {
            Span<RaylibMatrix> source = batch.Transforms.AsSpan(0, batch.Count);
            int firstInvisible = -1;
            for (int i = 0; i < source.Length; i++)
            {
                if (!IsInstanceWithinFrameFrustum(in source[i], localRadiusMeters))
                {
                    firstInvisible = i;
                    break;
                }
            }

            if (firstInvisible < 0)
            {
                return (batch.Transforms, batch.Count);
            }

            if (_laneCullScratch.Length < batch.Count)
            {
                _laneCullScratch = new RaylibMatrix[Math.Max(64, batch.Count * 2)];
            }

            Span<RaylibMatrix> target = _laneCullScratch.AsSpan(0, batch.Count);
            int kept = 0;
            for (int i = 0; i < firstInvisible; i++)
            {
                target[kept++] = source[i];
            }

            int culled = 1;
            for (int i = firstInvisible + 1; i < source.Length; i++)
            {
                if (IsInstanceWithinFrameFrustum(in source[i], localRadiusMeters))
                {
                    target[kept++] = source[i];
                }
                else
                {
                    culled++;
                }
            }

            LastInstancedLaneCullSkippedCount += culled;
            return (_laneCullScratch, kept);
        }

'''
t = t[:ls2] + new_block + t[end_after:]

# 3) fields
old = """        private Vector4[] _frameFrustumPlanes = Array.Empty<Vector4>();
        private bool _frameFrustumValid;
        private const float InstanceCullRadiusMeters = 4f;
        public int LastInstancedLaneCullSkippedCount { get; private set; }"""
new = """        private Vector4[] _frameFrustumPlanes = Array.Empty<Vector4>();
        private bool _frameFrustumValid;
        private RaylibMatrix[] _laneCullScratch = Array.Empty<RaylibMatrix>();
        private const float UnitCubeRadiusMeters = 0.867f;
        public int LastInstancedLaneCullSkippedCount { get; private set; }"""
assert old in t
t = t.replace(old, new, 1)

# 4) primitive lane submit point
old = """            int drawCalls = 0;
            fixed (RaylibMatrix* transforms = batch.Transforms)
            {
                for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                {
                    int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                    Rl.DrawMeshInstanced(mesh, _material, transforms + offset, chunkCount);
                    drawCalls++;
                }
            }

            LastInstancedMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000.0 / Stopwatch.Frequency;
            LastInstancedInstances += batch.Count;
            LastInstancedBatches += drawCalls;
        }"""
new = """            int drawCalls = 0;
            (RaylibMatrix[] visible, int visibleCount) = CompactVisibleInstances(batch, UnitCubeRadiusMeters);
            fixed (RaylibMatrix* transforms = visible)
            {
                for (int offset = 0; offset < visibleCount; offset += _maxModelInstancesPerDraw)
                {
                    int chunkCount = Math.Min(_maxModelInstancesPerDraw, visibleCount - offset);
                    Rl.DrawMeshInstanced(mesh, _material, transforms + offset, chunkCount);
                    drawCalls++;
                }
            }

            LastInstancedMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000.0 / Stopwatch.Frequency;
            LastInstancedInstances += visibleCount;
            LastInstancedBatches += drawCalls;
        }"""
assert old in t, "primitive submit not found"
t = t.replace(old, new, 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)
print("culling v2 applied")
