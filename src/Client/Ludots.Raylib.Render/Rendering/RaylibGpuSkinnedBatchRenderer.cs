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
            return true;
        }

        public void Flush(Shader instancingShader, in RaylibPbrUniformLocations instancingPbrLocs, RaylibSkyIbl? skyIbl)
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
            if (_skinningShaderReady)
            {
                Rl.UnloadShader(_skinningShader);
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

            int clipIndex = batch.Key.ClipIndex;
            int frameIndex = batch.Key.FrameIndex;
            if ((uint)clipIndex >= (uint)batch.AnimCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinned batch clipIndex={clipIndex} outside animCount={batch.AnimCount}.");
            }

            ModelAnimation anim = batch.Animations[clipIndex];
            if (!batch.BonesPrepared)
            {
                Rl.UpdateModelAnimationBones(model, anim, frameIndex);
                batch.BonesPrepared = true;
            }

            EnsureFrameLightingApplied();
            int drawCalls = 0;
            uint colorKey = batch.Key.ColorKey;
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
                    ApplyGpuSkinnedMaterialTint(ref material, colorKey);

                    if (mesh.boneMatrices != null && mesh.boneCount > 0)
                    {
                        Rl.rlEnableShader(_skinningShader.id);
                        Rl.rlSetUniformMatrices(_locBoneMatrices, mesh.boneMatrices, mesh.boneCount);
                    }

                    for (int offset = 0; offset < batch.Count; offset += _maxModelInstancesPerDraw)
                    {
                        int chunkCount = Math.Min(_maxModelInstancesPerDraw, batch.Count - offset);
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

            int clipIndex = batch.Key.ClipIndex;
            int frameIndex = batch.Key.FrameIndex;
            if ((uint)clipIndex >= (uint)batch.AnimCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinned shadow batch clipIndex={clipIndex} outside animCount={batch.AnimCount}.");
            }

            ModelAnimation anim = batch.Animations[clipIndex];
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
            string vsPath = Path.Combine(baseDir, "skinning_instanced.vs");
            string fsPath = Path.Combine(baseDir, "skinning_instanced.fs");
            if (!File.Exists(vsPath) || !File.Exists(fsPath))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibGpuSkinnedBatchRenderer)} GpuSkinnedInstance requires skinning_instanced.vs/.fs beside the binary (missing under '{baseDir}').");
            }

            _skinningShader = RaylibShaderLoader.Load(baseDir, "skinning_instanced.vs", "skinning_instanced.fs", "skinning_instanced");

            _locBoneMatrices = Rl.GetShaderLocation(_skinningShader, "boneMatrices");
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
            _skinningShader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_BONE_MATRICES] = _locBoneMatrices;

            if (_locBoneMatrices < 0) throw new InvalidOperationException("Skinning shader uniform 'boneMatrices' not found.");
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

        private readonly record struct GpuSkinnedInstanceBatchKey(
            int MeshAssetId,
            int MaterialId,
            uint ColorKey,
            int ClipIndex,
            int FrameIndex);

        private sealed class GpuSkinnedInstanceBatch
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
        }
    }
}
