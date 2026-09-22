using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Persistence;
using Ludots.Core.Gameplay.GAS.Orders;
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

            // Collection sources hold entity references into the replaced world; without this
            // reset a restored session would reconcile selections against dead entities. The
            // reset must precede domain restore: save participants rebind collections (e.g. the
            // command-source selection) into the now-clean store, and clearing afterwards would
            // wipe the rebind and desynchronize the restored session from the checkpoint.
            if (TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollections.EntityCollectionStore? collectionStore))
            {
                collectionStore!.Clear();
            }

            registry.RestoreDomains(domains);
            admissionResults.ResetForWorldRestore();

            // Presentation-side mirror of the collection store: the event system's per-collection
            // baselines are engine-local state that never rode the snapshot. Re-baseline them
            // against the participant-rebound store so the first post-restore tick publishes no
            // phantom collection member events (same boundary contract as the admission reset).
            if (_presentationSystems != null)
            {
                for (int i = 0; i < _presentationSystems.Count; i++)
                {
                    (_presentationSystems[i] as Ludots.Core.Presentation.Systems.EntityCollectionPresentationEventSystem)
                        ?.ResetForWorldRestore();
                }
            }

            // The presenter runtime's indexes also mirror world entities that the restore just
            // replaced; re-baseline them from the restored world on the same boundary contract.
            if (TryGetService(CoreServiceKeys.PresenterEntityRuntime, out Presentation.Presenters.PresenterEntityRuntime? presenterRuntime) &&
                presenterRuntime != null)
            {
                presenterRuntime.ResetForWorldRestore();
            }


            // Cross-tick pending work must not straddle the restore boundary in either direction:
            // orders queued pre-restore would replay against the restored world as ghosts, and
            // orders queued by the live session after the checkpoint would leak into the replay.
            if (TryGetService(CoreServiceKeys.OrderQueue, out Ludots.Core.Gameplay.GAS.Orders.OrderQueue? orderQueue))
            {
                orderQueue!.Clear();
            }

            if (TryGetService(CoreServiceKeys.ChainOrderQueue, out Ludots.Core.Gameplay.GAS.Orders.OrderQueue? chainOrderQueue))
            {
                chainOrderQueue!.Clear();
            }

            // Determinism basis: elapsed engine time is simulation state, not wall time. Rewind it
            // so time-derived fields (e.g. Physics2DRuntimeState.LastPhysicsStepTime) resume from
            // the checkpoint instead of carrying pre-restore drift.
            Time.FixedTotalTime = GameSession.CurrentTick * Time.FixedDeltaTime;
            Time.TotalTime = GameSession.CurrentTick * Time.FixedDeltaTime;
        }
    }
}
