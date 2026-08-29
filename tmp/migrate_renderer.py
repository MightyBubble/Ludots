import re

path = 'src/Client/Ludots.Raylib.Render/Rendering/RaylibGpuSkinnedBatchRenderer.cs'
with open(path, encoding='utf-8', newline='') as f:
    t = f.read()

# ============================================================
# 1. Add fields: pose texture palette + pose row cache + instance table tracking
# ============================================================
old_fields = """        private RaylibFrameLighting? _frameLighting;
        private Vector3 _frameViewPos;
        private bool _hasFrameViewPos;
        private RaylibDirectionalShadowMap? _frameShadow;
        private float _frameShadowTexelWorld = 0.04f;"""
new_fields = """        private RaylibFrameLighting? _frameLighting;
        private Vector3 _frameViewPos;
        private bool _hasFrameViewPos;
        private RaylibDirectionalShadowMap? _frameShadow;
        private float _frameShadowTexelWorld = 0.04f;
        private RaylibPoseTexturePalette? _posePalette;
        private readonly Dictionary<int, int> _poseRowByHash = new();
        private readonly List<(int PoseRow, int MeshAssetId, int ClipIndex, int FrameIndex)> _dirtyPoseRows = new();
        private int _globalInstanceCount;
        private int _locBonePaletteSampler = -1;
        private int _locInstanceTableSampler = -1;
        private int _locInstanceBase = -1;"""
assert old_fields in t, "fields not found"
t = t.replace(old_fields, new_fields, 1)

# ============================================================
# 2. Simplify batch key to (meshAssetId, materialId) — pose/color move to per-instance
# ============================================================
old_key = """        private readonly record struct GpuSkinnedInstanceBatchKey(
            int MeshAssetId,
            int MaterialId,
            uint ColorKey,
            int ClipIndex,
            int FrameIndex);"""
new_key = """        private readonly record struct GpuSkinnedInstanceBatchKey(
            int MeshAssetId,
            int MaterialId);"""
assert old_key in t, "batch key not found"
t = t.replace(old_key, new_key, 1)

# ============================================================
# 3. Batch struct: add per-instance pose/tint data
# ============================================================
old_batch = """        private sealed class GpuSkinnedInstanceBatch
        {
            public readonly GpuSkinnedInstanceBatchKey Key;
            public RaylibMatrix[] Transforms;
            public int Count;
            public Model Model;
            public ModelAnimation* Animations;
            public int AnimCount;
            public bool BonesPrepared;

            public GpuSkinnedInstanceBatch(GpuSkinnedInstanceBatchKey key, int initialCapacity = 256)
            {
                Key = key;
                Transforms = new RaylibMatrix[Math.Max(4, initialCapacity)];
            }

            public void Add(in RaylibMatrix matrix)
            {
                if (Count >= Transforms.Length)
                {
                    Array.Resize(ref Transforms, Transforms.Length * 2);
                }

                Transforms[Count++] = matrix;
            }
        }"""
new_batch = """        private sealed class GpuSkinnedInstanceBatch
        {
            public readonly GpuSkinnedInstanceBatchKey Key;
            public RaylibMatrix[] Transforms;
            public int[] PoseRows;
            public Vector4[] Tints;
            public int Count;
            public Model Model;
            public ModelAnimation* Animations;
            public int AnimCount;
            public bool BonesPrepared;
            public int GlobalInstanceBase;

            public GpuSkinnedInstanceBatch(GpuSkinnedInstanceBatchKey key, int initialCapacity = 256)
            {
                Key = key;
                Transforms = new RaylibMatrix[Math.Max(4, initialCapacity)];
                PoseRows = new int[Math.Max(4, initialCapacity)];
                Tints = new Vector4[Math.Max(4, initialCapacity)];
            }

            public void Add(in RaylibMatrix matrix, int poseRow, in Vector4 tint)
            {
                if (Count >= Transforms.Length)
                {
                    Array.Resize(ref Transforms, Transforms.Length * 2);
                    Array.Resize(ref PoseRows, PoseRows.Length * 2);
                    Array.Resize(ref Tints, Tints.Length * 2);
                }

                Transforms[Count] = matrix;
                PoseRows[Count] = poseRow;
                Tints[Count] = tint;
                Count++;
            }
        }"""
