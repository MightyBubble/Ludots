using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// Activation truth for panel types (#1014 / constitution contract five).
    /// Written exclusively by <see cref="PanelActivationApi"/> via the write token;
    /// everyone else reads. Show/hide requests come from ShowPanel/HidePanel graph ops
    /// or direct system API calls — the store never decides, it records.
    /// </summary>
    public sealed class UiPanelActivationStore
    {
        private readonly Dictionary<string, bool> _visible = new(StringComparer.Ordinal);

        public bool IsVisible(string panelType)
        {
            return _visible.TryGetValue(panelType, out bool visible) && visible;
        }

        public IReadOnlyDictionary<string, bool> Snapshot => _visible;

        internal void SetVisible(string panelType, bool visible)
        {
            if (string.IsNullOrWhiteSpace(panelType))
            {
                throw new ArgumentException("Panel type must be non-empty.", nameof(panelType));
            }

            _visible[panelType.Trim()] = visible;
        }
    }
}
