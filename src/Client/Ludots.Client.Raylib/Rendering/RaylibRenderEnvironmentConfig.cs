using System;
using System.Numerics;
using Raylib_cs;

namespace Ludots.Client.Raylib.Rendering
{
    public sealed record RaylibRenderEnvironmentConfig(
        RaylibLightingConfig Lighting,
        RaylibSkyboxConfig Skybox,
        RaylibWaterRenderConfig Water,
        RaylibPostProcessConfig PostProcess)
    {
        public static RaylibRenderEnvironmentConfig CreateDefault()
        {
            return new RaylibRenderEnvironmentConfig(
                RaylibLightingConfig.CreateDefault(),
                RaylibSkyboxConfig.CreateDefault(),
                RaylibWaterRenderConfig.CreateDefault(),
                RaylibPostProcessConfig.CreateDefault()).NormalizeAndValidate();
        }

        public RaylibRenderEnvironmentConfig NormalizeAndValidate()
        {
            return this with
            {
                Lighting = Lighting.NormalizeAndValidate(),
                Skybox = Skybox.Validate(),
                Water = Water.Validate(),
                PostProcess = PostProcess.Validate()
            };
        }

        internal static void RequireFinite(float value, string name)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must be finite.");
            }
        }

        internal static void RequirePositive(float value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must be greater than zero.");
            }
        }

        internal static void RequireRange(float value, float min, float max, string name)
        {
            RequireFinite(value, name);
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must be between {min} and {max}.");
            }
        }

        internal static Vector3 RequireUnitDirection(Vector3 value, string name)
        {
            RequireFinite(value, name);
            float lengthSquared = value.LengthSquared();
            if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.000001f)
            {
                throw new ArgumentOutOfRangeException(name, $"{name} must be a non-zero vector.");
            }

            return Vector3.Normalize(value);
        }

        internal static void RequireFinite(Vector3 value, string name)
        {
            RequireFinite(value.X, $"{name}.X");
            RequireFinite(value.Y, $"{name}.Y");
            RequireFinite(value.Z, $"{name}.Z");
        }

        internal static void RequireColor(Vector3 value, string name)
        {
            RequireFinite(value, name);
            RequireRange(value.X, 0f, 1f, $"{name}.X");
            RequireRange(value.Y, 0f, 1f, $"{name}.Y");
            RequireRange(value.Z, 0f, 1f, $"{name}.Z");
        }
    }

    public readonly record struct RaylibLightingConfig(
        Vector3 SunDirection,
        Vector3 SunColor,
        Vector3 AmbientColor,
        float AmbientStrength,
        float SunStrength,
        Vector3 FogColor,
        float FogNearMeters,
        float FogFarMeters,
        float FogDensity)
    {
        public static RaylibLightingConfig CreateDefault()
        {
            return new RaylibLightingConfig(
                new Vector3(-0.36f, 0.82f, -0.44f),
                new Vector3(1.0f, 0.93f, 0.78f),
                new Vector3(0.50f, 0.62f, 0.74f),
                0.38f,
                0.92f,
                new Vector3(0.62f, 0.72f, 0.82f),
                700f,
                4200f,
                0.00018f);
        }

        public RaylibLightingConfig NormalizeAndValidate()
        {
            Vector3 sunDirection = RaylibRenderEnvironmentConfig.RequireUnitDirection(SunDirection, nameof(SunDirection));
            RaylibRenderEnvironmentConfig.RequireColor(SunColor, nameof(SunColor));
            RaylibRenderEnvironmentConfig.RequireColor(AmbientColor, nameof(AmbientColor));
            RaylibRenderEnvironmentConfig.RequireRange(AmbientStrength, 0f, 4f, nameof(AmbientStrength));
            RaylibRenderEnvironmentConfig.RequireRange(SunStrength, 0f, 8f, nameof(SunStrength));
            RaylibRenderEnvironmentConfig.RequireColor(FogColor, nameof(FogColor));
            RaylibRenderEnvironmentConfig.RequirePositive(FogNearMeters, nameof(FogNearMeters));
            RaylibRenderEnvironmentConfig.RequirePositive(FogFarMeters, nameof(FogFarMeters));
            if (FogFarMeters <= FogNearMeters)
            {
                throw new ArgumentOutOfRangeException(nameof(FogFarMeters), "FogFarMeters must be greater than FogNearMeters.");
            }

            RaylibRenderEnvironmentConfig.RequireRange(FogDensity, 0f, 0.02f, nameof(FogDensity));
            return this with { SunDirection = sunDirection };
        }
    }

    public readonly record struct RaylibSkyboxConfig(
        bool Enabled,
        float SizeMeters,
        Vector3 ZenithColor,
        Vector3 HorizonColor,
        Vector3 GroundHazeColor,
        Color ClearColor,
        Color DeepClearColor)
    {
        public static RaylibSkyboxConfig CreateDefault()
        {
            return new RaylibSkyboxConfig(
                Enabled: true,
                SizeMeters: 9000f,
                ZenithColor: new Vector3(0.16f, 0.36f, 0.62f),
                HorizonColor: new Vector3(0.72f, 0.84f, 0.92f),
                GroundHazeColor: new Vector3(0.52f, 0.64f, 0.66f),
                ClearColor: new Color(84, 125, 158, 255),
                DeepClearColor: new Color(6, 10, 16, 255));
        }

        public RaylibSkyboxConfig Validate()
        {
            RaylibRenderEnvironmentConfig.RequirePositive(SizeMeters, nameof(SizeMeters));
            RaylibRenderEnvironmentConfig.RequireColor(ZenithColor, nameof(ZenithColor));
            RaylibRenderEnvironmentConfig.RequireColor(HorizonColor, nameof(HorizonColor));
            RaylibRenderEnvironmentConfig.RequireColor(GroundHazeColor, nameof(GroundHazeColor));
            return this;
        }
    }

    public readonly record struct RaylibWaterRenderConfig(
        Vector3 ShallowColor,
        Vector3 DeepColor,
        float WaveAmplitudeMeters,
        float WaveFrequency,
        float WaveSpeed,
        float FresnelStrength)
    {
        public static RaylibWaterRenderConfig CreateDefault()
        {
            return new RaylibWaterRenderConfig(
                ShallowColor: new Vector3(0.18f, 0.56f, 0.72f),
                DeepColor: new Vector3(0.02f, 0.17f, 0.36f),
                WaveAmplitudeMeters: 0.045f,
                WaveFrequency: 0.09f,
                WaveSpeed: 0.72f,
                FresnelStrength: 0.38f);
        }

        public RaylibWaterRenderConfig Validate()
        {
            RaylibRenderEnvironmentConfig.RequireColor(ShallowColor, nameof(ShallowColor));
            RaylibRenderEnvironmentConfig.RequireColor(DeepColor, nameof(DeepColor));
            RaylibRenderEnvironmentConfig.RequireRange(WaveAmplitudeMeters, 0f, 4f, nameof(WaveAmplitudeMeters));
            RaylibRenderEnvironmentConfig.RequireRange(WaveFrequency, 0f, 16f, nameof(WaveFrequency));
            RaylibRenderEnvironmentConfig.RequireRange(WaveSpeed, 0f, 16f, nameof(WaveSpeed));
            RaylibRenderEnvironmentConfig.RequireRange(FresnelStrength, 0f, 2f, nameof(FresnelStrength));
            return this;
        }
    }

    public readonly record struct RaylibPostProcessConfig(
        bool Enabled,
        float Exposure,
        float Contrast,
        float Saturation,
        float VignetteStrength)
    {
        public static RaylibPostProcessConfig CreateDefault()
        {
            return new RaylibPostProcessConfig(
                Enabled: true,
                Exposure: 1.03f,
                Contrast: 1.06f,
                Saturation: 1.05f,
                VignetteStrength: 0.14f);
        }

        public RaylibPostProcessConfig Validate()
        {
            RaylibRenderEnvironmentConfig.RequireRange(Exposure, 0.05f, 8f, nameof(Exposure));
            RaylibRenderEnvironmentConfig.RequireRange(Contrast, 0.05f, 8f, nameof(Contrast));
            RaylibRenderEnvironmentConfig.RequireRange(Saturation, 0f, 8f, nameof(Saturation));
            RaylibRenderEnvironmentConfig.RequireRange(VignetteStrength, 0f, 1f, nameof(VignetteStrength));
            return this;
        }
    }
}
