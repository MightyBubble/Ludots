using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Client;
using Ludots.Core.Persistence;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Runtime;

/// <summary>
/// Persists the RTS command-source selection across save/load by primary entity name and
/// rebinds it against the restored world. Collection sources hold entity references into the
/// captured world; the restore boundary clears them, and this participant reseeds from fresh
/// world queries so continuous and restored sessions stay symmetric.
/// </summary>
public sealed class RtsSelectionSaveParticipant : ISaveParticipant
{
    private readonly GameEngine _engine;

    public RtsSelectionSaveParticipant(GameEngine engine)
    {
        _engine = engine;
    }

    public string DomainKey => "rts.commandSourceSelection";

    public JsonNode CaptureState()
    {
        var state = new JsonObject();
        if (!ClientLocalSeatAccess.RequireRegistry(_engine).TryGetSolePossessedRep(out Entity owner) ||
            !_engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollectionStore store))
        {
            state["primaryName"] = null;
            return state;
        }

        state["primaryName"] = EntityCollectionContextRuntime.TryGetPrimary(
            _engine.World,
            _engine.GlobalContext,
            owner,
            "collection.command.source",
            out Entity primary) &&
            _engine.World.TryGet(primary, out Name name)
                ? name.Value
                : null;
        return state;
    }

    public void RestoreState(JsonNode state)
    {
        string? primaryName = state["primaryName"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(primaryName) ||
            !ClientLocalSeatAccess.RequireRegistry(_engine).TryGetSolePossessedRep(out Entity owner) ||
            !_engine.TryGetService(CoreServiceKeys.CollectionApplier, out CollectionApplier applier) ||
            !_engine.TryGetService(CoreServiceKeys.EntityCollectionStore, out EntityCollectionStore? store) ||
            store == null)
        {
            return;
        }

        Entity primary = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        _engine.World.Query(in query, (Entity entity, ref Name name) =>
        {
            if (primary == Entity.Null && string.Equals(name.Value, primaryName, System.StringComparison.Ordinal))
            {
                primary = entity;
            }
        });

        if (primary == Entity.Null)
        {
            return;
        }

        int keyId = store.KeyRegistry.GetId("collection.command.source");
        Span<Entity> members = stackalloc Entity[1];
        members[0] = primary;
        applier.Apply(owner, keyId, CollectionWriteOp.Replace, members);
    }
}
