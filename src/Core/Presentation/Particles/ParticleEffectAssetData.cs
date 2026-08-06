using System;
using System.Numerics;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Particles
{
    public enum ParticleEmitterShapeKind : byte
    {
        Point = 0,
        Circle = 1,
        Cone = 2,
        Sphere = 3,
        Hemisphere = 4,
    }

    public enum ParticleRenderMode : byte
    {
        Billboard = 0,
        StretchedBillboard = 1,
        Mesh = 2,
        Trail = 3,
    }

    public enum ParticlePrimitiveKind : byte
    {
        Sphere = 0,
        Cube = 1,
    }

    public enum ParticleOverflowPolicy : byte
    {
        DropNewest = 0,
    }

    public readonly struct ParticleValueRange
    {
        public ParticleValueRange(float min, float max)
        {
            if (!float.IsFinite(min) || !float.IsFinite(max) || min < 0f || max < min)
            {
                throw new ArgumentOutOfRangeException(nameof(min), "Particle value ranges require finite values with 0 <= min <= max.");
            }

            Min = min;
            Max = max;
        }

        public float Min { get; }

        public float Max { get; }

        public float Sample(ref ParticleRandom random)
        {
            return Min + ((Max - Min) * random.NextFloat());
        }
    }

    public readonly struct ParticleCurveKey
    {
        public ParticleCurveKey(float position, float value)
        {
            if (!float.IsFinite(position) || position < 0f || position > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Position = position;
            Value = value;
        }

        public float Position { get; }

        public float Value { get; }
    }

    public sealed class ParticleScalarCurve
    {
        private readonly ParticleCurveKey[] _keys;

        public ParticleScalarCurve(ParticleCurveKey[] keys)
        {
            if (keys == null || keys.Length == 0)
            {
                throw new ArgumentException("Particle curves require at least one key.", nameof(keys));
            }

            for (int i = 1; i < keys.Length; i++)
            {
                if (keys[i].Position < keys[i - 1].Position)
                {
                    throw new ArgumentException("Particle curve keys must be sorted by position.", nameof(keys));
                }
            }

            _keys = (ParticleCurveKey[])keys.Clone();
        }

        public ReadOnlySpan<ParticleCurveKey> Keys => _keys;

        public float Evaluate(float position)
        {
            float t = Math.Clamp(position, 0f, 1f);
            if (_keys.Length == 1 || t <= _keys[0].Position)
            {
                return _keys[0].Value;
            }

            for (int i = 1; i < _keys.Length; i++)
            {
                ParticleCurveKey right = _keys[i];
                if (t <= right.Position)
                {
                    ParticleCurveKey left = _keys[i - 1];
                    float span = right.Position - left.Position;
                    if (span <= 0f)
                    {
                        return right.Value;
                    }

                    float localT = (t - left.Position) / span;
                    return left.Value + ((right.Value - left.Value) * localT);
                }
            }

            return _keys[^1].Value;
        }
    }

    public readonly struct ParticleColorKey
    {
        public ParticleColorKey(float position, in Vector4 color)
        {
            if (!float.IsFinite(position) || position < 0f || position > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (!IsNormalizedColor(in color))
            {
                throw new ArgumentOutOfRangeException(nameof(color));
            }

            Position = position;
            Color = color;
        }

        public float Position { get; }

        public Vector4 Color { get; }

        private static bool IsNormalizedColor(in Vector4 color)
        {
            return IsUnit(color.X) && IsUnit(color.Y) && IsUnit(color.Z) && IsUnit(color.W);
        }

        private static bool IsUnit(float value)
        {
            return float.IsFinite(value) && value >= 0f && value <= 1f;
        }
    }

    public sealed class ParticleColorGradient
    {
        private readonly ParticleColorKey[] _keys;

        public ParticleColorGradient(ParticleColorKey[] keys)
        {
            if (keys == null || keys.Length == 0)
            {
                throw new ArgumentException("Particle color gradients require at least one key.", nameof(keys));
            }

            for (int i = 1; i < keys.Length; i++)
            {
                if (keys[i].Position < keys[i - 1].Position)
                {
                    throw new ArgumentException("Particle color gradient keys must be sorted by position.", nameof(keys));
                }
            }

            _keys = (ParticleColorKey[])keys.Clone();
        }

        public ReadOnlySpan<ParticleColorKey> Keys => _keys;

        public Vector4 Evaluate(float position)
        {
            float t = Math.Clamp(position, 0f, 1f);
            if (_keys.Length == 1 || t <= _keys[0].Position)
            {
                return _keys[0].Color;
            }

            for (int i = 1; i < _keys.Length; i++)
            {
                ParticleColorKey right = _keys[i];
                if (t <= right.Position)
                {
                    ParticleColorKey left = _keys[i - 1];
                    float span = right.Position - left.Position;
                    if (span <= 0f)
                    {
                        return right.Color;
                    }

                    float localT = (t - left.Position) / span;
                    return Vector4.Lerp(left.Color, right.Color, localT);
                }
            }

            return _keys[^1].Color;
        }
    }

    public sealed class ParticleEffectAssetData
    {
        public ParticleEffectAssetData(
            PrefabVfxSpawnMode spawnMode,
            ParticleEmitterShapeKind emitterShape,
            ParticleRenderMode renderMode,
            ParticlePrimitiveKind primitiveKind,
            ParticleOverflowPolicy overflowPolicy,
            int maxParticles,
            uint seed,
            float durationSeconds,
            float emissionRatePerSecond,
            int burstCount,
            float shapeRadius,
            float shapeAngleRadians,
            float shapeThickness,
            in ParticleValueRange startLife,
            in ParticleValueRange startSpeed,
            in ParticleValueRange startSize,
            in Vector4 startColor,
            ParticleScalarCurve sizeOverLife,
            ParticleColorGradient colorOverLife,
            in Vector3 gravity,
            float drag,
            bool worldSpace)
        {
            if (!Enum.IsDefined(spawnMode))
            {
                throw new ArgumentOutOfRangeException(nameof(spawnMode));
            }

            if (!Enum.IsDefined(emitterShape))
            {
                throw new ArgumentOutOfRangeException(nameof(emitterShape));
            }

            if (!Enum.IsDefined(renderMode))
            {
                throw new ArgumentOutOfRangeException(nameof(renderMode));
            }

            if (!Enum.IsDefined(primitiveKind))
            {
                throw new ArgumentOutOfRangeException(nameof(primitiveKind));
            }

            if (!Enum.IsDefined(overflowPolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(overflowPolicy));
            }

            if (maxParticles <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxParticles));
            }

            if (seed == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), "Particle effects require a non-zero deterministic seed.");
            }

            ValidatePositive(durationSeconds, nameof(durationSeconds));
            ValidateNonNegative(emissionRatePerSecond, nameof(emissionRatePerSecond));
            if (burstCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(burstCount));
            }

            ValidateNonNegative(shapeRadius, nameof(shapeRadius));
            ValidateAngle(shapeAngleRadians, nameof(shapeAngleRadians));
            if (!float.IsFinite(shapeThickness) || shapeThickness < 0f || shapeThickness > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(shapeThickness));
            }

            if (!IsNormalizedColor(in startColor))
            {
                throw new ArgumentOutOfRangeException(nameof(startColor));
            }

            if (!float.IsFinite(gravity.X) || !float.IsFinite(gravity.Y) || !float.IsFinite(gravity.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(gravity));
            }

            ValidateNonNegative(drag, nameof(drag));
            SizeOverLife = sizeOverLife ?? throw new ArgumentNullException(nameof(sizeOverLife));
            ColorOverLife = colorOverLife ?? throw new ArgumentNullException(nameof(colorOverLife));

            SpawnMode = spawnMode;
            EmitterShape = emitterShape;
            RenderMode = renderMode;
            PrimitiveKind = primitiveKind;
            OverflowPolicy = overflowPolicy;
            MaxParticles = maxParticles;
            Seed = seed;
            DurationSeconds = durationSeconds;
            EmissionRatePerSecond = emissionRatePerSecond;
            BurstCount = burstCount;
            ShapeRadius = shapeRadius;
            ShapeAngleRadians = shapeAngleRadians;
            ShapeThickness = shapeThickness;
            StartLife = startLife;
            StartSpeed = startSpeed;
            StartSize = startSize;
            StartColor = startColor;
            Gravity = gravity;
            Drag = drag;
            WorldSpace = worldSpace;
        }

        public PrefabVfxSpawnMode SpawnMode { get; }

        public ParticleEmitterShapeKind EmitterShape { get; }

        public ParticleRenderMode RenderMode { get; }

        public ParticlePrimitiveKind PrimitiveKind { get; }

        public ParticleOverflowPolicy OverflowPolicy { get; }

        public int MaxParticles { get; }

        public uint Seed { get; }

        public float DurationSeconds { get; }

        public float EmissionRatePerSecond { get; }

        public int BurstCount { get; }

        public float ShapeRadius { get; }

        public float ShapeAngleRadians { get; }

        public float ShapeThickness { get; }

        public ParticleValueRange StartLife { get; }

        public ParticleValueRange StartSpeed { get; }

        public ParticleValueRange StartSize { get; }

        public Vector4 StartColor { get; }

        public ParticleScalarCurve SizeOverLife { get; }

        public ParticleColorGradient ColorOverLife { get; }

        public Vector3 Gravity { get; }

        public float Drag { get; }

        public bool WorldSpace { get; }

        public bool IsValid =>
            MaxParticles > 0 &&
            Seed != 0 &&
            DurationSeconds > 0f &&
            EmissionRatePerSecond >= 0f &&
            BurstCount >= 0 &&
            SizeOverLife != null &&
            ColorOverLife != null;

        private static void ValidatePositive(float value, string name)
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidateNonNegative(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidateAngle(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f || value > MathF.PI)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static bool IsNormalizedColor(in Vector4 color)
        {
            return IsUnit(color.X) && IsUnit(color.Y) && IsUnit(color.Z) && IsUnit(color.W);
        }

        private static bool IsUnit(float value)
        {
            return float.IsFinite(value) && value >= 0f && value <= 1f;
        }
    }

    public struct ParticleRandom
    {
        private uint _state;

        public ParticleRandom(uint seed)
        {
            if (seed == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), "Particle random requires a non-zero deterministic seed.");
            }

            _state = seed;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public float NextFloat()
        {
            return (NextUInt() & 0x00FFFFFFu) / 16777216f;
        }

        public Vector3 NextUnitVector()
        {
            float z = (NextFloat() * 2f) - 1f;
            float angle = NextFloat() * MathF.Tau;
            float radial = MathF.Sqrt(MathF.Max(0f, 1f - (z * z)));
            return new Vector3(
                radial * MathF.Cos(angle),
                radial * MathF.Sin(angle),
                z);
        }
    }
}
