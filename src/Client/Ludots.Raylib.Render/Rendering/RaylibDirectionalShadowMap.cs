using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 方向光 shadow map：深度打包进颜色 RT（RGBA 256 进位），接收端经 uLightSpaceMatrix
    /// （MAT4 uniform，native ≥5.5）投影 + 3x3 PCF。深度 pass 与接收端共用同一份手工
    /// lookAt/ortho 矩阵保证 NDC 深度严格一致；RT 纹理 Y 翻转在采样端取 1-y。
    /// </summary>
    public sealed unsafe class RaylibDirectionalShadowMap : IDisposable
    {
        public const int MapSize = 2048;

        private readonly RenderTexture2D _rt;
        private readonly Shader _depthShader;
        private Material _depthMaterial;
        private RaylibMatrix _lightView;
        private RaylibMatrix _lightProjection;
        private bool _frameActive;
        private bool _disposed;

        public RaylibDirectionalShadowMap()
        {
            _rt = Rl.LoadRenderTexture(MapSize, MapSize);
            string baseDir = AppContext.BaseDirectory;
            _depthShader = Rl.LoadShader(
                System.IO.Path.Combine(baseDir, "shadow_depth.vs"),
                System.IO.Path.Combine(baseDir, "shadow_depth.fs"));
            if (_depthShader.id == 0)
            {
                throw new InvalidOperationException("Failed to load shadow_depth shader (shader.id == 0).");
            }

            _depthMaterial = Rl.LoadMaterialDefault();
            _depthMaterial.shader = _depthShader;
        }

        public Texture2D DepthTexture => _rt.texture;

        public bool HasFrame { get; private set; }

        public RaylibMatrix LightViewProjection => Multiply(_lightProjection, _lightView);

        public void BeginFrame(Vector3 lightDirectionToward, Vector3 sceneCenter, float sceneRadius)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibDirectionalShadowMap));
            }

            if (_frameActive)
            {
                throw new InvalidOperationException("Shadow frame already active; call EndFrame first.");
            }

            Vector3 forward = Vector3.Normalize(lightDirectionToward);
            if (forward.LengthSquared() < 0.5f)
            {
                forward = -Vector3.UnitY;
            }

            Vector3 upHint = MathF.Abs(Vector3.Dot(forward, Vector3.UnitY)) > 0.95f
                ? Vector3.UnitX
                : Vector3.UnitY;
            float eyeDistance = MathF.Max(sceneRadius * 1.8f, 8f);
            Vector3 eye = sceneCenter - (forward * eyeDistance);

            _lightView = BuildLookAt(eye, sceneCenter, upHint);

            float halfExtent = MathF.Max(sceneRadius * 1.35f, 4f);
            _lightProjection = BuildOrtho(
                -halfExtent, halfExtent, -halfExtent, halfExtent,
                0.1f, eyeDistance + (sceneRadius * 2.2f));

            Rl.BeginTextureMode(_rt);
            Rl.ClearBackground(new Color(255, 255, 255, 255));
            Rl.rlEnableDepthTest();
            Rl.rlEnableDepthMask();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_PROJECTION);
            Rl.rlLoadIdentity();
            MultMatrix(ref _lightProjection);
            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlLoadIdentity();
            MultMatrix(ref _lightView);
            _frameActive = true;
            HasFrame = true;
        }

        public void DrawMeshShadow(Mesh mesh, RaylibMatrix transform)
        {
            EnsureFrameActive();
            Rl.DrawMesh(mesh, _depthMaterial, transform);
        }

        /// <summary>模型深度：换装深度材质经 DrawModelEx 原生路径绘制后还原。</summary>
        public void DrawModelShadow(Model model, Vector3 position, float rotationAngleY, Vector3 scale)
        {
            EnsureFrameActive();
            Span<Shader> original = stackalloc Shader[model.materialCount];
            for (int i = 0; i < model.materialCount; i++)
            {
                original[i] = model.materials[i].shader;
                model.materials[i].shader = _depthShader;
            }

            Rl.DrawModelEx(model, position, Vector3.UnitY, rotationAngleY, scale, new Color(255, 255, 255, 255));

            for (int i = 0; i < model.materialCount; i++)
            {
                model.materials[i].shader = original[i];
            }
        }

        public void EndFrame()
        {
            if (!_frameActive)
            {
                return;
            }

            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlLoadIdentity();
            Rl.rlMatrixMode((int)RlMatrixMode.RL_PROJECTION);
            Rl.rlLoadIdentity();
            Rl.EndTextureMode();
            _frameActive = false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EndFrame();
            _depthMaterial.shader = default;
            Rl.UnloadMaterial(_depthMaterial);
            Rl.UnloadShader(_depthShader);
            Rl.UnloadRenderTexture(_rt);
            _disposed = true;
        }

        private void EnsureFrameActive()
        {
            if (!_frameActive)
            {
                throw new InvalidOperationException("Shadow draws require BeginFrame first.");
            }
        }

        private static void MultMatrix(ref RaylibMatrix matrix)
        {
            RaylibMatrix local = matrix;
            Rl.rlMultMatrixf((float*)&local);
        }

        private static RaylibMatrix BuildLookAt(Vector3 eye, Vector3 target, Vector3 up)
        {
            Vector3 f = Vector3.Normalize(target - eye);
            Vector3 s = Vector3.Normalize(Vector3.Cross(f, up));
            Vector3 u = Vector3.Cross(s, f);

            return new RaylibMatrix
            {
                m0 = s.X, m4 = s.Y, m8 = s.Z, m12 = -Vector3.Dot(s, eye),
                m1 = u.X, m5 = u.Y, m9 = u.Z, m13 = -Vector3.Dot(u, eye),
                m2 = -f.X, m6 = -f.Y, m10 = -f.Z, m14 = Vector3.Dot(f, eye),
                m3 = 0f, m7 = 0f, m11 = 0f, m15 = 1f,
            };
        }

        private static RaylibMatrix BuildOrtho(float left, float right, float bottom, float top, float near, float far)
        {
            float rl = 1f / (right - left);
            float tb = 1f / (top - bottom);
            float fn = 1f / (far - near);

            return new RaylibMatrix
            {
                m0 = 2f * rl, m4 = 0f, m8 = 0f, m12 = -(right + left) * rl,
                m1 = 0f, m5 = 2f * tb, m9 = 0f, m13 = -(top + bottom) * tb,
                m2 = 0f, m6 = 0f, m10 = -2f * fn, m14 = -(far + near) * fn,
                m3 = 0f, m7 = 0f, m11 = 0f, m15 = 1f,
            };
        }

        private static RaylibMatrix Multiply(in RaylibMatrix a, in RaylibMatrix b)
        {
            return new RaylibMatrix
            {
                m0 = (a.m0 * b.m0) + (a.m4 * b.m1) + (a.m8 * b.m2) + (a.m12 * b.m3),
                m1 = (a.m1 * b.m0) + (a.m5 * b.m1) + (a.m9 * b.m2) + (a.m13 * b.m3),
                m2 = (a.m2 * b.m0) + (a.m6 * b.m1) + (a.m10 * b.m2) + (a.m14 * b.m3),
                m3 = (a.m3 * b.m0) + (a.m7 * b.m1) + (a.m11 * b.m2) + (a.m15 * b.m3),
                m4 = (a.m0 * b.m4) + (a.m4 * b.m5) + (a.m8 * b.m6) + (a.m12 * b.m7),
                m5 = (a.m1 * b.m4) + (a.m5 * b.m5) + (a.m9 * b.m6) + (a.m13 * b.m7),
                m6 = (a.m2 * b.m4) + (a.m6 * b.m5) + (a.m10 * b.m6) + (a.m14 * b.m7),
                m7 = (a.m3 * b.m4) + (a.m7 * b.m5) + (a.m11 * b.m6) + (a.m15 * b.m7),
                m8 = (a.m0 * b.m8) + (a.m4 * b.m9) + (a.m8 * b.m10) + (a.m12 * b.m11),
                m9 = (a.m1 * b.m8) + (a.m5 * b.m9) + (a.m9 * b.m10) + (a.m13 * b.m11),
                m10 = (a.m2 * b.m8) + (a.m6 * b.m9) + (a.m10 * b.m10) + (a.m14 * b.m11),
                m11 = (a.m3 * b.m8) + (a.m7 * b.m9) + (a.m11 * b.m10) + (a.m15 * b.m11),
                m12 = (a.m0 * b.m12) + (a.m4 * b.m13) + (a.m8 * b.m14) + (a.m12 * b.m15),
                m13 = (a.m1 * b.m12) + (a.m5 * b.m13) + (a.m9 * b.m14) + (a.m13 * b.m15),
                m14 = (a.m2 * b.m12) + (a.m6 * b.m13) + (a.m10 * b.m14) + (a.m14 * b.m15),
                m15 = (a.m3 * b.m12) + (a.m7 * b.m13) + (a.m11 * b.m14) + (a.m15 * b.m15),
            };
        }
    }
}
