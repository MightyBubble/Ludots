using System;
using System.Numerics;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// 平面投影阴影：方向光把几何沿光向投影到地面平面（经典 planar shadow 矩阵）。
    /// 投影矩阵经 DrawMesh/DrawModelEx 的原生 transform 通道生效，零自定义 uniform
    /// （native 5.0 的矩阵 uniform 通道不可用；变换通道已被深度绘制路径验证）。
    /// 限制：接收面为水平面 y=GroundY；重叠投影会加深（可后续以模板缓冲收紧）。
    /// </summary>
    public sealed unsafe class RaylibPlanarShadows : IDisposable
    {
        private readonly Color _tint;
        private readonly Shader _defaultShader;
        private Material _meshMaterial;
        private bool _disposed;

        public RaylibPlanarShadows(byte alpha = 110)
        {
            _tint = new Color(10, 12, 18, alpha);
            _defaultShader = Rl.LoadShader(null!, null!);
            _meshMaterial = Rl.LoadMaterialDefault();
            _meshMaterial.shader = _defaultShader;
            if (_defaultShader.id == 0)
            {
                throw new InvalidOperationException("Failed to acquire raylib default shader for planar shadows.");
            }
        }

        public float GroundY { get; set; }

        public void DrawMeshShadow(Mesh mesh, RaylibMatrix modelTransform, Vector3 lightDirectionToward)
        {
            ThrowIfDisposed();
            RaylibMatrix shadowMatrix = Multiply(
                BuildPlanarMatrix(lightDirectionToward, GroundY),
                modelTransform);
            Rl.rlDisableDepthMask();
            Rl.BeginBlendMode(BlendMode.BLEND_ALPHA);
            Rl.DrawMesh(mesh, _meshMaterial, shadowMatrix);
            Rl.EndBlendMode();
            Rl.rlEnableDepthMask();
        }

        public void DrawModelShadow(Model model, Vector3 position, float rotationAngleY, Vector3 scale, Vector3 lightDirectionToward)
        {
            ThrowIfDisposed();
            RaylibMatrix planar = BuildPlanarMatrix(lightDirectionToward, GroundY);

            Span<Shader> swap = stackalloc Shader[model.materialCount];
            for (int i = 0; i < model.materialCount; i++)
            {
                swap[i] = model.materials[i].shader;
                model.materials[i].shader = _defaultShader;
            }

            Rl.rlMatrixMode((int)RlMatrixMode.RL_MODELVIEW);
            Rl.rlPushMatrix();
            MultMatrix(ref planar);
            Rl.DrawModelEx(model, position, Vector3.UnitY, rotationAngleY, scale, _tint);
            Rl.rlPopMatrix();

            for (int i = 0; i < model.materialCount; i++)
            {
                model.materials[i].shader = swap[i];
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _meshMaterial.shader = default;
            Rl.UnloadMaterial(_meshMaterial);
            Rl.UnloadShader(_defaultShader);
            _disposed = true;
        }

        /// <summary>经典平面投影矩阵：p' = p - ((n·p - groundY)/(n·l)) · l（l 指向光源）。</summary>
        private static RaylibMatrix BuildPlanarMatrix(Vector3 lightToward, float groundY)
        {
            Vector3 l = Vector3.Normalize(lightToward);
            if (l.Y <= 0.02f)
            {
                l = new Vector3(l.X, 0.02f, l.Z);
                l = Vector3.Normalize(l);
            }

            Vector3 n = Vector3.UnitY;
            float dot = Vector3.Dot(n, l);
            float c = -groundY;

            var rowMajor = new Matrix4x4(
                dot - (l.X * n.X), -l.X * n.Y, -l.X * n.Z, -l.X * c,
                -l.Y * n.X, dot - (l.Y * n.Y), -l.Y * n.Z, -l.Y * c,
                -l.Z * n.X, -l.Z * n.Y, dot - (l.Z * n.Z), -l.Z * c,
                -n.X, -n.Y, -n.Z, dot - c);

            return RaylibMatrix.FromSystemNumerics(rowMajor);
        }

        private static void MultMatrix(ref RaylibMatrix matrix)
        {
            RaylibMatrix local = matrix;
            Rl.rlMultMatrixf((float*)&local);
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

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibPlanarShadows));
            }
        }
    }
}
