using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Mathematics;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.Camera
{
    public enum CameraImpulseFalloff
    {
        Linear = 0,
        SmoothStep = 1
    }

    public struct CameraImpulseSource
    {
        public Vector2 PositionCm;
        public float HeightCm;
        public float InnerRadiusCm;
        public float RadiusCm;
        public float DurationSeconds;
        public float FrequencyHz;
        public float PhaseRadians;
        public float PositionAmplitudeCm;
        public float YawAmplitudeDeg;
        public float PitchAmplitudeDeg;
        public CameraImpulseFalloff Falloff;
    }

    public readonly struct CameraImpulseListener
    {
        public CameraImpulseListener(Vector2 positionCm, float heightCm, float yawDeg)
        {
            PositionCm = positionCm;
            HeightCm = heightCm;
            YawDeg = yawDeg;
        }

        public Vector2 PositionCm { get; }
        public float HeightCm { get; }
        public float YawDeg { get; }
    }

    public readonly struct CameraImpulseSample
    {
        public CameraImpulseSample(Vector3 positionOffsetCm, float yawOffsetDeg, float pitchOffsetDeg)
        {
            PositionOffsetCm = positionOffsetCm;
            YawOffsetDeg = yawOffsetDeg;
            PitchOffsetDeg = pitchOffsetDeg;
        }

        public Vector3 PositionOffsetCm { get; }
        public float YawOffsetDeg { get; }
        public float PitchOffsetDeg { get; }
        public bool HasValue =>
            PositionOffsetCm.LengthSquared() > 0.000001f ||
            MathF.Abs(YawOffsetDeg) > 0.000001f ||
            MathF.Abs(PitchOffsetDeg) > 0.000001f;
    }

    public sealed class CameraImpulseRuntime
    {
        private readonly List<ActiveImpulse> _active = new();

        public int ActiveCount => _active.Count;

        public void Clear()
        {
            _active.Clear();
        }

        public void Emit(in CameraImpulseSource source)
        {
            ValidateSource(in source);
            _active.Add(new ActiveImpulse(source));
        }

        public CameraImpulseSample Sample(in CameraImpulseListener listener, float dt)
        {
            ValidateListener(in listener);
            if (!float.IsFinite(dt) || dt < 0f)
            {
                throw new InvalidOperationException("Camera impulse sampling requires a finite non-negative dt.");
            }

            Vector3 positionOffset = Vector3.Zero;
            float yawOffset = 0f;
            float pitchOffset = 0f;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveImpulse active = _active[i];
                active.AgeSeconds += dt;
                if (active.AgeSeconds > active.Source.DurationSeconds)
                {
                    RemoveAtSwapBack(i);
                    continue;
                }

                float attenuation = ResolveDistanceAttenuation(in active.Source, in listener);
                if (attenuation <= 0f)
                {
                    _active[i] = active;
                    continue;
                }

                float lifeT = active.AgeSeconds / active.Source.DurationSeconds;
                float envelope = Smooth01(1f - Math.Clamp(lifeT, 0f, 1f));
                float wave = MathF.Sin(active.Source.PhaseRadians + (active.AgeSeconds * active.Source.FrequencyHz * WorldPlane2D.TwoPi));
                float strength = attenuation * envelope * wave;

                positionOffset += ResolveImpulseDirectionCm(in active.Source, in listener) *
                                  (active.Source.PositionAmplitudeCm * strength);
                yawOffset += active.Source.YawAmplitudeDeg * strength;
                pitchOffset += active.Source.PitchAmplitudeDeg * attenuation * envelope *
                               MathF.Cos(active.Source.PhaseRadians + (active.AgeSeconds * active.Source.FrequencyHz * WorldPlane2D.TwoPi));

                _active[i] = active;
            }

            return new CameraImpulseSample(positionOffset, yawOffset, pitchOffset);
        }

        private static Vector3 ResolveImpulseDirectionCm(in CameraImpulseSource source, in CameraImpulseListener listener)
        {
            Vector3 direction = new(
                listener.PositionCm.X - source.PositionCm.X,
                listener.HeightCm - source.HeightCm,
                listener.PositionCm.Y - source.PositionCm.Y);

            if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 0.000001f)
            {
                Vector3 forward = WorldPlane2D.VisualCameraForwardFromYawPitchDegrees(listener.YawDeg, 0f);
                direction = new Vector3(forward.X, 0f, forward.Z);
            }

            return Vector3.Normalize(direction);
        }

        private static float ResolveDistanceAttenuation(in CameraImpulseSource source, in CameraImpulseListener listener)
        {
            float dx = listener.PositionCm.X - source.PositionCm.X;
            float dy = listener.PositionCm.Y - source.PositionCm.Y;
            float dz = listener.HeightCm - source.HeightCm;
            float distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (distance <= source.InnerRadiusCm)
            {
                return 1f;
            }

            if (distance >= source.RadiusCm)
            {
                return 0f;
            }

            float t = (distance - source.InnerRadiusCm) / MathF.Max(1f, source.RadiusCm - source.InnerRadiusCm);
            float linear = 1f - Math.Clamp(t, 0f, 1f);
            return source.Falloff switch
            {
                CameraImpulseFalloff.Linear => linear,
                CameraImpulseFalloff.SmoothStep => Smooth01(linear),
                _ => throw new InvalidOperationException($"Unsupported camera impulse falloff '{source.Falloff}'.")
            };
        }

        private static float Smooth01(float value)
        {
            float t = Math.Clamp(value, 0f, 1f);
            return t * t * (3f - (2f * t));
        }

        private void RemoveAtSwapBack(int index)
        {
            int last = _active.Count - 1;
            if (index != last)
            {
                _active[index] = _active[last];
            }

            _active.RemoveAt(last);
        }

        private static void ValidateSource(in CameraImpulseSource source)
        {
            ValidateFinite(nameof(source.PositionCm), source.PositionCm.X);
            ValidateFinite(nameof(source.PositionCm), source.PositionCm.Y);
            ValidateFinite(nameof(source.HeightCm), source.HeightCm);
            ValidateFinite(nameof(source.InnerRadiusCm), source.InnerRadiusCm);
            ValidateFinite(nameof(source.RadiusCm), source.RadiusCm);
            ValidateFinite(nameof(source.DurationSeconds), source.DurationSeconds);
            ValidateFinite(nameof(source.FrequencyHz), source.FrequencyHz);
            ValidateFinite(nameof(source.PhaseRadians), source.PhaseRadians);
            ValidateFinite(nameof(source.PositionAmplitudeCm), source.PositionAmplitudeCm);
            ValidateFinite(nameof(source.YawAmplitudeDeg), source.YawAmplitudeDeg);
            ValidateFinite(nameof(source.PitchAmplitudeDeg), source.PitchAmplitudeDeg);

            if (source.InnerRadiusCm < 0f)
            {
                throw new InvalidOperationException("Camera impulse inner radius must be >= 0.");
            }

            if (source.RadiusCm <= source.InnerRadiusCm)
            {
                throw new InvalidOperationException("Camera impulse radius must be greater than inner radius.");
            }

            if (source.DurationSeconds <= 0f)
            {
                throw new InvalidOperationException("Camera impulse duration must be > 0.");
            }

            if (source.FrequencyHz < 0f)
            {
                throw new InvalidOperationException("Camera impulse frequency must be >= 0.");
            }

            if (!Enum.IsDefined(source.Falloff))
            {
                throw new InvalidOperationException($"Unsupported camera impulse falloff '{source.Falloff}'.");
            }
        }

        private static void ValidateListener(in CameraImpulseListener listener)
        {
            ValidateFinite(nameof(listener.PositionCm), listener.PositionCm.X);
            ValidateFinite(nameof(listener.PositionCm), listener.PositionCm.Y);
            ValidateFinite(nameof(listener.HeightCm), listener.HeightCm);
            ValidateFinite(nameof(listener.YawDeg), listener.YawDeg);
        }

        private static void ValidateFinite(string name, float value)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException($"Camera impulse {name} must be finite.");
            }
        }

        private struct ActiveImpulse
        {
            public ActiveImpulse(in CameraImpulseSource source)
            {
                Source = source;
                AgeSeconds = 0f;
            }

            public CameraImpulseSource Source;
            public float AgeSeconds;
        }
    }
}
