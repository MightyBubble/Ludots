using System;
using System.IO;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    internal sealed unsafe class RaylibVegetationCutoutRenderer : IDisposable
    {
        private Shader _shader;
        private bool _shaderReady;
        private int _locColDiffuse;
        private int _locAlphaCutoff;

        public void DrawBillboard(
            in Camera3D camera,
            Texture2D texture,
            in Rectangle source,
            Vector3 center,
            Vector2 size,
            Color tint,
            float alphaCutoff)
        {
            EnsureShader();
            Vector4 colDiffuse = Vector4.One;
            float cutoff = alphaCutoff;
            Rl.SetShaderValue(
                _shader,
                _locColDiffuse,
                &colDiffuse,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            Rl.SetShaderValue(
                _shader,
                _locAlphaCutoff,
                &cutoff,
                (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.BeginShaderMode(_shader);
            try
            {
                Rl.DrawBillboardRec(camera, texture, source, center, size, tint);
            }
            finally
            {
                Rl.EndShaderMode();
            }
        }

        public void Dispose()
        {
            if (_shaderReady)
            {
                Rl.UnloadShader(_shader);
                _shader = default;
                _shaderReady = false;
            }
        }

        private void EnsureShader()
        {
            if (_shaderReady)
            {
                return;
            }

            string baseDir = AppContext.BaseDirectory;
            string vsPath = Path.Combine(baseDir, "vegetation_cutout.vs");
            string fsPath = Path.Combine(baseDir, "vegetation_cutout.fs");
            if (!File.Exists(vsPath))
            {
                throw new FileNotFoundException(
                    $"{nameof(RaylibVegetationCutoutRenderer)} vegetation cutout vertex shader missing under BaseDirectory '{baseDir}'. Expected '{vsPath}'.",
                    vsPath);
            }

            if (!File.Exists(fsPath))
            {
                throw new FileNotFoundException(
                    $"{nameof(RaylibVegetationCutoutRenderer)} vegetation cutout fragment shader missing under BaseDirectory '{baseDir}'. Expected '{fsPath}'.",
                    fsPath);
            }

            _shader = Rl.LoadShader(vsPath, fsPath);
            if (_shader.id == 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVegetationCutoutRenderer)} failed to compile vegetation_cutout from '{vsPath}' + '{fsPath}' (shader.id == 0).");
            }

            int locVertexPosition = Rl.GetShaderLocationAttrib(_shader, "vertexPosition");
            int locVertexTexCoord = Rl.GetShaderLocationAttrib(_shader, "vertexTexCoord");
            int locVertexColor = Rl.GetShaderLocationAttrib(_shader, "vertexColor");
            int locMvp = Rl.GetShaderLocation(_shader, "mvp");
            int locMapDiffuse = Rl.GetShaderLocation(_shader, "texture0");
            _locColDiffuse = Rl.GetShaderLocation(_shader, "colDiffuse");
            _locAlphaCutoff = Rl.GetShaderLocation(_shader, "alphaCutoff");

            if (locVertexPosition < 0 || locMvp < 0 || locMapDiffuse < 0 ||
                _locColDiffuse < 0 || _locAlphaCutoff < 0)
            {
                Rl.UnloadShader(_shader);
                _shader = default;
                throw new InvalidOperationException(
                    $"{nameof(RaylibVegetationCutoutRenderer)} vegetation_cutout is missing required attribs/uniforms (vertexPosition/mvp/texture0/colDiffuse/alphaCutoff).");
            }

            if (_shader.locs != null)
            {
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = locVertexTexCoord;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = locVertexColor;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapDiffuse;
                _shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = _locColDiffuse;
            }

            _shaderReady = true;
        }
    }
}
