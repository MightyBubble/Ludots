using Ludots.Core.Engine;

namespace ParticipantViewCapabilityMod.Runtime;

public interface IParticipantViewCommandService
{
    ParticipantViewMode Mode { get; }

    int SelectedPlayerId { get; }

    int SelectedTeamId { get; }

    void SelectPlayer(GameEngine engine, int playerId);

    void SelectTeam(GameEngine engine, int teamId);
}
