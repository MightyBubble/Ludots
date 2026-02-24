using System;
using System.Collections.Generic;

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
                Console.WriteLine("[RegistrationConflictReport] No registration conflicts detected.");
                return;
            }

            Console.WriteLine($"[RegistrationConflictReport] ===== {_conflicts.Count} registration conflict(s) detected =====");
            foreach (var c in _conflicts)
            {
                Console.WriteLine($"  [{c.RegistryName}] '{c.Key}' registered by '{c.ExistingModId}', overwritten by '{c.NewModId}'");
            }
            Console.WriteLine("[RegistrationConflictReport] ===== End of conflict report =====");
        }

        public void Clear()
        {
            _conflicts.Clear();
        }
    }
}
