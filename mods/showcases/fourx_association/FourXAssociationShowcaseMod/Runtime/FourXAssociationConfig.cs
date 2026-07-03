using System;
using System.IO;
using System.Text.Json;

namespace FourXAssociationShowcaseMod.Runtime;

internal sealed class FourXAssociationConfig
{
    public string Header { get; set; } = "4X Association Showcase";
    public string Summary { get; set; } = string.Empty;
    public string Controls { get; set; } = string.Empty;
    public string PlayerAName { get; set; } = "Azure Compact";
    public string PlayerBName { get; set; } = "Amber League";
    public string CityAName { get; set; } = "Azure Foundry";
    public string CityBName { get; set; } = "Amber Spire";
    public string HiddenUnitName { get; set; } = "Amber Envoy";
    public string TeamHostName { get; set; } = "Azure Research Council";
    public string ResearcherName { get; set; } = "Signal Cartographer";
    public string GoldAttribute { get; set; } = "Gold";
    public int StartingGold { get; set; } = 18;
    public int KnowledgeTtlTicks { get; set; } = 3;
    public int ConfidencePermille { get; set; } = 900;
    public string DiplomacyType { get; set; } = "FourXAssociation.Diplomacy";
    public string TrustMetric { get; set; } = "FourXAssociation.Trust";
    public string TradePactFlag { get; set; } = "FourXAssociation.TradePact";
    public short PactTrust { get; set; } = 70;
    public string TradeOperation { get; set; } = "fourx_association.trade_supply";
    public string SupplyItem { get; set; } = "fourx_association_supply";
    public string CityStashLayout { get; set; } = "fourx_association_city_stash";
    public string TeamScope { get; set; } = "fourx.association.team";
    public string CollectionKey { get; set; } = "fourx.association.team.members";
    public string ProgressionId { get; set; } = "Progression.FourXAssociation.SignalNet";
    public string RequirementId { get; set; } = "Req.FourXAssociation.SignalNet.Use";
    public string MemberTag { get; set; } = "Role.FourXAssociation.Researcher";
    public int RequiredMembers { get; set; } = 2;
    public int ResearchCost { get; set; } = 100;
    public int ContributionPerMember { get; set; } = 25;
    public string UnlockLabel { get; set; } = "Signal Net";
    public MemberConfig[] Members { get; set; } = Array.Empty<MemberConfig>();

    public static FourXAssociationConfig Load(Stream stream)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        FourXAssociationConfig config = JsonSerializer.Deserialize<FourXAssociationConfig>(stream, options)
            ?? throw new InvalidOperationException("Failed to deserialize FourXAssociationConfig.");
        config.Validate();
        return config;
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(TradeOperation) ||
            string.IsNullOrWhiteSpace(SupplyItem) ||
            string.IsNullOrWhiteSpace(CityStashLayout) ||
            string.IsNullOrWhiteSpace(TeamScope) ||
            string.IsNullOrWhiteSpace(CollectionKey) ||
            string.IsNullOrWhiteSpace(ProgressionId) ||
            string.IsNullOrWhiteSpace(RequirementId))
        {
            throw new InvalidOperationException("FourX association showcase config is missing required ids.");
        }

        if (StartingGold <= 0 || KnowledgeTtlTicks <= 0 || RequiredMembers <= 0 || ResearchCost <= 0 || ContributionPerMember <= 0)
        {
            throw new InvalidOperationException("FourX association showcase numeric config values must be positive.");
        }

        if (Members.Length == 0)
        {
            throw new InvalidOperationException("FourX association showcase requires configured research members.");
        }
    }
}

internal sealed class MemberConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int ContributionMultiplier { get; set; } = 1;
    public bool StartsActive { get; set; }
}
