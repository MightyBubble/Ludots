using System;
using System.Numerics;
using Ludots.Raylib.Render;
using Raylib_cs;

namespace Ludots.App.RaylibEngineGallery
{
    internal static class GallerySunSky
    {
        public static RaylibRenderEnvironmentConfig CreateConfig(
            RaylibFrameLighting lighting,
            float sizeMeters = 1200f)
        {
            RaylibRenderEnvironmentConfig defaults = RaylibRenderEnvironmentConfig.CreateDefault();
            Vector3 zenith = Vector3.Clamp(
                (lighting.SkyZenithColor * 1.10f) + new Vector3(0.02f, 0.035f, 0.055f),
                Vector3.Zero,
                Vector3.One);
            Vector3 horizon = Vector3.Clamp(
                (lighting.SkyGroundColor * 1.45f) + new Vector3(0.38f, 0.34f, 0.24f),
                Vector3.Zero,
                Vector3.One);
            Vector3 groundHaze = Vector3.Clamp(
                (lighting.SkyGroundColor * 1.15f) + new Vector3(0.20f, 0.18f, 0.14f),
                Vector3.Zero,
                Vector3.One);

            return defaults with
            {
                Lighting = defaults.Lighting with
                {
                    SunDirection = lighting.SunDirectionToward,
                    SunColor = Vector3.Clamp(lighting.LightColor, Vector3.Zero, Vector3.One),
                    SunStrength = lighting.LightIntensity,
                    AmbientColor = new Vector3(lighting.AmbientRgba.X, lighting.AmbientRgba.Y, lighting.AmbientRgba.Z),
                    AmbientStrength = lighting.AmbientRgba.W,
                },
                Skybox = defaults.Skybox with
                {
                    SizeMeters = sizeMeters,
                    ZenithColor = zenith,
                    HorizonColor = horizon,
                    GroundHazeColor = groundHaze,
                    ClearColor = ToRayColor(horizon),
                },
            };
        }

        private static Color ToRayColor(Vector3 color)
        {
            return new Color(
                Clamp01ToByte(color.X),
                Clamp01ToByte(color.Y),
                Clamp01ToByte(color.Z),
                255);
        }

        private static byte Clamp01ToByte(float value)
        {
            return (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
        }
    }
}
