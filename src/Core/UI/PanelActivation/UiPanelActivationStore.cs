using System;
using System.Collections.Generic;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// Activation truth for panel types (#1014 / constitution contract five).
    /// Written exclusively by <see cref="PanelActivationApi"/> via the write token;
    /// everyone else reads. Show/hide requests come from ShowPanel/HidePanel graph ops
    /// or direct system API calls — the store never decides, it records.
    /// Audience overrides follow the same shape: the declared template audience is the
    /// default, runtime overrides (hotseat rotation via the SetPanelAudience graph op)
    /// are recorded here and resolved by <see cref="PanelAudienceResolution"/>.
    /// </summary>
    public sealed class UiPanelActivationStore
    {
        private readonly Dictionary<string, bool> _visible = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PanelAudience> _audienceOverrides = new(StringComparer.Ordinal);

        public bool IsVisible(string panelType)
        {
            return _visible.TryGetValue(panelType, out bool visible) && visible;
        }

        public IReadOnlyDictionary<string, bool> Snapshot => _visible;

        /// <summary>Runtime audience override for a panel type; false when none is set (declared template audience rules).</summary>
        public bool TryGetAudienceOverride(string panelType, out PanelAudience audience)
        {
            audience = null!;
            return !string.IsNullOrWhiteSpace(panelType) &&
                _audienceOverrides.TryGetValue(panelType.Trim(), out audience!);
        }

        internal void SetVisible(string panelType, bool visible)
        {
            if (string.IsNullOrWhiteSpace(panelType))
            {
                throw new ArgumentException("Panel type must be non-empty.", nameof(panelType));
            }

            _visible[panelType.Trim()] = visible;
        }

        internal void SetAudienceOverride(string panelType, PanelAudience audience)
        {
            if (string.IsNullOrWhiteSpace(panelType))
            {
                throw new ArgumentException("Panel type must be non-empty.", nameof(panelType));
            }

            ArgumentNullException.ThrowIfNull(audience);
            _audienceOverrides[panelType.Trim()] = audience;
        }

        internal void ClearAudienceOverride(string panelType)
        {
            if (string.IsNullOrWhiteSpace(panelType))
            {
                throw new ArgumentException("Panel type must be non-empty.", nameof(panelType));
            }

            _audienceOverrides.Remove(panelType.Trim());
        }
    }
}
