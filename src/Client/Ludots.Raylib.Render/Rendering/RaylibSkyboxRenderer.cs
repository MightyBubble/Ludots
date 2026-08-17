using System;
using System.IO;
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

            _cubeMesh = Rl.GenMeshCube(1f, 1f, 1f);

            string baseDir = AppContext.BaseDirectory;
            _shader = Rl.LoadShader(Path.Combine(baseDir, "skybox.vs"), Path.Combine(baseDir, "skybox.fs"));
            if (_shader.id == 0)
            {
                throw new InvalidOperationException("Failed to load skybox shader (shader.id == 0).");
            }

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

            Rl.SetShaderValue(_shader, _locSunDirection, &sunDirection, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(_shader, _locSunColor, &sunColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
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
