using System;
using System.IO;
using System.Text.Json;
using Ludots.Core.Config;

namespace TeamResearchShowcaseMod.Runtime;

internal sealed class TeamResearchConfig
{
    public string MapId { get; set; } = TeamResearchIds.MapId;
    public string Header { get; set; } = "Team Research";
    public string Summary { get; set; } = "Team members contribute to one shared technology.";
    public string Controls { get; set; } = "A add member | Space research | R reset";
    public string TeamScope { get; set; } = "team";
    public string CollectionKey { get; set; } = "team.members";
    public string ProgressionId { get; set; } = "Progression.Showcase.TeamResearch.SignalRelay";
    public string RequirementId { get; set; } = "Req.Showcase.TeamResearch.SignalRelay.Use";
    public int RequiredMembers { get; set; } = 2;
    public int ResearchCost { get; set; } = 100;
    public int ContributionPerMember { get; set; } = 25;
    public string UnlockLabel { get; set; } = "Signal Relay";
    public string MemberTag { get; set; } = "Role.Showcase.Researcher";
    public string TeamHostName { get; set; } = "Team Research Cell";
    public string ResearcherName { get; set; } = "Lead Researcher";
    public MemberConfig[] Members { get; set; } = Array.Empty<MemberConfig>();

    public static TeamResearchConfig Load(Stream stream)
    {
        TeamResearchConfig? config = JsonSerializer.Deserialize<TeamResearchConfig>(
            stream,
            StrictJsonOptions.CreateExact());
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize TeamResearchConfig.");
        }

        if (config.Members.Length == 0)
        {
            throw new InvalidOperationException("Team research showcase requires at least one configured member.");
        }

        if (config.RequiredMembers <= 0 ||
            config.ResearchCost <= 0 ||
            config.ContributionPerMember <= 0)
        {
            throw new InvalidOperationException("Team research numeric settings must be greater than zero.");
        }

        return config;
    }
}

internal sealed class MemberConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int ContributionMultiplier { get; set; } = 1;
    public bool StartsActive { get; set; }
}
