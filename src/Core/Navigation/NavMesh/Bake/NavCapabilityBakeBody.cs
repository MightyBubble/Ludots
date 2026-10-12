using System;
using Ludots.Core.Navigation.AgentProfiles;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.NavMesh.Bake
{
    public static class NavCapabilityBakeBody
    {
        public static float RequireRecastSlope(NavMeshAgentProfileConfig navProfile)
        {
            ArgumentNullException.ThrowIfNull(navProfile);
            if (navProfile.MaxSlopeDeg is not float slope)
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.profiles.{navProfile.Id} is baked by Recast and must declare maxSlopeDeg. Omitting maxSlopeDeg means unlimited, which Recast cannot bake.");
            }

            if (slope <= 0f || slope >= 90f || float.IsNaN(slope))
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.profiles.{navProfile.Id} maxSlopeDeg must be > 0 and < 90 for Recast bake.");
            }

            return slope;
        }

        public static AgentProfileConfig RequireBody(AgentProfileRegistry agents, NavMeshAgentProfileConfig navProfile)
        {
            ArgumentNullException.ThrowIfNull(agents);
            ArgumentNullException.ThrowIfNull(navProfile);
            if (string.IsNullOrWhiteSpace(navProfile.AgentProfileId))
            {
                throw new InvalidOperationException(
                    $"NavMeshBakeConfig.profiles.{navProfile.Id} 必须声明 agentProfileId（Recast 烘焙使用的体型）。");
            }

            return agents.Require(navProfile.AgentProfileId, $"NavMeshBakeConfig.profiles.{navProfile.Id}.agentProfileId");
        }

        public static AgentProfileConfig RequireRecast(AgentProfileRegistry agents, NavMeshAgentProfileConfig navProfile)
        {
            _ = RequireRecastSlope(navProfile);
            return RequireBody(agents, navProfile);
        }
    }
}
