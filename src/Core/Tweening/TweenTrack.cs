using System;

namespace Ludots.Core.Tweening
{
    public delegate T TweenInterpolator<T>(T from, T to, float t);

    public sealed class TweenTrack<T>
    {
        private readonly TweenInterpolator<T> _interpolator;
        private TweenProgress _progress;
        private T _from;
        private T _to;

        public TweenTrack(TweenInterpolator<T> interpolator)
        {
            _interpolator = interpolator ?? throw new ArgumentNullException(nameof(interpolator));
            _progress.Complete();
            _from = default!;
            _to = default!;
            Current = default!;
        }

        public T Current { get; private set; }
        public T Target => _to;
        public bool IsActive => _progress.IsActive;

        public void Snap(T value)
        {
            _from = value;
            _to = value;
            Current = value;
            _progress.Complete();
        }

        public void Start(T from, T to, float durationSeconds, TweenEasing easing)
        {
            _from = from;
            _to = to;
            Current = from;
            _progress.Start(durationSeconds, easing);
            if (!_progress.IsActive)
            {
                Current = to;
            }
        }

        public void Retarget(T to, float durationSeconds, TweenEasing easing)
        {
            Start(Current, to, durationSeconds, easing);
        }

        public T Tick(float dt)
        {
            if (!_progress.IsActive)
            {
                return Current;
            }

            float t = _progress.Tick(dt);
            Current = _interpolator(_from, _to, t);
            if (!_progress.IsActive)
            {
                Current = _to;
            }

            return Current;
        }
    }
}
