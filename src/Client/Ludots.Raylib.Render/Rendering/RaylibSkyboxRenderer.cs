using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    public sealed unsafe class RaylibSkyboxRenderer : IDisposable
    {
        private Shader _shader;
        private Material _material;
        private Mesh _cubeMesh;
        private bool _initialized;
        private int _locSunDirection;
        private int _locSunColor;
        private int _locZenithColor;
        private int _locHorizonColor;
        private int _locGroundHazeColor;
        private int _locTime;
        private int _locSunDiskSharpness;
        private int _locSunDiskIntensity;
        private int _locSunGlowSharpness;
        private int _locSunGlowIntensity;

        public void Draw(in Camera3D camera, double timeSeconds, RaylibRenderEnvironmentConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            RaylibRenderEnvironmentConfig normalized = config.NormalizeAndValidate();
            if (!normalized.Skybox.Enabled)
            {
                return;
            }

            EnsureInitialized();
            UpdateUniforms(timeSeconds, normalized);

            float size = normalized.Skybox.SizeMeters;
            RaylibMatrix transform = RaylibMatrix.FromScaleTranslation(
                camera.position.X,
                camera.position.Y,
                camera.position.Z,
                size,
                size,
                size);

            Rl.rlDisableDepthMask();
            Rl.rlDisableBackfaceCulling();
            Rl.DrawMesh(_cubeMesh, _material, transform);
            Rl.rlEnableBackfaceCulling();
            Rl.rlEnableDepthMask();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _cubeMesh = Rl.GenMeshSphere(0.5f, 64, 32);

            string baseDir = AppContext.BaseDirectory;
            _shader = RaylibShaderLoader.Load(baseDir, "skybox.vs", "skybox.fs", "skybox");

            _material = Rl.LoadMaterialDefault();
            _material.shader = _shader;

            int locMvp = RaylibShaderBindingGuard.RequireUniform(_shader, "mvp", "skybox");
            int locMatModel = RaylibShaderBindingGuard.RequireUniform(_shader, "matModel", "skybox");
            int locVertexPosition = RaylibShaderBindingGuard.RequireAttribute(_shader, "vertexPosition", "skybox");
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD02] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_NORMAL] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TANGENT] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = -1;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
            _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locMatModel;

            _locSunDirection = RaylibShaderBindingGuard.RequireUniform(_shader, "uSunDirection", "skybox");
            _locSunColor = RaylibShaderBindingGuard.RequireUniform(_shader, "uSunColor", "skybox");
            _locSunDiskSharpness = RaylibShaderBindingGuard.RequireUniform(_shader, "uSunDiskSharpness", "skybox");
            _locSunDiskIntensity = RaylibShaderBindingGuard.RequireUniform(_shader, "uSunDiskIntensity", "skybox");
            _locSunGlowSharpness = RaylibShaderBindingGuard.RequireUniform(_shader, "uSunGlowSharpness", "skybox");
            _locSunGlowIntensity = RaylibShaderBindingGuard.RequireUniform(_shader, "uSunGlowIntensity", "skybox");
            _locZenithColor = RaylibShaderBindingGuard.RequireUniform(_shader, "uZenithColor", "skybox");
            _locHorizonColor = RaylibShaderBindingGuard.RequireUniform(_shader, "uHorizonColor", "skybox");
            _locGroundHazeColor = RaylibShaderBindingGuard.RequireUniform(_shader, "uGroundHazeColor", "skybox");
            _locTime = RaylibShaderBindingGuard.RequireUniform(_shader, "uTime", "skybox");
            _initialized = true;
        }

        private void UpdateUniforms(double timeSeconds, RaylibRenderEnvironmentConfig config)
        {
            RaylibLightingConfig lighting = config.Lighting;
            RaylibSkyboxConfig skybox = config.Skybox;
            Vector3 sunDirection = lighting.SunDirection;
            Vector3 sunColor = lighting.SunColor;
            Vector3 zenithColor = skybox.ZenithColor;
            Vector3 horizonColor = skybox.HorizonColor;
            Vector3 groundHazeColor = skybox.GroundHazeColor;
            float time = (float)(timeSeconds % 100000.0);
            float sunDiskSharpness = skybox.SunDiskSharpness;
            float sunDiskIntensity = skybox.SunDiskIntensity;
            float sunGlowSharpness = skybox.SunGlowSharpness;
            float sunGlowIntensity = skybox.SunGlowIntensity;

            Rl.SetShaderValue(_shader, _locSunDirection, &sunDirection, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locSunColor, &sunColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locSunDiskSharpness, &sunDiskSharpness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locSunDiskIntensity, &sunDiskIntensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locSunGlowSharpness, &sunGlowSharpness, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locSunGlowIntensity, &sunGlowIntensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(_shader, _locZenithColor, &zenithColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locHorizonColor, &horizonColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locGroundHazeColor, &groundHazeColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locTime, &time, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
        }

        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

            if (_cubeMesh.vertexCount > 0)
            {
                Rl.UnloadMesh(_cubeMesh);
            }

            _material.shader = default;
            Rl.UnloadMaterial(_material);
            Rl.UnloadShader(_shader);
            _initialized = false;
        }
    }
}
