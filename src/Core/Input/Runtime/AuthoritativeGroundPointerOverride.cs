using System;
using System.Numerics;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Input.Runtime
{
    /// <summary>
    /// One-frame authoritative world-point override for pointer actions handled by core UI.
    /// </summary>
    public sealed class AuthoritativeGroundPointerOverride
    {
        private string _actionId = string.Empty;
        private WorldCmInt2 _worldCm;
        private bool _hasOverride;

        public bool HasOverride => _hasOverride;

        public string ActionId => _hasOverride ? _actionId : string.Empty;

        public void Set(string actionId, Vector2 worldCm)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                throw new ArgumentException("Ground pointer override requires an action id.", nameof(actionId));
            }

            _actionId = actionId;
            _worldCm = new WorldCmInt2(
                (int)MathF.Round(worldCm.X, MidpointRounding.AwayFromZero),
                (int)MathF.Round(worldCm.Y, MidpointRounding.AwayFromZero));
            _hasOverride = true;
        }

        public bool TryConsume(string actionId, out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!_hasOverride || !string.Equals(_actionId, actionId, StringComparison.Ordinal))
            {
                return false;
            }

            worldCm = _worldCm;
            Clear();
            return true;
        }

        public void Clear()
        {
            _actionId = string.Empty;
            _worldCm = default;
            _hasOverride = false;
        }
    }
}
