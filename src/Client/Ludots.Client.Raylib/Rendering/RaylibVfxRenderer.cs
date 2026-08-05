using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Assets;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Client.Raylib.Rendering
{
    public readonly record struct RaylibVfxEmitterPlan(
        int StableId,
        int EffectAssetId,
        VfxEmitterShape Shape,
        PrefabVfxSpawnMode SpawnMode,
        Vector3 Position,
        Quaternion Rotation,
        float AgeSeconds,
        float Life01,
        float ShellRadius,
        float CoreRadius,
        float ParticleRadius,
        int ParticleCount,
        int RingSegments,
        float OrbitPhase,
        Vector4 CoreColor,
        Vector4 ShellColor,
        Vector4 ParticleColor);

    internal readonly record struct RaylibVfxEffectKey(int StableId, int EffectAssetId);

    public sealed class RaylibVfxRenderer
    {
        private readonly Dictionary<RaylibVfxEffectKey, double> _effectStartSeconds = new();
        private readonly HashSet<RaylibVfxEffectKey> _activeKeys = new();
        private readonly List<RaylibVfxEffectKey> _inactiveKeys = new();

        public void BeginFrame()
        {
            _activeKeys.Clear();
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
            if (!_effectStartSeconds.TryGetValue(key, out double startSeconds))
            {
                startSeconds = timeSeconds;
                _effectStartSeconds.Add(key, startSeconds);
            }

            double ageSeconds = Math.Max(0d, timeSeconds - startSeconds);
            RaylibVfxEmitterPlan plan = BuildEmitterPlan(in visual, in descriptor, ageSeconds);
            if (plan.CoreColor.W <= 0.001f &&
                plan.ShellColor.W <= 0.001f &&
                plan.ParticleColor.W <= 0.001f)
            {
                return;
            }

            Draw(in plan);
        }

        public void EndFrame()
        {
            if (_effectStartSeconds.Count == _activeKeys.Count)
            {
                return;
            }

            _inactiveKeys.Clear();
            foreach (RaylibVfxEffectKey key in _effectStartSeconds.Keys)
            {
                if (!_activeKeys.Contains(key))
                {
                    _inactiveKeys.Add(key);
                }
            }

            for (int i = 0; i < _inactiveKeys.Count; i++)
            {
                _effectStartSeconds.Remove(_inactiveKeys[i]);
            }
        }

        public static RaylibVfxEmitterPlan BuildEmitterPlan(
            in PrefabFinalizedVisual visual,
            in MeshAssetDescriptor effectDescriptor,
            double ageSeconds)
        {
            if (visual.Kind != PrefabVisualPartKind.Vfx)
            {
                throw new InvalidOperationException(
                    $"Cannot build a VFX emitter plan for finalized visual kind '{visual.Kind}'.");
            }

            if (visual.StableId <= 0)
            {
                throw new InvalidOperationException("VFX emitter plans require a positive stableId.");
            }

            if (visual.EffectAssetId <= 0)
            {
                throw new InvalidOperationException("VFX emitter plans require a positive effectAssetId.");
            }

            if (!effectDescriptor.VfxEffectData.IsValid)
            {
                throw new InvalidOperationException(
                    $"VFX effect asset id {effectDescriptor.Id} must declare vfx emitter data.");
            }

            VfxEmitterDescriptor emitter = effectDescriptor.VfxEffectData.Emitter;
            float age = Math.Max(0f, (float)ageSeconds);
            float maxScale = MathF.Max(
                MathF.Abs(visual.Scale.X),
                MathF.Max(MathF.Abs(visual.Scale.Y), MathF.Abs(visual.Scale.Z)));
            float baseExtent = MathF.Max(0.12f, maxScale * 0.45f);
            float life01 = visual.VfxSpawnMode == PrefabVfxSpawnMode.Once
                ? Math.Clamp(age / emitter.LifetimeSeconds, 0f, 1f)
                : 0f;
            float pulse01 = visual.VfxSpawnMode == PrefabVfxSpawnMode.Loop
                ? (MathF.Sin(age * emitter.PulseSpeedRadPerSecond) * 0.5f) + 0.5f
                : 1f - life01;
            float alphaMultiplier = visual.VfxSpawnMode == PrefabVfxSpawnMode.Once
                ? 1f - life01
                : 0.72f + (pulse01 * 0.28f);

            Vector4 core = BlendSemanticColor(visual.Color, visual.EffectAssetId, 0.6f);
            Vector4 shell = LerpColor(core, new Vector4(1f, 1f, 1f, core.W), 0.35f);
            Vector4 particle = LerpColor(core, shell, 0.5f);
            core.W *= alphaMultiplier;
            shell.W *= alphaMultiplier * 0.72f;
            particle.W *= alphaMultiplier * 0.9f;

            float burstScale = visual.VfxSpawnMode == PrefabVfxSpawnMode.Once
                ? 0.85f + (life01 * 0.45f)
                : 0.92f + (pulse01 * 0.12f);
            float shellRadius = baseExtent * emitter.RadiusScale * burstScale;

            return new RaylibVfxEmitterPlan(
                visual.StableId,
                visual.EffectAssetId,
                emitter.Shape,
                visual.VfxSpawnMode,
                visual.Position,
                WorldPlane2D.NormalizeOrIdentity(visual.Rotation),
                age,
                life01,
                shellRadius,
                MathF.Max(0.025f, shellRadius * emitter.CoreRadiusScale),
                MathF.Max(0.012f, shellRadius * emitter.ParticleRadiusScale),
                emitter.ParticleCount,
                emitter.RingSegments,
                age * emitter.OrbitSpeedRadPerSecond,
                ClampColor(core),
                ClampColor(shell),
                ClampColor(particle));
        }

        private static void Draw(in RaylibVfxEmitterPlan plan)
        {
            DrawCore(in plan);
            DrawRotatedRing(plan.Position, plan.Rotation, plan.ShellRadius, plan.RingSegments, plan.ShellColor);
            DrawRotatedRing(
                plan.Position,
                plan.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * 0.5f),
                plan.ShellRadius * 0.82f,
                Math.Max(3, plan.RingSegments - 4),
                MultiplyColor(plan.ShellColor, 0.92f, 1f, 1.1f, 0.8f));

            Vector3 right = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, plan.Rotation));
            Vector3 up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, plan.Rotation));
            Vector3 forward = Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, plan.Rotation));
            Color beamColor = ToRaylibColor(MultiplyColor(plan.CoreColor, 1.1f, 1.08f, 1.18f, 0.82f));
            Rl.DrawLine3D(plan.Position - (right * plan.ShellRadius), plan.Position + (right * plan.ShellRadius), beamColor);
            Rl.DrawLine3D(plan.Position - (up * plan.ShellRadius * 0.8f), plan.Position + (up * plan.ShellRadius * 0.8f), beamColor);
            Rl.DrawLine3D(plan.Position - (forward * plan.ShellRadius * 0.9f), plan.Position + (forward * plan.ShellRadius * 0.9f), beamColor);

            DrawParticles(in plan);
        }

        private static void DrawCore(in RaylibVfxEmitterPlan plan)
        {
            Color color = ToRaylibColor(plan.CoreColor);
            switch (plan.Shape)
            {
                case VfxEmitterShape.BillboardSprite:
                    throw new InvalidOperationException(
                        "Raylib VFX emitter shape 'BillboardSprite' requires a billboard texture renderer. Author PrimitiveSphere or PrimitiveCube until that renderer exists.");
                case VfxEmitterShape.PrimitiveSphere:
                    Rl.DrawSphere(plan.Position, plan.CoreRadius, color);
                    break;
                case VfxEmitterShape.PrimitiveCube:
                    Vector3 size = new(plan.CoreRadius * 1.6f, plan.CoreRadius * 1.6f, plan.CoreRadius * 1.6f);
                    Rl.DrawCube(plan.Position, size.X, size.Y, size.Z, color);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Raylib VFX emitter shape '{plan.Shape}' is not supported.");
            }
        }

        private static void DrawParticles(in RaylibVfxEmitterPlan plan)
        {
            const float goldenAngle = 2.3999631f;
            for (int i = 0; i < plan.ParticleCount; i++)
            {
                float lane = i / (float)Math.Max(1, plan.ParticleCount - 1);
                float angle = (i * goldenAngle) + plan.OrbitPhase;
                float radius = plan.ShellRadius * (0.55f + (0.32f * MathF.Sin(plan.OrbitPhase * 0.7f + i)));
                float y = plan.ShellRadius * (lane - 0.5f) * 0.9f;
                Vector3 local = new(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
                Vector3 particlePosition = TransformLocal(plan.Position, plan.Rotation, local);
                Vector4 color = MultiplyColor(
                    plan.ParticleColor,
                    1f,
                    1f,
                    1f,
                    0.55f + (0.45f * MathF.Sin(plan.OrbitPhase + i)));
                if (plan.Shape == VfxEmitterShape.PrimitiveCube)
                {
                    Vector3 size = new(plan.ParticleRadius * 1.55f, plan.ParticleRadius * 1.55f, plan.ParticleRadius * 1.55f);
                    Rl.DrawCube(particlePosition, size.X, size.Y, size.Z, ToRaylibColor(color));
                }
                else
                {
                    Rl.DrawSphere(particlePosition, plan.ParticleRadius, ToRaylibColor(color));
                }
            }
        }

        internal static RaylibVfxEffectKey ComposeEffectKey(int stableId, int effectAssetId)
        {
            return new RaylibVfxEffectKey(stableId, effectAssetId);
        }

        private static void DrawRotatedRing(Vector3 center, Quaternion rotation, float radius, int segments, Vector4 color)
        {
            if (segments < 3 || radius <= 0f)
            {
                return;
            }

            Quaternion normalized = WorldPlane2D.NormalizeOrIdentity(rotation);
            Color ringColor = ToRaylibColor(color);
            float step = MathF.Tau / segments;
            Vector3 previous = TransformLocal(center, normalized, new Vector3(radius, 0f, 0f));
            for (int index = 1; index <= segments; index++)
            {
                float angle = index * step;
                Vector3 current = TransformLocal(
                    center,
                    normalized,
                    new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius));
                Rl.DrawLine3D(previous, current, ringColor);
                previous = current;
            }
        }

        private static Vector3 TransformLocal(Vector3 origin, Quaternion rotation, Vector3 local)
        {
            return origin + Vector3.Transform(local, WorldPlane2D.NormalizeOrIdentity(rotation));
        }

        private static Vector4 BlendSemanticColor(Vector4 baseColor, int semanticId, float semanticWeight)
        {
            Vector4 semanticColor = ResolveSemanticColor(semanticId, baseColor.W);
            Vector4 tinted = LerpColor(baseColor, semanticColor, semanticWeight);
            tinted.W = Math.Max(baseColor.W, semanticColor.W * 0.8f);
            return tinted;
        }

        private static Vector4 ResolveSemanticColor(int semanticId, float alpha)
        {
            uint seed = semanticId == 0 ? 0x9E3779B9u : Hash((uint)semanticId);
            float r = 0.28f + (((seed >> 0) & 0xFF) / 255f) * 0.62f;
            float g = 0.28f + (((seed >> 8) & 0xFF) / 255f) * 0.62f;
            float b = 0.28f + (((seed >> 16) & 0xFF) / 255f) * 0.62f;
            return new Vector4(r, g, b, Math.Clamp(alpha, 0.35f, 1f));
        }

        private static uint Hash(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }

        private static Vector4 MultiplyColor(Vector4 color, float r, float g, float b, float a)
        {
            return new Vector4(
                Math.Clamp(color.X * r, 0f, 1f),
                Math.Clamp(color.Y * g, 0f, 1f),
                Math.Clamp(color.Z * b, 0f, 1f),
                Math.Clamp(color.W * a, 0f, 1f));
        }

        private static Vector4 LerpColor(Vector4 from, Vector4 to, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vector4(
                from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t,
                from.Z + (to.Z - from.Z) * t,
                from.W + (to.W - from.W) * t);
        }

        private static Vector4 ClampColor(Vector4 color)
        {
            return new Vector4(
                Math.Clamp(color.X, 0f, 1f),
                Math.Clamp(color.Y, 0f, 1f),
                Math.Clamp(color.Z, 0f, 1f),
                Math.Clamp(color.W, 0f, 1f));
        }

        private static Color ToRaylibColor(in Vector4 color)
        {
            return RaylibColorUtil.ToRaylibColor(in color);
        }
    }
}
