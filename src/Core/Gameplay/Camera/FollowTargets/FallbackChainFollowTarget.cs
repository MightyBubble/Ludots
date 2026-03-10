using System.Numerics;

namespace Ludots.Core.Gameplay.Camera.FollowTargets
{
    public sealed class FallbackChainFollowTarget : ICameraFollowTarget
    {
        private readonly ICameraFollowTarget[] _targets;

        public FallbackChainFollowTarget(params ICameraFollowTarget[] targets)
        {
            _targets = targets;
        }

        public bool TryGetPosition(out Vector2 positionCm)
        {
            positionCm = default;
            for (int i = 0; i < _targets.Length; i++)
            {
                if (_targets[i] != null && _targets[i].TryGetPosition(out positionCm))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
