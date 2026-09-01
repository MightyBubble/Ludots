using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
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

        string seatId = $"seat.{response.Seat.Slot}";
        ClientLocalSeatRegistry seats = _engine.GetService(CoreServiceKeys.ClientLocalSeatRegistry)
            ?? throw new InvalidOperationException(
                "Replicated client handshake requires ClientLocalSeatRegistry.");
        LogicViewRegistry views = _engine.GetService(CoreServiceKeys.LogicViewRegistry)
            ?? throw new InvalidOperationException(
                "Replicated client handshake requires LogicViewRegistry.");

        if (seats.Count > 0 &&
            (!seats.TryGet(seatId, out ClientLocalSeat existingSeat) ||
             (existingSeat.HasPossession &&
              (existingSeat.PossessedPlayerId != playerId || existingSeat.PossessedRep != player))))
        {
            throw new InvalidOperationException(
                $"Replicated client handshake cannot replace existing local seat bindings for '{seatId}'.");
        }

        if (!seats.TryGet(seatId, out ClientLocalSeat seat))
        {
            seat = new ClientLocalSeat(seatId);
            seats.Add(seat);
        }

        seats.SetPossession(seatId, playerId, player);
        string viewId = views.TryGetDefaultViewId(player, out string existingViewId)
            ? existingViewId
            : views.EnsureDefaultView(player);
        if (seat.PresentBinding is PresentBinding present &&
            !string.Equals(present.LogicViewId, viewId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Replicated client seat '{seatId}' PresentBinding targets LogicView '{present.LogicViewId}', not '{viewId}'.");
        }

        session.LocalSeats = new[]
        {
            new ResolvedLocalSeatPossession(seatId, playerId, player, seat.ControlSchemeId),
        };
    }
}
