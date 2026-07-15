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

            // Time-sliced systems may hold entity handles and deferred commands from the current
            // world. Abort those transactions before replacing entity storage so reset cannot act
            // on coincidentally reused ids in the restored snapshot.
            _cooperativeSimulation?.Reset();
            LudotsWorldStateImporter.ImportOwnedSnapshotInto(restoredWorld, World);
            SetService(CoreServiceKeys.World, World);
            registry.RestoreDomains(domains);
            admissionResults.ResetForWorldRestore();
        }
    }
}
