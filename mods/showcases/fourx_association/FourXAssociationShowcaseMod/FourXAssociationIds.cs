namespace FourXAssociationShowcaseMod;

public static class FourXAssociationIds
{
    public const string MapId = "fourx_association_showcase";
    public const string InstalledKey = "FourXAssociationShowcase.Installed";
    public const string RuntimeServiceKey = "FourXAssociationShowcase.Runtime";
    public const string InputContextId = "FourXAssociation.Controls";
    public const string ScoutActionId = "FourXAssociation.Scout";
    public const string AdvanceFogActionId = "FourXAssociation.AdvanceFog";
    public const string PactActionId = "FourXAssociation.Pact";
    public const string TradeActionId = "FourXAssociation.Trade";
    public const string AddResearcherActionId = "FourXAssociation.AddResearcher";
    public const string ResearchActionId = "FourXAssociation.Research";

    public static bool IsShowcaseMap(string? mapId)
    {
        return string.Equals(mapId, MapId, System.StringComparison.OrdinalIgnoreCase);
    }
}
