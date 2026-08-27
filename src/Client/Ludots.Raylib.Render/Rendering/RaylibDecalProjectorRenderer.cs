using System;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;
using Ludots.Platform.Abstractions;

namespace Ludots.Raylib.Render
{
    internal sealed unsafe class RaylibDecalProjectorRenderer : IDisposable
    {
        private readonly IRenderMaterialAssets? _materials;
        private readonly RaylibMaterialLibrary? _materialLibrary;

        private Shader _decalProjectShader;
        private Material _decalMaterial;
        private bool _decalMaterialLoaded;
        private bool _decalProjectShaderReady;
        private int _locDecalProjectColDiffuse;
        private int _locDecalProjectTint;
        private int _locDecalProjectWorldToDecal;
        private int _locDecalProjectAlphaCutoff;
        private int _locDecalProjectMinReceiverNDotUp;
        private int _locDecalProjectReceiverDepthBias;
        private const float DecalMinReceiverNDotUp = 0.05f;
        internal const float DecalReceiverDepthBiasMeters = 0.04f;
        internal const float DecalReceiverDepthBiasPerStampMeter = 2e-4f;
        internal const float DecalBoardScaleStampMeters = 1000f;
        private const float DecalAlphaBlendCutoff = 0.02f;
        private const float DecalCutoutAlphaCutoff = RaylibPrimitiveRenderer.DefaultVegetationAlphaCutoff;

        public RaylibDecalProjectorRenderer(IRenderMaterialAssets? materials, RaylibMaterialLibrary? materialLibrary)
        {
            _materials = materials;
            _materialLibrary = materialLibrary;
        }

        public void Draw(
            in Vector3 position,
            in Quaternion rotation,
            in ProjectedDecalVolume volume,
            in Vector4 color,
            int materialId,
            int stableId,
            IRaylibReceiverMeshProjector projector)
        {
            if (materialId <= 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} Decal stableId={stableId} requires a positive materialId with host albedo.");
            }

            if (_materialLibrary == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} Decal stableId={stableId} materialId={materialId} requires {nameof(RaylibMaterialLibrary)}.");
            }

            RaylibMaterialDrawState.RequireLaneShaderKey(_materials, materialId, nameof(RaylibDecalProjectorRenderer));

