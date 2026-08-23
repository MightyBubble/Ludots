using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// Region-side executor (#1014): turns activation-store truth into surface
    /// lease diffs. The host never decides visibility; it reconciles leases with
    /// the snapshot written by ShowPanel/HidePanel ops (or direct API calls).
    /// </summary>
    public sealed class PanelRegionHost
    {
        private readonly Dictionary<string, bool> _leased = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, bool> Leased => _leased;

        /// <summary>
        /// Reconciles current leases with the activation snapshot; returns the diff
        /// (activated, deactivated) so surface adapters can acquire/release.
        /// </summary>
        public (IReadOnlyList<string> Activated, IReadOnlyList<string> Deactivated) Reconcile(UiPanelActivationStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            var activated = new List<string>();
            var deactivated = new List<string>();
            foreach (KeyValuePair<string, bool> entry in store.Snapshot)
            {
                bool was = _leased.TryGetValue(entry.Key, out bool leased) && leased;
                if (entry.Value && !was)
                {
                    activated.Add(entry.Key);
                }
                else if (!entry.Value && was)
                {
                    deactivated.Add(entry.Key);
                }

                _leased[entry.Key] = entry.Value;
            }

            return (activated, deactivated);
        }
    }
}
