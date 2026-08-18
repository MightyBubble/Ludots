using System;
using System.Collections.Generic;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// Compile-time single-writer credential for <see cref="UiPanelActivationStore"/>.
    /// The internal constructor means only the Core orchestration runtime can mint one;
    /// panels, surfaces, and mods cannot construct it.
    /// </summary>
    public readonly struct PanelActivationWriteToken
    {
        internal PanelActivationWriteToken(byte _)
        {
        }
    }

    /// <summary>
    /// Activation truth for panel types (#1014 / constitution contract five).
    /// Written exclusively by <see cref="PanelOrchestrationRuntime"/> via the write token;
    /// everyone else reads. Full-set applies return the lease diff for region hosts.
    /// </summary>
    public sealed class UiPanelActivationStore
    {
        private readonly Dictionary<string, bool> _visible = new(StringComparer.Ordinal);

        public bool IsVisible(string panelType)
        {
            return _visible.TryGetValue(panelType, out bool visible) && visible;
        }

        public IReadOnlyDictionary<string, bool> Snapshot => _visible;

        public PanelActivationDiff Apply(PanelActivationWriteToken _, IReadOnlyDictionary<string, bool> desired)
        {
            ArgumentNullException.ThrowIfNull(desired);

            var activated = new List<string>();
            var deactivated = new List<string>();
            foreach (KeyValuePair<string, bool> entry in desired)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    throw new InvalidOperationException("Activation entry panelType must be non-empty.");
                }

                bool was = _visible.TryGetValue(entry.Key, out bool previous) && previous;
                bool now = entry.Value;
                if (now && !was)
                {
                    activated.Add(entry.Key);
                }
                else if (!now && was)
                {
                    deactivated.Add(entry.Key);
                }

                _visible[entry.Key] = now;
            }

            List<string> stale = new List<string>();
            foreach (string panelType in _visible.Keys)
            {
                if (!desired.ContainsKey(panelType))
                {
                    stale.Add(panelType);
                }
            }

            foreach (string panelType in stale)
            {
                if (_visible[panelType])
                {
                    deactivated.Add(panelType);
                }

                _visible.Remove(panelType);
            }

            return new PanelActivationDiff(activated, deactivated);
        }
    }

    public sealed record PanelActivationDiff(IReadOnlyList<string> Activated, IReadOnlyList<string> Deactivated);
}
