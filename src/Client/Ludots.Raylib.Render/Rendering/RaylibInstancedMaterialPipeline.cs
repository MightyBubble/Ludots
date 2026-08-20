using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    internal readonly record struct RaylibPbrUniformLocations(
        int Roughness,
        int Metallic,
        int HasRoughnessMap,
        int HasMetallicMap);

    internal sealed unsafe class RaylibInstancedMaterialPipeline
    {
        private readonly RaylibMaterialLibrary? _materialLibrary;
        private readonly HashSet<int> _reportedInvalidInstancedMaterials = new HashSet<int>();

        public RaylibInstancedMaterialPipeline(RaylibMaterialLibrary? materialLibrary)
        {
            _materialLibrary = materialLibrary;
        }

        public void ApplyHostMaterialMaps(ref Material material, int materialId, Shader shader, in RaylibPbrUniformLocations pbrLocs)
        {
            if (_materialLibrary == null || materialId <= 0)
            {
                _materialLibrary?.DetachOwnedMaps(ref material);
                ApplyPbrUniforms(shader, in pbrLocs, materialId: 0, hostBound: false);
                return;
            }

            bool hostBound = _materialLibrary.TryApplyMaps(ref material, materialId);
            if (!hostBound)
            {
                _materialLibrary.DetachOwnedMaps(ref material);
            }

            ApplyPbrUniforms(shader, in pbrLocs, materialId, hostBound);
        }

        public bool TryResolveInstancedModelMaterial(
            Model model,
            int meshIndex,
            int materialId,
            Shader instancingShader,
            in RaylibPbrUniformLocations pbrLocs,
            RaylibSkyIbl? skyIbl,
            RaylibDirectionalShadowMap? frameShadow,
            out Material material)
        {
            if (model.materialCount <= 0 || model.materials == null)
            {
                material = default;
                int reportKey = HashCode.Combine(model.meshCount, meshIndex, model.materialCount);
                if (_reportedInvalidInstancedMaterials.Add(reportKey))
                {
                    RenderDiagnostics.Warn($"Raylib instanced model skipped meshIndex={meshIndex}: imported model has no material. Host asset material import must provide an explicit material.");
                }

                return false;
            }

            int materialIndex = 0;
            if (model.meshMaterial != null && meshIndex >= 0 && meshIndex < model.meshCount)
            {
                materialIndex = model.meshMaterial[meshIndex];
            }

            if (materialIndex < 0 || materialIndex >= model.materialCount)
            {
                int reportKey = HashCode.Combine(model.meshCount, meshIndex, materialIndex);
                if (_reportedInvalidInstancedMaterials.Add(reportKey))
                {
                    RenderDiagnostics.Warn($"Raylib instanced model skipped meshIndex={meshIndex}: meshMaterial index {materialIndex} is outside materialCount={model.materialCount}.");
                }

                material = default;
                return false;
            }

            material = model.materials[materialIndex];
            material.shader = instancingShader;
            if (skyIbl != null)
            {
                // maps 为材质共享指针：写入即持久挂载；重烘后 id 变化由每帧重挂覆盖。
                Rl.SetMaterialTexture(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_CUBEMAP, skyIbl.EnvCubemap);
                Rl.SetMaterialTexture(ref material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_BRDF, skyIbl.BrdfLut);
            }

            ApplyHostMaterialMaps(ref material, materialId, instancingShader, in pbrLocs);
            BindFrameShadow(ref material, frameShadow);
            return true;
        }

        public static void BindFrameShadow(ref Material material, RaylibDirectionalShadowMap? frameShadow)
        {
            RaylibShadowSampling.BindTexture(ref material, frameShadow);
        }

        public static void RestoreOpaqueModelState()
        {
            Rl.rlEnableDepthTest();
            Rl.rlEnableDepthMask();
            Rl.rlEnableBackfaceCulling();
        }

        public static void RequireMeshNormals(in Mesh mesh, string lane)
        {
            if (mesh.normals == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibInstancedMaterialPipeline)} {lane} lit path requires mesh normals (vertexCount={mesh.vertexCount}); silent flat shading is forbidden.");
            }
        }

        public static uint PackRgba(in Vector4 c)
        {
            uint r = RaylibColorUtil.Clamp01ToByte(c.X);
            uint g = RaylibColorUtil.Clamp01ToByte(c.Y);
            uint b = RaylibColorUtil.Clamp01ToByte(c.Z);
            uint a = RaylibColorUtil.Clamp01ToByte(c.W);
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        public void ApplyDefaultPbrUniforms(Shader shader, in RaylibPbrUniformLocations locs)
        {
            ApplyPbrUniforms(shader, in locs, materialId: 0, hostBound: false);
        }

        private void ApplyPbrUniforms(Shader shader, in RaylibPbrUniformLocations locs, int materialId, bool hostBound)
        {
            float roughness = RaylibMaterialLibrary.DefaultRoughness;
            float metallic = RaylibMaterialLibrary.DefaultMetallic;
            int hasRoughnessMap = 0;
            int hasMetallicMap = 0;

            if (hostBound &&
                _materialLibrary != null &&
                _materialLibrary.TryGetPbrParams(
                    materialId,
                    out roughness,
                    out metallic,
                    out bool hasRoughness,
                    out bool hasMetallic,
                    out _))
            {
                hasRoughnessMap = hasRoughness ? 1 : 0;
                hasMetallicMap = hasMetallic ? 1 : 0;
            }

            if (locs.Roughness >= 0)
            {
                Rl.SetShaderValue(shader, locs.Roughness, &roughness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            }

            if (locs.Metallic >= 0)
            {
                Rl.SetShaderValue(shader, locs.Metallic, &metallic, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            }

            if (locs.HasRoughnessMap >= 0)
            {
                Rl.SetShaderValue(shader, locs.HasRoughnessMap, &hasRoughnessMap, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            }

            if (locs.HasMetallicMap >= 0)
            {
                Rl.SetShaderValue(shader, locs.HasMetallicMap, &hasMetallicMap, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_INT);
            }
        }
    }
}