assert old_batch in t, "batch struct not found"
t = t.replace(old_batch, new_batch, 1)

# ============================================================
# 4. TrySubmit: simplified key + per-instance pose/tint tracking
# ============================================================
old_submit = """            long start = Stopwatch.GetTimestamp();
            uint colorKey = RaylibInstancedMaterialPipeline.PackRgba(item.Color);
            var key = new GpuSkinnedInstanceBatchKey(
                item.MeshAssetId,
                item.MaterialId,
                colorKey,
                clipIndex,
                frameIndex);
            if (!_gpuSkinnedInstanceBatches.TryGetValue(key, out GpuSkinnedInstanceBatch? batch))
            {
                batch = new GpuSkinnedInstanceBatch(key);
                _gpuSkinnedInstanceBatches.Add(key, batch);
            }

            if (batch.Count == 0)
            {
                batch.Model = entry.Model;
                batch.Animations = entry.Animations;
                batch.AnimCount = entry.AnimCount;
                _activeGpuSkinnedInstanceBatches.Add(batch);
            }

            if (_gpuSkinnedBatchesPreparedForShadow && batch.Count > 0)
            {
                return true;
            }

            batch.Add(RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(item.Scale * scaleMul) *
                Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(item.Rotation)) *
                Matrix4x4.CreateTranslation(item.Position)));
            LastMatrixBuildMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            return true;"""
new_submit = """            long start = Stopwatch.GetTimestamp();

            // 姿势纹理蒙皮（#1395）：桶键只含 (mesh, material)——姿势与颜色按实例记录
            var key = new GpuSkinnedInstanceBatchKey(item.MeshAssetId, item.MaterialId);
            if (!_gpuSkinnedInstanceBatches.TryGetValue(key, out GpuSkinnedInstanceBatch? batch))
            {
                batch = new GpuSkinnedInstanceBatch(key);
                _gpuSkinnedInstanceBatches.Add(key, batch);
            }

            if (batch.Count == 0)
            {
                batch.Model = entry.Model;
                batch.Animations = entry.Animations;
                batch.AnimCount = entry.AnimCount;
                _activeGpuSkinnedInstanceBatches.Add(batch);
            }

            if (_gpuSkinnedBatchesPreparedForShadow && batch.Count > 0)
            {
                return true;
            }

            // 姿势行分配：(meshAssetId, clipIndex, frameIndex) 唯一映射到调色板一行
            int poseHash = HashCode.Combine(item.MeshAssetId, clipIndex, frameIndex);
            if (!_poseRowByHash.TryGetValue(poseHash, out int poseRow))
            {
                _posePalette ??= new RaylibPoseTexturePalette();
                poseRow = _poseRowByHash.Count;
                _poseRowByHash[poseHash] = poseRow;
                _dirtyPoseRows.Add((poseRow, item.MeshAssetId, clipIndex, frameIndex));
            }

            batch.Add(
                RaylibMatrix.FromSystemNumerics(
                    Matrix4x4.CreateScale(item.Scale * scaleMul) *
                    Matrix4x4.CreateFromQuaternion(VisualMath.NormalizeOrIdentity(item.Rotation)) *
                    Matrix4x4.CreateTranslation(item.Position)),
                poseRow,
                item.Color);
            LastMatrixBuildMs += (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            return true;"""
assert old_submit in t, "submit not found"
t = t.replace(old_submit, new_submit, 1)

# ============================================================
# 5. Prepare(): reset pose allocator + dirty rows
# ============================================================
old_prepare = """        public void Prepare()
        {
            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                _activeGpuSkinnedInstanceBatches[i].Count = 0;
                _activeGpuSkinnedInstanceBatches[i].BonesPrepared = false;
            }

            _activeGpuSkinnedInstanceBatches.Clear();
        }"""
new_prepare = """        public void Prepare()
        {
            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                _activeGpuSkinnedInstanceBatches[i].Count = 0;
                _activeGpuSkinnedInstanceBatches[i].BonesPrepared = false;
            }

            _activeGpuSkinnedInstanceBatches.Clear();
            _poseRowByHash.Clear();
            _dirtyPoseRows.Clear();
            _globalInstanceCount = 0;
        }"""
