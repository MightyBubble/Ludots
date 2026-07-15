using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;

namespace Ludots.Core.Engine
{
    public partial class GameEngine
    {
        public void RestoreWorldSnapshot(World restoredWorld, JsonObject domains)
        {
            if (restoredWorld == null) throw new ArgumentNullException(nameof(restoredWorld));
            if (domains == null) throw new ArgumentNullException(nameof(domains));

            SaveParticipantRegistry registry = GetService(CoreServiceKeys.SaveParticipants);
            if (registry == null)
            {
                throw new SaveContextException("Save participant registry is not available.");
            }
            var admissionResults = GetService(CoreServiceKeys.OrderAdmissionResultBuffer);
            if (admissionResults == null)
            {
                throw new SaveContextException("Order admission result buffer is not available.");
            }

            LudotsWorldStateImporter.ImportOwnedSnapshotInto(restoredWorld, World);
            SetService(CoreServiceKeys.World, World);
            registry.RestoreDomains(domains);
            admissionResults.ResetForWorldRestore();
            _cooperativeSimulation?.Reset();
        }
    }
}
