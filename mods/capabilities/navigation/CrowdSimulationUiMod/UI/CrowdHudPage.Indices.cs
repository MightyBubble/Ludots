using Ludots.Core.CrowdSimulation.Presentation;

namespace CrowdSimulationUiMod.UI;

internal sealed partial class CrowdHudPage
{
    private int ClickTemplateIndex(string? templateId)
    {
        CrowdHudCatalog? c = _catalog;
        if (c == null || string.IsNullOrEmpty(templateId)) return -1;
        for (int i = 0; i < c.ClickTemplates.Count; i++)
        {
            if (string.Equals(c.ClickTemplates[i], templateId, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private int LayerTypeIndex()
    {
        CrowdHudCatalog? c = _catalog;
        if (c == null) return -1;
        for (int i = 0; i < c.UnitTypes.Count; i++)
        {
            if (string.Equals(c.UnitTypes[i], _player.LayerId, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private int WindowLo() => ClickTemplateIndex(_player.WindowFrom);

    private int WindowHi() => ClickTemplateIndex(_player.WindowTo);

    private int InspectAgentIndex()
    {
        CrowdHudCatalog? c = _catalog;
        if (c == null) return -1;
        for (int i = 0; i < c.AgentTypes.Count; i++)
        {
            if (string.Equals(c.AgentTypes[i], _inspect.AgentTypeId, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private int InspectRadiusIndex()
    {
        CrowdHudCatalog? c = _catalog;
        if (c == null || string.IsNullOrEmpty(_inspect.ProfileId)) return -1;
        for (int i = 0; i < c.RadiusCm.Count; i++)
        {
            if (ProfileMatchesRadiusClass(_inspect.ProfileId, c.RadiusCm[i])) return i;
        }

        return -1;
    }

    private static bool ProfileMatchesRadiusClass(string profileId, int radiusCm)
    {
        int dot = profileId.LastIndexOf('.');
        if (dot < 0) return false;
        ReadOnlySpan<char> tail = profileId.AsSpan(dot + 1);
        return tail.StartsWith("r".AsSpan(), StringComparison.Ordinal)
            && int.TryParse(tail[1..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int cm)
            && cm == radiusCm;
    }
}
