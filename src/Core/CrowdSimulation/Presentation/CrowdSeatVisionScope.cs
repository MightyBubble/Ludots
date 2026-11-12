using System;
using Ludots.Core.Client;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.CrowdSimulation.Presentation;

public sealed class CrowdSeatVisionScope : ISeatVisionScope
{
    private readonly ClientLocalSeatRegistry _seats;
    private readonly Func<CrowdSimSession?> _session;

    public CrowdSeatVisionScope(ClientLocalSeatRegistry seats, Func<CrowdSimSession?> session)
    {
        _seats = seats ?? throw new ArgumentNullException(nameof(seats));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool TryGetScopeKeyId(string seatId, out int scopeKeyId)
    {
        scopeKeyId = 0;
        if (!_seats.TryGet(seatId, out ClientLocalSeat seat) || !seat.HasPossession)
        {
            return false;
        }

        CrowdSimSession? session = _session();
        var fog = session?.Fog;
        if (fog == null)
        {
            return false;
        }

        int[] index = session!.Config.PairRules.IndexByPlayerId;
        int playerId = seat.PossessedPlayerId;
        if ((uint)playerId >= (uint)index.Length)
        {
            return false;
        }

        int player = index[playerId];
        if ((uint)player >= (uint)fog.GroupOf.Length)
        {
            return false;
        }

        scopeKeyId = fog.GroupOf[player] + 1;
        return scopeKeyId > 0;
    }
}
