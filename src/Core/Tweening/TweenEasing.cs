using System;

namespace Ludots.Core.Tweening
{
    public enum TweenEasing
    {
        Cut,
        Linear,
        SmoothStep
    }

    public static class TweenEasingUtil
    {
        public static float Evaluate(float t, TweenEasing easing)
        {
            t = Math.Clamp(t, 0f, 1f);
            return easing switch
            {
                TweenEasing.Cut => 1f,
                TweenEasing.Linear => t,
                TweenEasing.SmoothStep => t * t * (3f - (2f * t)),
                _ => t
            };
        }
    }

    public struct TweenProgress
    {
        private float _durationSeconds;
        private float _elapsedSeconds;

        public bool IsActive { get; private set; }
        public TweenEasing Easing { get; private set; }
        public float Progress { get; private set; }

        public float DurationSeconds => _durationSeconds;
        public float ElapsedSeconds => _elapsedSeconds;

        public void Start(float durationSeconds, TweenEasing easing)
        {
            _durationSeconds = Math.Max(0f, durationSeconds);
            _elapsedSeconds = 0f;
            Easing = easing;
            IsActive = _durationSeconds > 0f && easing != TweenEasing.Cut;
            Progress = IsActive ? 0f : 1f;
        }

        public float Tick(float dt)
        {
            if (!IsActive)
            {
                return Progress;
            }

            _elapsedSeconds += Math.Max(0f, dt);
            float rawT = _durationSeconds <= 0f ? 1f : Math.Clamp(_elapsedSeconds / _durationSeconds, 0f, 1f);
            Progress = TweenEasingUtil.Evaluate(rawT, Easing);
            if (rawT >= 1f)
            {
                Progress = 1f;
                IsActive = false;
            }

            return Progress;
        }

        public void Complete()
        {
            _durationSeconds = 0f;
            _elapsedSeconds = 0f;
            Easing = TweenEasing.Cut;
            IsActive = false;
            Progress = 1f;
        }
    }
}
