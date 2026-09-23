using System;
using Ludots.Core.Config;
using Ludots.Core.Scripting;

namespace ParticipantViewCapabilityMod.Runtime;

public static class ParticipantViewCapabilityIds
{
    public const string ActivationMapTag = "capability.participant_view";
    public const string RelationshipType = "Participant";
    public const string PanelDisabledServiceKey = "ParticipantViewCapabilityMod.PanelDisabled";
    public static readonly ServiceKey<IParticipantViewCommandService> CommandService = new("ParticipantViewCapabilityMod.CommandService");

    public static bool IsParticipantViewMap(MapConfig? mapConfig)
    {
        if (mapConfig?.Tags == null)
        {
            return false;
        }

        for (int i = 0; i < mapConfig.Tags.Count; i++)
        {
            if (string.Equals(mapConfig.Tags[i], ActivationMapTag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