assert old_prepare in t, "prepare not found"
t = t.replace(old_prepare, new_prepare, 1)

# ============================================================
# 6. Flush(): build pose palette + instance table before drawing
# ============================================================
old_flush = """        public void Flush(Shader instancingShader, in RaylibPbrUniformLocations instancingPbrLocs, RaylibSkyIbl? skyIbl)
        {
            if (_activeGpuSkinnedInstanceBatches.Count > 0)
            {
                EnsureShaderInitialized();
                long drawStart = Stopwatch.GetTimestamp();
                for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
                {
                    GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                    if (batch.Count == 0)
                    {
                        continue;
                    }

                    LastInstances += batch.Count;
                    LastBatches += DrawBatch(batch, instancingShader, in instancingPbrLocs, skyIbl);
                }

                LastMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000d / Stopwatch.Frequency;
            }

            _gpuSkinnedBatchesPreparedForShadow = false;
        }"""
new_flush = """        public void Flush(Shader instancingShader, in RaylibPbrUniformLocations instancingPbrLocs, RaylibSkyIbl? skyIbl)
        {
            if (_activeGpuSkinnedInstanceBatches.Count > 0)
            {
                EnsureShaderInitialized();
                BuildAndUploadPoseTextures();
                long drawStart = Stopwatch.GetTimestamp();
                for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
                {
                    GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                    if (batch.Count == 0)
                    {
                        continue;
                    }

                    batch.GlobalInstanceBase = _globalInstanceCount;
                    _globalInstanceCount += batch.Count;
                    LastInstances += batch.Count;
                    LastBatches += DrawBatch(batch, instancingShader, in instancingPbrLocs, skyIbl);
                }

                LastMeshDrawMs += (Stopwatch.GetTimestamp() - drawStart) * 1000d / Stopwatch.Frequency;
            }

            _gpuSkinnedBatchesPreparedForShadow = false;
        }

        /// <summary>把 dirty 姿势行的骨骼矩阵从 native 内存按列序重排写入调色板 staging 并上传；
        /// 同时把全部活跃实例的 (poseRow, tint) 写入实例表 staging 并上传（#1395）。</summary>
        private unsafe void BuildAndUploadPoseTextures()
        {
            if (_posePalette == null)
            {
                return;
            }

            // 1. 姿势调色板：对每个 dirty 行，调 UpdateModelAnimationBones 后立刻按列序复制
            for (int i = 0; i < _dirtyPoseRows.Count; i++)
            {
                (int poseRow, int meshAssetId, int clipIndex, int frameIndex) = _dirtyPoseRows[i];
                var batch = _activeGpuSkinnedInstanceBatches.FirstOrDefault(b => b.Key.MeshAssetId == meshAssetId);
                if (batch == null || batch.Animations == null)
                {
                    continue;
                }

                Model model = batch.Model;
                ModelAnimation anim = batch.Animations[clipIndex];
                Rl.UpdateModelAnimationBones(model, anim, frameIndex);
                batch.BonesPrepared = true;

                // 把每个 mesh 的 boneMatrices native 指针按列序复制到调色板行
                for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                {
                    Mesh mesh = model.meshes[meshIndex];
                    if (mesh.boneMatrices == null || mesh.boneCount <= 0)
                    {
                        continue;
                    }

                    for (int b = 0; b < mesh.boneCount && b < RaylibPoseTexturePalette.MaxBoneCount; b++)
                    {
                        _posePalette.WriteBoneMatrix(poseRow, b, mesh.boneMatrices[b]);
                    }
                }

                _posePalette.CommitPoseRow(poseRow);
                _posePalette.FlushPaletteRow(poseRow);
            }

            // 2. 实例表：每实例 (poseRow + RGBA tint) 写入 staging 并上传
            int totalInstances = 0;
            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                totalInstances += _activeGpuSkinnedInstanceBatches[i].Count;
            }

            if (totalInstances > 0)
            {
                _posePalette.EnsureInstanceCapacity(totalInstances + 1); // +1 for alpha texel
                int global = 0;
                for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
                {
                    GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                    for (int j = 0; j < batch.Count; j++)
                    {
                        Vector4 tint = batch.Tints[j];
                        _posePalette.WriteInstance(global, batch.PoseRows[j], tint.X, tint.Y, tint.Z, tint.W);
                        global++;
                    }
                }

                int rows = (global + RaylibPoseTexturePalette.InstanceTableWidth - 1) / RaylibPoseTexturePalette.InstanceTableWidth;
                if (rows > 0)
                {
                    _posePalette.FlushInstanceRows(0, rows);
                }
            }
        }"""
