using System;
using Arch.Core;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// The only writer of <see cref="UiPanelActivationStore"/> (#1014 / constitution
    /// contract five). Called by ShowPanel/HidePanel graph op handlers and by systems
    /// that need to show/hide panels directly. Graphs decide WHEN; this API records
    /// the decision; the store never orchestrates.
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
    }
}
