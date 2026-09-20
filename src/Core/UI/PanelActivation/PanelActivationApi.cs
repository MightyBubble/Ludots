using System;
using Arch.Core;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// The only writer of <see cref="UiPanelActivationStore"/> (#1014 / constitution
    /// contract five). Called by ShowPanel/HidePanel graph op handlers and by systems
    /// that need to show/hide panels directly. Graphs decide WHEN; this API records
    /// the decision; the store never orchestrates. Audience overrides (hotseat
    /// rotation) take the same path via SetPanelAudience/ClearPanelAudience.
    /// </summary>
    public sealed class PanelActivationApi
    {
        private readonly UiPanelActivationStore _store;

        public PanelActivationApi(UiPanelActivationStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public UiPanelActivationStore Store => _store;

        public void ShowPanel(string panelType)
        {
            _store.SetVisible(panelType, true);
        }

        public void HidePanel(string panelType)
        {
            _store.SetVisible(panelType, false);
        }

        /// <summary>Overrides the panel type's declared audience with one explicit seat audience (hotseat turn handoff).</summary>
        public void SetPanelAudience(string panelType, PanelAudience audience)
        {
            if (string.IsNullOrWhiteSpace(panelType))
            {
                throw new ArgumentException("Panel type must be non-empty.", nameof(panelType));
            }

            _store.SetAudienceOverride(panelType, audience);
        }

        /// <summary>Drops the runtime override; the template's declared audience rules again.</summary>
        public void ClearPanelAudience(string panelType)
        {
            if (string.IsNullOrWhiteSpace(panelType))
            {
                throw new ArgumentException("Panel type must be non-empty.", nameof(panelType));
            }

            _store.ClearAudienceOverride(panelType);
        }
    }
}
