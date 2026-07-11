using System;
using System.Text.Json.Serialization;
using Ludots.Core.Navigation.AgentProfiles;

namespace Ludots.Core.MassNavigation.Runtime;

public sealed class MassNavigationAgentProfileSetConfig
{
    [JsonRequired] public string DefaultProfileId { get; set; } = string.Empty;
    [JsonRequired] public MassNavigationAgentProfileConfig[] Profiles { get; set; } = Array.Empty<MassNavigationAgentProfileConfig>();
    private AgentProfileRegistry? _agentProfiles;

    public void BindAgentProfiles(AgentProfileRegistry agentProfiles)
    {
        _agentProfiles = agentProfiles ?? throw new ArgumentNullException(nameof(agentProfiles));
        ValidateAgentProfileReferences();
    }

    public void Validate()
    {
        if (Profiles.Length <= 0)
        {
            throw new InvalidOperationException("MassNavigation agentProfiles requires at least one profile.");
        }

        bool foundDefault = false;
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Profiles.Length; i++)
        {
            Profiles[i].Validate(i);
            if (!profileIds.Add(Profiles[i].Id))
            {
                throw new InvalidOperationException($"MassNavigation agentProfiles contains duplicate profile id '{Profiles[i].Id}'.");
            }

            if (string.Equals(Profiles[i].Id, DefaultProfileId, StringComparison.Ordinal))
            {
                foundDefault = true;
            }
        }

        if (!foundDefault)
        {
            throw new InvalidOperationException($"MassNavigation agentProfiles defaultProfileId '{DefaultProfileId}' is not defined.");
        }
    }

    public void ValidateAgentProfileReferences()
    {
        if (_agentProfiles == null)
        {
            throw new InvalidOperationException("MassNavigation agentProfiles requires the unified AgentProfileRegistry before runtime use.");
        }

        for (int i = 0; i < Profiles.Length; i++)
        {
            _agentProfiles.Require(Profiles[i].Id, $"MassNavigationConfig.agentProfiles.profiles[{i}]");
        }
    }

    public MassNavigationAgentProfileConfig ResolveForLocalIndex(int localIndex)
    {
        MassNavigationAgentProfileConfig defaultProfile = Resolve(DefaultProfileId);
        for (int i = 0; i < Profiles.Length; i++)
        {
            MassNavigationAgentProfileConfig profile = Profiles[i];
            if (profile.EveryNth <= 0)
            {
                continue;
            }

            if (localIndex % profile.EveryNth == profile.NthOffset)
            {
                return profile;
            }
        }

        return defaultProfile;
    }

    public MassNavigationAgentProfileConfig Resolve(string id)
    {
        for (int i = 0; i < Profiles.Length; i++)
        {
            if (string.Equals(Profiles[i].Id, id, StringComparison.Ordinal))
            {
                return Profiles[i];
            }
        }

        throw new InvalidOperationException($"MassNavigation agent profile '{id}' is not configured.");
    }

    public AgentProfileConfig ResolveGeometry(string id)
    {
        if (_agentProfiles == null)
        {
            throw new InvalidOperationException("MassNavigation agent profile geometry was requested before AgentProfileRegistry was bound.");
        }

        return _agentProfiles.Require(id, "MassNavigationConfig.agentProfiles");
    }
}

public sealed class MassNavigationAgentProfileConfig
{
    [JsonRequired] public string Id { get; set; } = string.Empty;
    [JsonRequired] public bool Heavy { get; set; }
    [JsonRequired] public float VisualScale { get; set; }
    [JsonRequired] public float SpeedCmPerSecond { get; set; }
    [JsonRequired] public int EveryNth { get; set; }
    [JsonRequired] public int NthOffset { get; set; }

    public void Validate(int index)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException($"MassNavigation agentProfiles.profiles[{index}] requires id.");
        }

        if (!(VisualScale > 0f))
        {
            throw new InvalidOperationException($"MassNavigation agent profile '{Id}' requires VisualScale > 0.");
        }

        if (!(SpeedCmPerSecond > 0f))
        {
            throw new InvalidOperationException($"MassNavigation agent profile '{Id}' requires SpeedCmPerSecond > 0.");
        }

        if (EveryNth < 0)
        {
            throw new InvalidOperationException($"MassNavigation agent profile '{Id}' requires EveryNth >= 0.");
        }

        if (NthOffset < 0)
        {
            throw new InvalidOperationException($"MassNavigation agent profile '{Id}' requires NthOffset >= 0.");
        }

        if (EveryNth > 0 && NthOffset >= EveryNth)
        {
            throw new InvalidOperationException($"MassNavigation agent profile '{Id}' requires NthOffset < EveryNth.");
        }
    }
}
