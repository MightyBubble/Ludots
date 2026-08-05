using System;
using System.Numerics;

namespace Ludots.Core.Presentation.Assets
{
    public enum VfxEmitterShape : byte
    {
        None = 0,
        BillboardSprite = 1,
        PrimitiveCube = 2,
        PrimitiveSphere = 3,
    }

    public readonly struct VfxEmitterDescriptor
    {
        public VfxEmitterDescriptor(
            VfxEmitterShape shape,
            int particleCount,
            int ringSegments,
            float radiusScale,
            float coreRadiusScale,
            float particleRadiusScale,
            float lifetimeSeconds,
            float pulseSpeedRadPerSecond,
            float orbitSpeedRadPerSecond,
            int shellRingCount,
            int beamCount)
        {
            if (shape == VfxEmitterShape.None)
            {
                throw new ArgumentOutOfRangeException(nameof(shape));
            }

            if (particleCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(particleCount));
            }

            if (ringSegments < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(ringSegments));
            }

            ValidatePositive(radiusScale, nameof(radiusScale));
            ValidatePositive(coreRadiusScale, nameof(coreRadiusScale));
            ValidatePositive(particleRadiusScale, nameof(particleRadiusScale));
            ValidatePositive(lifetimeSeconds, nameof(lifetimeSeconds));
            ValidateNonNegative(pulseSpeedRadPerSecond, nameof(pulseSpeedRadPerSecond));
            ValidateNonNegative(orbitSpeedRadPerSecond, nameof(orbitSpeedRadPerSecond));
            if (shellRingCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(shellRingCount));
            }

            if (beamCount < 0 || beamCount > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(beamCount));
            }

            Shape = shape;
            ParticleCount = particleCount;
            RingSegments = ringSegments;
            RadiusScale = radiusScale;
            CoreRadiusScale = coreRadiusScale;
            ParticleRadiusScale = particleRadiusScale;
            LifetimeSeconds = lifetimeSeconds;
            PulseSpeedRadPerSecond = pulseSpeedRadPerSecond;
            OrbitSpeedRadPerSecond = orbitSpeedRadPerSecond;
            ShellRingCount = shellRingCount;
            BeamCount = beamCount;
        }

        public VfxEmitterShape Shape { get; }

        public int ParticleCount { get; }

        public int RingSegments { get; }

        public float RadiusScale { get; }

        public float CoreRadiusScale { get; }

        public float ParticleRadiusScale { get; }

        public float LifetimeSeconds { get; }

        public float PulseSpeedRadPerSecond { get; }

        public float OrbitSpeedRadPerSecond { get; }

        public int ShellRingCount { get; }

        public int BeamCount { get; }

        public bool IsValid =>
            Shape != VfxEmitterShape.None &&
            ParticleCount > 0 &&
            RingSegments >= 3 &&
            RadiusScale > 0f &&
            CoreRadiusScale > 0f &&
            ParticleRadiusScale > 0f &&
            LifetimeSeconds > 0f &&
            PulseSpeedRadPerSecond >= 0f &&
            OrbitSpeedRadPerSecond >= 0f &&
            ShellRingCount >= 0 &&
            BeamCount is >= 0 and <= 3;

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
    }

    public readonly struct VfxEffectAssetData
    {
        public VfxEffectAssetData(
            in VfxEmitterDescriptor emitter,
            PrefabVfxSpawnMode spawnMode,
            in Vector4 coreColor,
            in Vector4 shellColor,
            in Vector4 particleColor)
        {
            if (!emitter.IsValid)
            {
                throw new ArgumentException("VFX effect assets require a valid emitter descriptor.", nameof(emitter));
            }

            if (!Enum.IsDefined(typeof(PrefabVfxSpawnMode), spawnMode))
            {
                throw new ArgumentOutOfRangeException(nameof(spawnMode));
            }

            ValidateColor(in coreColor, nameof(coreColor));
            ValidateColor(in shellColor, nameof(shellColor));
            ValidateColor(in particleColor, nameof(particleColor));

            Emitter = emitter;
            SpawnMode = spawnMode;
            CoreColor = coreColor;
            ShellColor = shellColor;
            ParticleColor = particleColor;
        }

        public VfxEmitterDescriptor Emitter { get; }

        public PrefabVfxSpawnMode SpawnMode { get; }

        public Vector4 CoreColor { get; }

        public Vector4 ShellColor { get; }

        public Vector4 ParticleColor { get; }

        public bool IsValid =>
            Emitter.IsValid &&
            Enum.IsDefined(typeof(PrefabVfxSpawnMode), SpawnMode) &&
            IsValidColor(CoreColor) &&
            IsValidColor(ShellColor) &&
            IsValidColor(ParticleColor);

        private static void ValidateColor(in Vector4 value, string name)
        {
            if (!IsValidColor(value))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static bool IsValidColor(Vector4 value)
        {
            return IsUnit(value.X) &&
                   IsUnit(value.Y) &&
                   IsUnit(value.Z) &&
                   IsUnit(value.W);
        }

        private static bool IsUnit(float value)
        {
            return float.IsFinite(value) && value >= 0f && value <= 1f;
        }
    }
}
