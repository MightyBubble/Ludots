using System;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    public sealed class DirectTransformFollowTarget : ICameraFollowTarget
    {
        private readonly Func<CameraTargetTransformSnapshot> _provider;

        public DirectTransformFollowTarget(CameraTargetTransformSnapshot snapshot)
            : this(() => snapshot)
        {
        }

        public DirectTransformFollowTarget(Func<CameraTargetTransformSnapshot> provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public bool TryGetTransform(out CameraTargetTransformSnapshot transform)
        {
            transform = _provider();
            return true;
        }
    }
}
