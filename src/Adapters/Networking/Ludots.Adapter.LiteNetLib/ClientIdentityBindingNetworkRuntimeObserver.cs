using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Networking.Commands;
using Ludots.Core.Networking.Protocol;
using Ludots.Core.Networking.Replication;
using Ludots.Core.Networking.Runtime;
using Ludots.Core.Networking.Session;
using Ludots.Core.Networking.Transport;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.LiteNetLib;

internal sealed class ClientIdentityBindingNetworkRuntimeObserver : INetworkRuntimeObserver
{
    private readonly GameEngine _engine;
    private readonly INetworkRuntimeObserver _inner;

    public ClientIdentityBindingNetworkRuntimeObserver(GameEngine engine, INetworkRuntimeObserver inner)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void OnFault(in NetworkRuntimeFault fault) => _inner.OnFault(in fault);

    public void OnServerSeatConnected(in SessionSeatBinding seat, bool reconnected) =>
        _inner.OnServerSeatConnected(in seat, reconnected);

    public void OnServerSeatDisconnected(in SessionSeatBinding seat, TransportDisconnectReason reason) =>
        _inner.OnServerSeatDisconnected(in seat, reason);

    public void OnServerSeatReleased(in SessionSeatBinding seat) => _inner.OnServerSeatReleased(in seat);

    public void OnServerRoomSnapshot(
        in NetworkRoomSnapshotHeader snapshot,
        ReadOnlySpan<NetworkRoomSeatSnapshot> seats) =>
        _inner.OnServerRoomSnapshot(in snapshot, seats);

    public void OnClientHandshake(in SessionHandshakeResponse response)
    {
        if (response.Accepted)
        {
            BindLocalPlayer(in response);
        }

        _inner.OnClientHandshake(in response);
    }

    public void OnClientAdmission(in NetworkCommandAdmissionOutcome outcome) =>
        _inner.OnClientAdmission(in outcome);

    public void OnClientResyncRequired(in NetworkResyncRequired message) =>
        _inner.OnClientResyncRequired(in message);

    public void OnClientRoomSnapshot(
        in NetworkRoomSnapshotHeader snapshot,
        ReadOnlySpan<NetworkRoomSeatSnapshot> seats) =>
        _inner.OnClientRoomSnapshot(in snapshot, seats);

    private void BindLocalPlayer(in SessionHandshakeResponse response)
    {
        var session = _engine.CurrentMapSession ??
            throw new InvalidOperationException("Replicated client handshake requires the startup map to be loaded.");
        int playerId = response.PlayerId.Value;
        if (!session.PlayerEntityLookup.TryGet(playerId, out var player) ||
            !_engine.World.IsAlive(player) ||
            !_engine.World.TryGet(player, out PlayerIdentity identity) ||
            identity.PlayerId != playerId)
        {
            throw new InvalidOperationException(
                $"Replicated client map requires one live PlayerIdentity representative for assigned player {playerId}.");
        }

        ClientLocalSeatRegistry seats = ClientLocalSeatAccess.RequireRegistry(_engine);
        string schemeId = RequireReboundControlScheme(session, seats, playerId);
        seats.ReplaceAll(new[]
        {
            new ClientLocalSeat("seat.0", schemeId)
            {
                PossessedPlayerId = playerId,
                PossessedRep = player,
            },
        });
        ClientLocalSeatAccess.RequireLogicViews(_engine).EnsureDefaultView(player);
        session.LocalSeats = new[]
        {
            new Ludots.Core.Gameplay.Teams.ResolvedLocalSeatPossession("seat.0", playerId, player, schemeId),
        };
    }

    private string RequireReboundControlScheme(MapSession session, ClientLocalSeatRegistry seats, int playerId)
    {
        string? scheme = null;
        System.Collections.Generic.IReadOnlyList<string> existingIds = seats.SeatIds;
        for (int i = 0; i < existingIds.Count; i++)
        {
            string? existing = seats.Require(existingIds[i]).ControlSchemeId;
            if (!string.IsNullOrWhiteSpace(existing))
            {
                scheme = existing;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(scheme) && session.LaunchContext != null)
        {
            System.Collections.Generic.IReadOnlyList<LocalSeatLaunchBinding> launchSeats = session.LaunchContext.LocalSeats;
            for (int i = 0; i < launchSeats.Count; i++)
            {
                LocalSeatLaunchBinding binding = launchSeats[i];
                if (binding.PlayerId == playerId && !string.IsNullOrWhiteSpace(binding.ControlSchemeId))
                {
                    scheme = binding.ControlSchemeId;
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(scheme) &&
            _engine.GlobalContext.TryGetValue(CoreServiceKeys.ControlSchemeRuntime.Name, out object? schemeObj) &&
            schemeObj is Ludots.Core.Input.Interaction.ControlSchemeRuntime schemes &&
            schemes.ActiveSchemeId != 0)
        {
            string active = schemes.SchemeIdRegistry.GetName(schemes.ActiveSchemeId);
            if (!string.IsNullOrWhiteSpace(active))
            {
                scheme = active;
            }
        }

        if (string.IsNullOrWhiteSpace(scheme))
        {
            throw new InvalidOperationException("ClientLocalSeat 'seat.0' requires controlSchemeId.");
        }

        return scheme.Trim();
    }
}
