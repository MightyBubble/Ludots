using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    /// <summary>
    /// split-sum IBL 烘焙端：CPU 按解析天空函数（昼夜 ramp 派生的天顶/地平线/地面色 + 太阳光晕）
    /// 逐像素生成环境立方图 mip 链（每级按 roughness = mip/MaxLod 做 GGX 重要性采样预滤波），
    /// 以及 GGX 数值积分的 BRDF LUT（R = specular scale，G = bias）。零额外 GL pass；
    /// 昼夜相位步进超过 <see cref="RebakePhaseStep"/> 才重烘环境图。
    /// </summary>
    public sealed unsafe class RaylibSkyIbl : IDisposable
    {
        public const int EnvFaceSize = 64;
        public const int MipCount = 7;
        public const float MaxLod = MipCount - 1;
        public const float RebakePhaseStep = 0.02f;
        private const int PrefilterSamples = 64;
        private const int BrdfLutSize = 512;
        private const int BrdfLutSamples = 256;
        private const int PixelFormatR8G8B8A8 = 7;

        private Texture2D _envCubemap;
        private Texture2D _brdfLut;
        private float _bakedPhase = float.NaN;
        private bool _disposed;

        public Texture2D EnvCubemap
        {
            get
            {
                ThrowIfDisposed();
                return _envCubemap;
            }
        }

        public Texture2D BrdfLut
        {
            get
            {
                ThrowIfDisposed();
                return _brdfLut;
            }
        }

        public void Ensure(RaylibFrameLighting lighting)
        {
            ThrowIfDisposed();
            if (lighting == null)
            {
                throw new ArgumentNullException(nameof(lighting));
            }

            if (_brdfLut.id == 0)
            {
                BakeBrdfLut();
            }

            float phase = lighting.DayPhase01;
            if (_envCubemap.id == 0 || PhaseDistance(phase, _bakedPhase) > RebakePhaseStep)
            {
                BakeEnvCubemap(lighting);
            }
        }

        /// <summary>BRDF LUT 与光照无关，构造期预烘可把一次性开销移出首帧 Draw。</summary>
        public void PrewarmLut()
        {
            ThrowIfDisposed();
            if (_brdfLut.id == 0)
            {
                BakeBrdfLut();
            }
        }

        private void BakeEnvCubemap(RaylibFrameLighting lighting)
        {
            Vector3 zenith = lighting.SkyZenithColor;
            Vector3 ground = lighting.SkyGroundColor;
            Vector3 horizon = Vector3.Lerp(zenith, ground, 0.55f);
            Vector3 sunDirection = lighting.SunDirectionToward;
            Vector3 sunColor = lighting.LightColor;

            int bufferLength = 0;
            for (int mip = 0; mip < MipCount; mip++)
            {
                int size = EnvFaceSize >> mip;
                bufferLength += 6 * size * size * 4;
            }

            byte[] buffer = new byte[bufferLength];
            Parallel.For(0, 6, face =>
            {
                int offset = 0;
                for (int mip = 0; mip < MipCount; mip++)
                {
                    int size = EnvFaceSize >> mip;
                    int faceBytes = size * size * 4;
                    BakePrefilteredFace(buffer, offset + (face * faceBytes), face, size, mip, zenith, horizon, ground, sunDirection, sunColor);
                    offset += 6 * faceBytes;
                }
            });

            if (_envCubemap.id != 0)
            {
                RaylibNativeResources.UnloadTexture(_envCubemap);
                _envCubemap = default;
            }

            fixed (byte* data = buffer)
            {
                _envCubemap = RaylibNativeResources.LoadTextureCubemap(data, EnvFaceSize, PixelFormatR8G8B8A8, MipCount);
            }

            _bakedPhase = lighting.DayPhase01;
        }

        private static void BakePrefilteredFace(
            byte[] buffer,
            int writeOffset,
            int face,
            int size,
            int mip,
            Vector3 zenith,
            Vector3 horizon,
            Vector3 ground,
            Vector3 sunDirection,
            Vector3 sunColor)
        {
            float roughness = mip / MaxLod;
            float alpha = MathF.Max(roughness * roughness, 1e-4f);
            for (int y = 0; y < size; y++)
            {
                float v = 1f - ((2f * (y + 0.5f)) / size - 1f);
                for (int x = 0; x < size; x++)
                {
                    float u = (2f * (x + 0.5f)) / size - 1f;
                    Vector3 r = CubeFaceDirection(face, u, v);
                    Vector3 prefiltered;
                    if (mip == 0)
                    {
                        // roughness=0 时 GGX 重要性采样退化为镜面方向，直接取解析天空值。
                        prefiltered = SkyRadiance(r, zenith, horizon, ground, sunDirection, sunColor);
                    }
                    else
                    {
                        Vector3 sum = Vector3.Zero;
                        float totalWeight = 0f;
                        for (int s = 0; s < PrefilterSamples; s++)
                        {
                            Hammersley(s, PrefilterSamples, out float xi1, out float xi2);
                            Vector3 h = ImportanceSampleGgx(xi1, xi2, alpha, r);
                            Vector3 l = Vector3.Reflect(-r, h);
                            float ndl = Vector3.Dot(r, l);
                            if (ndl <= 0f)
                            {
                                continue;
                            }

                            sum += SkyRadiance(l, zenith, horizon, ground, sunDirection, sunColor) * ndl;
                            totalWeight += ndl;
                        }

                        prefiltered = totalWeight > 1e-6f ? sum / totalWeight : Vector3.Zero;
                    }

                    int index = writeOffset + (((y * size) + x) * 4);
                    buffer[index] = ToByte(prefiltered.X);
                    buffer[index + 1] = ToByte(prefiltered.Y);
                    buffer[index + 2] = ToByte(prefiltered.Z);
                    buffer[index + 3] = 255;
                }
            }
        }

        /// <summary>skybox.fs 同形解析天空：天顶/地平线/地面 haze 分带 + 太阳高光晕，供 CPU 烘焙复用。</summary>
        private static Vector3 SkyRadiance(
            Vector3 direction,
            Vector3 zenith,
            Vector3 horizon,
            Vector3 ground,
            Vector3 sunDirection,
            Vector3 sunColor)
        {
            float y = direction.Y;
            Vector3 sky = Vector3.Lerp(horizon, zenith, SmoothStep(-0.10f, 0.85f, y));
            sky = Vector3.Lerp(sky, ground, (1f - SmoothStep(-0.22f, 0.12f, y)) * 0.42f);

            float sunDot = MathF.Max(Vector3.Dot(direction, sunDirection), 0f);
            float sunDisk = MathF.Pow(sunDot, 720f);
            float sunGlow = MathF.Pow(sunDot, 22f) * 0.34f;
            return sky + (sunColor * (sunGlow + (sunDisk * 2.4f)));
        }

        private void BakeBrdfLut()
        {
            Image image = Rl.GenImageColor(BrdfLutSize, BrdfLutSize, new Color(0, 0, 0, 255));
            byte* pixels = (byte*)image.data;
            Parallel.For(0, BrdfLutSize, y =>
            {
                // 标量内联 Hammersley + GGX 重要性采样（N=+Y，V 在 XY 平面），避免热循环内的
                // 结构体往返与方法调用开销（Debug JIT 不内联，LUT 烘焙耗时会放大一个量级）。
                float roughness = (y + 0.5f) / BrdfLutSize;
                float rr = MathF.Max(roughness, 1e-4f);
                float alpha = rr * rr;
                float kk = ((rr + 1f) * (rr + 1f)) / 8f;
                for (int x = 0; x < BrdfLutSize; x++)
                {
                    float ndv = (x + 0.5f) / BrdfLutSize;
                    if (ndv < 1e-4f)
                    {
                        ndv = 1e-4f;
                    }

                    float viewX = MathF.Sqrt(MathF.Max(1f - (ndv * ndv), 0f));
                    float scale = 0f;
                    float bias = 0f;
                    for (int s = 0; s < BrdfLutSamples; s++)
                    {
                        uint bits = (uint)s;
                        bits = (bits << 16) | (bits >> 16);
                        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
                        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
                        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
                        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
                        float xi2 = bits * (1f / 4294967296f);

                        float phi = MathF.Tau * (s / (float)BrdfLutSamples);
                        float cosTheta = MathF.Sqrt((1f - xi2) / (1f + ((alpha * alpha - 1f) * xi2)));
                        float sinTheta = MathF.Sqrt(MathF.Max(1f - (cosTheta * cosTheta), 0f));
                        float hx = MathF.Cos(phi) * sinTheta;
                        float hy = cosTheta;

                        float vdh = (viewX * hx) + (ndv * hy);
                        if (vdh < 1e-6f)
                        {
                            vdh = 1e-6f;
                        }

                        float ndl = (2f * vdh * hy) - ndv;
                        if (ndl <= 0f)
                        {
                            continue;
                        }

                        float ndh = hy < 1e-6f ? 1e-6f : hy;
                        float g1 = ndv / ((ndv * (1f - kk)) + kk);
                        float g2 = ndl / ((ndl * (1f - kk)) + kk);
                        float gVis = ((g1 * g2) * vdh) / ((ndh * ndv) + 1e-6f);
                        float inv = 1f - vdh;
                        float inv2 = inv * inv;
                        float fresnel = inv2 * inv2 * inv;
                        scale += gVis * (1f - fresnel);
                        bias += gVis * fresnel;
                    }

                    byte* pixel = pixels + (((y * BrdfLutSize) + x) * 4);
                    pixel[0] = ToByte(scale / BrdfLutSamples);
                    pixel[1] = ToByte(bias / BrdfLutSamples);
                    pixel[2] = 0;
                    pixel[3] = 255;
                }
            });

            Texture2D lut = RaylibNativeResources.LoadTextureFromImage(image);
            Rl.UnloadImage(image);
            if (lut.id == 0)
            {
                throw new InvalidOperationException($"{nameof(RaylibSkyIbl)} failed to upload the BRDF LUT.");
            }

            Rl.SetTextureFilter(lut, Rl.TextureFilter.TEXTURE_FILTER_BILINEAR);
            _brdfLut = lut;
        }

        private static Vector3 CubeFaceDirection(int face, float u, float v)
        {
            Vector3 direction = face switch
            {
                0 => new Vector3(1f, -v, -u),
                1 => new Vector3(-1f, -v, u),
                2 => new Vector3(u, 1f, v),
                3 => new Vector3(u, -1f, -v),
                4 => new Vector3(u, -v, 1f),
                _ => new Vector3(-u, -v, -1f),
            };
            return Vector3.Normalize(direction);
        }

        private static Vector3 ImportanceSampleGgx(float xi1, float xi2, float alpha, Vector3 normal)
        {
            float phi = MathF.Tau * xi1;
            float cosTheta = MathF.Sqrt((1f - xi2) / (1f + ((alpha * alpha - 1f) * xi2)));
            float sinTheta = MathF.Sqrt(MathF.Max(1f - (cosTheta * cosTheta), 0f));
            Vector3 tangentX = MathF.Abs(normal.X) > 0.5f
                ? Vector3.Normalize(new Vector3(-normal.Z, 0f, normal.X))
                : new Vector3(1f, 0f, 0f);
            Vector3 tangentY = Vector3.Cross(normal, tangentX);
            return (tangentX * (MathF.Cos(phi) * sinTheta)) +
                   (tangentY * (MathF.Sin(phi) * sinTheta)) +
                   (normal * cosTheta);
        }

        private static void Hammersley(int index, int count, out float xi1, out float xi2)
        {
            xi1 = index / (float)count;
            uint bits = (uint)index;
            bits = (bits << 16) | (bits >> 16);
            bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
            bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
            bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
            bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
            xi2 = bits * (1f / 4294967296f);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return (t * t) * (3f - (2f * t));
        }

        private static float PhaseDistance(float a, float b)
        {
            if (float.IsNaN(b))
            {
                return float.MaxValue;
            }

            float delta = MathF.Abs(a - b);
            return MathF.Min(delta, 1f - delta);
        }

        private static byte ToByte(float value)
        {
            return (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_envCubemap.id != 0)
            {
                RaylibNativeResources.UnloadTexture(_envCubemap);
                _envCubemap = default;
            }

            if (_brdfLut.id != 0)
            {
                RaylibNativeResources.UnloadTexture(_brdfLut);
                _brdfLut = default;
            }

            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RaylibSkyIbl));
            }
        }
    }

}
