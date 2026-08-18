using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Raylib.Render
{
    public readonly struct RaylibFrameLightingLocations
    {
        public readonly int LightDir;
        public readonly int Ambient;
        public readonly int LightColor;
        public readonly int LightIntensity;
        public readonly int FogColor;
        public readonly int FogParams;
        public readonly int ViewPos;

        public RaylibFrameLightingLocations(
            int lightDir,
            int ambient,
            int lightColor,
            int lightIntensity,
            int fogColor,
            int fogParams,
            int viewPos)
        {
            LightDir = lightDir;
            Ambient = ambient;
            LightColor = lightColor;
            LightIntensity = lightIntensity;
            FogColor = fogColor;
            FogParams = fogParams;
            ViewPos = viewPos;
        }

        public static RaylibFrameLightingLocations ResolveOrThrow(Shader shader, string shaderLabel)
        {
            int lightDir = Rl.GetShaderLocation(shader, "uLightDir");
            int ambient = Rl.GetShaderLocation(shader, "uAmbient");
            int lightColor = Rl.GetShaderLocation(shader, "uLightColor");
            int lightIntensity = Rl.GetShaderLocation(shader, "uLightIntensity");
            int fogColor = Rl.GetShaderLocation(shader, "uFogColor");
            int fogParams = Rl.GetShaderLocation(shader, "uFogParams");
            int viewPos = Rl.GetShaderLocation(shader, "uViewPos");
            if (lightDir < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} uniform 'uLightDir' not found.");
            }

            if (ambient < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} uniform 'uAmbient' not found.");
            }

            if (lightColor < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} uniform 'uLightColor' not found.");
            }

            if (lightIntensity < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} uniform 'uLightIntensity' not found.");
            }

            if (fogColor < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} uniform 'uFogColor' not found.");
            }

            if (fogParams < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} uniform 'uFogParams' not found.");
            }

            if (viewPos < 0)
            {
                throw new InvalidOperationException($"{shaderLabel} uniform 'uViewPos' not found.");
            }

            return new RaylibFrameLightingLocations(
                lightDir,
                ambient,
                lightColor,
                lightIntensity,
                fogColor,
                fogParams,
                viewPos);
        }
    }

    public sealed class RaylibFrameLighting
    {
        /// <summary>
        /// When the sun is below the horizon, the same directional slot becomes moonlight at this
        /// fraction of author <c>lightIntensity</c> (elevated moon dir; never a downward N·L=0 key).
        /// </summary>
        private const float MoonlightKeyScale = 0.16f;

        private readonly AmbientSample[] _ramp;
        private readonly Vector3 _lightColor;
        private readonly float _lightIntensity;
        private readonly DistanceFogSettings _fog;
        private bool _moonlightKeyActive;
        private float _solarElevationY;

        public float DayPhase01 { get; private set; }

        public Vector3 SunDirectionToward { get; private set; }

        public Vector3 SkyZenithColor { get; private set; }

        public Vector3 SkyGroundColor { get; private set; }

        public Vector4 AmbientRgba { get; private set; }

        public Vector3 LightColor =>
            _moonlightKeyActive
                ? new Vector3(
                    Math.Clamp(_lightColor.X * 0.55f, 0f, 1f),
                    Math.Clamp(_lightColor.Y * 0.68f, 0f, 1f),
                    Math.Clamp(MathF.Max(_lightColor.Z, 0.92f), 0f, 1f))
                : _lightColor;

        /// <summary>
        /// Authoring peak intensity gated by solar elevation; below-horizon remaps to a modest moonlight key.
        /// </summary>
        public float LightIntensity
        {
            get
            {
                if (_moonlightKeyActive)
                {
                    // Direction is already remapped to an elevated moon; keep a flat modest key in author-units.
                    return _lightIntensity * MoonlightKeyScale;
                }

                float sunElevation = MathF.Max(0f, SunDirectionToward.Y);
                // Soft shoulder so dusk still carries a little key light before ambient takes over.
                float elevationGate = sunElevation * sunElevation;
                return _lightIntensity * elevationGate;
            }
        }

        public bool FogEnabled => _fog.Enabled;

        public Vector3 FogColor => _fog.Color;

        public Vector4 FogParams => _fog.Params;

        private RaylibFrameLighting(
            AmbientSample[] ramp,
            Vector3 lightColor,
            float lightIntensity,
            DistanceFogSettings fog,
            float dayPhase01)
        {
            _ramp = ramp;
            _lightColor = lightColor;
            _lightIntensity = lightIntensity;
            _fog = fog;
            SetDayPhase(dayPhase01);
        }

        public static RaylibFrameLighting LoadFromDefaultPath(float dayPhase01 = 0.42f)
        {
            string ambientPath = Path.Combine(AppContext.BaseDirectory, "ambient_day_ramp.json");
            string fogPath = Path.Combine(AppContext.BaseDirectory, "distance_fog.json");
            return LoadFromJsonFile(ambientPath, fogPath, dayPhase01);
        }

        public static RaylibFrameLighting LoadFromJsonFile(string path, float dayPhase01 = 0.42f)
        {
            string fogPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(path)) ?? AppContext.BaseDirectory,
                "distance_fog.json");
            return LoadFromJsonFile(path, fogPath, dayPhase01);
        }

        public static RaylibFrameLighting LoadFromJsonFile(string ambientPath, string fogPath, float dayPhase01 = 0.42f)
        {
            if (string.IsNullOrWhiteSpace(ambientPath))
            {
                throw new ArgumentException("Ambient ramp path is required.", nameof(ambientPath));
            }

            if (!File.Exists(ambientPath))
            {
                throw new FileNotFoundException(
                    $"{nameof(RaylibFrameLighting)} requires data-driven ambient ramp at '{ambientPath}'.",
                    ambientPath);
            }

            string json = File.ReadAllText(ambientPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("samples", out JsonElement samplesElement) ||
                samplesElement.ValueKind != JsonValueKind.Array ||
                samplesElement.GetArrayLength() < 2)
            {
                throw new InvalidOperationException(
                    $"Ambient ramp '{ambientPath}' must define a 'samples' array with at least two entries.");
            }

            AmbientSample[] samples = new AmbientSample[samplesElement.GetArrayLength()];
            for (int i = 0; i < samples.Length; i++)
            {
                JsonElement sample = samplesElement[i];
                samples[i] = new AmbientSample(
                    ReadRequiredFloat(sample, "phase", ambientPath),
                    ReadRequiredFloat(sample, "r", ambientPath),
                    ReadRequiredFloat(sample, "g", ambientPath),
                    ReadRequiredFloat(sample, "b", ambientPath),
                    ReadRequiredFloat(sample, "intensity", ambientPath));
            }

            for (int i = 1; i < samples.Length; i++)
            {
                if (samples[i].Phase < samples[i - 1].Phase)
                {
                    throw new InvalidOperationException(
                        $"Ambient ramp '{ambientPath}' samples must be sorted by ascending phase.");
                }
            }

            if (samples[0].Phase > 0f || samples[^1].Phase < 1f)
            {
                throw new InvalidOperationException(
                    $"Ambient ramp '{ambientPath}' must cover phase range [0, 1].");
            }

            Vector3 lightColor = new(1f, 0.96f, 0.9f);
            if (root.TryGetProperty("lightColor", out JsonElement lightColorElement))
            {
                if (lightColorElement.ValueKind != JsonValueKind.Array || lightColorElement.GetArrayLength() != 3)
                {
                    throw new InvalidOperationException($"Ambient ramp '{ambientPath}' lightColor must be [r,g,b].");
                }

                lightColor = new Vector3(
                    lightColorElement[0].GetSingle(),
                    lightColorElement[1].GetSingle(),
                    lightColorElement[2].GetSingle());
            }

            float lightIntensity = 1f;
            if (root.TryGetProperty("lightIntensity", out JsonElement lightIntensityElement))
            {
                lightIntensity = lightIntensityElement.GetSingle();
            }

            if (lightIntensity < 0f)
            {
                throw new InvalidOperationException($"Ambient ramp '{ambientPath}' lightIntensity must be >= 0.");
            }

            DistanceFogSettings fog = DistanceFogSettings.LoadOrThrow(fogPath, requiredWhenEnabled: true);
            return new RaylibFrameLighting(samples, lightColor, lightIntensity, fog, dayPhase01);
        }

        public void SetDayPhase(float dayPhase01)
        {
            if (float.IsNaN(dayPhase01) || float.IsInfinity(dayPhase01))
            {
                throw new ArgumentOutOfRangeException(nameof(dayPhase01), dayPhase01, "Day phase must be finite.");
            }

            DayPhase01 = dayPhase01 - MathF.Floor(dayPhase01);
            Evaluate();
        }

        public void Evaluate()
        {
            Vector3 solarToward = DeriveSunDirectionToward(DayPhase01);
            _solarElevationY = solarToward.Y;
            if (solarToward.Y <= 0f)
            {
                // Same directional light slot: opposite-phase elevated moon (never leave key pointing down).
                Vector3 moonToward = DeriveSunDirectionToward(DayPhase01 + 0.5f);
                if (moonToward.Y <= 0f)
                {
                    throw new InvalidOperationException(
                        $"Moonlight remap at phase {DayPhase01:0.###} produced non-elevated direction " +
                        $"(Y={moonToward.Y:0.####}); solar Y was {_solarElevationY:0.####}.");
                }

                SunDirectionToward = moonToward;
                _moonlightKeyActive = true;
            }
            else
            {
                SunDirectionToward = solarToward;
                _moonlightKeyActive = false;
            }

            AmbientRgba = SampleAmbient(DayPhase01);
            Vector3 ambient = new(AmbientRgba.X, AmbientRgba.Y, AmbientRgba.Z);
            float ambientLuma = MathF.Max(ambient.X, MathF.Max(ambient.Y, ambient.Z));
            SkyZenithColor = Vector3.Clamp(ambient * 1.35f + new Vector3(0.04f), Vector3.Zero, Vector3.One);
            SkyGroundColor = Vector3.Clamp(
                new Vector3(ambientLuma, ambientLuma * 0.94f, ambientLuma * 0.82f) * 0.42f,
                Vector3.Zero,
                Vector3.One);
        }

        public unsafe void Apply(Shader shader, in RaylibFrameLightingLocations locations)
        {
            Vector3 lightDir = SunDirectionToward;
            Vector4 ambient = AmbientRgba;
            Vector3 lightColor = LightColor;
            float lightIntensity = LightIntensity;
            if (lightIntensity > 0f && lightDir.Y <= 0f)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibFrameLighting)} refuses LightIntensity={lightIntensity:0.####} with " +
                    $"non-elevated light direction Y={lightDir.Y:0.####} (N·L would stay 0).");
            }

            // Daytime fog stays author cyan; night/dusk dims so aerial water is not a noon wash.
            // Moonlight ambient intensity is raised for terrain readability — fog dim follows solar elevation, not ambient.W.
            float fogDim;
            Vector3 fogColor;
            if (_moonlightKeyActive)
            {
                float nightLift = Math.Clamp((-_solarElevationY) * 1.25f, 0f, 1f);
                fogDim = Math.Clamp(0.34f - (nightLift * 0.18f), 0.10f, 0.34f);
                Vector3 coolFog = new(0.42f, 0.52f, 0.78f);
                fogColor = Vector3.Lerp(_fog.Color, coolFog, 0.55f) * fogDim;
            }
            else
            {
                fogDim = Math.Clamp(ambient.W / 0.28f, 0.06f, 1f);
                fogColor = _fog.Color * fogDim;
            }

            Vector4 fogParams = _fog.Params;
            Rl.SetShaderValue(shader, locations.LightDir, &lightDir, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(shader, locations.Ambient, &ambient, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            Rl.SetShaderValue(shader, locations.LightColor, &lightColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(shader, locations.LightIntensity, &lightIntensity, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);
            Rl.SetShaderValue(shader, locations.FogColor, &fogColor, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
            Rl.SetShaderValue(shader, locations.FogParams, &fogParams, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        public unsafe void ApplyViewPosition(Shader shader, in RaylibFrameLightingLocations locations, Vector3 viewPos)
        {
            Rl.SetShaderValue(shader, locations.ViewPos, &viewPos, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_VEC3);
        }

        public Vector3 FarLightPosition(float distance = 1000f)
        {
            if (distance <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(distance), distance, "Distance must be > 0.");
            }

            return SunDirectionToward * distance;
        }

        public static Vector3 DeriveSunDirectionToward(float dayPhase01)
        {
            float phase = dayPhase01 - MathF.Floor(dayPhase01);
            float sunAngle = float.Lerp(-MathF.PI * 0.5f, MathF.PI * 1.5f, phase);
            float cos = MathF.Cos(sunAngle);
            float sin = MathF.Sin(sunAngle);
            float z = MathF.Max(sin * 0.9f, -0.25f);
            Vector3 dir = new(cos, sin, z);
            float lenSq = dir.LengthSquared();
            if (lenSq <= 1e-12f)
            {
                throw new InvalidOperationException("Derived sun direction degenerated to zero length.");
            }

            return dir / MathF.Sqrt(lenSq);
        }

        private Vector4 SampleAmbient(float phase)
        {
            if (phase <= _ramp[0].Phase)
            {
                AmbientSample s = _ramp[0];
                return new Vector4(s.R, s.G, s.B, s.Intensity);
            }

            AmbientSample last = _ramp[^1];
            if (phase >= last.Phase)
            {
                return new Vector4(last.R, last.G, last.B, last.Intensity);
            }

            for (int i = 1; i < _ramp.Length; i++)
            {
                AmbientSample b = _ramp[i];
                if (phase > b.Phase)
                {
                    continue;
                }

                AmbientSample a = _ramp[i - 1];
                float span = b.Phase - a.Phase;
                float t = span <= 1e-8f ? 0f : (phase - a.Phase) / span;
                return new Vector4(
                    float.Lerp(a.R, b.R, t),
                    float.Lerp(a.G, b.G, t),
                    float.Lerp(a.B, b.B, t),
                    float.Lerp(a.Intensity, b.Intensity, t));
            }

            return new Vector4(last.R, last.G, last.B, last.Intensity);
        }

        private static float ReadRequiredFloat(JsonElement sample, string name, string path)
        {
            if (!sample.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Ambient ramp '{path}' sample is missing numeric '{name}'.");
            }

            return value.GetSingle();
        }

        private readonly struct AmbientSample
        {
            public readonly float Phase;
            public readonly float R;
            public readonly float G;
            public readonly float B;
            public readonly float Intensity;

            public AmbientSample(float phase, float r, float g, float b, float intensity)
            {
                Phase = phase;
                R = r;
                G = g;
                B = b;
                Intensity = intensity;
            }
        }

        private readonly struct DistanceFogSettings
        {
            public readonly bool Enabled;
            public readonly Vector3 Color;
            public readonly Vector4 Params;

            private DistanceFogSettings(bool enabled, Vector3 color, Vector4 parameters)
            {
                Enabled = enabled;
                Color = color;
                Params = parameters;
            }

            public static DistanceFogSettings LoadOrThrow(string path, bool requiredWhenEnabled)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new ArgumentException("Distance fog path is required.", nameof(path));
                }

                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"{nameof(RaylibFrameLighting)} distance fog is enabled for lit terrain/mesh channels but config is missing at '{path}'.",
                        path);
                }

                string json = File.ReadAllText(path);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                bool enabled = true;
                if (root.TryGetProperty("enabled", out JsonElement enabledElement))
                {
                    if (enabledElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        throw new InvalidOperationException($"Distance fog '{path}' 'enabled' must be a boolean.");
                    }

                    enabled = enabledElement.GetBoolean();
                }

                if (!enabled)
                {
                    return new DistanceFogSettings(
                        enabled: false,
                        color: new Vector3(0.62f, 0.72f, 0.85f),
                        parameters: Vector4.Zero);
                }

                if (requiredWhenEnabled &&
                    (!root.TryGetProperty("density", out _) ||
                     !root.TryGetProperty("start", out _) ||
                     !root.TryGetProperty("end", out _) ||
                     !root.TryGetProperty("color", out _)))
                {
                    throw new InvalidOperationException(
                        $"Distance fog '{path}' is enabled but missing required fields density/start/end/color.");
                }

                float density = ReadRequiredFogFloat(root, "density", path);
                float start = ReadRequiredFogFloat(root, "start", path);
                float end = ReadRequiredFogFloat(root, "end", path);
                if (density < 0f)
                {
                    throw new InvalidOperationException($"Distance fog '{path}' density must be >= 0.");
                }

                if (end <= start)
                {
                    throw new InvalidOperationException($"Distance fog '{path}' end must be > start.");
                }

                if (!root.TryGetProperty("color", out JsonElement colorElement) ||
                    colorElement.ValueKind != JsonValueKind.Array ||
                    colorElement.GetArrayLength() != 3)
                {
                    throw new InvalidOperationException($"Distance fog '{path}' color must be [r,g,b].");
                }

                Vector3 color = new(
                    colorElement[0].GetSingle(),
                    colorElement[1].GetSingle(),
                    colorElement[2].GetSingle());
                return new DistanceFogSettings(
                    enabled: true,
                    color,
                    parameters: new Vector4(density, start, end, 1f));
            }

            private static float ReadRequiredFogFloat(JsonElement root, string name, string path)
            {
                if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidOperationException(
                        $"Distance fog '{path}' is missing numeric '{name}'.");
                }

                return value.GetSingle();
            }
        }
    }
}
