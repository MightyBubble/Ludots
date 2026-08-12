using System;
using System.Numerics;
using Ludots.Core.Presentation.Assets;

namespace Ludots.Core.Presentation.Particles
{
    public readonly ref struct ParticleSystemSnapshot
    {
        public ParticleSystemSnapshot(
            ReadOnlySpan<Vector3> positions,
            ReadOnlySpan<Vector3> velocities,
            ReadOnlySpan<float> sizes,
            ReadOnlySpan<float> ages,
            ReadOnlySpan<float> lives,
            ReadOnlySpan<Vector4> colors,
            ReadOnlySpan<int> frameIndices,
            int rejectedSpawnCount)
        {
            Positions = positions;
            Velocities = velocities;
            Sizes = sizes;
            Ages = ages;
            Lives = lives;
            Colors = colors;
            FrameIndices = frameIndices;
            RejectedSpawnCount = rejectedSpawnCount;
        }

        public ReadOnlySpan<Vector3> Positions { get; }

        public ReadOnlySpan<Vector3> Velocities { get; }

        public ReadOnlySpan<float> Sizes { get; }

        public ReadOnlySpan<float> Ages { get; }

        public ReadOnlySpan<float> Lives { get; }

        public ReadOnlySpan<Vector4> Colors { get; }

        public ReadOnlySpan<int> FrameIndices { get; }

        public int Count => Positions.Length;

        public int RejectedSpawnCount { get; }
    }

    public sealed class ParticleSystemRuntime
    {
        private readonly Vector3[] _positions;
        private readonly Vector3[] _velocities;
        private readonly float[] _startSizes;
        private readonly float[] _sizes;
        private readonly float[] _ages;
        private readonly float[] _lives;
        private readonly Vector4[] _colors;
        private readonly int[] _startFrames;
        private readonly int[] _frameIndices;

        private ParticleRandom _random;
        private float _elapsedSeconds;
        private float _emissionAccumulator;
        private bool _burstEmitted;
        private int _particleCount;

