using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Particles;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    internal readonly record struct RaylibVfxEffectKey(int StableId, int EffectAssetId);

    public sealed class RaylibVfxRenderer
    {
        private readonly Dictionary<RaylibVfxEffectKey, RaylibParticleEffectInstance> _particleEffects = new();
        private readonly HashSet<RaylibVfxEffectKey> _activeKeys = new();
        private readonly List<RaylibVfxEffectKey> _inactiveKeys = new();

        public int LastDrawnEffectCount { get; private set; }

        public int TotalDrawnEffectCount { get; private set; }

        public void BeginFrame()
        {
            _activeKeys.Clear();
            LastDrawnEffectCount = 0;
        }

        public void Draw(in PrefabFinalizedVisual visual, MeshAssetRegistry effectAssets, double timeSeconds)
        {
            if (effectAssets == null)
            {
                throw new ArgumentNullException(nameof(effectAssets));
            }

            if (visual.Kind != PrefabVisualPartKind.Vfx)
            {
                throw new InvalidOperationException(
                    $"{nameof(RaylibVfxRenderer)} can only draw finalized VFX visuals, but received '{visual.Kind}'.");
            }

            if (visual.StableId <= 0)
            {
                throw new InvalidOperationException(
                    $"VFX visual effectAssetId={visual.EffectAssetId} requires a positive stableId for renderer lifetime tracking.");
            }

            if (!effectAssets.TryGetDescriptor(visual.EffectAssetId, out MeshAssetDescriptor descriptor))
            {
                throw new InvalidOperationException(
                    $"VFX visual stableId={visual.StableId} references unknown effect asset id {visual.EffectAssetId}.");
            }

            RaylibVfxEffectKey key = ComposeEffectKey(visual.StableId, visual.EffectAssetId);
            _activeKeys.Add(key);
            VfxEffectAssetData effect = descriptor.VfxEffectData;
            if (!effect.IsValid || effect.ParticleSystem is null)
            {
                throw new InvalidOperationException(
                    $"VFX effect asset id {visual.EffectAssetId} must reference a registered Quarks particle effect.");
            }

            RaylibParticleEffectInstance particleEffect = GetOrCreateParticleEffect(key, effect.ParticleSystem);
            particleEffect.Update(effect.ParticleSystem, timeSeconds, visual.Position, visual.Rotation);
            DrawParticleEffect(in visual, effect.ParticleSystem, particleEffect);
            LastDrawnEffectCount++;
            TotalDrawnEffectCount++;
        }

        public void EndFrame()
        {
            if (_particleEffects.Count == _activeKeys.Count)
            {
                return;
            }

            _inactiveKeys.Clear();
            foreach (RaylibVfxEffectKey key in _particleEffects.Keys)
            {
                if (!_activeKeys.Contains(key))
                {
                    _inactiveKeys.Add(key);
                }
            }

            for (int i = 0; i < _inactiveKeys.Count; i++)
            {
                _particleEffects.Remove(_inactiveKeys[i]);
            }
        }

        private RaylibParticleEffectInstance GetOrCreateParticleEffect(
            in RaylibVfxEffectKey key,
            ParticleEffectAssetData effect)
        {
            if (_particleEffects.TryGetValue(key, out RaylibParticleEffectInstance? existing))
            {
                return existing;
            }

            var created = new RaylibParticleEffectInstance(effect);
            _particleEffects.Add(key, created);
            return created;
        }

        private static void DrawParticleEffect(
            in PrefabFinalizedVisual visual,
            ParticleEffectAssetData effect,
            RaylibParticleEffectInstance particleEffect)
        {
            ParticleSystemSnapshot snapshot = particleEffect.Runtime.GetSnapshot();
            if (effect.RenderMode == ParticleRenderMode.Billboard)
            {
                throw new InvalidOperationException(
                    "Particle render mode 'Billboard' requires an authored texture asset and a billboard texture renderer.");
            }

            if (effect.RenderMode == ParticleRenderMode.StretchedBillboard)
            {
                throw new InvalidOperationException(
                    "Particle render mode 'StretchedBillboard' requires an authored texture asset and a billboard texture renderer.");
            }

            float visualScale = MathF.Max(
                0.01f,
                MathF.Max(MathF.Abs(visual.Scale.X), MathF.Max(MathF.Abs(visual.Scale.Y), MathF.Abs(visual.Scale.Z))));
            Quaternion rotation = WorldPlane2D.NormalizeOrIdentity(visual.Rotation);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Vector3 position = snapshot.Positions[i];
                Vector3 velocity = snapshot.Velocities[i];
                if (!effect.WorldSpace)
                {
                    position = visual.Position + Vector3.Transform(position, rotation);
                    velocity = Vector3.TransformNormal(velocity, Matrix4x4.CreateFromQuaternion(rotation));
                }

                Vector4 color = ModulateColor(snapshot.Colors[i], visual.Color);
                float size = MathF.Max(0.0025f, snapshot.Sizes[i] * visualScale);
                Color raylibColor = ToRaylibColor(color);
                if (effect.RenderMode == ParticleRenderMode.Trail)
                {
                    Vector3 previous = position - (velocity * 0.08f);
                    Rl.DrawLine3D(previous, position, raylibColor);
                    continue;
                }

                if (effect.PrimitiveKind == ParticlePrimitiveKind.Cube)
                {
                    Rl.DrawCube(position, size, size, size, raylibColor);
                }
                else
                {
                    Rl.DrawSphere(position, size * 0.5f, raylibColor);
                }
            }
        }

        internal static RaylibVfxEffectKey ComposeEffectKey(int stableId, int effectAssetId)
        {
            return new RaylibVfxEffectKey(stableId, effectAssetId);
        }

        private static Vector4 ModulateColor(Vector4 authored, Vector4 tint)
        {
            return new Vector4(
                authored.X * tint.X,
                authored.Y * tint.Y,
                authored.Z * tint.Z,
                authored.W * tint.W);
        }

        private static Color ToRaylibColor(in Vector4 color)
        {
            return RaylibColorUtil.ToRaylibColor(in color);
        }

        private sealed class RaylibParticleEffectInstance
        {
            private bool _hasLastTime;
            private double _lastTimeSeconds;

            public RaylibParticleEffectInstance(ParticleEffectAssetData effect)
            {
                Runtime = new ParticleSystemRuntime(effect.MaxParticles, effect.Seed);
            }

            public ParticleSystemRuntime Runtime { get; }

            public void Update(
                ParticleEffectAssetData effect,
                double timeSeconds,
                in Vector3 position,
                in Quaternion rotation)
            {
                if (!double.IsFinite(timeSeconds))
                {
                    throw new ArgumentOutOfRangeException(nameof(timeSeconds));
                }

                float deltaSeconds = _hasLastTime
                    ? checked((float)(timeSeconds - _lastTimeSeconds))
                    : 0f;
                if (deltaSeconds < 0f)
                {
                    throw new InvalidOperationException(
                        "Raylib particle effect time must be monotonic for a stable effect identity.");
                }

                Runtime.Update(effect, deltaSeconds, in position, in rotation);
                _lastTimeSeconds = timeSeconds;
                _hasLastTime = true;
            }
        }
    }
}