            EnsureDecalResources();
            if (!_materialLibrary.TryApplyMaps(ref _decalMaterial, materialId))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} Decal stableId={stableId} materialId={materialId} has no host albedo binding in Presentation/host_assets.json.");
            }

            if (!VisualMath.TryExtractFacingRadFromVisualYRotation(rotation, out float yaw))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} Decal stableId={stableId} projector is yaw-about-Y only; authored rotation is not a planar yaw.");
            }

            Vector3 projectorCenter = projector.FitYawedStampProjectorCenter(
                position,
                yaw,
                volume.StampSizeMeters,
                stableId);
            if (!volume.TryBuildWorldToLocal(
                    projectorCenter,
                    yaw,
                    out Matrix4x4 worldToDecal,
                    out float minX,
                    out float minY,
                    out float minZ,
                    out float maxX,
                    out float maxY,
                    out float maxZ))
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} Decal stableId={stableId} projector volume is not invertible.");
            }

            MaterialBlendMode blendMode = RaylibMaterialDrawState.ResolveBlendMode(
                _materials,
                materialId,
                MaterialBlendMode.AlphaBlend,
                nameof(RaylibDecalProjectorRenderer));
            if (blendMode == MaterialBlendMode.Opaque)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} Decal stableId={stableId} materialId={materialId} must use AlphaBlend or Cutout, not Opaque.");
            }

            if (_decalMaterial.maps == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} Decal stableId={stableId} material maps were not allocated by LoadMaterialDefault.");
            }

            int albedoIndex = (int)Rl.MaterialMapIndex.MATERIAL_MAP_ALBEDO;
            _decalMaterial.maps[albedoIndex].color = Color.WHITE;

            EnsureDecalProjectShader();
            float alphaCutoff = blendMode == MaterialBlendMode.Cutout
                ? DecalCutoutAlphaCutoff
                : DecalAlphaBlendCutoff;
            float minReceiver = ResolveMinReceiverNDotUp(volume.StampSizeMeters);
            float receiverDepthBias = ResolveReceiverDepthBiasMeters(volume.StampSizeMeters);
            Vector4 colDiffuse = Vector4.One;
            Vector4 tint = color;
            RaylibMatrix worldToDecalRay = RaylibMatrix.FromSystemNumerics(worldToDecal);
            Rl.SetShaderValue(
                _decalProjectShader,
                _locDecalProjectColDiffuse,
                &colDiffuse,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            Rl.SetShaderValue(
                _decalProjectShader,
                _locDecalProjectTint,
                &tint,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            Rl.SetShaderValueMatrix(_decalProjectShader, _locDecalProjectWorldToDecal, worldToDecalRay);
            Rl.SetShaderValue(
                _decalProjectShader,
                _locDecalProjectAlphaCutoff,
                &alphaCutoff,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(
                _decalProjectShader,
                _locDecalProjectMinReceiverNDotUp,
                &minReceiver,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(
                _decalProjectShader,
                _locDecalProjectReceiverDepthBias,
                &receiverDepthBias,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

            bool blending = RaylibMaterialDrawState.TryBeginAuthorBlendMode(blendMode, nameof(RaylibDecalProjectorRenderer));
            bool depthMaskDisabled = blending;
            if (depthMaskDisabled)
            {
                Rl.rlDisableDepthMask();
            }

            Shader previousShader = _decalMaterial.shader;
            try
            {
                _decalMaterial.shader = _decalProjectShader;
                int drawn = projector.DrawMeshesOverlappingAabbMeters(
                    minX,
                    minY,
                    minZ,
                    maxX,
                    maxY,
                    maxZ,
                    _decalMaterial);
                if (drawn <= 0)
                {
                    throw new InvalidOperationException(
                        $"{nameof(RaylibDecalProjectorRenderer)} Decal stableId={stableId} projector AABB " +
                        $"({minX:F1},{minY:F1},{minZ:F1})-({maxX:F1},{maxY:F1},{maxZ:F1}) overlaps no receiver meshes.");
                }
            }
            finally
            {
                _decalMaterial.shader = previousShader;

                if (depthMaskDisabled)
                {
                    Rl.rlEnableDepthMask();
                }

                if (blending)
                {
                    Rl.EndBlendMode();
                }

                _materialLibrary.DetachOwnedMaps(ref _decalMaterial);
            }
        }

        public void Dispose()
        {
            if (_decalMaterialLoaded)
            {
                _materialLibrary?.DetachOwnedMaps(ref _decalMaterial);
                _decalMaterial.shader = default;
                Rl.UnloadMaterial(_decalMaterial);
                _decalMaterialLoaded = false;
            }

            if (_decalProjectShaderReady)
            {
                Rl.UnloadShader(_decalProjectShader);
                _decalProjectShaderReady = false;
            }
        }

        internal static float ResolveReceiverDepthBiasMeters(in Vector2 stampSizeMeters)
        {
            float span = MathF.Max(stampSizeMeters.X, stampSizeMeters.Y);
            if (!float.IsFinite(span) || span <= 0f)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} stamp size must be finite and positive, got {stampSizeMeters}.");
            }

            return MathF.Max(DecalReceiverDepthBiasMeters, span * DecalReceiverDepthBiasPerStampMeter);
        }

        internal static float ResolveMinReceiverNDotUp(in Vector2 stampSizeMeters)
        {
            float span = MathF.Max(stampSizeMeters.X, stampSizeMeters.Y);
            if (!float.IsFinite(span) || span <= 0f)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} stamp size must be finite and positive, got {stampSizeMeters}.");
            }

            return span >= DecalBoardScaleStampMeters ? -1f : DecalMinReceiverNDotUp;
        }

        private void EnsureDecalResources()
        {
            if (!_decalMaterialLoaded)
            {
                _decalMaterial = Rl.LoadMaterialDefault();
                _decalMaterialLoaded = true;
            }
        }

        private void EnsureDecalProjectShader()
        {
            if (_decalProjectShaderReady)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            _decalProjectShader = Rl.LoadShader(
                Path.Combine(baseDir, "decal_project.vs"),
                Path.Combine(baseDir, "decal_project.fs"));
            if (_decalProjectShader.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} failed to load decal_project shader.");
            }

            _locDecalProjectColDiffuse = Rl.GetShaderLocation(_decalProjectShader, "colDiffuse");
            _locDecalProjectTint = Rl.GetShaderLocation(_decalProjectShader, "tint");
            _locDecalProjectWorldToDecal = Rl.GetShaderLocation(_decalProjectShader, "matWorldToDecal");
            _locDecalProjectAlphaCutoff = Rl.GetShaderLocation(_decalProjectShader, "alphaCutoff");
            _locDecalProjectMinReceiverNDotUp = Rl.GetShaderLocation(_decalProjectShader, "minReceiverNDotUp");
            _locDecalProjectReceiverDepthBias = Rl.GetShaderLocation(_decalProjectShader, "receiverDepthBias");
            int locMap = Rl.GetShaderLocation(_decalProjectShader, "texture0");
            int locMvp = Rl.GetShaderLocation(_decalProjectShader, "mvp");
            int locMatModel = Rl.GetShaderLocation(_decalProjectShader, "matModel");
            int locVertexPosition = Rl.GetShaderLocationAttrib(_decalProjectShader, "vertexPosition");
            int locVertexNormal = Rl.GetShaderLocationAttrib(_decalProjectShader, "vertexNormal");
            if (_locDecalProjectColDiffuse < 0 ||
                _locDecalProjectTint < 0 ||
                _locDecalProjectWorldToDecal < 0 ||
                _locDecalProjectAlphaCutoff < 0 ||
                _locDecalProjectMinReceiverNDotUp < 0 ||
                _locDecalProjectReceiverDepthBias < 0 ||
                locMap < 0 ||
                locMvp < 0 ||
                locMatModel < 0 ||
                locVertexPosition < 0 ||
                locVertexNormal < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibDecalProjectorRenderer)} decal_project is missing required attribs/uniforms.");
            }

            _decalProjectShaderReady = true;
        }
    }
}
