using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 实例化合批车道的一份着色程序装订：program + 全部 uniform/attribute 位置。
    /// 接线契约（instancing 词汇表：instanceTransform 属性、mvp/tint/colDiffuse、PBR、天空 IBL、阴影）
    /// 对全部 shaderKey 一致——自定义 shader 必须声明并消费同一词汇表，缺任何一个在接线期即抛。
    /// </summary>
    public sealed unsafe class RaylibLaneShader
    {
        private RaylibLaneShader(
            Shader shader,
            int locTint,
            int locColDiffuse,
            RaylibPbrUniformLocations pbrLocs,
            RaylibFrameLightingLocations lightingLocs,
            RaylibShadowSamplingLocations shadowLocs,
            int locSkyZenith,
            int locSkyGround,
            int locEnvSpecular)
        {
            Shader = shader;
            LocTint = locTint;
            LocColDiffuse = locColDiffuse;
            PbrLocs = pbrLocs;
            LightingLocs = lightingLocs;
            ShadowLocs = shadowLocs;
            LocSkyZenith = locSkyZenith;
            LocSkyGround = locSkyGround;
            LocEnvSpecular = locEnvSpecular;
        }

        public readonly Shader Shader;
        internal readonly int LocTint;
        internal readonly int LocColDiffuse;
        internal readonly RaylibPbrUniformLocations PbrLocs;
        internal readonly RaylibFrameLightingLocations LightingLocs;
        internal readonly RaylibShadowSamplingLocations ShadowLocs;
        internal readonly int LocSkyZenith;
        internal readonly int LocSkyGround;
        internal readonly int LocEnvSpecular;

        public static RaylibLaneShader LoadInstancing(string baseDir, string vsName, string fsName, string label)
        {
            Shader shader = RaylibShaderLoader.Load(baseDir, vsName, fsName, label);
            return WireInstancing(shader, label);
        }

        public static RaylibLaneShader WireInstancing(Shader shader, string label)
        {
            int locColDiffuse = Rl.GetShaderLocation(shader, "colDiffuse");
            int locTint = Rl.GetShaderLocation(shader, "tint");
            int locRoughness = Rl.GetShaderLocation(shader, "uRoughness");
            int locMetallic = Rl.GetShaderLocation(shader, "uMetallic");
            int locHasRoughnessMap = Rl.GetShaderLocation(shader, "uHasRoughnessMap");
            int locHasMetallicMap = Rl.GetShaderLocation(shader, "uHasMetallicMap");
            var pbrLocs = new RaylibPbrUniformLocations(locRoughness, locMetallic, locHasRoughnessMap, locHasMetallicMap);
            int locSkyZenith = RaylibShaderBindingGuard.RequireUniform(shader, "uSkyZenith", label);
            int locSkyGround = RaylibShaderBindingGuard.RequireUniform(shader, "uSkyGround", label);
            int locEnvSpecular = RaylibShaderBindingGuard.RequireUniform(shader, "uEnvSpecular", label);
            int locMapAlbedo = Rl.GetShaderLocation(shader, "texture0");
            int locMapMetalness = Rl.GetShaderLocation(shader, "texture1");
            int locMapRoughness = Rl.GetShaderLocation(shader, "texture3");
            int locMvp = Rl.GetShaderLocation(shader, "mvp");
            int locInstance = Rl.GetShaderLocationAttrib(shader, "instanceTransform");
            int locVertexPosition = Rl.GetShaderLocationAttrib(shader, "vertexPosition");
            int locVertexTexCoord = Rl.GetShaderLocationAttrib(shader, "vertexTexCoord");
            int locVertexNormal = Rl.GetShaderLocationAttrib(shader, "vertexNormal");
            var lightingLocs = RaylibFrameLightingLocations.ResolveOrThrow(shader, label);
            var shadowLocs = RaylibShadowSamplingLocations.ResolveOrThrow(
                shader,
                label,
                RaylibShadowSampling.ShaderTextureSlot);

            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD02] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = locVertexNormal;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TANGENT] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_VIEW] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_PROJECTION] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_NORMAL] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VECTOR_VIEW] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = locColDiffuse;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_SPECULAR] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_AMBIENT] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_METALNESS] = locMapMetalness;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_NORMAL] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ROUGHNESS] = locMapRoughness;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_OCCLUSION] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_EMISSION] = shadowLocs.ShadowMap;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_HEIGHT] = -1;
            // split-sum IBL 采样器：cubemap 走 MATERIAL_MAP_CUBEMAP 槽位（native 5.5
            // DrawMeshInstanced 对该槽以 GL_TEXTURE_CUBE_MAP 绑定并回填 uniform=槽位号），LUT 走 BRDF 槽。
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_CUBEMAP] =
                RaylibShaderBindingGuard.RequireUniform(shader, "uPrefilteredEnv", label);
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_IRRADIANCE] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_PREFILTER] = -1;
            shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_BRDF] =
                RaylibShaderBindingGuard.RequireUniform(shader, "uBrdfLut", label);

            if (locVertexPosition < 0) throw new InvalidOperationException($"Shader '{label}' attrib 'vertexPosition' not found.");
            if (locVertexTexCoord < 0) throw new InvalidOperationException($"Shader '{label}' attrib 'vertexTexCoord' not found.");
            if (locVertexNormal < 0) throw new InvalidOperationException($"Shader '{label}' attrib 'vertexNormal' not found.");
            if (locMvp < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'mvp' not found.");
            if (locInstance < 0) throw new InvalidOperationException($"Shader '{label}' attrib 'instanceTransform' not found.");
            if (locColDiffuse < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'colDiffuse' not found.");
            if (locTint < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'tint' not found.");
            if (locMapAlbedo < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'texture0' not found.");
            if (locRoughness < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'uRoughness' not found.");
            if (locMetallic < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'uMetallic' not found.");
            if (locHasRoughnessMap < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'uHasRoughnessMap' not found.");
            if (locHasMetallicMap < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'uHasMetallicMap' not found.");
            if (locMapMetalness < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'texture1' not found.");
            if (locMapRoughness < 0) throw new InvalidOperationException($"Shader '{label}' uniform 'texture3' not found.");

            return new RaylibLaneShader(
                shader,
                locTint,
                locColDiffuse,
                pbrLocs,
                lightingLocs,
                shadowLocs,
                locSkyZenith,
                locSkyGround,
                locEnvSpecular);
        }

        internal void ApplyFrameLighting(RaylibFrameLighting lighting, Vector3 viewPos)
        {
            lighting.Apply(Shader, in LightingLocs);
            lighting.ApplyViewPosition(Shader, in LightingLocs, viewPos);
        }

        internal void ApplySkyUniforms(RaylibFrameLighting lighting, float envSpecular)
        {
            lighting.ApplySkyIrradiance(Shader, LocSkyZenith, LocSkyGround);
            Rl.SetShaderValue(Shader, LocEnvSpecular, &envSpecular, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        internal void ApplyFrameShadow(RaylibDirectionalShadowMap? shadow, float shadowTexelWorld)
        {
            ShadowLocs.ApplyUniforms(Shader, shadow, shadowTexelWorld);
        }

        internal void SetTint(Vector4 tint)
        {
            Rl.SetShaderValue(Shader, LocTint, &tint, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        internal void SetColDiffuse(Vector4 diffuse)
        {
            Rl.SetShaderValue(Shader, LocColDiffuse, &diffuse, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }
    }
}
