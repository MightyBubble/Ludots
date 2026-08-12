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
        Primitive = 2,
        Trail = 3,
    }

    public enum ParticleBlendMode : byte
    {
        Alpha = 0,
        Additive = 1,
        PremultipliedAlpha = 2,
        Multiply = 3,
    }

    public enum ParticlePrimitiveKind : byte
    {
        Sphere = 0,
        Cube = 1,
    }

    public enum ParticleTextureSheetPlaybackMode : byte
    {
        Loop = 0,
        Clamp = 1,
    }

    public readonly struct ParticleIntRange
    {
        public ParticleIntRange(int min, int max)
        {
            if (min < 0 || max < min)
            {
                throw new ArgumentOutOfRangeException(nameof(min), "Particle integer ranges require 0 <= min <= max.");
            }

            Min = min;
            Max = max;
        }

        public int Min { get; }

        public int Max { get; }

        public int Sample(ref ParticleRandom random)
        {
            int span = Max - Min + 1;
            if (span <= 1)
            {
                return Min;
            }

            return Min + (int)MathF.Floor(random.NextFloat() * span);
        }
    }

    public sealed class ParticleTextureSheetAsset
    {
        public ParticleTextureSheetAsset(
            string textureAssetId,
            int columns,
            int rows,
            int frameCount,
            float framesPerSecond,
            in ParticleIntRange startFrame,
            ParticleTextureSheetPlaybackMode playbackMode)
        {
            if (string.IsNullOrWhiteSpace(textureAssetId) ||
                !string.Equals(textureAssetId, textureAssetId.Trim(), StringComparison.Ordinal))
            {
                throw new ArgumentException("Particle texture sheets require a non-empty canonical texture asset id.", nameof(textureAssetId));
            }

            if (columns <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(columns));
            }

            if (rows <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows));
            }

            int cellCapacity = checked(columns * rows);
            if (frameCount <= 0 || frameCount > cellCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            }

            if (!float.IsFinite(framesPerSecond) || framesPerSecond <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
            }

            if (startFrame.Max >= frameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(startFrame), "Particle texture sheet start frames must stay inside frameCount.");
            }

            if (!Enum.IsDefined(playbackMode))
            {
                throw new ArgumentOutOfRangeException(nameof(playbackMode));
            }

            TextureAssetId = textureAssetId;
            Columns = columns;
            Rows = rows;
            FrameCount = frameCount;
            FramesPerSecond = framesPerSecond;
            StartFrame = startFrame;
            PlaybackMode = playbackMode;
        }

        public string TextureAssetId { get; }

        public int Columns { get; }

        public int Rows { get; }

        public int FrameCount { get; }

        public float FramesPerSecond { get; }

        public ParticleIntRange StartFrame { get; }

        public ParticleTextureSheetPlaybackMode PlaybackMode { get; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(TextureAssetId) &&
            Columns > 0 &&
            Rows > 0 &&
            FrameCount > 0 &&
            FrameCount <= Columns * Rows &&
            FramesPerSecond > 0f &&
            StartFrame.Max < FrameCount &&
            Enum.IsDefined(typeof(ParticleTextureSheetPlaybackMode), PlaybackMode);

        public int SampleStartFrame(ref ParticleRandom random)
        {
            return StartFrame.Sample(ref random);
        }

        public int EvaluateFrame(int startFrame, float ageSeconds)
        {
            if (startFrame < 0 || startFrame >= FrameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(startFrame));
            }

            if (!float.IsFinite(ageSeconds) || ageSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(ageSeconds));
            }

            int offset = (int)MathF.Floor(ageSeconds * FramesPerSecond);
            int authoredFrame = startFrame + offset;
            return PlaybackMode == ParticleTextureSheetPlaybackMode.Loop
                ? authoredFrame % FrameCount
                : Math.Min(authoredFrame, FrameCount - 1);
        }
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
            ParticleBlendMode blendMode,
            ParticlePrimitiveKind primitiveKind,
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
            bool worldSpace,
            ParticleTextureSheetAsset? textureSheet,
            float stretchedLengthScale,
            float trailLengthSeconds)
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

            if (!Enum.IsDefined(blendMode))
            {
                throw new ArgumentOutOfRangeException(nameof(blendMode));
            }

            if (!Enum.IsDefined(primitiveKind))
            {
                throw new ArgumentOutOfRangeException(nameof(primitiveKind));
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
            if (startLife.Min <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(startLife), "Particle startLife requires min > 0.");
            }

            bool texturedRenderMode =
                renderMode == ParticleRenderMode.Billboard ||
                renderMode == ParticleRenderMode.StretchedBillboard;
            if (texturedRenderMode)
            {
                if (textureSheet == null || !textureSheet.IsValid)
                {
                    throw new ArgumentException("Billboard particle render modes require a valid texture sheet.", nameof(textureSheet));
                }
            }
            else if (textureSheet != null)
            {
                throw new ArgumentException("Only billboard particle render modes may declare a texture sheet.", nameof(textureSheet));
            }

            if (renderMode == ParticleRenderMode.StretchedBillboard)
            {
                ValidatePositive(stretchedLengthScale, nameof(stretchedLengthScale));
            }
            else if (stretchedLengthScale != 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(stretchedLengthScale), "stretchedLengthScale is only valid for StretchedBillboard particles.");
            }

            if (renderMode == ParticleRenderMode.Trail)
            {
                ValidatePositive(trailLengthSeconds, nameof(trailLengthSeconds));
            }
            else if (trailLengthSeconds != 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(trailLengthSeconds), "trailLengthSeconds is only valid for Trail particles.");
            }

            SizeOverLife = sizeOverLife ?? throw new ArgumentNullException(nameof(sizeOverLife));
            ColorOverLife = colorOverLife ?? throw new ArgumentNullException(nameof(colorOverLife));

            SpawnMode = spawnMode;
            EmitterShape = emitterShape;
            RenderMode = renderMode;
            BlendMode = blendMode;
            PrimitiveKind = primitiveKind;
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
            TextureSheet = textureSheet;
            StretchedLengthScale = stretchedLengthScale;
            TrailLengthSeconds = trailLengthSeconds;
        }

        public PrefabVfxSpawnMode SpawnMode { get; }

        public ParticleEmitterShapeKind EmitterShape { get; }

        public ParticleRenderMode RenderMode { get; }

        public ParticleBlendMode BlendMode { get; }

        public ParticlePrimitiveKind PrimitiveKind { get; }

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

        public ParticleTextureSheetAsset? TextureSheet { get; }

        public float StretchedLengthScale { get; }

        public float TrailLengthSeconds { get; }

        public bool IsValid =>
            MaxParticles > 0 &&
            Seed != 0 &&
            DurationSeconds > 0f &&
            EmissionRatePerSecond >= 0f &&
            BurstCount >= 0 &&
            StartLife.Min > 0f &&
            SizeOverLife != null &&
            ColorOverLife != null &&
            Enum.IsDefined(typeof(ParticleBlendMode), BlendMode) &&
            IsTextureContractValid() &&
            IsTrailContractValid();

        private bool IsTextureContractValid()
        {
            bool texturedRenderMode =
                RenderMode == ParticleRenderMode.Billboard ||
                RenderMode == ParticleRenderMode.StretchedBillboard;
            if (texturedRenderMode)
            {
                return TextureSheet is { IsValid: true } &&
                       (RenderMode != ParticleRenderMode.StretchedBillboard || StretchedLengthScale > 0f);
            }

            return TextureSheet == null && StretchedLengthScale == 0f;
        }

        private bool IsTrailContractValid()
        {
            if (RenderMode == ParticleRenderMode.Trail)
            {
                return TrailLengthSeconds > 0f;
            }

            return TrailLengthSeconds == 0f;
        }

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
