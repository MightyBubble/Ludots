using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    public sealed class RaylibEffectShaderRegistry : IDisposable
    {
        public const string DefaultUnlitTintKey = "vfx_unlit_tint";

        private readonly string _baseDirectory;
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
        private bool _disposed;

        public RaylibEffectShaderRegistry(string? baseDirectory = null)
        {
            _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? AppContext.BaseDirectory
                : baseDirectory;
        }

        public RaylibEffectShader GetOrLoad(string key)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Effect shader key must be a non-empty string.", nameof(key));
            }

            if (_entries.TryGetValue(key, out Entry cached))
            {
                return cached.Effect;
            }

            string vsPath = Path.Combine(_baseDirectory, key + ".vs");
            string fsPath = Path.Combine(_baseDirectory, key + ".fs");
            if (!File.Exists(vsPath))
            {
                throw new FileNotFoundException(
                    $"Effect shader '{key}' vertex file missing under BaseDirectory '{_baseDirectory}'. Expected '{vsPath}'.",
                    vsPath);
            }

            if (!File.Exists(fsPath))
            {
                throw new FileNotFoundException(
                    $"Effect shader '{key}' fragment file missing under BaseDirectory '{_baseDirectory}'. Expected '{fsPath}'.",
                    fsPath);
            }

            Shader shader = Rl.LoadShader(vsPath, fsPath);
            if (shader.id == 0)
            {
                throw new InvalidOperationException(
                    $"Failed to compile effect shader '{key}' from '{vsPath}' + '{fsPath}' (shader.id == 0).");
            }

            int locTint = Rl.GetShaderLocation(shader, "tint");
            int locTime = Rl.GetShaderLocation(shader, "uTime");
            int locColDiffuse = Rl.GetShaderLocation(shader, "colDiffuse");
            int locMvp = Rl.GetShaderLocation(shader, "mvp");
            int locModel = Rl.GetShaderLocation(shader, "matModel");
            int locVertexPosition = Rl.GetShaderLocationAttrib(shader, "vertexPosition");

            if (locVertexPosition < 0)
            {
                Rl.UnloadShader(shader);
                throw new InvalidOperationException($"Effect shader '{key}' is missing required attrib 'vertexPosition'.");
            }

            if (locMvp < 0)
            {
                Rl.UnloadShader(shader);
                throw new InvalidOperationException($"Effect shader '{key}' is missing required uniform 'mvp'.");
            }

            if (locModel < 0)
            {
                Rl.UnloadShader(shader);
                throw new InvalidOperationException($"Effect shader '{key}' is missing required uniform 'matModel'.");
            }

            if (locTint < 0)
            {
                Rl.UnloadShader(shader);
                throw new InvalidOperationException($"Effect shader '{key}' is missing required uniform 'tint'.");
            }

            if (locTime < 0)
            {
                Rl.UnloadShader(shader);
                throw new InvalidOperationException($"Effect shader '{key}' is missing required uniform 'uTime'.");
            }

            if (locColDiffuse < 0)
            {
                Rl.UnloadShader(shader);
                throw new InvalidOperationException($"Effect shader '{key}' is missing required uniform 'colDiffuse'.");
            }

            unsafe
            {
                if (shader.locs != null)
                {
                    shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = locVertexPosition;
                    shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
                    shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locModel;
                    shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = locColDiffuse;
                }
            }

            var effect = new RaylibEffectShader(key, shader, locTint, locTime, locColDiffuse);
            _entries[key] = new Entry(effect);
            return effect;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            foreach (Entry entry in _entries.Values)
            {
                if (entry.Effect.Shader.id != 0)
                {
                    Rl.UnloadShader(entry.Effect.Shader);
                }
            }

            _entries.Clear();
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibEffectShaderRegistry));
            }
        }

        private readonly struct Entry
        {
            public Entry(RaylibEffectShader effect)
            {
                Effect = effect;
            }

            public RaylibEffectShader Effect { get; }
        }
    }

    public readonly struct RaylibEffectShader
    {
        public RaylibEffectShader(string key, Shader shader, int locTint, int locTime, int locColDiffuse)
        {
            Key = key;
            Shader = shader;
            LocTint = locTint;
            LocTime = locTime;
            LocColDiffuse = locColDiffuse;
        }

        public string Key { get; }

        public Shader Shader { get; }

        public int LocTint { get; }

        public int LocTime { get; }

        public int LocColDiffuse { get; }
    }
}
