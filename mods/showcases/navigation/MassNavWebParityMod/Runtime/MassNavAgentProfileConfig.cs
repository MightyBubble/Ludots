using System;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavAgentProfileSetConfig
{
    public string DefaultProfileId { get; set; } = "light";
    public MassNavAgentProfileConfig[] Profiles { get; set; } = Array.Empty<MassNavAgentProfileConfig>();

    public void Validate()
    {
        if (Profiles.Length <= 0)
        {
            throw new InvalidOperationException("Mass-nav agentProfiles requires at least one profile.");
        }

        bool foundDefault = false;
        for (int i = 0; i < Profiles.Length; i++)
        {
            Profiles[i].Validate(i);
            if (string.Equals(Profiles[i].Id, DefaultProfileId, StringComparison.OrdinalIgnoreCase))
            {
                foundDefault = true;
            }
        }

        if (!foundDefault)
        {
            throw new InvalidOperationException($"Mass-nav agentProfiles defaultProfileId '{DefaultProfileId}' is not defined.");
        }
    }

    public MassNavAgentProfileConfig ResolveForLocalIndex(int localIndex)
    {
        MassNavAgentProfileConfig defaultProfile = Resolve(DefaultProfileId);
        for (int i = 0; i < Profiles.Length; i++)
        {
            MassNavAgentProfileConfig profile = Profiles[i];
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

    private MassNavAgentProfileConfig Resolve(string id)
    {
        for (int i = 0; i < Profiles.Length; i++)
        {
            if (string.Equals(Profiles[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return Profiles[i];
            }
        }

        throw new InvalidOperationException($"Mass-nav agent profile '{id}' is not configured.");
    }
}

public sealed class MassNavAgentProfileConfig
{
    public string Id { get; set; } = string.Empty;
    public bool Heavy { get; set; }
    public float NavMass { get; set; } = 1f;
    public float VisualScale { get; set; } = 0.22f;
    public int EveryNth { get; set; }
    public int NthOffset { get; set; }

    public void Validate(int index)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidOperationException($"Mass-nav agentProfiles.profiles[{index}] requires id.");
        }

        if (!(NavMass > 0f))
        {
            throw new InvalidOperationException($"Mass-nav agent profile '{Id}' requires NavMass > 0.");
        }

        if (!(VisualScale > 0f))
        {
            throw new InvalidOperationException($"Mass-nav agent profile '{Id}' requires VisualScale > 0.");
        }

        if (EveryNth < 0)
        {
            throw new InvalidOperationException($"Mass-nav agent profile '{Id}' requires EveryNth >= 0.");
        }

        if (NthOffset < 0)
        {
            throw new InvalidOperationException($"Mass-nav agent profile '{Id}' requires NthOffset >= 0.");
        }

        if (EveryNth > 0 && NthOffset >= EveryNth)
        {
            throw new InvalidOperationException($"Mass-nav agent profile '{Id}' requires NthOffset < EveryNth.");
        }
    }
}
