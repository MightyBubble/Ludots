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

    private int UnitTypeIndex(string? unitTypeId)
    {
        CrowdHudCatalog? c = _catalog;
        if (c == null || string.IsNullOrEmpty(unitTypeId)) return -1;
        for (int i = 0; i < c.UnitTypes.Count; i++)
        {
            if (string.Equals(c.UnitTypes[i], unitTypeId, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    private int LayerTypeIndex() => UnitTypeIndex(_player.LayerId);

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

    private int InspectProfileIndex()
    {
        CrowdHudCatalog? c = _catalog;
        if (c == null || string.IsNullOrEmpty(_inspect.ProfileId)) return -1;
        for (int i = 0; i < c.ProfileIds.Count; i++)
        {
            if (string.Equals(c.ProfileIds[i], _inspect.ProfileId, StringComparison.Ordinal)) return i;
        }

        return -1;
    }
}