assert old_flush in t, "flush not found"
t = t.replace(old_flush, new_flush, 1)

# ============================================================
# 7. DrawBatch: replace bone uniform with texture binding, use pose-texture shader
# ============================================================
old_drawbatch_core = """            ModelAnimation anim = batch.Animations[clipIndex];
            if (!batch.BonesPrepared)
            {
                Rl.UpdateModelAnimationBones(model, anim, frameIndex);
                batch.BonesPrepared = true;
            }

            EnsureFrameLightingApplied();
            int drawCalls = 0;
            uint colorKey = batch.Key.ColorKey;
            int materialId = batch.Key.MaterialId;"""
new_drawbatch_core = """            EnsureFrameLightingApplied();
            int drawCalls = 0;
            int materialId = batch.Key.MaterialId;"""
assert old_drawbatch_core in t, "drawbatch core not found"
t = t.replace(old_drawbatch_core, new_drawbatch_core, 1)

# remove the rlSetUniformMatrices call in DrawBatch (replaced by texture binding)
old_bone_uniform = """                    if (mesh.boneMatrices != null && mesh.boneCount > 0)
                    {
                        Rl.rlEnableShader(_skinningShader.id);
                        Rl.rlSetUniformMatrices(_locBoneMatrices, mesh.boneMatrices, mesh.boneCount);
                    }

                    for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                        Rl.DrawMeshInstanced(mesh, material, transforms + offset, chunkCount);
                        drawCalls++;
                    }"""
new_bone_uniform = """                    // 姿势纹理蒙皮：骨骼矩阵已在调色板纹理中，无需 uniform 上传
                    BindPoseTextures();
                    for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                        SetInstanceBaseUniform(batch.GlobalInstanceBase + offset);
                        Rl.DrawMeshInstanced(mesh, material, transforms + offset, chunkCount);
                        drawCalls++;
                    }"""
assert old_bone_uniform in t, "bone uniform not found"
t = t.replace(old_bone_uniform, new_bone_uniform, 1)

# ============================================================
# 8. DrawBatchShadow: use pose-texture shadow shader (no bone uniform)
# ============================================================
old_shadow_bones = """            ModelAnimation anim = batch.Animations[clipIndex];
            Rl.UpdateModelAnimationBones(model, anim, frameIndex);
            batch.BonesPrepared = true;
            fixed (RaylibMatrix* transforms = batch.Transforms)
            {
                for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                {
                    Mesh mesh = model.meshes[meshIndex];
                    if (mesh.vertexCount <= 0)
                    {
                        continue;
                    }

                    for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                        shadow.DrawSkinnedMeshInstancedShadow(mesh, transforms + offset, chunkCount);
                    }
                }
            }"""
new_shadow_bones = """            fixed (RaylibMatrix* transforms = batch.Transforms)
            {
                for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                {
                    Mesh mesh = model.meshes[meshIndex];
                    if (mesh.vertexCount <= 0)
                    {
                        continue;
                    }

                    for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                        shadow.DrawSkinnedMeshInstancedShadow(mesh, transforms + offset, chunkCount);
                    }
                }
            }"""
assert old_shadow_bones in t, "shadow bones not found"
t = t.replace(old_shadow_bones, new_shadow_bones, 1)

# ============================================================
# 9. Shader initialization: load pose-texture variants, add sampler/base uniforms
# ============================================================
old_shader_load = """            string baseDir = AppContext.BaseDirectory;
            string vsPath = Path.Combine(baseDir, "skinning_instanced.vs");
            string fsPath = Path.Combine(baseDir, "skinning_instanced.fs");
            if (!File.Exists(vsPath) || !File.Exists(fsPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinnedInstance requires skinning_instanced.vs/.fs beside the binary (missing under '{baseDir}').");
            }

            _skinningShader = RaylibShaderLoader.Load(baseDir, "skinning_instanced.vs", "skinning_instanced.fs", "skinning_instanced");"""
