using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Particles;

namespace Ludots.Core.Presentation.Config
{
    public static class ParticleEffectConfigParser
    {
        public const string CurrentVersion = "quarks.ludots.v1";

        public static ParticleEffectAssetData ParseCatalogEntry(JsonNode? node, string key, string sourceLabel)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException($"{sourceLabel} asset '{key}' must be an object.");
            }

            ValidateObjectFields(
                obj,
                $"{sourceLabel} asset '{key}'",
                "id",
                "version",
                "spawnMode",
                "shape",
                "renderMode",
                "primitive",
                "overflowPolicy",
                "maxParticles",
                "seed",
                "durationSeconds",
                "emissionRatePerSecond",
                "burstCount",
                "shapeRadius",
                "shapeAngleRadians",
                "shapeThickness",
                "startLife",
                "startSpeed",
                "startSize",
                "startColor",
                "sizeOverLife",
                "colorOverLife",
                "gravity",
                "drag",
                "worldSpace");

            string version = ReadRequiredString(obj["version"], $"{sourceLabel} asset '{key}'.version");
            if (!string.Equals(version, CurrentVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{sourceLabel} asset '{key}'.version has unsupported value '{version}'. Expected '{CurrentVersion}'.");
            }

            PrefabVfxSpawnMode spawnMode = ReadRequiredEnum<PrefabVfxSpawnMode>(
                obj["spawnMode"],
                $"{sourceLabel} asset '{key}'.spawnMode");
            return ParseObject(obj, $"{sourceLabel} asset '{key}'", spawnMode);
        }

        private static ParticleEffectAssetData ParseObject(JsonObject obj, string label, PrefabVfxSpawnMode spawnMode)
        {
            return new ParticleEffectAssetData(
                spawnMode,
                ReadRequiredEnum<ParticleEmitterShapeKind>(obj["shape"], $"{label}.shape"),
                ReadRequiredEnum<ParticleRenderMode>(obj["renderMode"], $"{label}.renderMode"),
                ReadRequiredEnum<ParticlePrimitiveKind>(obj["primitive"], $"{label}.primitive"),
                ReadRequiredEnum<ParticleOverflowPolicy>(obj["overflowPolicy"], $"{label}.overflowPolicy"),
                ReadRequiredPositiveInt(obj["maxParticles"], $"{label}.maxParticles"),
                ReadRequiredSeed(obj["seed"], $"{label}.seed"),
                ReadRequiredPositiveFloat(obj["durationSeconds"], $"{label}.durationSeconds"),
                ReadRequiredNonNegativeFloat(obj["emissionRatePerSecond"], $"{label}.emissionRatePerSecond"),
                ReadRequiredMinInt(obj["burstCount"], 0, $"{label}.burstCount"),
                ReadRequiredNonNegativeFloat(obj["shapeRadius"], $"{label}.shapeRadius"),
                ReadRequiredAngle(obj["shapeAngleRadians"], $"{label}.shapeAngleRadians"),
                ReadRequiredNormalizedFloat(obj["shapeThickness"], $"{label}.shapeThickness"),
                ReadRequiredParticleRange(obj["startLife"], $"{label}.startLife"),
                ReadRequiredParticleRange(obj["startSpeed"], $"{label}.startSpeed"),
                ReadRequiredParticleRange(obj["startSize"], $"{label}.startSize"),
                ReadRequiredColor(obj["startColor"], $"{label}.startColor"),
                ReadRequiredParticleCurve(obj["sizeOverLife"], $"{label}.sizeOverLife"),
                ReadRequiredParticleGradient(obj["colorOverLife"], $"{label}.colorOverLife"),
                ReadRequiredVector3(obj["gravity"], $"{label}.gravity"),
                ReadRequiredNonNegativeFloat(obj["drag"], $"{label}.drag"),
                ReadRequiredBool(obj["worldSpace"], $"{label}.worldSpace"));
        }

