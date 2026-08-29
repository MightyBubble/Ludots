using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    internal sealed unsafe class RaylibGpuSkinnedBatchRenderer : IDisposable
    {
        private readonly RaylibGpuSkinnedModelCache _modelCache;
        private readonly RaylibInstancedMaterialPipeline _materials;
        private readonly int _maxModelInstancesPerDraw;

        private Shader _skinningShader;
        private bool _skinningShaderReady;
        private int _locSkinningColDiffuse;
        private int _locSkinningTint;
        private int _locSkinningRoughness;
        private int _locSkinningMetallic;
        private int _locSkinningHasRoughnessMap;
        private int _locSkinningHasMetallicMap;
        private int _locBoneMatrices;
        private RaylibPbrUniformLocations _skinningPbrLocs;
        private RaylibFrameLightingLocations _skinningLightingLocs;
        private RaylibShadowSamplingLocations _skinningShadowLocs;

        private readonly Dictionary<GpuSkinnedInstanceBatchKey, GpuSkinnedInstanceBatch> _gpuSkinnedInstanceBatches = new();
        private readonly List<GpuSkinnedInstanceBatch> _activeGpuSkinnedInstanceBatches = new(64);
        private bool _gpuSkinnedBatchesPreparedForShadow;

        private RaylibFrameLighting? _frameLighting;
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
        private int _locInstanceBase = -1;

        public RaylibGpuSkinnedBatchRenderer(
            RaylibGpuSkinnedModelCache modelCache,
            RaylibInstancedMaterialPipeline materials,
            int maxModelInstancesPerDraw)
        {
            _modelCache = modelCache ?? throw new ArgumentNullException(nameof(modelCache));
            _materials = materials ?? throw new ArgumentNullException(nameof(materials));
            _maxModelInstancesPerDraw = maxModelInstancesPerDraw;
        }

        public int LastInstances { get; private set; }
        public int LastBatches { get; private set; }
        public double LastMatrixBuildMs { get; private set; }
        public double LastMeshDrawMs { get; private set; }

        public bool BatchesPreparedForShadow => _gpuSkinnedBatchesPreparedForShadow;

        public bool HasActiveBatches => _activeGpuSkinnedInstanceBatches.Count > 0;

        public void ResetStats()
        {
            LastInstances = 0;
            LastBatches = 0;
            LastMatrixBuildMs = 0d;
            LastMeshDrawMs = 0d;
        }

        public void ApplyFrameLighting(RaylibFrameLighting lighting, Vector3 viewPos, RaylibDirectionalShadowMap? shadow, float shadowTexelWorld)
        {
            _frameLighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
            _frameViewPos = viewPos;
            _hasFrameViewPos = true;
            _frameShadow = shadow;
            _frameShadowTexelWorld = shadowTexelWorld;
            if (_skinningShaderReady)
            {
                lighting.Apply(_skinningShader, in _skinningLightingLocs);
                lighting.ApplyViewPosition(_skinningShader, in _skinningLightingLocs, viewPos);
                _skinningShadowLocs.ApplyUniforms(_skinningShader, _frameShadow, _frameShadowTexelWorld);
            }
        }

        public void Prepare()
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
        }

        public bool TrySubmit(in SkinnedVisualBatchItem item, IRenderMeshAssets meshes, float scaleMul)
        {
            if (item.RenderPath != VisualRenderPath.GpuSkinnedInstance ||
                !meshes.TryGetDescriptor(item.MeshAssetId, out MeshAssetDescriptor descriptor) ||
                descriptor.Type != MeshAssetType.Model)
            {
                return false;
            }

            RaylibGpuSkinnedModelCache.Entry entry = _modelCache.GetOrLoad(item.MeshAssetId, in descriptor);
            AnimatorPackedState animator = item.Animator;
            RaylibSkinnedPlayback.ResolveFromAnimator(
                in animator,
                entry.Animations,
                entry.AnimCount,
                stateToClipMap: null,
                out int clipIndex,
                out int frameIndex);

            long start = Stopwatch.GetTimestamp();

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
            return true;
        }

        public void Flush(Shader instancingShader, in RaylibPbrUniformLocations instancingPbrLocs, RaylibSkyIbl? skyIbl)
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
        }

        public void FlushShadow(RaylibDirectionalShadowMap shadow)
        {
            if (_activeGpuSkinnedInstanceBatches.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _activeGpuSkinnedInstanceBatches.Count; i++)
            {
                GpuSkinnedInstanceBatch batch = _activeGpuSkinnedInstanceBatches[i];
                if (batch.Count == 0)
                {
                    continue;
                }

                DrawBatchShadow(batch, shadow);
            }

            _gpuSkinnedBatchesPreparedForShadow = true;
        }

        public void Dispose()
        {
            _posePalette?.Dispose();
            _posePalette = null;
            if (_skinningShaderReady)
            {
                RaylibNativeResources.UnloadShader(_skinningShader);
                _skinningShader = default;
                _skinningShaderReady = false;
            }
        }

        private int DrawBatch(GpuSkinnedInstanceBatch batch, Shader instancingShader, in RaylibPbrUniformLocations instancingPbrLocs, RaylibSkyIbl? skyIbl)
        {
            Model model = batch.Model;
            if (model.meshCount <= 0 || batch.Count <= 0)
            {
                return 0;
            }

            if (batch.Animations == null || batch.AnimCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinned batch meshAssetId={batch.Key.MeshAssetId} has no animations; silent static draw is forbidden.");
            }

            EnsureFrameLightingApplied();
            int drawCalls = 0;
            int materialId = batch.Key.MaterialId;
            if (_materials.TryGetResolvedForLane(materialId, out ResolvedMaterialAsset skinnedResolved))
            {
                RaylibMaterialDrawState.RequireLaneShaderKey(in skinnedResolved, materialId, "GpuSkinnedInstance");
            }
            RaylibInstancedMaterialPipeline.RestoreOpaqueModelState();
            fixed (RaylibMatrix* transforms = batch.Transforms)
            {
                for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                {
                    Mesh mesh = model.meshes[meshIndex];
                    if (mesh.vertexCount <= 0)
                    {
                        continue;
                    }

                    RaylibInstancedMaterialPipeline.RequireMeshNormals(in mesh, "GpuSkinnedInstance");
                    if (!_materials.TryResolveInstancedModelMaterial(model, meshIndex, materialId, instancingShader, in instancingPbrLocs, skyIbl, _frameShadow, out Material material))
                    {
                        continue;
                    }

                    material.shader = _skinningShader;
                    _materials.ApplyHostMaterialMaps(ref material, materialId, _skinningShader, in _skinningPbrLocs);
                    RaylibInstancedMaterialPipeline.BindFrameShadow(ref material, _frameShadow);
                    // tint 经实例表按实例传入（#1395）

                    // 姿势纹理蒙皮：骨骼矩阵已在调色板纹理中，无需 uniform 上传
                    BindPoseTextures(ref material);
                    for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
                        SetInstanceBaseUniform(batch.GlobalInstanceBase + offset);
                        Rl.DrawMeshInstanced(mesh, material, transforms + offset, chunkCount);
                        drawCalls++;
                    }
                }
            }

            return drawCalls;
        }

        private void DrawBatchShadow(GpuSkinnedInstanceBatch batch, RaylibDirectionalShadowMap shadow)
        {
            Model model = batch.Model;
            if (model.meshCount <= 0 || batch.Count <= 0)
            {
                return;
            }

            if (batch.Animations == null || batch.AnimCount <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinned shadow batch meshAssetId={batch.Key.MeshAssetId} has no animations; silent static shadow is forbidden.");
            }

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
            }
        }

        private void ApplyGpuSkinnedMaterialTint(ref Material material, uint colorKey)
        {
            if (_locSkinningTint >= 0)
            {
                float r = (colorKey & 0xFF) / 255f;
                float g = ((colorKey >> 8) & 0xFF) / 255f;
                float b = ((colorKey >> 16) & 0xFF) / 255f;
                float a = ((colorKey >> 24) & 0xFF) / 255f;
                var tint = new Vector4(r, g, b, a);
                Rl.SetShaderValue(_skinningShader, _locSkinningTint, &tint, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            }

            if (_locSkinningColDiffuse >= 0 && material.maps != null)
            {
                int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
                Color color = material.maps[albedoIndex].color;
                Vector4 diffuse = new(color.r / 255f, color.g / 255f, color.b / 255f, color.a / 255f);
                Rl.SetShaderValue(_skinningShader, _locSkinningColDiffuse, &diffuse, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            }
        }

        private void EnsureFrameLightingApplied()
        {
            if (_frameLighting == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} lit GpuSkinnedInstance requires ApplyFrameLighting before draw.");
            }

            if (!_hasFrameViewPos)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} lit GpuSkinnedInstance requires camera view position before draw.");
            }

            EnsureShaderInitialized();
            _frameLighting.Apply(_skinningShader, in _skinningLightingLocs);
            _frameLighting.ApplyViewPosition(_skinningShader, in _skinningLightingLocs, _frameViewPos);
            _skinningShadowLocs.ApplyUniforms(_skinningShader, _frameShadow, _frameShadowTexelWorld);
        }

        private void EnsureShaderInitialized()
        {
            if (_skinningShaderReady)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            string vsPath = Path.Combine(baseDir, "skinning_instanced_pose_texture.vs");
            string fsPath = Path.Combine(baseDir, "skinning_instanced.fs");
            if (!File.Exists(vsPath) || !File.Exists(fsPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinnedInstance requires skinning_instanced_pose_texture.vs/.fs beside the binary (missing under '{baseDir}').");
            }

            _skinningShader = RaylibShaderLoader.Load(baseDir, "skinning_instanced_pose_texture.vs", "skinning_instanced.fs", "skinning_instanced");

            _locBoneMatrices = Rl.GetShaderLocation(_skinningShader, "boneMatrices");
            _locBonePaletteSampler = Rl.GetShaderLocation(_skinningShader, "uBonePalette");
            _locInstanceTableSampler = Rl.GetShaderLocation(_skinningShader, "uInstanceTable");
            _locInstanceBase = Rl.GetShaderLocation(_skinningShader, "uInstanceBase");
            _locSkinningTint = Rl.GetShaderLocation(_skinningShader, "tint");
            _locSkinningColDiffuse = Rl.GetShaderLocation(_skinningShader, "colDiffuse");
            _locSkinningRoughness = Rl.GetShaderLocation(_skinningShader, "uRoughness");
            _locSkinningMetallic = Rl.GetShaderLocation(_skinningShader, "uMetallic");
            _locSkinningHasRoughnessMap = Rl.GetShaderLocation(_skinningShader, "uHasRoughnessMap");
            _locSkinningHasMetallicMap = Rl.GetShaderLocation(_skinningShader, "uHasMetallicMap");
            int locMapAlbedo = Rl.GetShaderLocation(_skinningShader, "texture0");
            int locMapMetalness = Rl.GetShaderLocation(_skinningShader, "texture1");
            int locMapRoughness = Rl.GetShaderLocation(_skinningShader, "texture3");
            int locMvp = Rl.GetShaderLocation(_skinningShader, "mvp");
            int locInstance = Rl.GetShaderLocationAttrib(_skinningShader, "instanceTransform");
            int locVertexPosition = Rl.GetShaderLocationAttrib(_skinningShader, "vertexPosition");
            int locVertexTexCoord = Rl.GetShaderLocationAttrib(_skinningShader, "vertexTexCoord");
            int locVertexNormal = Rl.GetShaderLocationAttrib(_skinningShader, "vertexNormal");
            int locVertexColor = Rl.GetShaderLocationAttrib(_skinningShader, "vertexColor");
            int locBoneIds = Rl.GetShaderLocationAttrib(_skinningShader, "vertexBoneIds");
            int locBoneWeights = Rl.GetShaderLocationAttrib(_skinningShader, "vertexBoneWeights");
            _skinningLightingLocs = RaylibFrameLightingLocations.ResolveOrThrow(_skinningShader, "skinning_instanced");
            _skinningShadowLocs = RaylibShadowSamplingLocations.ResolveOrThrow(
                _skinningShader,
                "skinning_instanced",
                RaylibShadowSampling.ShaderTextureSlot);
            _skinningPbrLocs = new RaylibPbrUniformLocations(
                _locSkinningRoughness,
                _locSkinningMetallic,
                _locSkinningHasRoughnessMap,
                _locSkinningHasMetallicMap);

            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locVertexColor;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEIDS] = locBoneIds;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEWEIGHTS] = locBoneWeights;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = _locSkinningColDiffuse;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_METALNESS] = locMapMetalness;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_NORMAL] = -1;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ROUGHNESS] = locMapRoughness;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_EMISSION] = _skinningShadowLocs.ShadowMap;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_OCCLUSION] = _locBonePaletteSampler;
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_HEIGHT] = _locInstanceTableSampler;

            // 姿势纹理蒙皮（#1395）：boneMatrices uniform 不再存在，调色板走纹理
            if (_locBonePaletteSampler < 0) throw new InvalidOperationException("Skinning shader sampler 'uBonePalette' not found.");
            if (_locInstanceTableSampler < 0) throw new InvalidOperationException("Skinning shader sampler 'uInstanceTable' not found.");
            if (_locInstanceBase < 0) throw new InvalidOperationException("Skinning shader uniform 'uInstanceBase' not found.");
            if (locMvp < 0) throw new InvalidOperationException("Skinning shader uniform 'mvp' not found.");
            if (locInstance < 0) throw new InvalidOperationException("Skinning shader attrib 'instanceTransform' not found.");
            if (locVertexPosition < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexPosition' not found.");
            if (locVertexNormal < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexNormal' not found.");
            if (locBoneIds < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexBoneIds' not found.");
            if (locBoneWeights < 0) throw new InvalidOperationException("Skinning shader attrib 'vertexBoneWeights' not found.");
            if (_locSkinningColDiffuse < 0) throw new InvalidOperationException("Skinning shader uniform 'colDiffuse' not found.");
            if (_locSkinningTint < 0) throw new InvalidOperationException("Skinning shader uniform 'tint' not found.");
            if (locMapAlbedo < 0) throw new InvalidOperationException("Skinning shader uniform 'texture0' not found.");
            if (_locSkinningRoughness < 0) throw new InvalidOperationException("Skinning shader uniform 'uRoughness' not found.");
            if (_locSkinningMetallic < 0) throw new InvalidOperationException("Skinning shader uniform 'uMetallic' not found.");
            if (_locSkinningHasRoughnessMap < 0) throw new InvalidOperationException("Skinning shader uniform 'uHasRoughnessMap' not found.");
            if (_locSkinningHasMetallicMap < 0) throw new InvalidOperationException("Skinning shader uniform 'uHasMetallicMap' not found.");
            if (locMapMetalness < 0) throw new InvalidOperationException("Skinning shader uniform 'texture1' not found.");
            if (locMapRoughness < 0) throw new InvalidOperationException("Skinning shader uniform 'texture3' not found.");

            _materials.ApplyDefaultPbrUniforms(_skinningShader, in _skinningPbrLocs);

            _skinningShaderReady = true;
            _skinningShadowLocs.ApplyUniforms(_skinningShader, _frameShadow, _frameShadowTexelWorld);
            if (_frameLighting != null)
            {
                _frameLighting.Apply(_skinningShader, in _skinningLightingLocs);
                if (_hasFrameViewPos)
                {
                    _frameLighting.ApplyViewPosition(_skinningShader, in _skinningLightingLocs, _frameViewPos);
                }
            }
        }

        private unsafe void BindPoseTextures(ref Material material)
        {
            if (_posePalette == null)
            {
                return;
            }

            // 经 raylib 材质槽绑定（raylib 在 DrawMesh 时自动绑纹理并设 sampler uniform）：
            // OCCLUSION(4)=调色板，HEIGHT(6)=实例表——避开 EMISSION(5)=阴影 / CUBEMAP(7)+BRDF(10)=IBL
            Rl.SetMaterialTexture(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_OCCLUSION, _posePalette.BonePalette);
            Rl.SetMaterialTexture(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_HEIGHT, _posePalette.InstanceTable);
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

        private readonly record struct GpuSkinnedInstanceBatchKey(
            int MeshAssetId,
            int MaterialId);

        private sealed class GpuSkinnedInstanceBatch
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
        }
    }
}