        public ParticleSystemRuntime(int capacity, uint seed)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (seed == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), "Particle runtimes require a non-zero deterministic seed.");
            }

            _positions = new Vector3[capacity];
            _velocities = new Vector3[capacity];
            _startSizes = new float[capacity];
            _sizes = new float[capacity];
            _ages = new float[capacity];
            _lives = new float[capacity];
            _colors = new Vector4[capacity];
            _startFrames = new int[capacity];
            _frameIndices = new int[capacity];
            _random = new ParticleRandom(seed);
        }

        public int Capacity => _positions.Length;

        public int ParticleCount => _particleCount;

        public int RejectedSpawnCount { get; private set; }

        public float ElapsedSeconds => _elapsedSeconds;

        public void Reset(uint seed)
        {
            if (seed == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed), "Particle runtimes require a non-zero deterministic seed.");
            }

            _random = new ParticleRandom(seed);
            _elapsedSeconds = 0f;
            _emissionAccumulator = 0f;
            _burstEmitted = false;
            _particleCount = 0;
            RejectedSpawnCount = 0;
        }

        public void Update(
            ParticleVfxAssetData effect,
            float deltaSeconds,
            in Vector3 emitterPosition,
            in Quaternion emitterRotation)
        {
            if (effect == null)
            {
                throw new ArgumentNullException(nameof(effect));
            }

            if (!effect.IsValid)
            {
                throw new InvalidOperationException("Particle runtime requires a valid particle VFX asset.");
            }

            if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            if (Capacity < effect.MaxParticles)
            {
                throw new InvalidOperationException(
                    $"Particle runtime capacity {Capacity} is smaller than authored maxParticles {effect.MaxParticles}.");
            }

            float previousTime = _elapsedSeconds;
            if (effect.SpawnMode == PrefabVfxSpawnMode.Loop)
            {
                _elapsedSeconds += deltaSeconds;
                if (_elapsedSeconds >= effect.DurationSeconds)
                {
                    _elapsedSeconds %= effect.DurationSeconds;
                    _burstEmitted = false;
                }
            }
            else
            {
                _elapsedSeconds = MathF.Min(effect.DurationSeconds, _elapsedSeconds + deltaSeconds);
            }

            if (!_burstEmitted)
            {
                Spawn(effect, effect.BurstCount, in emitterPosition, in emitterRotation);
                _burstEmitted = true;
            }

            float emissionDelta = effect.SpawnMode == PrefabVfxSpawnMode.Once
                ? MathF.Max(0f, _elapsedSeconds - previousTime)
                : deltaSeconds;
            if (effect.SpawnMode == PrefabVfxSpawnMode.Once && previousTime >= effect.DurationSeconds)
            {
                emissionDelta = 0f;
            }

            _emissionAccumulator += effect.EmissionRatePerSecond * emissionDelta;
            int emitCount = (int)MathF.Floor(_emissionAccumulator);
            if (emitCount > 0)
            {
                _emissionAccumulator -= emitCount;
                Spawn(effect, emitCount, in emitterPosition, in emitterRotation);
            }

            Simulate(effect, deltaSeconds);
        }

        public ParticleSystemSnapshot GetSnapshot()
        {
            return new ParticleSystemSnapshot(
                _positions.AsSpan(0, _particleCount),
                _velocities.AsSpan(0, _particleCount),
                _sizes.AsSpan(0, _particleCount),
                _ages.AsSpan(0, _particleCount),
                _lives.AsSpan(0, _particleCount),
                _colors.AsSpan(0, _particleCount),
                _frameIndices.AsSpan(0, _particleCount),
                RejectedSpawnCount);
        }

        private void Spawn(
            ParticleVfxAssetData effect,
            int count,
            in Vector3 emitterPosition,
            in Quaternion emitterRotation)
        {
            for (int i = 0; i < count; i++)
            {
                if (_particleCount >= effect.MaxParticles || _particleCount >= Capacity)
                {
                    RejectedSpawnCount++;
                    continue;
                }

                Vector3 localPosition;
                Vector3 localDirection;
                BuildEmitterSample(effect, out localPosition, out localDirection);

                float life = effect.StartLife.Sample(ref _random);
                if (life <= 0f)
                {
                    throw new InvalidOperationException(
                        "Particle startLife samples must stay positive; reject non-positive authored ranges at asset load.");
                }
                float size = effect.StartSize.Sample(ref _random);
                float speed = effect.StartSpeed.Sample(ref _random);
                Vector3 position = localPosition;
                Vector3 velocity = localDirection * speed;
                if (effect.WorldSpace)
                {
                    position = emitterPosition + Vector3.Transform(localPosition, emitterRotation);
                    velocity = Vector3.TransformNormal(velocity, Matrix4x4.CreateFromQuaternion(emitterRotation));
                }

                int slot = _particleCount++;
                _positions[slot] = position;
                _velocities[slot] = velocity;
                _startSizes[slot] = size;
                _sizes[slot] = size * effect.SizeOverLife.Evaluate(0f);
                _ages[slot] = 0f;
                _lives[slot] = life;
                _colors[slot] = MultiplyColor(effect.StartColor, effect.ColorOverLife.Evaluate(0f));
                if (effect.TextureSheet != null)
                {
                    _startFrames[slot] = effect.TextureSheet.SampleStartFrame(ref _random);
                    _frameIndices[slot] = effect.TextureSheet.EvaluateFrame(_startFrames[slot], 0f);
                }
                else
                {
                    _startFrames[slot] = 0;
                    _frameIndices[slot] = 0;
                }
            }
        }

        private void Simulate(ParticleVfxAssetData effect, float deltaSeconds)
        {
            int index = 0;
            while (index < _particleCount)
            {
                float age = _ages[index] + deltaSeconds;
                if (age >= _lives[index])
                {
                    int last = _particleCount - 1;
                    _positions[index] = _positions[last];
                    _velocities[index] = _velocities[last];
                    _startSizes[index] = _startSizes[last];
                    _sizes[index] = _sizes[last];
                    _ages[index] = _ages[last];
                    _lives[index] = _lives[last];
                    _colors[index] = _colors[last];
                    _startFrames[index] = _startFrames[last];
                    _frameIndices[index] = _frameIndices[last];
                    _particleCount--;
                    continue;
                }

                Vector3 velocity = _velocities[index] + (effect.Gravity * deltaSeconds);
                if (effect.Drag > 0f && deltaSeconds > 0f)
                {
                    velocity *= MathF.Exp(-effect.Drag * deltaSeconds);
                }

                _velocities[index] = velocity;
                _positions[index] += velocity * deltaSeconds;
                _ages[index] = age;
                float life01 = Math.Clamp(age / _lives[index], 0f, 1f);
                _sizes[index] = _startSizes[index] * effect.SizeOverLife.Evaluate(life01);
                _colors[index] = MultiplyColor(effect.StartColor, effect.ColorOverLife.Evaluate(life01));
                if (effect.TextureSheet != null)
                {
                    _frameIndices[index] = effect.TextureSheet.EvaluateFrame(_startFrames[index], age);
                }

                index++;
            }
        }

        private void BuildEmitterSample(
            ParticleVfxAssetData effect,
            out Vector3 position,
            out Vector3 direction)
        {
            switch (effect.EmitterShape)
            {
                case ParticleEmitterShapeKind.Point:
                    position = Vector3.Zero;
                    direction = _random.NextUnitVector();
                    return;
                case ParticleEmitterShapeKind.Circle:
                {
                    float angle = _random.NextFloat() * MathF.Tau;
                    float radius = effect.ShapeRadius * MathF.Sqrt(_random.NextFloat());
                    position = new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
                    direction = Vector3.UnitY;
                    return;
                }
                case ParticleEmitterShapeKind.Cone:
                {
                    float angle = _random.NextFloat() * MathF.Tau;
                    float radius = effect.ShapeRadius * MathF.Sqrt(_random.NextFloat());
                    float coneAngle = effect.ShapeAngleRadians * _random.NextFloat();
                    Vector3 radial = new(MathF.Cos(angle), 0f, MathF.Sin(angle));
                    position = radial * radius * effect.ShapeThickness;
                    direction = Vector3.Normalize(
                        (Vector3.UnitY * MathF.Cos(coneAngle)) +
                        (radial * MathF.Sin(coneAngle)));
                    return;
                }
                case ParticleEmitterShapeKind.Sphere:
                case ParticleEmitterShapeKind.Hemisphere:
                {
                    direction = _random.NextUnitVector();
                    if (effect.EmitterShape == ParticleEmitterShapeKind.Hemisphere && direction.Y < 0f)
                    {
                        direction.Y = -direction.Y;
                    }

                    float radius = effect.ShapeRadius * Lerp(1f - effect.ShapeThickness, 1f, _random.NextFloat());
                    position = direction * radius;
                    return;
                }
                default:
                    throw new InvalidOperationException(
                        $"Particle emitter shape '{effect.EmitterShape}' is not supported.");
            }
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        private static Vector4 MultiplyColor(in Vector4 left, in Vector4 right)
        {
            return new Vector4(
                Math.Clamp(left.X * right.X, 0f, 1f),
                Math.Clamp(left.Y * right.Y, 0f, 1f),
                Math.Clamp(left.Z * right.Z, 0f, 1f),
                Math.Clamp(left.W * right.W, 0f, 1f));
        }
    }
}