new_shader_load = """            string baseDir = AppContext.BaseDirectory;
            string vsPath = Path.Combine(baseDir, "skinning_instanced_pose_texture.vs");
            string fsPath = Path.Combine(baseDir, "skinning_instanced.fs");
            if (!File.Exists(vsPath) || !File.Exists(fsPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinnedInstance requires skinning_instanced_pose_texture.vs/.fs beside the binary (missing under '{baseDir}').");
            }

            _skinningShader = RaylibShaderLoader.Load(baseDir, "skinning_instanced_pose_texture.vs", "skinning_instanced.fs", "skinning_instanced");"""
assert old_shader_load in t, "shader load not found"
t = t.replace(old_shader_load, new_shader_load, 1)

# Add sampler/uniform locations after existing location queries
old_loc_query = """            _locBoneMatrices = Rl.GetShaderLocation(_skinningShader, "boneMatrices");"""
new_loc_query = """            _locBoneMatrices = Rl.GetShaderLocation(_skinningShader, "boneMatrices");
            _locBonePaletteSampler = Rl.GetShaderLocation(_skinningShader, "uBonePalette");
            _locInstanceTableSampler = Rl.GetShaderLocation(_skinningShader, "uInstanceTable");
            _locInstanceBase = Rl.GetShaderLocation(_skinningShader, "uInstanceBase");"""
assert old_loc_query in t, "loc query not found"
t = t.replace(old_loc_query, new_loc_query, 1)

# Remove the boneMatrices throw (pose-texture shader doesn't have it)
old_bone_throw = """            if (_locBoneMatrices < 0) throw new InvalidOperationException("Skinning shader uniform 'boneMatrices' not found.");"""
new_bone_throw = """            // 姿势纹理蒙皮（#1395）：boneMatrices uniform 不再存在，调色板走纹理
            if (_locBonePaletteSampler < 0) throw new InvalidOperationException("Skinning shader sampler 'uBonePalette' not found.");
            if (_locInstanceTableSampler < 0) throw new InvalidOperationException("Skinning shader sampler 'uInstanceTable' not found.");
            if (_locInstanceBase < 0) throw new InvalidOperationException("Skinning shader uniform 'uInstanceBase' not found.");"""
assert old_bone_throw in t, "bone throw not found"
t = t.replace(old_bone_throw, new_bone_throw, 1)

# ============================================================
# 10. Add helper methods at end of class (before batch struct)
# ============================================================
anchor = "        private readonly record struct GpuSkinnedInstanceBatchKey("
helpers = """        private unsafe void BindPoseTextures()
        {
            if (_posePalette == null)
            {
                return;
            }

            // 绑定调色板与实例表到着色器采样器（纹理单元由 GL 分配器决定）
            Rl.rlEnableShader(_skinningShader.id);
            Rl.SetShaderValueTexture(_skinningShader, _locBonePaletteSampler, _posePalette.BonePalette);
            Rl.SetShaderValueTexture(_skinningShader, _locInstanceTableSampler, _posePalette.InstanceTable);
        }

        private unsafe void SetInstanceBaseUniform(int baseValue)
        {
            if (_locInstanceBase < 0)
            {
                return;
            }

            float value = baseValue;
            Rl.SetShaderValue(_skinningShader, _locInstanceBase, &value, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

""" + anchor
t = t.replace(anchor, helpers, 1)

# ============================================================
# 11. Dispose: also dispose pose palette
# ============================================================
old_dispose = """        public void Dispose()
        {
            if (_skinningShaderReady)
            {
                RaylibNativeResources.UnloadShader(_skinningShader);
                _skinningShader = default;
                _skinningShaderReady = false;
            }"""
new_dispose = """        public void Dispose()
        {
            _posePalette?.Dispose();
            _posePalette = null;
            if (_skinningShaderReady)
            {
                RaylibNativeResources.UnloadShader(_skinningShader);
                _skinningShader = default;
                _skinningShaderReady = false;
            }"""
assert old_dispose in t, "dispose not found"
t = t.replace(old_dispose, new_dispose, 1)

with open(path, 'w', encoding='utf-8', newline='') as f:
    f.write(t)

print("renderer migration complete")
