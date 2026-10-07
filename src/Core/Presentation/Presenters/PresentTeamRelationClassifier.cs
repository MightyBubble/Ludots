using System;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Presentation;

namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// Classifies the viewer team → owner team relation for presentation styling. Which relationship
    /// types read as "friendly" and "hostile" is declared by <c>presentation.teamRelationColors</c>.
    /// </summary>
    public sealed class PresentTeamRelationClassifier
    {
        private readonly TeamRelationQuery _teamRelations;
        private readonly int _friendlyRelationTypeId;
        private readonly int _hostileRelationTypeId;

        public PresentTeamRelationClassifier(TeamRelationQuery teamRelations, PresentationTeamRelationColorsConfig config)
        {
            _teamRelations = teamRelations ?? throw new ArgumentNullException(nameof(teamRelations));
            ArgumentNullException.ThrowIfNull(config);
            config.Validate();
            _friendlyRelationTypeId = RequireRelationType(teamRelations, config.FriendlyRelation, "presentation.teamRelationColors.friendlyRelation");
            _hostileRelationTypeId = RequireRelationType(teamRelations, config.HostileRelation, "presentation.teamRelationColors.hostileRelation");
        }

        public void Classify(int viewerTeamId, int ownerTeamId, out bool isFriendly, out bool isHostile)
        {
            isFriendly = _teamRelations.Has(viewerTeamId, ownerTeamId, _friendlyRelationTypeId);
            isHostile = _teamRelations.Has(viewerTeamId, ownerTeamId, _hostileRelationTypeId);
        }

        private static int RequireRelationType(TeamRelationQuery teamRelations, string name, string path)
        {
            if (!teamRelations.Types.TryGetId(name, out int typeId))
            {
                throw new InvalidOperationException($"{path} '{name}' is not a registered relationship type.");
            }

            return typeId;
        }
    }
}
