using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// Region-side executor (#1014 MVP): turns activation-store truth into surface
    /// lease diffs. The host never decides visibility; it only reconciles leases
    /// with the store snapshot after the orchestration runtime has written it.
    /// </summary>
    public sealed class PanelRegionHost
    {
        private readonly HashSet<string> _leased = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Leased => _leased;

        /// <summary>
        /// Reconciles current leases with the activation snapshot. Panels that became
        /// visible are activated; panels no longer visible are deactivated; stale
        /// leases for panels absent from the store are released.
        /// </summary>
        public PanelActivationDiff Reconcile(UiPanelActivationStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            var activated = new List<string>();
            var deactivated = new List<string>();
            foreach (KeyValuePair<string, bool> entry in store.Snapshot)
            {
                if (entry.Value && _leased.Add(entry.Key))
                {
                    activated.Add(entry.Key);
                }
                else if (!entry.Value && _leased.Remove(entry.Key))
                {
                    deactivated.Add(entry.Key);
                }
            }

            List<string> stale = new List<string>();
            foreach (string panelType in _leased)
            {
                if (!store.Snapshot.ContainsKey(panelType))
                {
                    stale.Add(panelType);
                }
            }

            foreach (string panelType in stale)
            {
                _leased.Remove(panelType);
                deactivated.Add(panelType);
            }

            return new PanelActivationDiff(activated, deactivated);
        }
    }
}
