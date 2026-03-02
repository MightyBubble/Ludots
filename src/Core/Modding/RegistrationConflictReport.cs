using System;
using System.Collections.Generic;
using Ludots.Core.Diagnostics;

namespace Ludots.Core.Modding
{
    /// <summary>
    /// Collects registration conflict entries from all registries and outputs
    /// a summary report after all mods have loaded.
    /// </summary>
    public sealed class RegistrationConflictReport
    {
        public struct ConflictEntry
        {
            public string RegistryName;
            public string Key;
            public string ExistingModId;
            public string NewModId;
        }

        private readonly List<ConflictEntry> _conflicts = new List<ConflictEntry>();

        public void Add(string registryName, string key, string existingModId, string newModId)
        {
            _conflicts.Add(new ConflictEntry
            {
                RegistryName = registryName,
                Key = key,
                ExistingModId = existingModId ?? "(core)",
                NewModId = newModId ?? "(core)"
            });
        }

        public int Count => _conflicts.Count;
        public IReadOnlyList<ConflictEntry> Conflicts => _conflicts;

        public void PrintSummary()
        {
            if (_conflicts.Count == 0)
            {
                Log.Info(in LogChannels.ModLoader, "No registration conflicts detected.");
                return;
            }

            Log.Info(in LogChannels.ModLoader, $"===== {_conflicts.Count} registration conflict(s) detected =====");
            foreach (var c in _conflicts)
            {
                Log.Info(in LogChannels.ModLoader, $"  [{c.RegistryName}] '{c.Key}' registered by '{c.ExistingModId}', overwritten by '{c.NewModId}'");
            }
            Log.Info(in LogChannels.ModLoader, "===== End of conflict report =====");
        }

        public void Clear()
        {
            _conflicts.Clear();
        }
    }
}