        private static string ReadRequiredString(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out string? value))
            {
                throw new InvalidOperationException($"{label} must be a non-empty string.");
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{label} must be a non-empty string.");
            }

            if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} must not include leading or trailing whitespace.");
            }

            return value;
        }

        private static T ReadRequiredEnum<T>(JsonNode? node, string label)
            where T : struct, Enum
        {
            string value = ReadRequiredString(node, label);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new InvalidOperationException($"{label} has invalid value '{value}'. Use the enum name, not a numeric string.");
            }

            if (!Enum.TryParse(value, ignoreCase: false, out T parsed) ||
                !Enum.IsDefined(typeof(T), parsed))
            {
                throw new InvalidOperationException($"{label} has invalid value '{value}'.");
            }

            return parsed;
        }

        private static int ReadRequiredPositiveInt(JsonNode? node, string label)
        {
            return ReadRequiredMinInt(node, 1, label);
        }

        private static int ReadRequiredMinInt(JsonNode? node, int min, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out int value))
            {
                throw new InvalidOperationException($"{label} must be an integer greater than or equal to {min}.");
            }

            if (value < min)
            {
                throw new InvalidOperationException($"{label} must be greater than or equal to {min}.");
            }

            return value;
        }

        private static float ReadRequiredPositiveFloat(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out float value))
            {
                throw new InvalidOperationException($"{label} must be a finite number greater than 0.");
            }

            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new InvalidOperationException($"{label} must be a finite number greater than 0.");
            }

            return value;
        }

        private static float ReadRequiredNonNegativeFloat(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out float value))
            {
                throw new InvalidOperationException($"{label} must be a finite number greater than or equal to 0.");
            }

            if (!float.IsFinite(value) || value < 0f)
            {
                throw new InvalidOperationException($"{label} must be a finite number greater than or equal to 0.");
            }

            return value;
        }

        private static uint ReadRequiredSeed(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out uint value) || value == 0)
            {
                throw new InvalidOperationException($"{label} must be a non-zero unsigned integer.");
            }

            return value;
        }

        private static float ReadRequiredAngle(JsonNode? node, string label)
        {
            float value = ReadRequiredNonNegativeFloat(node, label);
            if (value > MathF.PI)
            {
                throw new InvalidOperationException($"{label} must be between 0 and PI.");
            }

            return value;
        }

        private static float ReadRequiredNormalizedFloat(JsonNode? node, string label)
        {
            float value = ReadRequiredNonNegativeFloat(node, label);
            if (value > 1f)
            {
                throw new InvalidOperationException($"{label} must be between 0 and 1.");
            }

            return value;
        }

        private static bool ReadRequiredBool(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out bool value))
            {
                throw new InvalidOperationException($"{label} must be a boolean.");
            }

            return value;
        }

        private static Vector3 ReadRequiredVector3(JsonNode? node, string label)
        {
            if (node is not JsonArray arr || arr.Count != 3)
            {
                throw new InvalidOperationException($"{label} must be an array of exactly three finite numbers.");
            }

            return new Vector3(
                ReadRequiredFiniteFloat(arr[0], $"{label}[0]"),
                ReadRequiredFiniteFloat(arr[1], $"{label}[1]"),
                ReadRequiredFiniteFloat(arr[2], $"{label}[2]"));
        }

        private static Vector4 ReadRequiredColor(JsonNode? node, string label)
        {
            if (node is not JsonArray arr || arr.Count != 4)
            {
                throw new InvalidOperationException($"{label} must be an array of exactly four normalized numbers.");
            }

            return new Vector4(
                ReadColorChannel(arr[0], $"{label}[0]"),
                ReadColorChannel(arr[1], $"{label}[1]"),
                ReadColorChannel(arr[2], $"{label}[2]"),
                ReadColorChannel(arr[3], $"{label}[3]"));
        }

        private static float ReadColorChannel(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out float value))
            {
                throw new InvalidOperationException($"{label} must be a finite number between 0 and 1.");
            }

            if (!float.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new InvalidOperationException($"{label} must be between 0 and 1.");
            }

            return value;
        }

        private static ParticleValueRange ReadRequiredParticleRange(JsonNode? node, string label)
        {
            if (node is not JsonArray arr || arr.Count != 2)
            {
                throw new InvalidOperationException($"{label} must be an array [min, max].");
            }

            float min = ReadRequiredNonNegativeFloat(arr[0], $"{label}[0]");
            float max = ReadRequiredNonNegativeFloat(arr[1], $"{label}[1]");
            return new ParticleValueRange(min, max);
        }

        private static ParticleScalarCurve ReadRequiredParticleCurve(JsonNode? node, string label)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                throw new InvalidOperationException($"{label} must contain at least one key.");
            }

            var keys = new ParticleCurveKey[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject key)
                {
                    throw new InvalidOperationException($"{label}[{i}] must be an object.");
                }

                ValidateObjectFields(key, $"{label}[{i}]", "position", "value");
                keys[i] = new ParticleCurveKey(
                    ReadRequiredNormalizedFloat(key["position"], $"{label}[{i}].position"),
                    ReadRequiredNonNegativeFloat(key["value"], $"{label}[{i}].value"));
            }

            return new ParticleScalarCurve(keys);
        }

        private static ParticleColorGradient ReadRequiredParticleGradient(JsonNode? node, string label)
        {
            if (node is not JsonArray arr || arr.Count == 0)
            {
                throw new InvalidOperationException($"{label} must contain at least one key.");
            }

            var keys = new ParticleColorKey[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject key)
                {
                    throw new InvalidOperationException($"{label}[{i}] must be an object.");
                }

                ValidateObjectFields(key, $"{label}[{i}]", "position", "color");
                keys[i] = new ParticleColorKey(
                    ReadRequiredNormalizedFloat(key["position"], $"{label}[{i}].position"),
                    ReadRequiredColor(key["color"], $"{label}[{i}].color"));
            }

            return new ParticleColorGradient(keys);
        }

        private static float ReadRequiredFiniteFloat(JsonNode? node, string label)
        {
            if (node is not JsonValue valueNode || !valueNode.TryGetValue(out float value) || !float.IsFinite(value))
            {
                throw new InvalidOperationException($"{label} must be a finite number.");
            }

            return value;
        }

        private static void ValidateObjectFields(JsonObject obj, string context, params string[] allowedFields)
        {
            foreach (var property in obj)
            {
                bool allowed = false;
                for (int i = 0; i < allowedFields.Length; i++)
                {
                    if (string.Equals(property.Key, allowedFields[i], StringComparison.Ordinal))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    throw new InvalidOperationException($"{context} uses unsupported field '{property.Key}'.");
                }
            }
        }
    }
}
